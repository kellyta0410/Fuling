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
                // 蛇（眼镜蛇式）：停距按"蛇头几何够到范围"折算，而不是 attackRange×0.9 的估算。
                // 蛇身长短不一，0.9×attackRange 常大于蛇头(前伸+突刺)能到的最远距离，
                // 蛇停在够不到玩家的位置 → 永远不进攻击态。用实际够距×0.8 停，
                // 保证蛇一开局就能贴身追击并够到玩家咬到。
                agent.stoppingDistance = Mathf.Max(1.2f, GetHeadReach() * 0.8f);
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
                    // 蛇的占位点必须在蛇头够到范围内：蛇身长、基类占位环按 attackRange×0.9
                    // 算出的半径常比蛇头够距还大，蛇停上去够不到玩家 → 干瞪眼不咬。
                    // 占位点够不到就放弃占位，直接压向玩家贴身咬。
                    if (IsSerpentine && !IsSlotWithinHeadReach(slot.Value))
                        target = player.transform.position;
                    else
                        target = slot.Value;
                }
                else if (!IsSerpentine)
                {
                    Vector3? queue = GetQueueTarget();
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

    // 蛇头够到判定（从蛇根为中心的水平距离）：
    // 蛇头前伸(约半身长, HeadForwardOffset) + 突刺前扑(身长×headLungeRatio) + 蛇头判定球半径
    // + 玩家半径 + 缓冲。即便蛇头物理上还没碰到玩家（停 stoppingDistance），几何上够得到也算命中。
    private bool CanHeadReach(PlayerController target)
    {
        if (snakeBody == null) return true;

        float playerR = 0.5f;
        Collider pc = cachedPlayerCollider;
        if (pc == null && player != null)
        {
            cachedPlayerCollider = player.GetComponentInChildren<Collider>();
            pc = cachedPlayerCollider;
        }
        if (pc != null && pc.bounds.extents.x > 0f) playerR = pc.bounds.extents.x;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = target.transform.position; b.y = 0f;
        return Vector3.Distance(a, b) <= GetHeadReach(playerR);
    }

    // 蛇头从蛇根算起的最大够到距离（世界单位）。
    // 停距(OnStart)、攻击触发距离、命中判定共用同一口径，保证三者一致：
    // 停距 < 够距 ≤ 触发距离，蛇停在够距内就一定能进入攻击态并咬到玩家。
    private float GetHeadReach(float playerR = 0.5f)
    {
        // snakeBody 可能在 OnStart 时尚未赋值（CreateHeadHitbox 之后才有），就地查找
        SnakeBodyAnimation sb = snakeBody != null ? snakeBody : GetComponentInChildren<SnakeBodyAnimation>(true);
        if (sb == null) return (enemyData != null ? enemyData.attackRange : 3.6f);

        float bodyWorld = sb.HeadForwardOffset * 2f;
        return sb.HeadForwardOffset
             + bodyWorld * sb.headLungeRatio
             + headHitRadius + playerR + 0.15f;
    }

    // 环形占位点是否落在蛇头够到范围内（从玩家到占位点的水平距离 ≤ 够距）。
    // 蛇头判定是从"蛇根→玩家"距离来的，蛇走到占位点后根就在占位点，
    // 身长前伸的蛇头刚好向玩家方向拱出，所以够距判定用玩家到占位点。
    private bool IsSlotWithinHeadReach(Vector3 slot)
    {
        if (player == null) return true;
        Vector3 a = slot; a.y = 0f;
        Vector3 b = player.transform.position; b.y = 0f;
        return Vector3.Distance(a, b) <= GetHeadReach();
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

    // 攻击触发距离以"蛇头几何够到范围"为准：距玩家 ≤ 够距才进入攻击态。
    // 基类用根(agent)到玩家的距离判断，对长身体的蛇来说根停 4m 时蛇头已在 2m 外、够不着；
    // 且若触发距离 > 够距，蛇会在够不到的位置停住干瞪眼，永远不攻。
    // 用与停距同口径的够距做触发距离（不低于停距），保证蛇一停就能咬。
    protected override float GetAttackActivationRange()
    {
        if (!IsSerpentine || snakeBody == null) return base.GetAttackActivationRange();

        float reach = GetHeadReach();
        float stopping = agent != null ? agent.stoppingDistance : reach;
        return Mathf.Max(stopping, reach);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (headHitbox != null) Destroy(headHitbox);
    }
}