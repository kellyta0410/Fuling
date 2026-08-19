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

    [Header("攻击就位判定带")]
    [Tooltip("敌人距离 > attackRange 但 ≤ attackRange×该倍数 时算\"半就位\"：先原地转向玩家并继续压近，而不是反复进出攻击圈\"贴脸却不出手\"。普通/简单模式敌群贴脸卡住时加大到 1.2~1.5")]
    public float attackRangeSlackMultiplier = 1.2f;

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

    // ⭐ 恢复宽限期：agent 刚从"停止(攻击/硬直/StopAgent)"恢复移动时，
    // NavMeshAgent 需要几帧才能重新算路径并建立 velocity。此期间 velocity≈0 是正常现象，
    // 不作卡住判定，避免攻击结束后被误判 stuck → ResetPath 卡住。
    private float stuckGraceTimer = 0f;
    private const float stuckGraceDuration = 0.6f;
    // 卡住判定另外要求 desiredVelocity 有明显移动需求（agent 确实想走才算卡住）
    private const float stuckMinDesiredSpeed = 0.15f;
    private bool wasAgentStopped = false;

    // ⭐ 保守卡住恢复（偶发漏网）：TryStuckEscape 要求"视线被真墙挡(IsBlockedBetween)"才会自救，
    // 但少数 enemy 卡住时视线并没有被墙直挡（Carve 角落、夹在障碍缝里、或 path 已失效），
    // 那类永远等不到自救。这里补一层更宽的自救：
    //   · 只看"该动却没动"——正在追击、无攻击/硬直、isStopped=false、还有移动需求，
    //     却连续 stallRecoveryDelay 秒没产生位移 → 触发一次安全重寻路到可达追击点。
    //   · 触发后有较长冷却防抖，绝不做"每帧 Repath"。
    //   · 复用 GetChaseReachableTarget()（墙边缘可达点 / 玩家侧偏移可达点），不把目标强设回玩家脚下。
    private float stallRecoveryTimer = 0f;
    private float stallRecoveryCooldownTimer = 0f;
    private const float stallRecoveryDelay = 1.2f;     // 连续多久"该动没动"才算卡住（比 stuckEscapeDelay 更保守）
    private const float stallRecoveryCooldown = 2.5f;  // 触发后冷却，避免 Repath→Repath→Repath

    // ：绕墙追击已整体交给 NavMeshAgent 自行寻路移动，
    // 原有的"快速重规划/本侧可达点覆盖"逻辑会造成 hasPath 闪烁与停在墙边不去绕行，已移除。
    // 真正的物理卡住统一由 TryStuckEscape(含恢复宽限期)兜底。

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

    // 🔍 绕路 Path 诊断：周期性打印 agent 实际 Path corners + 一次性 NavMesh.CalculatePath 对照，
    // 用于确认"墙挡时 agent 到底有没有生成绕路路径"（A/B/C/D 判定）
    [Tooltip("🔍 绕路 Path 诊断：打印 agent.path.corners 与 NavMesh.CalculatePath 对照，确认是否生成绕路路径")]
    public bool logPathCorners = false;
    private float pathCornerLogTimer = 0f;
    private const float pathCornerLogInterval = 0.25f;   // 0.25s 一条，够看又不太吵
    private NavMeshPath calcComparePath;                  // 复用缓冲，避免每帧分配

    // 🔍 脱离 NavMesh 追踪：记录第一次 isOnNavMesh:true→false 的瞬间及其来源。
    // 只追踪、不改任何寻路逻辑。用快照避免在 isOnNavMesh=false 时读 isStopped/velocity 等
    // 会抛 "IsStopped can only be called on an active agent that has been placed on a NavMesh" 的属性。
    [Tooltip("🔍 脱离 NavMesh 诊断：isOnNavMesh 首次 true→false 时打印一次快照 + 最后路径操作来源")]
    public bool logNavMeshLoss = false;
    private bool lastTrackedOnNavMesh = true;            // 上一帧记录的 isOnNavMesh
    private bool navMeshLossLogged = false;              // 同一段脱离只打一次
    private string navMeshLossLastMutation = "none";     // 脱离前最后一次路径操作的来源
    private Vector3 navMeshLossLastPos;                  // 脱离前的位置
    private string navMeshLossLastState = "";            // 脱离前 chase/attack/stagger 状态

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
        // 🔍 脱离 NavMesh 追踪：放在最前，任何 return 分支都不会跳过它。
        // 先缓存"本帧 agent 在 NavMesh 上"时的状态；当首帧观察到 isOnNavMesh=false，
        // 用上一帧缓存快照打印（避免在脱离帧读 isStopped/velocity/destination 抛错）。
        TrackNavMeshLoss();

        // 🔍 绕路 Path 诊断：放在 Update 最前面，任何 return 分支都不会跳过它
        //（dead/stagger/attacking/就位/挡路 等所有状态都能打印 agent 当前路径与 CalculatePath 对照）
        LogPathCorners();

        // 🔍 攻击结束→恢复追击 跨帧检测：先记录本帧进入前的 isAttacking，用于下一帧判定"攻击刚结束"
        bool wasAttackingThisFrameEnter = wasAttackingLastFrame;
        wasAttackingLastFrame = isAttacking;

        // ⭐ Stuck 恢复宽限：每帧（无论死/硬直/攻击）记录 agent 停止状态。
        // 当 agent 由"停止(攻击/硬直/StopAgent)"→"恢复移动(isStopped=false)"的瞬间，
        // 进入宽限期：velocity 需要几帧才能由 NavMeshAgent 重建，宽限期内不判 stuck。
        bool stoppedNow = isAgentValid && agent != null && agent.isOnNavMesh && agent.isStopped;
        if (!stoppedNow && wasAgentStopped && !isDead && !isStaggering && !isAttacking)
            stuckGraceTimer = stuckGraceDuration;
        wasAgentStopped = stoppedNow;

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
                        $"agent stopped:{agent!=null&&agent.isOnNavMesh&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath}");
                }
                isAttacking = false;
                attackTimer = 0f;
                // ⭐ 攻击结束：立即进入 stuck 恢复宽限期，避免"刚恢复移动 velocity≈0"的
                // 头几帧被 Stuck Detector 误判 → ResetPath 卡住
                stuckGraceTimer = stuckGraceDuration;
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
                    $"agent stopped:{agent!=null&&agent.isOnNavMesh&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
                    $"vel:{(agent!=null?agent.velocity.magnitude:0f):F2}");
            }
            if (attackCooldownTimer >= GetAttackCooldown())
            {
                canAttack = true;
                attackCooldownTimer = 0f;
                if (logAttackResumeFlow)
                {
                    Debug.Log($"<color=green>[CooldownEnd]</color> <b>{GetType().Name}</b> name:{name} | canAtk:false→true | " +
                        $"agent stopped:{agent!=null&&agent.isOnNavMesh&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath}");
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
        // applyCloseCombatExemption=false：敌我贴薄墙(≤1.2m)时也认真判墙，
        // 中间有实墙(如 XRay 薄墙)就不算"已就位"，继续走 NavMesh steering 绕墙，
        // 不再出现"停下转身面向墙后玩家、却不去绕墙"的卡位。
        if (distance <= attackRange &&
            !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position, applyCloseCombatExemption: false))
        {
            if (TryPerformInRangeAttack())
                return;
            UpdateAnimations(0f, false);
            return;
        }

        // ⭐ 半就位带：距离 > attackRange 但 ≤ attackRange×attackRangeSlackMultiplier 时，
        // 不算"攻击圈外"（否则 separation 会把敌群在攻击边界反复推挤、贴脸却不出手），
        // 但也不停死攻击——先原地转向玩家，再继续走上面的追击/压近流程，进入严格距离后才出手。
        if (distance <= attackRange * attackRangeSlackMultiplier &&
            !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position, applyCloseCombatExemption: false))
        {
            RotateTowardsPlayer(Time.deltaTime);
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
                $"agent stopped:{agent!=null&&agent.isOnNavMesh&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
                $"vel:{(agent!=null?agent.velocity.magnitude:0f):F2} canAtk:{canAttack} " +
                $"cd:{attackCooldownTimer:F2} 距离玩家:{(player!=null?Vector3.Distance(transform.position,player.transform.position):-1f):F2}");
        }

HandleMovement();

        // 接近目标提前减速，避免冲出一小段才刹停
        if (isChasing && !isAttacking)
            ApplyApproachBrake();

        // ⭐ 绕墙追击：寻路与移动全部交给 NavMeshAgent 自己完成。
        // SetChaseDestination(玩家/环形占位点) 已在 HandleMovement 每帧设置目的地，
        // NavMeshAgent 会自行在有墙/装饰物(NavMeshObstacle carve)时算出绕行路径并沿 steering 走。
        // 这里**不再**做任何 ResetPath / 强制目标覆盖 / 快速重规划：
        //   ① 之前"隔墙时把目的地换成两侧可达点"反而让 agent 永远停在墙边不去绕墙；
        //   ② 每帧 ResetPath 会清掉 agent 刚算好的绕行路径(hasPath 闪烁)，正是"顶墙不绕"的根源；
        //   ③ 真正的物理卡住由 TryStuckEscape(0.5s+冷却)兜底，那里带了攻击/硬直恢复宽限期。
        // （仅保留攻击期间的日志跨帧检测用 wasAttacking 状态，见上方）

        // 卡住自救：追击中长时间几乎不动（被屏风/障碍物理挡停，agent 却以为路径仍有效）
        // → 强制 ResetPath 并绕到"墙这边可达"的目标点重寻路
        if (isChasing && !isAttacking)
        {
            TryStuckEscape();
            // ⭐ 保守补层：TryStuckEscape 要求"视线被真墙挡"才自救，这里补"该动却没动"的偶发卡住
            //（Carve 角落/夹缝/路径失效等视线检测恒为 false 的情况），复用同一可达追击点逻辑。
            TryStallRecovery();
        }

        // 已进入攻击范围 / 半就位带内不再施加分离位移，避免后排把前排往玩家方向顶出"推一下"、
        // 或把"半就位"的敌人从攻击判定带里推回攻击圈外反复震荡
        if (enableSeparation && distance > attackRange * attackRangeSlackMultiplier)
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

        // ⭐ 环半径按"实际攻击生效距离"折算：蛇等长身体敌人的 attackRange(4m) 只是根到玩家的
        // 原始人为设定，真实够到距离由 GetAttackActivationRange(蛇=蛇头可达范围) 决定。
        // 否则蛇会绕 3.6m 大圈往返占位，而攻击生效只有 2.17m → 停在大圈上永远不咬，
        // 只有玩家走近(≈2.2m)被"推"到才出手。
        float attackRange = GetAttackActivationRange();
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
            }
        }

        if (count > 0)
        {
            separationVelocity = Vector3.Lerp(separationVelocity, force, Time.deltaTime * separationSmoothSpeed);

            // 关键修复：分离位移不允许把敌人推向玩家。
            // 后排推前排时，前排的"指向玩家的分量"会被剔除，只保留横向/向外扩散，
            // 避免敌人被顶到玩家身上、物理解算把玩家一起推挤（玩家是 CharacterController，会被挤走）。
            FilterSeparationTowardPlayer();
            // 封死边缘情况：横向/切向位移也不得把敌人的碰撞体扫进玩家体积（否则橡皮式擦碰仍会推玩家，
            // 甚至把玩家顶到贴墙的 X-Ray 墙里）。以敌人包围球沿移动方向球扫玩家，命中则把位移缩到其表面外。
            ClampSeparationToPlayer();

            // ⭐ 分离只作小幅修正，不可破坏 NavMesh 寻路：
            //   - 不再因距离过近而 StopAgent（那会让敌人停在原地且 velocity 清零，诱发射击判定/stuck 误判）；
            //   - 单帧位移封顶(≈agent半径的一半，最多0.3m)，避免把 agent 推出 NavMesh 或一路硬顶。
            //     agent.Move 相对移动不重算/不清路径，配合封顶位移不会产生 hasPath 抖动。
            Vector3 delta = separationVelocity * Time.deltaTime;
            delta.y = 0f;
            float maxDelta = Mathf.Clamp((agent != null ? agent.radius : 0.25f) * 0.5f, 0.08f, 0.3f);
            if (delta.sqrMagnitude > maxDelta * maxDelta)
                delta = delta.normalized * maxDelta;
            if (delta.sqrMagnitude < 1e-6f) return;

            if (useDirectChase)
            {
                transform.position += delta;
            }
            else if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                NotePathMutation("ApplySeparation → agent.Move(分离位移)");
                agent.Move(delta);
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
    ///
    /// ⭐ 恢复宽限：agent 刚从 攻击/硬直/StopAgent 恢复移动时，velocity 需要几帧才建立，
    /// 期间不算卡住，避免攻击结束后被误判 stuck → ResetPath 卡住。
    /// </summary>
    protected void TryStuckEscape()
    {
        if (agent == null || !agent.isOnNavMesh || player == null) return;
        if (isAttacking || isStaggering) return;

        // ⭐ 恢复宽限：刚由"停止(攻击/硬直/StopAgent)"过渡到"恢复移动"的宽限期
        // 由 Update 顶部统一检测并赋值 stuckGraceTimer，这里只消费：
        if (stuckGraceTimer > 0f)
        {
            stuckGraceTimer -= Time.deltaTime;
            return; // 宽限期内不作卡住判定
        }

        // 视野可见且能直接走到（无墙）时不需要自救，等普通寻路即可，避免误触发
        // applyCloseCombatExemption=false：贴薄墙(≤1.2m)被实墙隔开也算被挡，
        // 卡在墙对面面向玩家的敌人才能触发自救绕墙。
        if (!WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position, applyCloseCombatExemption: false)) return;

        // 已到玩家身边：仅当中间无墙（能真正贴着攻击）才算就位、不必绕行。
        // 相隔一堵实墙（即便距离在攻击激活范围内）仍需自救绕到墙这一侧，
        // 否则会卡在墙对面面向玩家（玩家快速穿墙后敌人常处此态）。
        if (Vector3.Distance(transform.position, player.transform.position) <= GetAttackActivationRange()
            && !WallPenetrationResolve.IsBlockedBetween(transform.position, player.transform.position, applyCloseCombatExemption: false))
            return;

        if (stuckEscapeCooldownTimer > 0f)
        {
            stuckEscapeCooldownTimer -= Time.deltaTime;
            return;
        }

        float speed = agent.velocity.magnitude;

        // ⭐ 卡住判定四条件（严格化，避免误判）：
        //   ① velocity 持续接近 0
        //   ② desiredVelocity 有明显移动需求（agent 确实想走）
        //   ③ remainingDistance > stoppingDistance（还没到停车距离）
        //   ④ 实际位置没有明显变化（由下方 moved 判定）
        if (speed > stuckEscapeSpeed) { stuckEscapePosValid = false; return; }
        if (agent.desiredVelocity.magnitude < stuckMinDesiredSpeed) { stuckEscapePosValid = false; return; }
        if (agent.remainingDistance <= agent.stoppingDistance + 0.1f) { stuckEscapePosValid = false; return; }

        // 首次记录基准点
        if (!stuckEscapePosValid)
        {
            stuckEscapeLastPos = transform.position;
            stuckEscapePosValid = true;
            return;
        }

        float moved = Vector3.Distance(transform.position, stuckEscapeLastPos);
        stuckEscapeLastPos = transform.position;

        if (moved < 0.02f)
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

    // ⭐ 保守卡住恢复（偶发漏网的自救补层）：
    // TryStuckEscape 需要"视线被真墙挡(IsBlockedBetween)"，而少数 enemy 卡住时：
    //   · 卡在 Carve 障碍角落（墙并不在敌人↔玩家直线上）；
    //   · 夹在多个敌人/障碍缝里动弹不得；
    //   · agent 有 hasPath 但 path 实际已失效/到达不了目标/没朝 steeringTarget 走。
    // 这些情况视线检测恒为 false，永远不会触发自救。
    // 这里只看"该动却没动"：
    //   持续满足 → 追击中、无攻击/硬直/死亡、agent 有效在 NavMesh 上、isStopped=false、
    //              还有移动需求(未到停车距离 / 路径无效或丢失)、位移速度≈0 —— 才累计计时。
    //   达到 stallRecoveryDelay 秒 → 一次安全重寻路到"可达追击点"(GetChaseReachableTarget)，
    //   然后进入 stallRecoveryCooldown 冷却，避免每帧 Repath。
    // 攻击/冷却/硬直/击退恢复期间的 velocity=0 都不满足"还有移动需求"，且停在攻击范围会被上面
    // 的就位分支先返回，天然不会误触发。
    protected void TryStallRecovery()
    {
        if (agent == null || !agent.isOnNavMesh || player == null) return;
        if (isDead || isStaggering || isAttacking) { stallRecoveryTimer = 0f; return; }
        if (!isChasing) { stallRecoveryTimer = 0f; return; }
        if (agent.isStopped) { stallRecoveryTimer = 0f; return; }   // 攻击/排队等主动停下不算卡住

        // 恢复宽限期（攻击/硬直/击退刚结束）：此期间 velocity 未建立，不判定
        if (stuckGraceTimer > 0f)
        {
            stallRecoveryTimer = 0f;
            return;
        }

        // 冷却防抖：触发过一次后 cooldown 秒内不再重复 Repath
        if (stallRecoveryCooldownTimer > 0f)
        {
            stallRecoveryCooldownTimer -= Time.deltaTime;
            return;
        }

        // 是否"还有移动需求"：
        //   · 路径有效(Complete)且未到停车距离 → 还要走；
        //   · 路径无效(Partial/Invalid) 或根本没路径(hasPath=false) → 需要重寻路。
        bool needsMove = false;
        if (agent.hasPath && !agent.pathPending)
        {
            if (agent.pathStatus == NavMeshPathStatus.PathComplete)
                needsMove = agent.remainingDistance > agent.stoppingDistance + 0.15f;
            else
                needsMove = true;   // 路径 Partial/Invalid：到不了目标，需要重寻
        }
        else if (!agent.hasPath && !agent.pathPending)
        {
            needsMove = true;       // 追击中却完全没有路径 → 需要重寻
        }
        if (!needsMove) { stallRecoveryTimer = 0f; return; }

        // 位移速度很低才算"卡住"；还在动就清零计时
        if (agent.velocity.magnitude > stuckEscapeSpeed)
        {
            stallRecoveryTimer = 0f;
            return;
        }

        stallRecoveryTimer += Time.deltaTime;
        if (stallRecoveryTimer < stallRecoveryDelay) return;

        // 触发一次安全重寻路：目标用可达追击点，不直接 SetDestination(玩家原位置)
        stallRecoveryTimer = 0f;
        stallRecoveryCooldownTimer = stallRecoveryCooldown;
        NotePathMutation("TryStallRecovery(保守卡住) → ResetPath+SetDest(可达追击点)");
        agent.ResetPath();
        agent.isStopped = false;
        agent.SetDestination(GetChaseReachableTarget());
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

// 追击每帧设目的地统一走这里：直接喂玩家/环形占位点，
    // 由 NavMeshAgent 自己计算绕墙路径。不再做任何"本侧可达点"覆盖——
    // 那会让 agent 停在墙侧而不绕墙，绕行应完全依赖 agent 自身的寻路。
    protected void SetChaseDestination(Vector3 fallbackTarget)
    {
        if (agent == null || !agent.isOnNavMesh) return;
        NotePathMutation("SetChaseDestination → SetDestination");
        agent.SetDestination(fallbackTarget);
    }

    // ⭐ 半就位带压近目标点：当本敌人在"攻击就位判定带"内（距离 > attackRange 但 ≤ attackRange×slack）时，
    // 把追击目的地从"玩家脚下"收拢到"距玩家 attackRange×0.95"的站位点。
    // 目的：敌人仍能走进严格攻击范围出手（A/C 的收益不丢），但 NavMeshAgent 不再一路把根/身体
    // 怼进玩家体积 —— 否则敌群贴脸时重复穿透玩家胶囊、被 EnemyCollisionBlocker 每帧推出，
    // 表现就是"敌人一直推动玩家"。
    // 带外（远追/已就位）直接返回 fallback，不影响绕墙、环形占位与排队。
    protected Vector3 GetStandoffTarget(Vector3 fallback)
    {
        if (player == null) return fallback;
        float ar = GetAttackActivationRange();
        float dist = Vector3.Distance(transform.position, player.transform.position);
        if (dist <= ar * attackRangeSlackMultiplier && dist > ar * 0.95f && dist > 0.0001f)
        {
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                return player.transform.position + away.normalized * (ar * 0.95f);
        }
        return fallback;
    }

    // ⭐ 在被外部强制位移(穿墙兜底 Warp 等)清掉路径后，恢复追击：
    // Warp 会设 agent.enabled=false? 不——Warp 只清路径(hasPath→False、destination 复位)。
    // 这里立即：保持 enabled、isStopped=false、重新 SetDestination 回原追击目标/玩家。
    // 只在追击中才恢复；攻击/硬直/蓄力等其他状态保持 agent 由各自流程控制（RestoreChaseAfterStagger 等同理）。
    public void RestoreChaseAfterWarp(Vector3 destination)
    {
        if (agent == null || isDead || isStaggering || isAttacking) return;
        if (!agent.enabled) return;

        // ⭐ 确认仍在 NavMesh 上：Warp 推回墙外后若落点恰在 NavMesh 外（墙角/悬空/被推出烘焙边界），
        // 先贴回最近的可走 NavMesh 点，避免从此 onNavMesh=false 而永久失去寻路。
        if (!agent.isOnNavMesh)
        {
            NavMeshHit anchor;
            if (NavMesh.SamplePosition(transform.position, out anchor, 2f, NavMesh.AllAreas))
                agent.Warp(anchor.position);
            else
                return; // 周围确实无 NavMesh，无法自救
        }
        if (!agent.isOnNavMesh) return;

        if (!isChasing || player == null || player.IsDead())
        {
            agent.isStopped = true;
            return;
        }

        // 目标回退（Warp 推回墙外后旧 dest 可能在墙里/不可达 → 落到玩家位置附近）
        Vector3 dest = destination;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(dest, out hit, 3f, NavMesh.AllAreas))
            dest = player.transform.position;

        agent.isStopped = false;
        SetChaseDestination(dest);
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
                $"| agent stopped:{agent!=null&&agent.isOnNavMesh&&agent.isStopped} hasPath:{agent!=null&&agent.hasPath} " +
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
            SetChaseDestination(GetStandoffTarget(player.transform.position));
            // ⭐ 硬直/击退结束恢复移动：进入 stuck 宽限，避免刚恢复 velocity≈0 被误判卡住
            stuckGraceTimer = stuckGraceDuration;
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
        {
            string safeStopped = agent != null && agent.isOnNavMesh ? agent.isStopped.ToString() : "offNav";
            Debug.Log($"[{Time.time:F2}] {name} 路径操作 ← {source} | hasPath:{agent!=null&&agent.hasPath} pending:{agent!=null&&agent.pathPending} isStopped:{safeStopped}");
        }
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
                $"isStopped:{(agent.isOnNavMesh ? agent.isStopped.ToString() : "offNav")} enabled:{agent.enabled} onNavMesh:{agent.isOnNavMesh} | " +
                $"dest:{(agent.isOnNavMesh ? agent.destination.ToString() : "offNav")} steer:{steer} | " +
                $"最后调用:{lastPathMutation} | 玩家:{player?.transform.position}");
        }
        lastTrackedHasPath = hp;
        lastTrackedPending = pd;
    }

    // 🔍 脱离 NavMesh 追踪：consequence of Agent 被任何来源推到 NavMesh 多边形外，
    // isOnNavMesh 变 false 后，任何读 isStopped/velocity/destination/steeringTarget 都会抛
    // "IsStopped can only be called on an active agent that has been placed on a NavMesh"。
    // 这里在"还挂在 NavMesh 上"时每帧缓存安全快照；观察首帧 true→false 时，
    // 用上一帧快照打印（绝不在脱离帧读会抛错的属性），并标记同一段只打一次。
    private void TrackNavMeshLoss()
    {
        if (!logNavMeshLoss || agent == null) return;

        // ⭐ 死亡是预期脱离：Die() 会 agent.enabled=false，属正常流程（保留尸体、停用 agent）。
        // 这里跳过，避免把"死亡脱离"当成贴墙 bug 打印；同时复位标记，活着的敌人照常追踪。
        if (isDead)
        {
            lastTrackedOnNavMesh = true;
            navMeshLossLogged = false;
            return;
        }

        bool onNav = agent.enabled && agent.isOnNavMesh;

        if (onNav)
        {
            // 在 NavMesh 上：缓存当前安全快照，供脱离瞬间打印
            lastTrackedOnNavMesh = true;
            navMeshLossLogged = false;
            navMeshLossLastPos = transform.position;
            navMeshLossLastMutation = lastPathMutation;
            navMeshLossLastState =
                $"chase:{isChasing} atk:{isAttacking} stagger:{isStaggering} dead:{isDead} " +
                $"canAtk:{canAttack} forceReturn:{forceReturnToRange} wall挡:{WallPenetrationResolve.IsBlockedBetween(transform.position, player != null ? player.transform.position : transform.position, applyCloseCombatExemption:false)}";
            return;
        }

        // 观察到不在 NavMesh 上
        if (lastTrackedOnNavMesh && !navMeshLossLogged)
        {
            navMeshLossLogged = true;
            Debug.Log(
                $"<color=red>[NavMesh脱离]</color> <b>{GetType().Name}</b> name:{name} | isOnNavMesh:true→false 首次脱离\n" +
                $"  脱离前(缓存快照): pos:{navMeshLossLastPos:F2} | {navMeshLossLastState}\n" +
                $"  最后路径操作: {navMeshLossLastMutation}\n" +
                $"  本帧危险读取请勿做: 当前 agent enabled:{agent.enabled} onNavMesh:{agent.isOnNavMesh}"
            );
        }
        lastTrackedOnNavMesh = false;
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
            + "\nsteer:" + steer;
        pathDebugText.color = blocked ? Color.yellow : Color.white;
    }

    // 🔍 绕路 Path 诊断：周期性打印 agent 实际 Path 与 NavMesh.CalculatePath 的对照。
    // 判定 A/B/C/D：
    //   A) agent.path.corners 只有 [enemy, player] 两段 → NavMesh 没生成绕路路径（或路径被算成直线）
    //   B) corners 有墙角拐点 但 agent 不沿路径走（steeringTarget 是墙角 但 transform 直冲墙）→ 移动/旋转层问题
    //   C) agent 每次刚有绕路路径 就被 Warp/ResetPath 清掉 → 打印会看到 hasPath 反复 Y→N、corners 长度抖动
    //   D) destination 本身是墙对岸/错误位置 → 对照 CalculatePath 起点到 destination 是否可达
    private void LogPathCorners()
    {
        if (!logPathCorners || agent == null || !agent.isOnNavMesh || player == null) return;

        pathCornerLogTimer -= Time.deltaTime;
        if (pathCornerLogTimer > 0f) return;
        pathCornerLogTimer = pathCornerLogInterval;

        Vector3 myPos = transform.position;
        Vector3 targetPos = player.transform.position;
        string type = GetType().Name;

        // ① agent 当前实际路径的 corners
        Vector3[] corners = agent.path.corners;
        string agentCorners = corners == null ? "null" : "";
        if (corners != null)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < corners.Length; i++)
                parts.Add($"C{i}:{corners[i].ToString("F1")}");
            agentCorners = string.Join(" → ", parts);
        }

        // ② 独立跑一次 NavMesh.CalculatePath 对照（不经过 agent，看 NavMesh 本身能不能算出绕路路）
        if (calcComparePath == null) calcComparePath = new NavMeshPath();
        bool calcOK = NavMesh.CalculatePath(myPos, targetPos, NavMesh.AllAreas, calcComparePath);
        string calcStatus = calcOK ? calcComparePath.status.ToString() : "CALC-FAIL";
        string calcCorners = "N/A";
        if (calcOK && calcComparePath.corners != null)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < calcComparePath.corners.Length; i++)
                parts.Add($"C{i}:{calcComparePath.corners[i].ToString("F1")}");
            calcCorners = string.Join(" → ", parts);
        }

        bool wallBetween = WallPenetrationResolve.IsBlockedBetween(myPos, targetPos);

        Debug.Log(
            $"<color=#00ffff>[PathCorners]</color> <b>{type}</b> name:{name} 墙挡:{wallBetween}\n" +
            $"  agent: hasPath:{agent.hasPath} pending:{agent.pathPending} status:{agent.pathStatus} " +
            $"dest:{agent.destination.ToString("F1")} steer:{agent.steeringTarget.ToString("F1")} " +
            $"rem:{agent.remainingDistance.ToString("F2")} vel:{agent.velocity.ToString("F2")} " +
            $"desiredVel:{agent.desiredVelocity.ToString("F2")} stopped:{agent.isStopped}\n" +
            $"  agentCorners({(corners!=null?corners.Length:0)}): {agentCorners}\n" +
            $"  calcCorners({(calcOK?calcComparePath.corners.Length:0)}): {calcCorners}  calcStatus:{calcStatus}"
        );
    }
}