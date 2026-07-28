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

    [Header("追击检测")]
    public float detectionRange = 18f;
    public float loseTargetRange = 25f;

    [Header("群组行为（分离力）")]
    public bool enableSeparation = true;
    public float separationRadius = 1.5f;
    public float separationForce = 3f;
    public float separationSmoothSpeed = 8f;

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

    // 无限模式
    private bool useDirectChase = false;

    // ---------- 生命周期 ----------
    protected virtual void Start()
    {
        animator = GetComponent<Animator>();
        player = FindObjectOfType<PlayerController>();
        agent = GetComponent<NavMeshAgent>();
        myCollider = GetComponent<Collider>();

        isAgentValid = agent != null && agent.isOnNavMesh;
        enemyLayerMask = LayerMask.GetMask("Enemy");

        // 检测无限模式
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
                currentHealth = baseHealth;
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
                currentHealth = 50f;
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

        // 无限模式：直接追踪
        if (useDirectChase && player != null && !player.IsDead())
        {
            DirectChaseUpdate();
            return;
        }

        // 普通模式：NavMesh寻路
        if (isAttacking)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackDuration)
            {
                isAttacking = false;
                attackTimer = 0f;
                animator.SetBool("IsAttacking", false);
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
            if (attackCooldownTimer >= enemyData.attackCooldown)
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);

        if (distance <= detectionRange) isChasing = true;
        else if (distance > loseTargetRange) isChasing = false;

        HandleMovement();

        if (enableSeparation && isChasing && !isAttacking)
        {
            ApplySeparation();
        }

        UpdateHealthBarPosition();
        UpdateAnimations(GetCurrentSpeed(), isChasing && !isAttacking);
    }

    // ⭐ 直接追踪更新（无限模式专用）
    void DirectChaseUpdate()
    {
        if (player == null || isDead) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);

        // 攻击冷却
        if (!canAttack)
        {
            attackCooldownTimer += Time.deltaTime;
            if (attackCooldownTimer >= enemyData.attackCooldown)
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        // 攻击检测
        if (distance <= enemyData.attackRange && canAttack && IsFacingPlayer())
        {
            PerformAttack();
            return;
        }

        // ⭐ 停止距离（碰到玩家就停，不会推着玩家走）
        float stopDistance = 0.8f;
        if (distance < stopDistance)
        {
            // 只面向玩家，不移动
            Vector3 lookTarget = new Vector3(player.transform.position.x, transform.position.y, player.transform.position.z);
            transform.LookAt(lookTarget);
            UpdateAnimations(0f, false);
            return;
        }

        // 直接走向玩家
        Vector3 direction = (player.transform.position - transform.position).normalized;
        float speed = baseSpeed * currentSpeedMultiplier;
        Vector3 targetPosition = transform.position + direction * speed * Time.deltaTime;

        targetPosition.y = transform.position.y;
        transform.position = targetPosition;

        // 面向玩家
        Vector3 lookTarget2 = new Vector3(player.transform.position.x, transform.position.y, player.transform.position.z);
        transform.LookAt(lookTarget2);

        UpdateAnimations(speed, true);
        isChasing = true;
        UpdateHealthBarPosition();
    }

    // 应用分离力
    private void ApplySeparation()
    {
        if (myCollider == null) return;

        Collider[] nearbyEnemies = Physics.OverlapSphere(transform.position, separationRadius, enemyLayerMask);
        Vector3 force = Vector3.zero;
        int count = 0;

        foreach (Collider col in nearbyEnemies)
        {
            if (col.gameObject == gameObject) continue;

            Vector3 dir = (transform.position - col.transform.position);
            float dist = dir.magnitude;

            if (dist < separationRadius && dist > 0.01f)
            {
                // 距离越近力越大
                float strength = 1f - (dist / separationRadius);
                force += dir.normalized * strength * separationForce;
                count++;
            }
        }

        if (count > 0)
        {
            // 平滑加速
            separationVelocity = Vector3.Lerp(separationVelocity, force, Time.deltaTime * separationSmoothSpeed);

            // 应用分离力（无限模式直接移动）
            if (useDirectChase)
            {
                // ⭐ 乘大一点让分离效果更明显
                transform.position += separationVelocity * Time.deltaTime * 3f;
            }
            else if (isAgentValid && agent != null && agent.isOnNavMesh)
            {
                agent.Move(separationVelocity * Time.deltaTime * 3f);
            }
        }
        else
        {
            // 没有附近敌人时，逐渐停止
            separationVelocity = Vector3.Lerp(separationVelocity, Vector3.zero, Time.deltaTime * separationSmoothSpeed);
        }
    }

    // ---------- 子类需要重写的方法 ----------
    protected abstract void HandleMovement();
    protected virtual void HandleAttack() { }

    // ---------- 公共方法 ----------
    public void ApplyScalingMultipliers(float speedMult, float healthMult, float damageMult)
    {
        currentSpeedMultiplier = speedMult;
        currentHealthMultiplier = healthMult;
        currentDamageMultiplier = damageMult;
        ApplyCurrentMultipliers();
    }

    protected virtual void ApplyCurrentMultipliers()
    {
        if (enemyData == null) return;
        UpdateHealthBar();
        if (isAgentValid && !useDirectChase)
            agent.speed = baseSpeed * currentSpeedMultiplier;
    }

    protected void StopAgent()
    {
        if (isAgentValid && agent != null && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    protected float GetCurrentSpeed()
    {
        return isAgentValid ? agent.velocity.magnitude : 0f;
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
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Attack");
        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(DelayedDamage());
    }

    protected virtual IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);
        if (player != null && !player.IsDead() && !isDead)
        {
            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist <= enemyData.attackRange && IsFacingPlayer())
            {
                float finalDamage = baseAttackDamage * currentDamageMultiplier;
                player.TakeDamage(Mathf.RoundToInt(finalDamage));
                Debug.Log(gameObject.name + " 攻击造成 " + finalDamage + " 伤害");
            }
        }
    }

    // 血条相关
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
        if (healthSlider == null || enemyData == null) return;
        float maxHealth = baseHealth * currentHealthMultiplier;
        float percent = currentHealth / maxHealth;
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
        if (isDead || enemyData == null) return;
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

    // ⭐ 死亡（延迟销毁）
    protected virtual void Die()
    {
        isDead = true;
        if (healthBarInstance != null) healthBarInstance.SetActive(false);
        StopAgent();
        animator.SetTrigger("Die");

        int baseCoin = enemyData != null ? enemyData.coinReward : 10;
        int finalCoin = baseCoin;
        bool comboActive = ComboManager.Instance != null && ComboManager.Instance.IsComboActive();
        if (comboActive) finalCoin = baseCoin * 2;
        if (ComboManager.Instance != null) ComboManager.Instance.AddKill();
        SpawnCoin(finalCoin);
        if (player != null) player.AddKill();

        // ⭐ 延迟销毁（播放完死亡动画后销毁）
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