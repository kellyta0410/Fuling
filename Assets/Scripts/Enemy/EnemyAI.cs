using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

public abstract class EnemyAI : MonoBehaviour
{
    // ---------- 公共组件 ----------
    protected Animator animator;
    protected PlayerController player;
    protected NavMeshAgent agent;

    [Header("敌人数据")]
    public EnemyData enemyData;

    [Header("金币")]
    public GameObject coinPrefab;

    [Header("音效（Clip 放这里，走 AudioManager 播放池）")]
    [Tooltip("被玩家打中时的受击音效")]
    public AudioClip hitSFX;
    [Tooltip("发起攻击时的攻击音效")]
    public AudioClip attackSFX;
    [Tooltip("移动时循环播放的音效（如蛇的沙沙爬行声）；移动停下或进入攻击时自动停。非移动类敌人留空")]
    public AudioClip moveSFX;
    [Tooltip("死亡音效")]
    public AudioClip deathSFX;

    [Header("血条")]
    public GameObject healthBarPrefab;
    public Vector3 healthBarOffset = new Vector3(0, 1.5f, 0);
    protected GameObject healthBarInstance;
    protected Slider healthSlider;
    protected Image healthFillImage;
    protected float currentHealth;
    protected float maxHealth;   // 生成时锁定的最大血量（生成当刻的血量，后续升级不再改变）

    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;

    [Header("状态")]
    public bool isDead = false;
    // 只读状态，供蛇身动画等外部组件读取（无骨骼模型没有有效 Animator 参数）
    public bool IsAttackingNow { get { return isAttacking; } }
    public bool IsChasingNow { get { return isChasing; } }
    public bool IsMovingNow
    {
        get
        {
            if (isAttacking) return false;
            return agent != null && agent.velocity.sqrMagnitude > 0.01f;
        }
    }
    public PlayerController Player { get { return player; } }

    [Header("攻击通用")]
    protected float attackCooldownTimer = 0f;
    public bool canAttack = true;
    protected bool isAttacking = false;
    protected float attackTimer = 0f;
    public float attackDuration = 0.5f;

    [Header("被击退硬直")]
    [Tooltip("被击退后停顿（硬直）时长（秒），期间不移动不攻击")]
    public float staggerDuration = 0.4f;
    protected bool isStaggering = false;
    public float attackDamageDelay = 0.3f;
    protected Coroutine attackCoroutine;

    [Header("击退方向")]
    [Tooltip("击退方向掺入\"敌人自身后方\"的比例（0~1；0 = 纯远离玩家，1 = 纯自身后方）")]
    public float knockbackOwnBackBlend = 0.3f;

    [Header("接近刹停（避免冲过头）")]
    public bool enableApproachBrake = true;
    [Tooltip("离目标的 stoppingDistance 还有这么大范围时开始减速")]
    public float approachBrakeRange = 1.5f;
    [Tooltip("到达 stoppingDistance 时保留的最低速度倍数（0 = 完全停住，不再蠕动推玩家）")]
    public float approachStopSpeed = 0f;

    [Header("面向检测")]
    public float facingAngleThreshold = 45f;
    public float turnSpeed = 240f;

    [Header("追击检测（普通模式）")]
    public float detectionRange = 30f;
    public float loseTargetRange = 45f;

    [Header("追击检测（无限模式专用）")]
    public float infiniteDetectionRange = 60f;
    public float infiniteLoseTargetRange = 80f;

    [Header("群组行为（分离力）")]
    public bool enableSeparation = true;
    public float separationRadius = 2f;
    public float separationForce = 5f;
    public float separationSmoothSpeed = 10f;
    [Header("前方拥挤检测")]
    [Tooltip("追击时前方此距离内有敌人（朝玩家方向）则先停下，避免把前面敌人一路推挤到玩家")]
    public float frontEnemyStopRange = 1.5f;

    [Header("环形站位（寻找玩家周围空位往返占位）")]
    public bool enableFormation = true;
    public int formationSlots = 8;
    [Tooltip("某环占位点被附近敌人占据的判定半径")]
    public float formationOccupancyRadius = 1.2f;
    [Tooltip("站位环半径的附加余量（实际 = max(余量, attackRange*0.9)）")]
    public float formationRingPadding = 0.5f;

    // 分离力相关
    private Vector3 separationVelocity = Vector3.zero;
    private Collider myCollider;
    private int enemyLayerMask;
    protected Collider cachedPlayerCollider;
    // 被击退硬直结束后强制先走回 agent 停止距离，再进入攻击态（避免击退后原地站定攻击）
    protected bool forceReturnToRange = false;

    // 缩放倍率
    protected float currentSpeedMultiplier = 1f;
    protected float currentHealthMultiplier = 1f;
    protected float currentDamageMultiplier = 1f;

    // ---------- 卡住自救（绕障碍）----------
    [Header("卡住自救（绕障碍）")]
    [Tooltip("追击中位移低于此速度(米/秒)且持续超过 卡住时间 就判定被挡卡住")]
    public float stuckEscapeSpeed = 0.1f;
    [Tooltip("位移持续低于 stuckEscapeSpeed 的秒数后触发自救（重寻路绕障）")]
    public float stuckEscapeDelay = 0.5f;
    [Tooltip("自救后冷却（秒），避免每次都跟玩家贴脸时反复触发")]
    public float stuckEscapeCooldown = 1.0f;
    private float stuckTimer = 0f;
    private float stuckEscapeCooldownTimer = 0f;
    private Vector3 stuckEscapeLastPos = Vector3.zero;
    private bool stuckEscapePosValid = false;

    // ⭐ 快速重规划：玩家快速跨墙角时，不再等 TryStuckEscape 卡满 0.5s+1.0s 冷却，
    // 而是在"墙挡状态由未挡→变挡"(玩家已到墙另一侧/原路径不再合适)的下一帧立即重置路径换新路。
    private bool lastBlockedByWall = false;        // 上一帧墙挡状态
    private float fastReplanCooldownTimer = 0f;    // 快速重规划节流，避免每帧昂贵的 CalculatePath
    [SerializeField, Tooltip("快速重规划节流间隔(秒)：墙挡变化后过此间隔才允许再次整段重算")]
    private float fastReplanCooldown = 0.35f;

    // 隔墙追击的目标覆盖：hasPath=N+pending=Y 表示"墙对岸玩家点不可达"导致反复重算死循环。
    // 隔墙期间把追击目标持续设为"本侧可达的侧向绕行点"，让 agent 一直有可达落点沿墙侧走，
    // 直到 ≤1.2m 近身豁免进入墙角交战。不能锁 wall-edge(会走到墙边就停)，要用侧向点。
    private bool chaseOverrideActive = false;
    private Vector3 chaseOverrideTarget = Vector3.zero;
    private float chaseOverrideRecheckTimer = 0f;
    private const float chaseOverrideRecheckInterval = 0.3f;
    private float chasePathPendingTimer = 0f;      // 本侧目标下持续 pending 的时长，超时强制重算
    private const float chasePathPendingTimeout = 0.6f;

    [Header("调试")]
    [Tooltip("🔍 追击路径诊断：在敌人头顶显示实时的 墙挡/hasPath/pathPending/pathStatus/remainingDistance，用于定位隔墙追击为何顶墙")]
    public bool showPathDebug = false;
    private TextMesh pathDebugText;      // 头顶诊断文字（showPathDebug 开启时懒创建）
    private float pathDebugUpdateTimer = 0f;
    private const float pathDebugUpdateInterval = 0.1f;   // 0.1s 更新一次足够看清，省开销

    // 🔍 路径状态变更监控：勾上后，每帧对比 hasPath，任何 Y→N 或 N→Y 瞬间打印完整快照
    // 与"最后是谁动了路径"(lastPathMutation)。只用于定位 Y/N 闪烁的调用来源，不改任何逻辑。
    [Tooltip("🔍 路径状态监控：hasPath 变化瞬间打印完整快照+最后调用来源，用于定位 Y/N 闪烁")]
    public bool logPathStateChanges = false;
    private bool lastTrackedHasPath = false;
    private bool lastTrackedPending = false;
    private string lastPathMutation = "none";   // 最后执行 ResetPath/SetDestination/Stop/Warp/Move 的位置

    // 🔍 穿墙兜底诊断：勾上后本敌人每次触发『穿墙兜底 Warp』打印完整状态。
    // 运行时自动挂载的 EnemyCollisionBlocker 是脚本实例（Inspector 勾不了），这里透传给它的 logWallResolveDetails；
    // 也可用 EnemyCollisionBlocker.GlobalLogWallResolve 一次性对所有敌人开启。
    [Tooltip("🔍 穿墙兜底诊断：本敌人触发『穿墙兜底 Warp』时打印完整状态快照（透传给运行时挂载的 EnemyCollisionBlocker）")]
    public bool logWallResolveDetails = false;
    [Tooltip("🔍 全局穿墙兜底诊断：场景内所有敌人同时开启（等价于每个敌人勾上 logWallResolveDetails）")]
    public bool logWallResolveDetailsGlobal = false;

    // 🔍 攻击停止/恢复 流程诊断：StopAgent 调用来源 + 攻击 Start→Stop→Execute→End→Resume 全链路
    [Tooltip("🔍 攻击流程诊断：StopAgent 记录调用来源与完整状态；攻击开始/结束/恢复追击 各打一条日志")]
    public bool logAttackResumeFlow = false;
    private bool wasAttackingLastFrame = false;   // 🔍 用于检测"攻击结束→恢复追击"的跨帧转移

    // 🔍 stuck 检测诊断：打印 stuckPushing/TryStuckEscape 计算时的完整状态
    [Tooltip("🔍 stuck 检测诊断：重规划触发时打印 velocity/desiredVelocity/remainingDistance/isStopped/状态/是否攻击")]
    public bool logStuckDetection = false;

    protected float baseSpeed;
    protected float baseHealth;
    protected float baseAttackDamage;

    protected bool isHealthInitialized = false;
    protected bool isChasing = false;

    private float idleRotationTimer = 0f;
    private float idleRotationInterval = 3f;
    private Quaternion targetIdleRotation;

    protected bool isAgentValid = false;

    // 无限模式标志
    protected bool useDirectChase = false;

    // 所属Tile
    [Header("所属Tile")]
    public Tile ownerTile;

    // Animator 是否有 IsChasing 参数（仅 JiangShiMovement.controller 有，蛇等 controller 无此参数）
    private bool hasIsChasingParam = false;

    // ---------- 生命周期 ----------
    protected virtual void Start()
    {
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == "IsChasing")
                {
                    hasIsChasingParam = true;
                    break;
                }
            }
        }
        player = FindObjectOfType<PlayerController>();
        agent = GetComponent<NavMeshAgent>();
        myCollider = GetComponent<Collider>();
        if (myCollider == null) myCollider = GetComponentInChildren<Collider>();

        isAgentValid = agent != null && agent.isOnNavMesh;
        enemyLayerMask = LayerMask.GetMask("Enemy");

        // 强制将敌人位置修正到 NavMesh 上
        if (isAgentValid)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas))
            {
                Vector3 fixedPos = hit.position;
                transform.position = fixedPos;
                agent.Warp(fixedPos);
            }
            else
            {
                Debug.LogWarning($"{name} 附近无 NavMesh，请检查场景烘焙");
            }
        }

        if (GameManager.Instance != null)
        {
            useDirectChase = GameManager.Instance.IsInfiniteMode();
        }

        SetupNonBlockingPhysics();

        // 自动挂载碰撞解决器：检测敌人↔玩家/敌人↔敌人重叠，只移动敌人、不推玩家
        EnemyCollisionBlocker blocker = GetComponent<EnemyCollisionBlocker>();
        if (blocker == null)
        {
            blocker = gameObject.AddComponent<EnemyCollisionBlocker>();
        }
        // 🔍 透传穿墙兜底诊断开关（运行时自动挂载的实例无法在 Inspector 勾选，由这里转发）
        blocker.logWallResolveDetails = logWallResolveDetails;
        if (logWallResolveDetailsGlobal) EnemyCollisionBlocker.GlobalLogWallResolve = true;

        if (enemyData != null)
        {
            baseSpeed = enemyData.speed;
            baseHealth = enemyData.health;
            baseAttackDamage = enemyData.attackDamage;
            attackDamageDelay = enemyData.attackDamageDelay;

            if (!isHealthInitialized)
            {
                // 生成时锁定血量：满血 = 基础血量 × 生成当刻的倍率
                maxHealth = baseHealth * currentHealthMultiplier;
                currentHealth = maxHealth;
                isHealthInitialized = true;
            }

            if (isAgentValid)
            {
                agent.speed = baseSpeed;
                // 两个模式都设置 stoppingDistance：否则无限模式默认 0，敌人会走到玩家脚底贴脸才停
                // 取"攻击范围×0.5"且不小于 1m：让敌人更靠近玩家再停，避免离玩家太远就停下
                agent.stoppingDistance = Mathf.Max(1f, enemyData.attackRange * 0.5f);
                agent.autoBraking = true;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);
                agent.isStopped = true;
            }
        }
        else
        {
            baseSpeed = 2f;
            baseHealth = 50f;
            baseAttackDamage = 10f;
            if (!isHealthInitialized)
            {
                maxHealth = baseHealth * currentHealthMultiplier;
                currentHealth = maxHealth;
                isHealthInitialized = true;
            }
        }

        CreateHealthBar();
        ApplyCurrentMultipliers();
        targetIdleRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        idleRotationInterval = Random.Range(2f, 5f);
        idleRotationTimer = 0f;

        OnStart();
    }

    protected virtual void OnStart() { }

    // 物理"不互推"设置：敌人移动全部由 NavMeshAgent / transform 驱动，碰撞体只做实体阻挡（防穿模）。
    // 玩家是 CharacterController：一旦敌人碰撞体被物理穿透解算顶入玩家体积，会把玩家一起顶走。
    protected void SetupNonBlockingPhysics()
    {
        // 敌人主体不允许挂动态刚体：动态刚体在物理更新里会对玩家(CharacterController)施加
        // 冲量/穿透解算，把玩家顶开。若资产误挂了刚体，强制改成 kinematic + 无重力 + 零速度，
        // 让它退化成纯"挡板"（阻挡穿模，不产生任何顶人力）。
        if (TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 实体碰撞保持非 Trigger：保证敌人与玩家/墙体不穿模。
        // 距离由移动侧维持——近身刹停(stoppingDistance/approachBrake)、击退与分离的
        // ClampSeparationToPlayer / FilterSeparationTowardPlayer 保证敌人绝不把碰撞体扫进玩家体积，
        // 因此这块实体挡板在正常走位下不会顶动玩家。

        // NavMeshAgent 用高等级局部避障：成群敌人各自绕开走位，而不是靠碰撞体硬挤出一条路，
        // 避免互相顶推成堆、再把前排推进玩家。
        if (isAgentValid && agent != null)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }
    }

    /// <summary>被击退时调用（子类可重写清理自己的状态，如蓄力/索敌）</summary>
    protected virtual void OnKnockback() { }

    protected virtual void Update()
    {
        // 🔍 攻击结束→恢复追击 跨帧检测：先记录本帧进入前的 isAttacking，用于下一帧判定"攻击刚结束"
        bool wasAttackingThisFrameEnter = wasAttackingLastFrame;
        wasAttackingLastFrame = isAttacking;

        if (isDead)
        {
            StopAgent();
            if (healthBarInstance != null) healthBarInstance.SetActive(false);
            return;
        }

        // 被击退后的硬直：不动、不出手、不追击
        if (isStaggering)
        {
            StopAgent();
            UpdateAnimations(0f, false);
            return;
        }

        if (isAttacking)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackDuration)
            {
                if (logAttackResumeFlow)
                {
                    Debug.Log($"<color=cyan>[AttackEnd]</color> <b>{GetType().Name}</b> name:{name} | " +
                        $"atkTimer:{attackTimer:F2} dur:{attackDuration} → isAttacking:true→false | " +
                        $"agent stopped:{agent!=null&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath}");
                }
                isAttacking = false;
                attackTimer = 0f;
                if (animator != null) animator.SetBool("IsAttacking", false);
                if (attackCoroutine != null)
                {
                    StopCoroutine(attackCoroutine);
                    attackCoroutine = null;
                }
            }
            StopAgent();
            return;
        }

        if (player == null || player.IsDead())
        {
            isChasing = false;
            StopAgent();
            UpdateAnimations(0f, false);
            return;
        }

        if (!canAttack)
        {
            attackCooldownTimer += Time.deltaTime;
            if (logAttackResumeFlow && attackCooldownTimer < 0.05f)
            {
                // 🔍 冷却刚开始：记录冷却起点状态，确认攻击结束后确实进入了冷却
                Debug.Log($"<color=yellow>[CooldownStart]</color> <b>{GetType().Name}</b> name:{name} | " +
                    $"canAtk:false→冷却 cd:{attackCooldownTimer:F2} 目标:{GetAttackCooldown():F2}s | " +
                    $"agent stopped:{agent!=null&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
                    $"vel:{(agent!=null?agent.velocity.magnitude:0f):F2}");
            }
            if (attackCooldownTimer >= GetAttackCooldown())
            {
                canAttack = true;
                attackCooldownTimer = 0f;
                if (logAttackResumeFlow)
                {
                    Debug.Log($"<color=green>[CooldownEnd]</color> <b>{GetType().Name}</b> name:{name} | canAtk:false→true | " +
                        $"agent stopped:{agent!=null&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath}");
                }
            }
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);
        float attackRange = GetAttackActivationRange();

        // 被击退硬直结束后的回位：无论当前是否在攻击激活范围内，先强制走回 agent 停止距离再进攻。
        // 否则被推远后蛇会停在原地：若玩家不特意靠近/退出攻击范围，就永远不再贴回玩家。
        if (forceReturnToRange)
        {
            float stopping = isAgentValid && agent != null ? agent.stoppingDistance : 0f;
            if (distance > stopping)
            {
                isChasing = true;
                HandleMovement();
                ApplyApproachBrake();
                UpdateAnimations(GetCurrentSpeed(), true);
                return;
            }
            forceReturnToRange = false;
        }

        // 进入攻击范围：停住原地转向玩家，能攻则攻，不再前进贴脸
        // （子类可重写 TryPerformInRangeAttack 接管该行为，如蓄力/咏唱）
        // ⭐ 玩家被真墙挡(IsBlockedBetween)时不算"已就位"：停住攻击会挥空、被挡在原地，
        // 改为走下面的追击逻辑，靠 NavMeshAgent 绕行到能看见玩家的位置再攻。
        // 注意：这里用"真墙"判断(IsBlockedBetween)，不用 NavMesh 可达性——墙已 Bake，
        // "能绕到玩家身边"≠"现在能攻击"，被真墙隔开就该继续绕行而不是停住。
        // IsBlockedBetween 自带 ≤1.2m 近身豁免，墙角贴脸(≤1.2m)仍可正常就位攻击。
        if (distance <= attackRange &&
            !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position))
        {
            if (TryPerformInRangeAttack())
                return;
            UpdateAnimations(0f, false);
            return;
        }

        // ----- 根据模式选择追击范围 -----
        float detection, lose;
        if (useDirectChase)
        {
            detection = infiniteDetectionRange;
            lose = infiniteLoseTargetRange;
        }
        else
        {
            detection = detectionRange;
            lose = loseTargetRange;
        }

        if (distance <= detection)
            isChasing = true;
        else if (distance > lose)
            isChasing = false;

        // 追击时若前方有敌人挤成一团，先停下（仅非环形占位时启用兜底排队）
        if (isChasing && !isAttacking && !enableFormation && IsEnemyBlockingForward())
        {
            StopAgent();
            return;
        }

        // 🔍 攻击结束→恢复追击 跨帧检测：上一帧还在攻击(isAttacking)，本帧已走出攻击分支
        if (logAttackResumeFlow && wasAttackingThisFrameEnter && !isAttacking)
        {
            Debug.Log($"<color=cyan>[ResumeChase]</color> <b>{GetType().Name}</b> name:{name} | " +
                $"攻击结束跨帧 → 进入追击/就位流程 | " +
                $"agent stopped:{agent!=null&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
                $"vel:{(agent!=null?agent.velocity.magnitude:0f):F2} canAtk:{canAttack} " +
                $"cd:{attackCooldownTimer:F2} 距离玩家:{(player!=null?Vector3.Distance(transform.position,player.transform.position):-1f):F2}");
        }

HandleMovement();

        // 接近目标提前减速，避免冲出一小段才刹停
        if (isChasing && !isAttacking)
            ApplyApproachBrake();

        // ⭐ 快速重规划：玩家跨过墙角/双方贴墙时，原路径/旧走廊瞬间失效。
        // 靠 TryStuckEscape 要等卡满 0.5s+1.0s 冷却才换路，导致明显延迟顶墙。
        // 墙挡信号取"物理真墙(IsBlockedBetween) OR NavMesh 直线挡(Raycast)"的并集——
        // 有的墙是动态 NavMeshObstacle(遇到才 carve)，NavMesh.Raycast 可能长时间返回"未挡"。
        // ⭐ 敌人贴墙时自身可能半踩 Bake 挖掉的洞：射线/落点一律用 agent 在 NavMesh 上的真实
        // 位置(agent.nextPosition)而不是 transform.position，否则算出的挡位忽真忽假。
        // ⭐ 避免闪烁：正常的"持续隔墙"状态**不**反复 ResetPath——ResetPath 会立刻清掉 hasPath
        // 造成 pending/rem 忽有忽无的抖动。只有三类才整段重算：
        //   ① 边沿(未挡→变挡)进入；② 路径持续 pending 超时(目标仍算不出来)；③ 有有效路径却顶墙停滞。
        // 其余时间靠 SetChaseDestination 每帧喂"本侧可达点"，agent 拿着可达落点沿墙侧走。
        if (isChasing && !isAttacking && isAgentValid && agent != null && agent.isOnNavMesh && player != null)
        {
            Vector3 navPos = Vector3.Lerp(agent.nextPosition, transform.position, 0.5f); // 贴墙时会抖，取中稳一点
            Vector3 toPos = player.transform.position;
            toPos.y = navPos.y;
            NavMeshHit navHit = default;
            bool navBlocked = (navPos - toPos).sqrMagnitude > 0.01f &&
                NavMesh.Raycast(navPos, toPos, out navHit, NavMesh.AllAreas);
            bool physBlocked = WallPenetrationResolve.IsBlockedBetween(navPos, player.transform.position);
            bool wallBlocked = navBlocked || physBlocked;

            bool stuckPushing = agent.hasPath && !agent.pathPending
                && agent.velocity.magnitude <= stuckEscapeSpeed
                && agent.remainingDistance > 0.1f;

            if (logStuckDetection && stuckPushing)
            {
                Debug.Log(
                    $"<color=orange>[StuckDet]</color> <b>{GetType().Name}</b> name:{name} " +
                    $"stuckPushing:true | wallBlocked:{wallBlocked} | " +
                    $"vel:{agent.velocity.magnitude:F3}(≤{stuckEscapeSpeed}) " +
                    $"desiredVel:{agent.desiredVelocity.magnitude:F3} " +
                    $"rem:{agent.remainingDistance:F2}(>0.1) hasPath:{agent.hasPath} pending:{agent.pathPending} " +
                    $"stopped:{agent.isStopped} status:{agent.pathStatus} " +
                    $"| state: chase:{isChasing} atk:{isAttacking} stagger:{isStaggering} dead:{isDead} canAtk:{canAttack}");
            }

            // 隔墙期间：维护"本侧可达目标"，并持续喂给 agent(即使路径仍算不出来，也保证落点可达)。
            if (wallBlocked)
            {
                if (!chaseOverrideActive)
                {
                    chaseOverrideActive = true;
                    chaseOverrideTarget = GetFlankChaseTarget();
                }
                else
                {
                    // 周期重采样落点(玩家/自身挪动后旧点可能不够贴近)，仅当变化超阈值才换，
                    // 避免点抖动连带 ResetPath 闪烁。
                    chaseOverrideRecheckTimer -= Time.deltaTime;
                    if (chaseOverrideRecheckTimer <= 0f)
                    {
                        chaseOverrideRecheckTimer = chaseOverrideRecheckInterval;
                        Vector3 fresh = GetFlankChaseTarget();
                        float move = (fresh - chaseOverrideTarget).sqrMagnitude;
                        if (move > 0.25f) // 0.5m 移动才算变
                        {
                            chaseOverrideTarget = fresh;
                            NotePathMutation("隔墙落点变化>0.5m → ResetPath");
                            agent.ResetPath(); // 落点真变了才清路径
                        }
                    }
                }

                // 路径持续 pending 超时：目标仍算不出(抖动档口)→ 强制吊回本侧点重算一次
                if (agent.pathPending)
                {
                    chasePathPendingTimer += Time.deltaTime;
                    if (chasePathPendingTimer >= chasePathPendingTimeout)
                    {
                        NotePathMutation("pending超时0.6s → ResetPath+SetDest(本侧点)");
                        agent.ResetPath();
                        agent.isStopped = false;
                        agent.SetDestination(chaseOverrideTarget);
                        chasePathPendingTimer = 0f;
                    }
                }
                else
                {
                    chasePathPendingTimer = 0f;
                }

                // 真正需要整段重算的：①边沿进入(首次) ②stuckPushing(有效路径但顶墙)
                // ③hasPath=false(根本没路径可走，重置到本侧点看能不能算成功)。
                bool edgeTrigger = !lastBlockedByWall && wallBlocked;
                bool noPathAtAll = !agent.hasPath && !agent.pathPending;

                if ((edgeTrigger || stuckPushing || noPathAtAll) && fastReplanCooldownTimer <= 0f)
                {
                    NotePathMutation($"重规划触发(edge:{edgeTrigger} stuck:{stuckPushing} noPath:{noPathAtAll}) → ResetPath+SetDest");
                    agent.ResetPath();
                    agent.isStopped = false;
                    agent.SetDestination(chaseOverrideActive ? chaseOverrideTarget : player.transform.position);
                    fastReplanCooldownTimer = fastReplanCooldown;
                }
            }
            else
            {
                chaseOverrideActive = false;
                chasePathPendingTimer = 0f;
            }

            lastBlockedByWall = wallBlocked;
            if (fastReplanCooldownTimer > 0f)
                fastReplanCooldownTimer -= Time.deltaTime;
        }
        else
        {
            // 非追击/未激活时把边沿基准复位，避免上次会话残留 true，
            // 下次追击进入时能正确触发"墙挡由未挡→变挡"的快速重规划。
            lastBlockedByWall = false;
            chaseOverrideActive = false;
            chasePathPendingTimer = 0f;
        }

        // 卡住自救：追击中长时间几乎不动（被屏风/障碍物理挡停，agent 却以为路径仍有效）
        // → 强制 ResetPath 并绕到"墙这边可达"的目标点重寻路
        if (isChasing && !isAttacking)
            TryStuckEscape();

        // 已进入攻击范围（就位/攻击中）不再施加分离位移，避免后排把前排往玩家方向顶出"推一下"
        if (enableSeparation && distance > attackRange)
        {
            ApplySeparation();
        }

        UpdateHealthBarPosition();

        UpdatePathDebug();

        TrackPathState();   // 🔍 hasPath Y/N 闪烁监控（仅 logPathStateChanges 开启时打印）

        float currentSpeed = GetCurrentSpeed();
        bool isMovingState = isChasing && !isAttacking && currentSpeed > 0.05f;
        UpdateAnimations(currentSpeed, isMovingState);
    }

    // ---------- 分离力 ----------

    // 朝玩家方向前方是否有敌人堵路（避免成群把前面的敌人推挤到玩家）
    private bool IsEnemyBlockingForward()
    {
        if (player == null) return false;
        if (enemyLayerMask == 0) enemyLayerMask = LayerMask.GetMask("Enemy");

        Vector3 dir = player.transform.position - transform.position;
        dir.y = 0;
        float dist = dir.magnitude;
        if (dist < 0.01f) return false;
        dir /= dist;

        float range = Mathf.Min(frontEnemyStopRange, dist);
        Collider[] hits = Physics.OverlapSphere(transform.position + dir * range * 0.5f, range, enemyLayerMask);
        foreach (var c in hits)
        {
            if (c == null) continue;
            EnemyAI other = c.GetComponentInParent<EnemyAI>();
            if (other == null || other == this || other.isDead) continue;

            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0;
            if (toOther.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(dir, toOther.normalized) > 0.5f)
                return true;
        }
        return false;
    }

    // ---------- 环形占位 ----------

    // 玩家周围取 formationSlots 个等距占位点，找离自己最近且未被其他敌人占据的空位。
    // 返回 null 表示所有占位点都被占满（调用方应去排队）。
    protected Vector3? GetFormationTarget()
    {
        if (!enableFormation || player == null) return null;
        if (enemyLayerMask == 0) enemyLayerMask = LayerMask.GetMask("Enemy");

        float attackRange = enemyData != null ? enemyData.attackRange : 1.5f;
        float ringR = Mathf.Max(formationRingPadding, attackRange * 0.9f);
        if (ringR <= 0f) return null;

Vector3 center = player.transform.position;
        center.y = transform.position.y;

        int N = Mathf.Max(6, formationSlots);
        Vector3? best = null;
        float bestSq = float.MaxValue;

        for (int i = 0; i < N; i++)
        {
            float ang = i * (Mathf.PI * 2f / N);
            Vector3 slot = center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * ringR;

            // 空位被占 或 朝该空位的小通道被敌人挡着 → 不硬挤，跳过（改去畅通的空位）
            if (IsSlotOccupied(slot)) continue;
            if (IsForwardBlockedTo(slot)) continue;

            // ⭐ 空位在导航网格上不可达（墙/装饰物(NavMeshObstacle)挖洞把空位或路径挡住）→ 跳过。
            // 否则会追向墙侧/墙后的空位，agent 绕不过去表现为"面对玩家不绕开"。
            if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                if (!IsSlotReachable(slot)) continue;
            }

            float d2 = (slot - transform.position).sqrMagnitude;
            if (d2 < bestSq)
            {
                bestSq = d2;
                best = slot;
            }
        }
        return best;
    }

    /// <summary>
    /// ⭐ 空位可达性校验：空位本身须落在导航网格上，
    /// 且从敌人当前位置到空位的导航路径不能被 NavMeshObstacle(装饰物)挖洞 / 墙体阻断。
    /// 被阻断则放弃该空位，避免敌人追向墙侧空位却卡住（表现为面对玩家不绕开）。
    /// </summary>
    private bool IsSlotReachable(Vector3 slot)
    {
        if (agent == null || !agent.isOnNavMesh) return false;

        // 1) 空位是否落在导航网格上（墙内/浮空等不可行点直接排除）
        NavMeshHit nearest;
        if (!NavMesh.SamplePosition(slot, out nearest, 1f, NavMesh.AllAreas))
            return false;

        // 2) 从敌人到空位的导航直线路径是否被阻断（Deco 挖洞 / 墙体）。阻断 → 空位在这侧不可达
        Vector3 from = transform.position;
        Vector3 to = slot;
        from.y = to.y;
        if ((from - to).sqrMagnitude < 0.001f) return true;
        NavMeshHit pathHit;
        if (NavMesh.Raycast(from, to, out pathHit, NavMesh.AllAreas))
            return false;

        return true;
    }

    // 占位点附近是否已有其他存活敌人
    private bool IsSlotOccupied(Vector3 slot)
    {
        Collider[] cols = Physics.OverlapSphere(slot, formationOccupancyRadius, enemyLayerMask);
        foreach (var c in cols)
        {
            if (c == null) continue;
            EnemyAI other = c.GetComponentInParent<EnemyAI>();
            if (other == null || other == this || other.isDead) continue;
            return true;
        }
        return false;
    }

    // 从自身到目标空位的连线上（狭窄走廊内）是否挡着其他敌人。
    // 采用沿路径投影判定：只有在路径中间且横向偏移很小（即真的堵在通道上）才算挡，
    // 避免因侧后方敌人或远处敌人而误判，让敌人能正常绕到畅通空位。
    private bool IsForwardBlockedTo(Vector3 slot)
    {
        Vector3 to = slot - transform.position;
        to.y = 0;
        float dist = to.magnitude;
        if (dist < 0.6f) return false;
        Vector3 dir = to / dist;

        if (enemyLayerMask == 0) enemyLayerMask = LayerMask.GetMask("Enemy");
        Collider[] cols = Physics.OverlapSphere(transform.position, dist + formationOccupancyRadius, enemyLayerMask);
        foreach (var c in cols)
        {
            if (c == null) continue;
            EnemyAI other = c.GetComponentInParent<EnemyAI>();
            if (other == null || other == this || other.isDead) continue;

            Vector3 off = other.transform.position - transform.position;
            off.y = 0;
            float along = Vector3.Dot(off, dir);
            // 只在路径中段 0.2~dist 才算挡（避免堵在紧身后侧或目标点后的敌人）
            if (along < 0.2f || along > dist) continue;
            Vector3 perp = off - dir * along;
            if (perp.magnitude < formationOccupancyRadius)
                return true;
        }
        return false;
    }

    // 所有空位被占满时的兜底：站到"最近一个比我还接近玩家且挡在路上的敌人"身后排队。
    // 找不到则返回 null（调用方按原逻辑直追玩家）。
    protected Vector3? GetQueueTarget()
    {
        if (player == null) return null;
        if (enemyLayerMask == 0) enemyLayerMask = LayerMask.GetMask("Enemy");

        Vector3 center = player.transform.position;
        center.y = transform.position.y;
        float myDist = (transform.position - center).magnitude;

        float checkR = Mathf.Max(myDist + 3f, 8f);
        Collider[] cols = Physics.OverlapSphere(center, checkR, enemyLayerMask);

        EnemyAI nearest = null;
        float bestD = float.MaxValue;
        foreach (var c in cols)
        {
            if (c == null) continue;
            EnemyAI other = c.GetComponentInParent<EnemyAI>();
            if (other == null || other == this || other.isDead) continue;

            Vector3 o = other.transform.position; o.y = transform.position.y;
            if ((o - center).sqrMagnitude >= myDist * myDist) continue; // 只排更接近玩家的敌人后面

            float d2 = (o - transform.position).sqrMagnitude;
            if (d2 < bestD) { bestD = d2; nearest = other; }
        }

        if (nearest == null) return null;

        Vector3 p = nearest.transform.position; p.y = transform.position.y;
        Vector3 away = p - center;
        away.y = 0;
        if (away.sqrMagnitude < 0.0001f) return null;
        away.Normalize();
        return p + away * Mathf.Max(1.2f, formationOccupancyRadius + 0.3f);
    }

    private void ApplySeparation()
    {
        if (myCollider == null) return;

        Collider[] nearbyEnemies = Physics.OverlapSphere(transform.position, separationRadius, enemyLayerMask);
        Vector3 force = Vector3.zero;
        int count = 0;
        float minDist = float.MaxValue;

        foreach (Collider col in nearbyEnemies)
        {
            if (col == null || col.gameObject == gameObject) continue;

            EnemyAI other = col.GetComponentInParent<EnemyAI>();
            if (other == null || other == this || other.isDead) continue;

            Vector3 dir = transform.position - col.transform.position;
            dir.y = 0;
            float dist = dir.magnitude;

            if (dist < separationRadius && dist > 0.001f)
            {
                // 越近排斥力越强（平方曲线，避免远处微扰）
                float strength = 1f - (dist / separationRadius);
                force += dir.normalized * (strength * strength) * separationForce;
                count++;
                if (dist < minDist) minDist = dist;
            }
        }

        if (count > 0)
        {
            // 距离过近时停止追击（保持间距，避免互相推挤着走）
            if (minDist < separationRadius * 0.6f && !isAttacking && !isDead)
            {
                StopAgent();
            }

            separationVelocity = Vector3.Lerp(separationVelocity, force, Time.deltaTime * separationSmoothSpeed);

            // 关键修复：分离位移不允许把敌人推向玩家。
            // 后排推前排时，前排的"指向玩家的分量"会被剔除，只保留横向/向外扩散，
            // 避免敌人被顶到玩家身上、物理解算把玩家一起推挤（玩家是 CharacterController，会被挤走）。
            FilterSeparationTowardPlayer();
            // 封死边缘情况：横向/切向位移也不得把敌人的碰撞体扫进玩家体积（否则橡皮式擦碰仍会推玩家，
            // 甚至把玩家顶到贴墙的 X-Ray 墙里）。以敌人包围球沿移动方向球扫玩家，命中则把位移缩到其表面外。
            ClampSeparationToPlayer();

            if (useDirectChase)
            {
                transform.position += separationVelocity * Time.deltaTime;
            }
            else if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                NotePathMutation("ApplySeparation → agent.Move(分离位移)");
                agent.Move(separationVelocity * Time.deltaTime);
            }
        }
        else
        {
            separationVelocity = Vector3.Lerp(separationVelocity, Vector3.zero, Time.deltaTime * separationSmoothSpeed);
        }
    }

    // 分离速度只在"绕玩家横向/远离玩家"的方向上生效：剔除朝玩家的分量。
    // 否则后排把前排推向玩家时，前排被顶进玩家碰撞体，会把玩家一起推挤着走。
    private void FilterSeparationTowardPlayer()
    {
        if (player == null) return;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distSqr = toPlayer.sqrMagnitude;
        if (distSqr < 0.0001f) return;

        toPlayer /= Mathf.Sqrt(distSqr);
        float inward = Vector3.Dot(separationVelocity, toPlayer);
        if (inward > 0f)
            separationVelocity -= toPlayer * inward;
    }

    // 分离期间避免敌人碰撞体扫入玩家体积：沿本帧位移方向，用敌人包围球向玩家球扫，
    // 若一路会撞进玩家再把位移缩到恰好停在玩家表面外（留小缓冲）。
    // 解决：玩家被横移的蛇身/敌人擦到后，物理解算把玩家顶进贴墙的 X-Ray 墙里推出墙外。
    private void ClampSeparationToPlayer()
    {
        if (player == null || separationVelocity.sqrMagnitude < 1e-6f) return;
        if (myCollider == null) return;

        Collider pc = cachedPlayerCollider;
        if (pc == null)
        {
            cachedPlayerCollider = player.GetComponentInChildren<Collider>();
            pc = cachedPlayerCollider;
        }
        if (pc == null) return;

        float moveLen = separationVelocity.magnitude * Time.deltaTime;
        if (moveLen <= 0f) return;
        Vector3 moveDir = separationVelocity.normalized;

        // 敌人碰撞体近似成包围球（蛇长盒也能包住），从中心向前球扫
        Vector3 from = myCollider.bounds.center;
        float castR = Mathf.Max(myCollider.bounds.extents.magnitude, 0.2f);

        if (Physics.SphereCast(from, castR, moveDir, out RaycastHit hit,
                moveLen + castR + 0.1f, 1 << pc.gameObject.layer))
        {
            float allow = Mathf.Max(hit.distance - castR, 0f);  // 表面距离玩家还有多远可走
            if (allow < moveLen)
            {
                float scale = Mathf.Clamp01(Mathf.Max(allow - 0.05f, 0f) / moveLen);
                separationVelocity *= scale;
            }
        }
    }

    // ---------- 子类需要重写的方法 ----------
    protected abstract void HandleMovement();

    // ---------- 公共方法 ----------
    public void ApplyScalingMultipliers(float speedMult, float healthMult, float damageMult)
    {
        currentSpeedMultiplier = speedMult;
        currentHealthMultiplier = healthMult;
        currentDamageMultiplier = damageMult;
        ApplyCurrentMultipliers();
    }

    /// <summary>
    /// 单独更新速度倍率（用于压力系统实时调整）
    /// </summary>
    public void UpdateSpeedMultiplier(float newSpeedMultiplier)
    {
        currentSpeedMultiplier = newSpeedMultiplier;

        if (isAgentValid && !useDirectChase && agent != null)
        {
            agent.speed = baseSpeed * currentSpeedMultiplier;
        }
    }

    /// <summary>
    /// 获取当前速度倍率
    /// </summary>
    public float GetCurrentSpeedMultiplier()
    {
        return currentSpeedMultiplier;
    }

    protected virtual void ApplyCurrentMultipliers()
    {
        if (enemyData == null) return;

        UpdateHealthBar();

        if (isAgentValid && !useDirectChase && agent != null)
        {
            agent.speed = baseSpeed * currentSpeedMultiplier;
        }
    }

    protected void StopAgent([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (isAgentValid && agent != null && agent.isOnNavMesh)
        {
            NotePathMutation("StopAgent → isStopped=true+vel清零");
            if (logAttackResumeFlow)
            {
                string type = GetType().Name;
                Debug.Log(
                    $"<color=magenta>[StopAgent]</color> <b>{type}</b> name:{name} 来源:{caller} " +
                    $"| state: chase:{isChasing} atk:{isAttacking} stagger:{isStaggering} dead:{isDead} canAtk:{canAttack} " +
                    $"atkTimer:{attackTimer:F2}/{attackDuration:F2} cd:{attackCooldownTimer:F2} " +
                    $"| agent: stopped:{agent.isStopped} hasPath:{agent.hasPath} pending:{agent.pathPending} " +
                    $"status:{agent.pathStatus} vel:{agent.velocity.magnitude:F2} rem:{agent.remainingDistance:F2}"
                );
            }
            agent.isStopped = true;
            agent.velocity = Vector3.zero; // 立即清零残速，避免停下时还滑/推一下
        }
    }

    protected float GetCurrentSpeed()
    {
        return isAgentValid && agent != null ? agent.velocity.magnitude : 0f;
    }

    // 接近目标时按距离平滑减速，避免冲过 stoppingDistance 一点点再刹停
    protected void ApplyApproachBrake()
    {
        if (!enableApproachBrake || agent == null || !agent.isOnNavMesh || player == null) return;
        float baseSpd = baseSpeed * currentSpeedMultiplier;
        if (baseSpd <= 0f) return;

        float sd = agent.stoppingDistance;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0;
        float distance = toPlayer.magnitude;

        // 还没进减速区 → 全速
        if (distance > sd + approachBrakeRange)
        {
            agent.speed = baseSpd;
            return;
        }

        // 已到/超过 stoppingDistance → 用最低速度（趋近“准备停”）
        if (distance <= sd)
        {
            agent.speed = baseSpd * approachStopSpeed;
            return;
        }

        // 减速区内：按剩余距离从全速线性降到最低
        float f = Mathf.Clamp01((distance - sd) / approachBrakeRange);
        agent.speed = baseSpd * Mathf.Lerp(approachStopSpeed, 1f, f);
    }

    /// <summary>
    /// 卡住自救：追击中明明该朝玩家走，却在原地打转/被障碍(屏风等)物理挡停。
    /// 此时 NavMeshAgent 认为路径仍有效不重算，就强制 ResetPath 并重新寻路到玩家。
    /// 额外把目标从"玩家脚下"偏移回"本侧可达点"，避免落点在障碍物正对面导致 NoPath。
    /// </summary>
    protected void TryStuckEscape()
    {
        if (agent == null || !agent.isOnNavMesh || player == null) return;
        if (isAttacking || isStaggering) return;

        // 视野可见且能直接走到（无墙）时不需要自救，等普通寻路即可，避免误触发
        if (!WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position)) return;

        // 已到玩家身边：仅当中间无墙（能真正贴着攻击）才算就位、不必绕行。
        // 相隔一堵实墙（即便距离在攻击激活范围内）仍需自救绕到墙这一侧，
        // 否则会卡在墙对面面向玩家（玩家快速穿墙后敌人常处此态）。
        if (Vector3.Distance(transform.position, player.transform.position) <= GetAttackActivationRange()
            && !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position))
            return;

        if (stuckEscapeCooldownTimer > 0f)
        {
            stuckEscapeCooldownTimer -= Time.deltaTime;
            return;
        }

        float speed = agent.velocity.magnitude;
        bool slow = speed <= stuckEscapeSpeed;

        // 首次记录基准点
        if (!stuckEscapePosValid)
        {
            stuckEscapeLastPos = transform.position;
            stuckEscapePosValid = true;
            return;
        }

        float moved = Vector3.Distance(transform.position, stuckEscapeLastPos);
        stuckEscapeLastPos = transform.position;

        if (slow && moved < 0.02f)
        {
            stuckTimer += Time.deltaTime;
            if (logStuckDetection && stuckTimer >= 0.05f && (stuckTimer < 0.08f || stuckTimer >= stuckEscapeDelay - 0.02f))
            {
                // 🔍 仅在刚起测 和 将触发 两个时刻打印，避免刷屏
                Debug.Log(
                    $"<color=orange>[TryStuck]</color> <b>{GetType().Name}</b> name:{name} " +
                    $"stuckTimer:{stuckTimer:F2}/{stuckEscapeDelay} | " +
                    $"vel:{speed:F3}(≤{stuckEscapeSpeed}) desiredVel:{agent.desiredVelocity.magnitude:F3} " +
                    $"rem:{agent.remainingDistance:F2} stopped:{agent.isStopped} status:{agent.pathStatus} " +
                    $"| state: chase:{isChasing} atk:{isAttacking} stagger:{isStaggering} dead:{isDead} canAtk:{canAttack} " +
                    $"距玩家:{(player!=null?Vector3.Distance(transform.position,player.transform.position):-1f):F2}");
            }
        }
        else
        {
            stuckTimer = 0f;
            return;
        }

        if (stuckTimer >= stuckEscapeDelay)
        {
            stuckTimer = 0f;
            stuckEscapeCooldownTimer = stuckEscapeCooldown;

            NotePathMutation("TryStuckEscape(卡满0.5s) → ResetPath+SetDest(本侧点)");
            agent.ResetPath();
            agent.isStopped = false;
            agent.SetDestination(GetChaseReachableTarget());
        }
    }

    // 追击重寻路目标：优先用 NavMesh.Raycast 返回的"墙边缘可达点"(navHit，在敌人这一侧的 NavMesh
    // 边界上，永远可达、不会在墙对岸)；Raycast 未命中(无墙或极端情况)再回退到"玩家脚下偏移向敌人侧"
    // 的可达 NavMesh 点。避免落点恰在墙对岸导致 NoPath，或每次重算都仍落在墙后继续顶墙。
    protected Vector3 GetChaseReachableTarget(NavMeshHit navHit = default)
    {
        if (player == null) return transform.position + transform.forward * 2f;

        // 墙/障碍边缘是"敌人这一侧"确定可达的点：拿它当绕墙目标，agent 会先走到墙边再顺着绕。
        // 把落点往敌人方向再缩回一点(0.3f)，避免正好压在墙边缘/贴角造成一下顶住。
        if (navHit.hit)
        {
            Vector3 edge = navHit.position;
            Vector3 backFromEdge = transform.position - edge;
            backFromEdge.y = 0f;
            if (backFromEdge.sqrMagnitude > 0.0001f)
            {
                Vector3 towards = edge - backFromEdge.normalized * 0.3f;
                NavMeshHit sample;
                if (NavMesh.SamplePosition(towards, out sample, 1.2f, NavMesh.AllAreas))
                    return sample.position;
            }
            return edge;
        }

        Vector3 target = player.transform.position;
        Vector3 toEnemy = transform.position - player.transform.position;
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude > 0.0001f)
        {
            Vector3 offset = toEnemy.normalized * (agent.stoppingDistance + 0.5f);
            Vector3 probe = player.transform.position + offset;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(probe, out hit, 3f, NavMesh.AllAreas))
                target = hit.position;
        }
        return target;
    }

// 本侧追击目标（解决"玩家贴墙脚半踩进 bake 挖掉洞"导致的 NoPath）:
// 1) 优先从"玩家位置"沿"指向敌人"方向做小步进采样,取第一个落在 NavMesh 上的点——
//   玩家脚半踩进墙洞时,往回走一两步就到敌侧墙边,是该侧离玩家最近的可达点;
// 2) 采样一直失败(极端),再用"垂直两翼横移"(侧滑墙沿线走到墙头绕过去);
// 3) 全失败退 TryStuckEscape 同款"玩家身边本侧点"。
protected Vector3 GetFlankChaseTarget()
{
    if (player == null) return transform.position + transform.forward * 2f;

    Vector3 fromPlayer = transform.position - player.transform.position;
    fromPlayer.y = 0f;
    if (fromPlayer.sqrMagnitude < 0.0001f) return player.transform.position;
    Vector3 dir = fromPlayer.normalized; // 玩家→敌人的水平方向(回退方向)

    // (1) 步进回退采样：从玩家脚底出发往后找第一个可达 NavMesh 点
    Vector3 start = player.transform.position; start.y -= 0.3f; // 规避“玩家 pivot 在洞内/半空”
    for (float d = 0f; d <= 2.5f; d += 0.25f)
    {
        Vector3 probe = player.transform.position + dir * d;
        probe.y = start.y;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(probe, out hit, 0.6f, NavMesh.AllAreas))
        {
            NavMeshHit rayHit;
            // 该点要能直线直达（不要隔着墙取到对岸）
            if (!NavMesh.Raycast(transform.position, hit.position, out rayHit, NavMesh.AllAreas))
                return hit.position;
        }
    }

    // (2) 两翼横移兜底：垂直方向滑出墙沿线
    Vector3 toPlayer = player.transform.position - transform.position;
    toPlayer.y = 0f;
    Vector3 perp = Vector3.Cross(Vector3.up, toPlayer.normalized).normalized;
    float probeDist = Mathf.Max(1.5f, agent.radius + 1.0f);
    Vector3 basePos = transform.position + toPlayer.normalized * 0.5f;
    foreach (float side in new[] { 1f, -1f, 1.5f, -1.5f })
    {
        Vector3 probe = basePos + perp * (probeDist * side);
        NavMeshHit hit;
        if (NavMesh.Raycast(transform.position, probe, out hit, NavMesh.AllAreas)) continue;
        if (NavMesh.SamplePosition(probe, out hit, 2.5f, NavMesh.AllAreas))
            return hit.position;
    }

    // (3) 全能失败 → 退回 TryStuckEscape 同款"玩家身边本侧点"
    return GetChaseReachableTarget();
}

    // 追击每帧设目的地统一走这里：隔墙期间(chaseOverrideActive)用"本侧侧向可达目标"，
    // 否则用调用方给的落点。这样 HandleMovement 每帧 SetDestination 也不会把墙对岸玩家点
    // 写回隔墙侧——那是 NoPath+pending 死循环的源头。
    protected void SetChaseDestination(Vector3 fallbackTarget)
    {
        if (agent == null || !agent.isOnNavMesh) return;
        NotePathMutation("SetChaseDestination → SetDestination(" + (chaseOverrideActive ? "本侧点" : "玩家点") + ")");
        agent.SetDestination(chaseOverrideActive ? chaseOverrideTarget : fallbackTarget);
    }

    // 攻击冷却：读 EnemyType 资产，资产驱动
    protected float GetAttackCooldown()
    {
        return enemyData != null ? enemyData.attackCooldown : 1.5f;
    }

    // 有效攻击触发距离：取"攻击范围"与"agent 停距"较大者，
    // 否则像 Basic(stop 2.5 > attackRange 2) 会停在攻击范围外而永远不攻。
    protected virtual float GetAttackActivationRange()
    {
        float attack = enemyData != null ? enemyData.attackRange : 1.5f;
        float stopping = isAgentValid && agent != null ? agent.stoppingDistance : 0f;
        return Mathf.Max(attack, stopping);
    }

    protected bool IsFacingPlayer()
    {
        if (player == null) return false;
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        Vector3 forward = transform.forward;
        forward.y = 0;
        return Vector3.Angle(forward, dir) <= facingAngleThreshold;
    }

    // 原地旋转面向玩家（不移动，供射程内待机）
    protected void RotateTowardsPlayer(float dt)
    {
        if (player == null) return;
        Vector3 dir = player.transform.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Mathf.Max(dt, 0f));
    }

    // 进入攻击范围时由 Update 调用。默认：停住转向玩家，能攻则攻。
    // 返回 true 表示已处理（Update 不再执行 HandleMovement），子类可重写（如蓄力咏唱）。
    protected virtual bool TryPerformInRangeAttack()
    {
        RotateTowardsPlayer(Time.deltaTime);
        StopAgent();
        if (canAttack && IsFacingPlayer())
        {
            PerformAttack();
            return true;
        }
        return false;
    }

    protected virtual void PerformAttack()
    {
        if (!canAttack || isDead || isAttacking) return;
        if (logAttackResumeFlow)
        {
            Debug.Log($"<color=green>[AttackStart]</color> <b>{GetType().Name}</b> name:{name} | " +
                $"canAtk:{canAttack} isAttacking(前):{isAttacking} atkTimer:{attackTimer:F2} dur:{attackDuration} " +
                $"| agent stopped:{agent!=null&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
                $"vel:{(agent!=null?agent.velocity.magnitude:0f):F2}");
        }
        canAttack = false;
        attackCooldownTimer = 0f;
        isAttacking = true;
        attackTimer = 0f;
        StopAgent();
        if (animator != null)
        {
            animator.SetBool("IsAttacking", true);
            animator.SetTrigger("Attack");
        }
        PlayAttackSFX();
        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(DelayedDamage());
    }

    protected virtual IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);
        if (player != null && !player.IsDead() && !isDead)
        {
            float dist = Vector3.Distance(transform.position, player.transform.position);
            float attackRangeValue = GetAttackActivationRange();
            // 不穿墙：敌人到玩家之间被竖直墙体(实墙)挡就伤害不到；
            // 用真墙 IsBlockedBetween（自带 ≤1.2m 近身豁免），墙角贴脸不误判，
            // 但真隔墙(>1.2m 且中间有墙)时宁可不中，也不产生隔墙攻击。
            if (dist <= attackRangeValue && IsFacingPlayer() &&
                !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position))
            {
                float finalDamage = baseAttackDamage * currentDamageMultiplier;
                player.TakeDamage(Mathf.RoundToInt(finalDamage));
            }
        }
    }

    // ---------- 血条 ----------
    protected virtual void CreateHealthBar()
    {
        if (healthBarPrefab == null) return;
        healthBarInstance = Instantiate(healthBarPrefab, transform.position + healthBarOffset, Quaternion.identity);
        healthBarInstance.transform.SetParent(transform);

        // 血条 Canvas 排序提到墙（X-Ray 半透明墙）之上，避免血条在墙后时被透明墙混合而"变透明"
        Canvas hpCanvas = healthBarInstance.GetComponentInChildren<Canvas>();
        if (hpCanvas != null) hpCanvas.sortingOrder = 10;

        healthSlider = healthBarInstance.GetComponent<Slider>();
        if (healthSlider == null) healthSlider = healthBarInstance.GetComponentInChildren<Slider>();
        if (healthSlider != null)
        {
            Transform fill = healthSlider.transform.Find("Fill Area/Fill");
            if (fill != null) healthFillImage = fill.GetComponent<Image>();
        }
        if (healthFillImage == null)
        {
            Image[] imgs = healthBarInstance.GetComponentsInChildren<Image>();
            foreach (var img in imgs)
            {
                if (img.transform.parent != null && img.transform.parent.name.Contains("Fill"))
                {
                    healthFillImage = img;
                    break;
                }
            }
            if (healthFillImage == null && healthSlider != null)
                healthFillImage = healthSlider.GetComponentInChildren<Image>();
        }
        UpdateHealthBar();
    }

    protected void UpdateHealthBarPosition()
    {
        if (healthBarInstance != null)
        {
            healthBarInstance.transform.localPosition = healthBarOffset;
            if (Camera.main != null)
            {
                healthBarInstance.transform.LookAt(Camera.main.transform);
                healthBarInstance.transform.Rotate(0, 180, 0);
            }
        }
    }

    protected void UpdateHealthBar()
    {
        if (healthSlider == null) return;
        // 使用生成时锁定的 maxHealth，后续难度升级不会改变已生成敌人的血量显示
        float percent = maxHealth > 0 ? currentHealth / maxHealth : 0f;
        healthSlider.value = percent;
        if (healthFillImage != null)
        {
            Color color;
            if (percent >= 0.6f) color = fullHealthColor;
            else if (percent >= 0.3f) color = midHealthColor;
            else color = lowHealthColor;
            healthFillImage.color = color;
        }
    }

    public void TakeDamageImmediate(int damage)
    {
        if (isDead) return;

        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }
        PlayHitSFX();

        StartCoroutine(SmoothDamage(damage));
    }

    // 受击击退：平滑推出一段距离（SmoothStep 缓动，避免瞬移）
    private Coroutine knockRoutine;
    public void AddKnockback(Vector3 dir, float distance)
    {
        if (isDead) return;
        if (knockRoutine != null) StopCoroutine(knockRoutine);
        isStaggering = false;   // 打断残留硬直，避免新击退被旧 isStaggering 卡死

        // 被击退立即中断当前攻击，避免攻击/蓄力状态残留导致击退后卡住
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }
        isAttacking = false;
        attackTimer = 0f;
        if (animator != null) animator.SetBool("IsAttacking", false);
        OnKnockback();

        // 击退方向：始终以"远离玩家"为主（保证玩家能追踪到、打得到），
        // 只轻微掺少量"敌人自身后方"的朝向成分避免侧移太违和。
        // 不能只靠敌人自身朝向——蛇蓄力时转向，其"后方"有时会对着玩家，导致击退方向反了玩家打不到。
        Vector3 away = dir; away.y = 0;
        if (player != null)
        {
            Vector3 p = transform.position - player.transform.position;
            p.y = 0;
            if (p.sqrMagnitude > 0.0001f) away = p.normalized;
        }

        Vector3 back = -transform.forward; back.y = 0;
        Vector3 final = away;
        if (back.sqrMagnitude > 0.0001f)
            final = Vector3.Slerp(away.normalized, back, Mathf.Clamp01(knockbackOwnBackBlend));
        final.y = 0;
        if (final.sqrMagnitude < 0.0001f) return;

        knockRoutine = StartCoroutine(KnockbackRoutine(final.normalized, distance));
    }

    IEnumerator KnockbackRoutine(Vector3 dir, float distance)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f || distance <= 0f) { knockRoutine = null; yield break; }

        // 整个击退过程（移动+硬直）都屏蔽追击/攻击：移动阶段若 isStaggering=false，
        // Update 会照常进入攻击态，硬直结束后残留攻击状态导致"偶尔又停顿"。
        isStaggering = true;

        Vector3 mv = dir.normalized;
        float duration = Mathf.Clamp(distance * 0.12f, 0.1f, 0.4f);
        float t = 0f;
        float moved = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            float newMoved = Mathf.Lerp(0f, distance, k);
            float step = newMoved - moved;
            moved = newMoved;

            if (agent != null && agent.isOnNavMesh)
            {
                NotePathMutation("击退 → agent.Move(强制位移)");
                agent.Move(mv * step);
            }
            else transform.position += mv * step;

            yield return null;
        }
        knockRoutine = null;

        // 击退完成后原地停顿（硬直）一下（此处 isStaggering 已为 true，仅等待计时）
        yield return new WaitForSeconds(staggerDuration);
        isStaggering = false;

        // 硬直结束：恢复移动状态，避免停在原地不再追击
        // （击退可能把 agent 推到 NavMesh 外或使其 isStopped，这里统一复位）
        if (isDead) yield break;
        // 被击退后强制先走回 agent 停止距离再攻击，防击退后仍在攻击范围内就原地站定
        isChasing = true;
        forceReturnToRange = true;
        ResumeChaseAfterStagger();
    }

    // 击退硬直结束后的恢复：重新进入追击（若玩家仍在检测范围内），
    // 并复位 agent 的停止/寻路状态，确保不会因击退、停止移动或状态切换而卡住。
    protected virtual void ResumeChaseAfterStagger()
    {
        if (player == null || player.IsDead()) return;

        // 击退可能把 agent 推到 NavMesh 外（长蛇身/贴墙），先拉回 NavMesh 再继续寻路，
        // 否则 SetDestination 失效，敌人会原地站定永远不再靠近玩家。
        if (isAgentValid && agent != null && !agent.isOnNavMesh)
        {
            // 多级搜索：先在原地附近找，找不到再以玩家位置附近兜底（蛇可能被推远/卡进墙角）
            Vector3 restore = transform.position;
            NavMeshHit hit;
            bool found = NavMesh.SamplePosition(restore, out hit, 5f, NavMesh.AllAreas);
            if (!found && player != null)
            {
                Vector3 towardPlayer = player.transform.position - transform.position;
                towardPlayer.y = 0;
                if (towardPlayer.sqrMagnitude > 0.0001f)
                {
                    Vector3 dir = towardPlayer.normalized;
                    for (float r = 6f; r <= 16f && !found; r += 5f)
                    {
                        Vector3 probe = transform.position + dir * r;
                        found = NavMesh.SamplePosition(probe, out hit, 5f, NavMesh.AllAreas);
                        if (found) restore = hit.position;
                    }
                }
                if (!found)
                    found = NavMesh.SamplePosition(player.transform.position, out hit, 8f, NavMesh.AllAreas);
            }
            if (found) restore = hit.position;
            NotePathMutation("击退恢复 off-NavMesh → agent.Warp(拉回)");
            agent.Warp(restore);
        }

        float dist = Vector3.Distance(transform.position, player.transform.position);
        float detection, lose;
        if (useDirectChase)
        {
            detection = infiniteDetectionRange;
            lose = infiniteLoseTargetRange;
        }
        else
        {
            detection = detectionRange;
            lose = loseTargetRange;
        }

        if (dist <= detection)
            isChasing = true;
        else if (dist > lose)
            isChasing = false;

        if (isAgentValid && agent != null && agent.isOnNavMesh)
        {
            if (logAttackResumeFlow)
            {
                Debug.Log($"<color=cyan>[ResumeChaseAfterStagger]</color> <b>{GetType().Name}</b> name:{name} | " +
                    $"硬直结束 → isStopped:true→false + ResetPath + SetDest | " +
                    $"chase:{isChasing} atk:{isAttacking} stagger:{isStaggering} canAtk:{canAttack} " +
                    $"距玩家:{(player!=null?Vector3.Distance(transform.position,player.transform.position):-1f):F2}");
            }
            NotePathMutation("ResumeChaseAfterStagger → isStopped=false+ResetPath+SetDest");
            agent.isStopped = false;
            agent.ResetPath();
            SetChaseDestination(player.transform.position);
        }
    }

    protected virtual IEnumerator SmoothDamage(int damage)
    {
        float duration = 0.2f;
        float elapsed = 0f;
        float start = currentHealth;
        float target = Mathf.Max(currentHealth - damage, 0);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            currentHealth = Mathf.Lerp(start, target, elapsed / duration);
            UpdateHealthBar();
            yield return null;
        }
        currentHealth = target;
        UpdateHealthBar();
        if (currentHealth <= 0) Die();
    }

    protected virtual void Die()
    {
        isDead = true;
        if (healthBarInstance != null) healthBarInstance.SetActive(false);
        StopAgent();
        PlayDeathSFX();
        if (animator != null)
        {
            animator.SetBool("IsMoving", false);
            animator.SetTrigger("Die");
        }

        // 保留尸体的实体碰撞体（玩家仍会被拦住），只禁用 NavMeshAgent：
        // 尸体是静态碰撞（无 agent 驱动），不会被后续敌人物理推动，也就不会把尸体顶到玩家身上
        if (agent != null)
        {
            NotePathMutation("Die → agent.enabled=false");
            agent.enabled = false;
        }

        if (ownerTile != null)
        {
            ownerTile.UnregisterEnemy(gameObject);
        }

        int baseCoin = enemyData != null ? enemyData.coinReward : 10;
        SpawnCoin(baseCoin);

        // ⭐ 死亡时累计击杀数，每 buffKillInterval 个必定掉落一个随机 Buff（像金币一样弹出，拾取方式不变）
        if (RandomBuffSpawner.Instance != null)
        {
            RandomBuffSpawner.Instance.OnEnemyKilled(transform.position);
        }
        if (player != null) player.AddKill();

        float delay = enemyData != null ? enemyData.deathAnimationDelay : 2f;
        Destroy(gameObject, delay);
    }

    protected virtual void SpawnCoin(int amount)
    {
        if (coinPrefab == null) return;
        for (int i = 0; i < amount; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0.5f, Random.Range(-0.3f, 0.3f));
            GameObject coin = Instantiate(coinPrefab, transform.position + offset, Quaternion.identity);
            Coin coinScript = coin.GetComponent<Coin>();
            if (coinScript != null) coinScript.SetValue(1);
        }
    }

    protected void UpdateAnimations(float speed, bool isMoving)
    {
        if (animator == null || isAttacking) return;

        animator.SetFloat("Speed", speed);
        animator.SetBool("IsMoving", isMoving);
        if (hasIsChasingParam) animator.SetBool("IsChasing", isChasing);
    }

    // ---------- 敌人音效（走 AudioManager 播放池） ----------

    protected void PlayHitSFX()
    {
        if (hitSFX == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySFX(hitSFX, transform.position);
    }

    protected void PlayAttackSFX()
    {
        if (attackSFX == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySFX(attackSFX, transform.position);
    }

    protected void PlayDeathSFX()
    {
        if (deathSFX == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySFX(deathSFX, transform.position);
    }

    protected void IdleRotation()
    {
        idleRotationTimer += Time.deltaTime;
        if (idleRotationTimer >= idleRotationInterval)
        {
            idleRotationTimer = 0f;
            idleRotationInterval = Random.Range(2f, 6f);
            targetIdleRotation = Quaternion.Euler(0, Random.Range(-30f, 30f), 0) * transform.rotation;
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, targetIdleRotation, 0.5f * Time.deltaTime);
    }

    protected virtual void OnDestroy()
    {
        if (ownerTile != null && !isDead)
        {
            ownerTile.UnregisterEnemy(gameObject);
        }
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (enemyData == null || !enemyData.showGizmos) return;
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, loseTargetRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, enemyData.attackRange);
        Gizmos.color = Color.blue;
        Vector3 fwd = transform.forward;
        Quaternion left = Quaternion.Euler(0, -facingAngleThreshold, 0);
        Quaternion right = Quaternion.Euler(0, facingAngleThreshold, 0);
        Gizmos.DrawLine(transform.position, transform.position + left * fwd * 2f);
        Gizmos.DrawLine(transform.position, transform.position + right * fwd * 2f);

        if (enableSeparation)
        {
            Gizmos.color = new Color(0, 1, 0, 0.15f);
            Gizmos.DrawWireSphere(transform.position, separationRadius);
        }
    }

    // 🔍 记录"最后是谁动了 agent 路径/停止/移动"：每个调用点在动作前调用，便于追踪 Y→N 来源。
    protected void NotePathMutation(string source)
    {
        lastPathMutation = source;
        if (logPathStateChanges)
            Debug.Log($"[{Time.time:F2}] {name} 路径操作 ← {source} | hasPath:{agent!=null&&agent.hasPath} pending:{agent!=null&&agent.pathPending} isStopped:{agent!=null&&agent.isStopped}");
    }

    // 供外部组件(EnemyCollisionBlocker 等)记录路径变更来源
    public void NotePathMutationExternal(string source)
    {
        NotePathMutation(source);
    }

    // 🔍 供外部组件诊断用：当前行为状态
    public bool IsStaggeringNow { get { return isStaggering; } }
    public bool IsAgentValidNow { get { return isAgentValid; } }

    // 🔍 每帧对比 hasPath/pathPending，变化瞬间打印完整快照 + 最后调用来源。
    // 目标：定位第一次 hasPath Y→N 到底是 ResetPath / SetDestination 重算 / Stop / Warp / Carve。
    private void TrackPathState()
    {
        if (!logPathStateChanges || agent == null) return;

        bool hp = agent.hasPath;
        bool pd = agent.pathPending;

        // 首次运行：建立基准，不打印
        if (!lastTrackedHasPath && !lastTrackedPending && !hp && !pd)
        {
            lastTrackedHasPath = hp;
            lastTrackedPending = pd;
            return;
        }

        bool changed = (hp != lastTrackedHasPath) || (pd != lastTrackedPending);
        if (changed)
        {
            string pathStatus = "N/A";
            string steer = "N/A";
            string rem = "N/A";
            if (agent.isOnNavMesh)
            {
                pathStatus = agent.pathStatus.ToString();
                if (agent.hasPath)
                {
                    rem = agent.remainingDistance.ToString("F2");
                    if (!agent.pathPending) steer = agent.steeringTarget.ToString("F1");
                }
            }
            Debug.Log($"[{Time.time:F2}] {name} hasPath Y→N ← 变化 | " +
                $"hasPath:{hp} pending:{pd} status:{pathStatus} rem:{rem} | " +
                $"isStopped:{agent.isStopped} enabled:{agent.enabled} onNavMesh:{agent.isOnNavMesh} | " +
                $"dest:{agent.destination} steer:{steer} override:{chaseOverrideActive} | " +
                $"最后调用:{lastPathMutation} | 玩家:{player?.transform.position}");
        }
        lastTrackedHasPath = hp;
        lastTrackedPending = pd;
    }

    // 🔍 追击路径诊断：showPathDebug 开启时，在敌人头顶显示实时
    //   墙挡(IsBlockedBetween)|hasPath|pathPending|pathStatus|remainingDistance，
    //   用于定位"玩家隔墙、敌人却顶墙不绕行"时到底是哪一环（路径是否有效/是否在重算/是否停滞）。
    private void UpdatePathDebug()
    {
        if (!showPathDebug || isDead)
        {
            if (pathDebugText != null) { Destroy(pathDebugText.gameObject); pathDebugText = null; }
            return;
        }

        if (pathDebugText == null)
        {
            GameObject go = new GameObject("PathDebugText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 2.4f;
            pathDebugText = go.AddComponent<TextMesh>();
            pathDebugText.characterSize = 0.12f;
            pathDebugText.anchor = TextAnchor.MiddleCenter;
            pathDebugText.alignment = TextAlignment.Center;
            pathDebugText.fontSize = 40;
        }

        // 0.1s 节流：避免每帧拼字符串/改材质
        pathDebugUpdateTimer -= Time.deltaTime;
        if (pathDebugUpdateTimer > 0f) return;
        pathDebugUpdateTimer = pathDebugUpdateInterval;

        bool blocked = player != null &&
            WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position);

        string status = "N/A";
        string hasPath = "N/A";
        string pending = "N/A";
        string remDist = "N/A";
        string dest = "N/A";
        string steer = "N/A";
        if (agent != null && agent.isOnNavMesh)
        {
            hasPath = agent.hasPath ? "Y" : "N";
            pending = agent.pathPending ? "Y" : "N";
            status = agent.pathStatus.ToString();
            if (agent.hasPath) remDist = agent.remainingDistance.ToString("F2");
            dest = agent.destination.ToString("F1");
            // steeringTarget = 当前沿路径要去的拐点，用来验证"移动方向是否真的跟 NavMesh 绕行路"，
            // 而非只是目标点换了。追击中若 steeringTarget 贴着墙拐弯而 transform 直朝玩家，就是方向覆盖。
            if (agent.hasPath && agent.pathPending == false)
                steer = agent.steeringTarget.ToString("F1");
        }

        pathDebugText.text = "墙挡:" + (blocked ? "✔" : "✘")
            + "  hasPath:" + hasPath
            + "  pending:" + pending
            + "\n" + status
            + "  rem:" + remDist
            + "\n" + (player != null ? "玩家:" + player.transform.position.ToString("F1") : "无玩家")
            + "  dest:" + dest
            + "\nsteer:" + steer
            + "  override:" + (chaseOverrideActive ? "本侧点" : "玩家点");
        pathDebugText.color = blocked ? Color.yellow : Color.white;
    }
}