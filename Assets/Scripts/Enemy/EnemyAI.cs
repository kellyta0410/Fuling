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

    [Header("攻击通用")]
    protected float attackCooldownTimer = 0f;
    public bool canAttack = true;
    protected bool isAttacking = false;
    protected float attackTimer = 0f;
    public float attackDuration = 0.5f;
    public float attackDamageDelay = 0.3f;
    protected Coroutine attackCoroutine;

    [Header("面向检测")]
    public float facingAngleThreshold = 45f;

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

            if (!isHealthInitialized)
            {
                // 生成时锁定血量：满血 = 基础血量 × 生成当刻的倍率
                maxHealth = baseHealth * currentHealthMultiplier;
                currentHealth = maxHealth;
                isHealthInitialized = true;
            }

            if (isAgentValid && !useDirectChase)
            {
                agent.speed = baseSpeed;
                agent.stoppingDistance = enemyData.attackRange * 0.85f;
                agent.autoBraking = true;
                agent.radius = 0.4f;
                agent.height = 2f;
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
            if (attackCooldownTimer >= (enemyData != null ? enemyData.attackCooldown : 1.5f))
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);
        float attackRange = enemyData != null ? enemyData.attackRange : 1.5f;

        if (distance <= attackRange && canAttack && IsFacingPlayer())
        {
            PerformAttack();
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

        HandleMovement();

        if (enableSeparation && !isAttacking)
        {
            ApplySeparation();
        }

        UpdateHealthBarPosition();

        float currentSpeed = GetCurrentSpeed();
        bool isMovingState = isChasing && !isAttacking && currentSpeed > 0.05f;
        UpdateAnimations(currentSpeed, isMovingState);
    }

    // ---------- 分离力 ----------
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
            agent.isStopped = true;
    }

    protected float GetCurrentSpeed()
    {
        return isAgentValid && agent != null ? agent.velocity.magnitude : 0f;
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
            float attackRangeValue = enemyData != null ? enemyData.attackRange : 1.5f;
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