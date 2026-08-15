using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class SnakeEnemy : EnemyAI
{
    [Header("蛇形移动（头先转，身体后段跟随）")]
    [Tooltip("关闭 NavMeshAgent 的自动旋转（否则整个身体瞬转抽搐），由蛇自己平滑转动头部引导方向。仅挂有 SnakeBodyAnimation 的蛇类生效")]
    public bool serpentineMove = true;
    [Tooltip("追击时头部最大转向速度（度/秒）。越小形态越呆、拖尾越明显")]
    public float headTurnSpeed = 160f;

    [Header("蛇头攻击判定（以蛇头为攻击点）")]
    [Tooltip("蛇头攻击判定球半径（世界单位）。攻击动画期间跟随蛇头变形位置，只有蛇头碰到玩家才造成伤害")]
    public float headHitRadius = 0.5f;
    [Tooltip("蛇头判定球只在攻击动画期间启用（true），结束后关闭避免身体误伤")]
    public bool enableHeadHitbox = true;

    // 蛇形移动只在有蛇身分段动画时生效（避免影响同样挂 SnakeEnemy 的 Basic 等普通敌人。
    // 蛇身动画挂在子物体上，用 GetComponentInChildren 查找）
    private bool IsSerpentine => serpentineMove && GetComponentInChildren<SnakeBodyAnimation>(true) != null;

    private SnakeBodyAnimation snakeBody;
    private GameObject headHitbox;
    private SnakeHeadHitbox headHitboxScript;
    private bool wasAttacking;   // 攻击上升沿检测（启用判定球/重置命中标志）
    private bool attackHitDealt = false;   // 本轮攻击是否已造成伤害（触发球+距离兜底共用）
    private float attackStartTime = -1f;   // 本轮攻击开始时刻（用于延误到突刺时判定命中）
    private AudioSource moveAudioSource;   // 移动循环音效

    protected override void OnStart()
    {
        base.OnStart();
        if (isAgentValid && agent != null)
        {
            if (IsSerpentine)
            {
                // 蛇（眼镜蛇式）：身体停远些，攻击靠蛇头突刺命中。
                // 蛇头在根前方约半个身长，突刺+判定球还能再够 ~1.9m，
                // 根停约 0.9×攻击范围时蛇头(前伸+突刺)恰好够到玩家。
                agent.stoppingDistance = (enemyData != null ? enemyData.attackRange * 0.9f : 3.6f);
                // 旋转交给蛇自己平滑控制（头先转），否则 agent 会把整个身体瞬转抽搐
                agent.updateRotation = false;

                // 蛇攻击节奏提速：缩短击退硬直，让咬击更紧凑（攻击冷却由 Snake.asset 的 attackCooldown 控制）
                staggerDuration = 0.2f;
            }
            else
                // Basic（直接攻击）：停到 1.5m 再出拳，更贴近玩家
                agent.stoppingDistance = 1.5f;
        }
        CreateHeadHitbox();
    }

    protected override void Update()
    {
        base.Update();
        UpdateHeadHitbox();
        UpdateMoveSFX();
    }

    protected override void HandleMovement()
    {
        // 不在追击状态 → 待机
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            return;
        }

        if (isAgentValid)
        {
            Vector3 target = player.transform.position;

            // 环形占位：有免费空位就过去绕玩家。
            // 蛇类：找不到空位也不排队堵到别的敌人身后（那样会停着不攻），直接压向玩家，
            //       靠蛇头长距离突刺命中；普通敌人保留"全满则排前面敌人身后"的兜底。
            // 击退回位期间跳过环形占位：被击退时玩家就在身边，直接压回玩家贴到攻击距离，
            // 否则蛇会先去绕远占位点（甚至被挡停在半路），表现为"击退后停着不走近"。
            if (enableFormation && !forceReturnToRange)
            {
                Vector3? slot = GetFormationTarget();
                if (slot.HasValue)
                {
                    target = slot.Value;
                }
                else if (!IsSerpentine)
                {
                    Vector3? queue = GetQueueTarget();
                    if (queue.HasValue) target = queue.Value;
                }
            }

            agent.isStopped = false;
            agent.SetDestination(target);

            // 蛇式移动：追击绕行时蛇头跟随"移动方向"（agent 沿 NavMesh 路径绕墙），
            // 停下/攻击时才面向玩家。若始终朝向玩家，绕墙时蛇身会横着侧移、
            // 长条碰撞体横着卡墙角（agent.updateRotation=false 不会自己转方向绕开）。
            if (IsSerpentine)
            {
                Vector3 moveDir = agent.velocity;
                moveDir.y = 0f;
                bool moving = moveDir.sqrMagnitude > 0.01f;

                // 停下时若仍被实体墙隔开（卡在墙对面），改朝 agent 想去的绕行方向
                // （desiredVelocity，即便当前被挡未动也指向绕行意图），而非面向玩家，
                // 消除"蛇头对着墙对面"的视觉抖动；真正贴近玩家（无墙可攻）才面向玩家。
                if (!moving && player != null &&
                    WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position))
                {
                    Vector3 desired = agent.desiredVelocity;
                    desired.y = 0f;
                    if (desired.sqrMagnitude > 0.001f)
                    {
                        moveDir = desired;
                        moving = true;
                    }
                }

                if (moving)
                    RotateHeadTowards(moveDir, Time.deltaTime);
                else
                    RotateHeadTowards(player.transform.position - transform.position, Time.deltaTime);
            }
        }
    }

    // 限速转动头部（蛇身体不整体瞬转；超过最大转向速度时按速度截断）
    private void RotateHeadTowards(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized);
        float step = headTurnSpeed * Mathf.Max(dt, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, step);
    }

    // 生成蛇头攻击判定球（只对蛇生效）。攻击动画期间启用并跟随蛇头世界位置。
    private void CreateHeadHitbox()
    {
        if (!IsSerpentine || !enableHeadHitbox) return;

        snakeBody = GetComponentInChildren<SnakeBodyAnimation>(true);
        headHitbox = new GameObject("SnakeHeadHitbox");
        headHitbox.transform.SetParent(transform, false);

        SphereCollider sc = headHitbox.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = Mathf.Max(0.1f, headHitRadius);

        // 触发球需要刚体才能与玩家触发；kinematic 避免被物理推动
        Rigidbody rb = headHitbox.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        headHitboxScript = headHitbox.AddComponent<SnakeHeadHitbox>();
        headHitboxScript.SetOwner(this);
        headHitbox.SetActive(false);
    }

    // 攻击动画期间：把判定球贴到蛇头变形后的世界位置并启用；攻击结束关闭（身体/尾巴无判定）
    private void UpdateHeadHitbox()
    {
        if (headHitbox == null) return;

        bool attacking = isAttacking && !isDead;
        if (attacking)
        {
            if (!wasAttacking && headHitboxScript != null)
            {
                headHitboxScript.BeginAttack();     // 攻击上升沿：重置本轮命中
                attackHitDealt = false;
                attackStartTime = Time.time;
            }
            headHitbox.SetActive(true);
            if (snakeBody != null)
                headHitbox.transform.position = snakeBody.HeadWorldPosition;

            // 命中兜底：等到突刺时刻按"蛇头够到范围"判定，不再依赖物理碰触。
            // 停在 stoppingDistance 攻击（蛇头未必真的碰到玩家）也能稳定造成伤害。
            if (player != null && !attackHitDealt && Time.time - attackStartTime >= attackDamageDelay)
                TryDealHeadDamage(player);
        }
        else
        {
            headHitbox.SetActive(false);
        }
        wasAttacking = attacking;
    }

    // 蛇移动循环音效：进入追击至脱离追击/死亡间不间断播放（沙沙爬行感）。
    // 不依赖 agent 实际速度判定——攻击瞬间、转向、短暂停顿都会让声音忽断忽续，
    // 改为只要在追击就持续循环发出，保持连续无间断感。
    // 用一个循环 AudioSource 持续发声，不占 AudioManager 一次性播放池。
    private void UpdateMoveSFX()
    {
        if (moveSFX == null) return;

        if (moveAudioSource == null)
        {
            moveAudioSource = GetComponent<AudioSource>();
            if (moveAudioSource == null) moveAudioSource = gameObject.AddComponent<AudioSource>();
            moveAudioSource.clip = moveSFX;
            moveAudioSource.loop = true;
            moveAudioSource.playOnAwake = false;
            moveAudioSource.spatialBlend = AudioManager.Instance != null ? AudioManager.Instance.sfxSpatialBlend : 1f;
        }

        bool moving = isChasing && !isDead;

        if (moving)
        {
            if (!moveAudioSource.isPlaying)
            {
                moveAudioSource.volume = AudioManager.GetSFXVolume();
                moveAudioSource.Play();
            }
        }
        else if (moveAudioSource.isPlaying)
        {
            moveAudioSource.Stop();
        }
    }

    // 由 SnakeHeadHitbox 回调 / 攻击中距离兜底共用：蛇头够得到才造成伤害，每轮攻击最多一次
    public bool TryDealHeadDamage(PlayerController target)
    {
        if (isDead || target == null || target.IsDead() || attackHitDealt) return false;
        if (!CanHeadReach(target)) return false;
        // 不穿墙：蛇头与玩家之间被竖直墙体(实墙)挡就咬不到；
        // 用真墙 IsBlockedBetween（自带 ≤1.2m 近身豁免），墙角贴脸不误判，
        // 但真隔墙(>1.2m 且中间有墙)时宁可不中，也不产生隔墙咬。
        Vector3 headPos = snakeBody != null ? snakeBody.HeadWorldPosition : transform.position;
        if (WallPenetrationResolve.IsBlockedBetween(headPos, target.transform.position)) return false;

        float finalDamage = baseAttackDamage * currentDamageMultiplier;
        target.TakeDamage(Mathf.RoundToInt(finalDamage));
        attackHitDealt = true;
        return true;
    }

    // 蛇头够到判定（从蛇根为中心的水平距离）：
    // 蛇头前伸(约半身长, HeadForwardOffset) + 突刺前扑(身长×headLungeRatio) + 蛇头判定球半径
    // + 玩家半径 + 缓冲。即便蛇头物理上还没碰到玩家（停 stoppingDistance），几何上够得到也算命中。
    private bool CanHeadReach(PlayerController target)
    {
        if (snakeBody == null) return true;

        float bodyWorld = snakeBody.HeadForwardOffset * 2f;
        float playerR = 0.5f;
        Collider pc = cachedPlayerCollider;
        if (pc == null && player != null)
        {
            cachedPlayerCollider = player.GetComponentInChildren<Collider>();
            pc = cachedPlayerCollider;
        }
        if (pc != null && pc.bounds.extents.x > 0f) playerR = pc.bounds.extents.x;

        float reach = snakeBody.HeadForwardOffset
                    + bodyWorld * snakeBody.headLungeRatio
                    + headHitRadius + playerR + 0.15f;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = target.transform.position; b.y = 0f;
        return Vector3.Distance(a, b) <= reach;
    }

    // 蛇的伤害判定交给蛇头判定球，不再使用“身体中心到玩家距离”的通用判定，避免身体误伤
    protected override IEnumerator DelayedDamage()
    {
        if (IsSerpentine) yield break;
        yield return base.DelayedDamage();
    }

    // 蛇入攻击态不要求身体正对玩家（环形站位上朝向各异，只有正对的先打、其余干站）。
    // 蛇头可独立转头+突刺前伸，蛇头几何够到玩家就能咬，不必等身体完全转身。
    protected override bool TryPerformInRangeAttack()
    {
        if (IsSerpentine && player != null && !player.IsDead())
        {
            RotateHeadTowards(player.transform.position - transform.position, Time.deltaTime);
            if (canAttack && CanHeadReach(player))
            {
                PerformAttack();
                return true;
            }
            StopAgent();
            return false;
        }
        return base.TryPerformInRangeAttack();
    }

    // 攻击触发距离以蛇头为基准：蛇头在身体前端（约身长一半的前方）。
    // 基类用根(agent)到玩家的距离判断，对长身体的蛇来说根停 4m 时蛇头已在 2m 外、够不着；
    // 改成把触发距离减去蛇头前伸量，让根停得足够近、蛇头(前伸+突刺)恰好够到玩家，
    // 同时不低于 stoppingDistance，避免永远停不到攻击位置。
    protected override float GetAttackActivationRange()
    {
        if (!IsSerpentine || snakeBody == null) return base.GetAttackActivationRange();

        float baseRange = base.GetAttackActivationRange();
        float headOffset = snakeBody.HeadForwardOffset;     // 蛇头在根前方多远（世界单位）
        // 蛇头已经把"根到玩家"的有效距离前伸了半个身长，触发距离相应收紧半个身长，
        // 但至少不小于 agent 停距（否则蛇永远进不了攻击态）。
        return Mathf.Max(agent != null ? agent.stoppingDistance : baseRange,
                         baseRange - headOffset);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (headHitbox != null) Destroy(headHitbox);
    }
}