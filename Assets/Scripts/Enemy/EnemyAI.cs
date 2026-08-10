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

    [Header("攻击冷却")]
    [Tooltip("攻击冷却（>0 时覆盖 EnemyType 资产的 attackCooldown，方便在 Inspect差 逐个手动调，无需改资产）")]
    public float attackCooldownOverride = 0f;

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
    public float detectionRange = 18f;
    public float loseTargetRange = 25f;

    [Header("追击检测（无限模式专用）")]
    public float infiniteDetectionRange = 30f;
    public float infiniteLoseTargetRange = 40f;

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

    // 缩放倍率
    protected float currentSpeedMultiplier = 1f;
    protected float currentHealthMultiplier = 1f;
    protected float currentDamageMultiplier = 1f;

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

    // ---------- 生命周期 ----------
    protected virtual void Start()
    {
        animator = GetComponent<Animator>();
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
                Debug.Log($"{name} 已吸附到 NavMesh 位置: {fixedPos}");
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
                agent.stoppingDistance = enemyData.attackRange * 0.8f;
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

        isChasing = false;
        targetIdleRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        idleRotationInterval = Random.Range(2f, 5f);
        idleRotationTimer = 0f;

        OnStart();
    }

    protected virtual void OnStart() { }

    protected virtual void Update()
    {
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
            StopAgent();
            UpdateAnimations(0f, false);
            return;
        }

        if (!canAttack)
        {
            attackCooldownTimer += Time.deltaTime;
            if (attackCooldownTimer >= GetAttackCooldown())
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);
        float attackRange = GetAttackActivationRange();

        // 进入攻击范围：停住原地转向玩家，能攻则攻，不再前进贴脸
        // （子类可重写 TryPerformInRangeAttack 接管该行为，如蓄力/咏唱）
        if (distance <= attackRange)
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

HandleMovement();

        // 接近目标提前减速，避免冲出一小段才刹停
        if (isChasing && !isAttacking)
            ApplyApproachBrake();

        // 已进入攻击范围（就位/攻击中）不再施加分离位移，避免后排把前排往玩家方向顶出"推一下"
        if (enableSeparation && distance > attackRange)
        {
            ApplySeparation();
        }

        UpdateHealthBarPosition();

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

            float d2 = (slot - transform.position).sqrMagnitude;
            if (d2 < bestSq)
            {
                bestSq = d2;
                best = slot;
            }
        }
        return best;
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

            if (useDirectChase)
            {
                transform.position += separationVelocity * Time.deltaTime;
            }
            else if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                agent.Move(separationVelocity * Time.deltaTime);
            }
        }
        else
        {
            separationVelocity = Vector3.Lerp(separationVelocity, Vector3.zero, Time.deltaTime * separationSmoothSpeed);
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

    protected void StopAgent()
    {
        if (isAgentValid && agent != null && agent.isOnNavMesh)
        {
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

    // 攻击冷却：优先用 Inspector 的手动覆盖值（>0），否则读 EnemyType 资产
    protected float GetAttackCooldown()
    {
        if (attackCooldownOverride > 0f) return attackCooldownOverride;
        return enemyData != null ? enemyData.attackCooldown : 1.5f;
    }

    // 有效攻击触发距离：取"攻击范围"与"agent 停距"较大者，
    // 否则像 Basic(stop 2.5 > attackRange 2) 会停在攻击范围外而永远不攻。
    protected float GetAttackActivationRange()
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
            if (dist <= attackRangeValue && IsFacingPlayer())
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

        StartCoroutine(SmoothDamage(damage));
    }

    // 受击击退：平滑推出一段距离（SmoothStep 缓动，避免瞬移）
    private Coroutine knockRoutine;
    public void AddKnockback(Vector3 dir, float distance)
    {
        if (isDead) return;
        if (knockRoutine != null) StopCoroutine(knockRoutine);

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

            if (agent != null && agent.isOnNavMesh) agent.Move(mv * step);
            else transform.position += mv * step;

            yield return null;
        }
        knockRoutine = null;

        // 被击退后停顿（硬直）一下，再恢复行动
        if (isStaggering || isDead) yield break;
        isStaggering = true;
        yield return new WaitForSeconds(staggerDuration);
        isStaggering = false;
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
        if (animator != null)
        {
            animator.SetBool("IsMoving", false);
            animator.SetTrigger("Die");
        }

        // 保留尸体的实体碰撞体（玩家仍会被拦住），只禁用 NavMeshAgent：
        // 尸体是静态碰撞（无 agent 驱动），不会被后续敌人物理推动，也就不会把尸体顶到玩家身上
        if (agent != null) agent.enabled = false;

        if (ownerTile != null)
        {
            ownerTile.UnregisterEnemy(gameObject);
        }

        int baseCoin = enemyData != null ? enemyData.coinReward : 10;
        SpawnCoin(baseCoin);
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
}