using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

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
                // 停距按"蛇头够到距离"折算：根必须走进"头前伸+突刺+判定球+玩家半径"范围内，
                // 否则蛇根停 3.6m 而蛇头公式够不到玩家 → 站死永不咬（贴脸不出手的蛇类根因）。
                SnakeBodyAnimation body = GetComponentInChildren<SnakeBodyAnimation>(true);
                float headOffset = body != null ? body.HeadForwardOffset : 0f;
                float lungeRatio = body != null ? body.headLungeRatio : 0.25f;
                float headReach = headOffset + (headOffset * 2f) * lungeRatio + headHitRadius + 0.5f + 0.15f;
                agent.stoppingDistance = Mathf.Clamp(headReach * 0.9f, 1.2f,
                    enemyData != null ? Mathf.Max(enemyData.attackRange, 1.2f) : 4f);
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
                    // ⭐ 排队点距玩家超过攻击就位带（attackRange×attackRangeSlackMultiplier）时不排队：
                    // 排到远处只会站在别人身后等"前面的人让位"，玩家站着不动时永远轮不到出手。直接压向玩家。
                    if (queue.HasValue &&
                        Vector3.Distance(queue.Value, player.transform.position) >
                        GetAttackActivationRange() * attackRangeSlackMultiplier)
                    {
                        queue = null;
                    }
                    if (queue.HasValue) target = queue.Value;
                }
            }

            NotePathMutation("Snake.HandleMovement → isStopped=false");
            agent.isStopped = false;
            SetChaseDestination(target);

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
                // applyCloseCombatExemption=false：贴薄墙(≤1.2m)被实墙隔开也算被挡，
                // 蛇头转向绕行方向而非隔着墙面向玩家。
                if (!moving && player != null &&
                    WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position, applyCloseCombatExemption: false))
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
            // 统一走 Mixer：挂到 Sfx 组，音量交给 Sfx 组控制
            if (AudioManager.Instance != null && AudioManager.Instance.masterMixer != null)
            {
                AudioMixerGroup[] sfx = AudioManager.Instance.masterMixer.FindMatchingGroups("Sfx");
                if (sfx != null && sfx.Length > 0)
                    moveAudioSource.outputAudioMixerGroup = sfx[0];
            }
        }

        bool moving = isChasing && !isDead;

        if (moving)
        {
            if (!moveAudioSource.isPlaying)
            {
                // 已挂 Sfx 组时音量交给 Mixer；找不到 Mixer 时兜底直接用设置音量
                if (moveAudioSource.outputAudioMixerGroup != null)
                    moveAudioSource.volume = 1f;
                else
                    moveAudioSource.volume = Mathf.Clamp01(AudioManager.GetSFXVolume() * (AudioManager.Instance != null ? AudioManager.Instance.sfxVolumeGain : 1f));
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

    // 玩家碰撞体水平半径（估算，供蛇头够到/攻击触发距离用）
    private float EstimatePlayerRadius()
    {
        float playerR = 0.5f;
        Collider pc = cachedPlayerCollider;
        if (pc == null && player != null)
        {
            cachedPlayerCollider = player.GetComponentInChildren<Collider>();
            pc = cachedPlayerCollider;
        }
        if (pc != null && pc.bounds.extents.x > 0f) playerR = pc.bounds.extents.x;
        return playerR;
    }

    // 蛇头够到判定（以蛇头当前变形后世界位置到玩家的水平距离为准）：
    // 蛇头前伸(HeadWorldPosition 已含) + 突刺前扑(身长×headLungeRatio) + 蛇头判定球半径
    // + 玩家半径 + 缓冲。与蛇头判定球物理碰触口径一致，不再用"蛇根到玩家"的公式估算，
    // 避免蛇根停 3.6m 时蛇头几何其实够得着却因公式失真而永不咬。
    private bool CanHeadReach(PlayerController target)
    {
        if (snakeBody == null) return true;

        Vector3 a = snakeBody.HeadWorldPosition; a.y = 0f;
        Vector3 b = target.transform.position; b.y = 0f;
        float headDist = Vector3.Distance(a, b);

        float lunge = (snakeBody.HeadForwardOffset * 2f) * snakeBody.headLungeRatio;
        float reach = lunge + headHitRadius + EstimatePlayerRadius() + 0.15f;
        return headDist <= reach;
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
            // ⭐ 头还够不着（玩家小幅走远/动画帧差）：不停死原地站桩，
            // 保持 agent 未停并继续以玩家为目的地压近，下一帧进入攻击分支即咬。
            if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                SetChaseDestination(player.transform.position);
            }
            return false;
        }
        return base.TryPerformInRangeAttack();
    }

    // 攻击触发距离以蛇头为基准：蛇头在身体前端（约身长一半的前方）。
    // 基类用根(agent)到玩家的距离判断，对长身体的蛇来说根停 4m 时蛇头已在 2m 外、够不着；
    // 改成只把"根进入攻击分支"的门槛压到"蛇头(前伸+突刺+判定球)够到玩家"的范围，
    // 且不低于 0.8m——蛇根走进这个距离才停手，保证停定时蛇头一定咬得到。
    protected override float GetAttackActivationRange()
    {
        if (!IsSerpentine || snakeBody == null) return base.GetAttackActivationRange();

        float baseRange = base.GetAttackActivationRange();
        float headOffset = snakeBody.HeadForwardOffset;     // 蛇头在根前方多远（世界单位）
        float headReach = headOffset
                        + (headOffset * 2f) * snakeBody.headLungeRatio
                        + headHitRadius + EstimatePlayerRadius() + 0.15f;
        return Mathf.Max(0.8f, Mathf.Min(baseRange, headReach));
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (headHitbox != null) Destroy(headHitbox);
    }
}