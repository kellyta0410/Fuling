using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    private Animator animator;
    private PlayerController player;
    private NavMeshAgent agent;

    [Header("敌人数据")]
    public EnemyData enemyData;

    [Header("金币")]
    public GameObject coinPrefab;

    [Header("血条")]
    public GameObject healthBarPrefab;
    public Vector3 healthBarOffset = new Vector3(0, 1.5f, 0);
    private GameObject healthBarInstance;
    private Slider healthSlider;
    private Image healthFillImage;
    private float currentHealth;

    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;

    [Header("状态")]
    public bool isDead = false;

    [Header("攻击")]
    private float attackCooldownTimer = 0f;
    public bool canAttack = true;
    private bool isAttacking = false;
    private float attackTimer = 0f;
    public float attackDuration = 0.5f;
    public float attackDamageDelay = 0.3f;
    private Coroutine attackCoroutine;

    [Header("面向检测")]
    public float facingAngleThreshold = 45f;

    [Header("追击检测")]
    [Tooltip("检测玩家的范围（进入此范围才开始追击）")]
    public float detectionRange = 18f;
    [Tooltip("失去玩家目标的范围（比检测范围大，防止频繁切换）")]
    public float loseTargetRange = 25f;

    // 当前倍率（由 EnemySpawner 设置）
    private float currentSpeedMultiplier = 1f;
    private float currentHealthMultiplier = 1f;
    private float currentDamageMultiplier = 1f;

    // 存储基础值
    private float baseSpeed;
    private float baseHealth;
    private float baseAttackDamage;

    // 标记是否已初始化血量（防止重复重置）
    private bool isHealthInitialized = false;

    // 当前是否在追击状态
    private bool isChasing = false;

    // 原地待机时的随机旋转计时
    private float idleRotationTimer = 0f;
    private float idleRotationInterval = 3f;
    private Quaternion targetIdleRotation;

    // ⭐ 标记 NavMeshAgent 是否有效
    private bool isAgentValid = false;

    void Start()
    {
        animator = GetComponent<Animator>();
        player = FindObjectOfType<PlayerController>();
        agent = GetComponent<NavMeshAgent>();

        // ⭐ 检查 NavMeshAgent 是否有效
        if (agent != null && agent.isOnNavMesh)
        {
            isAgentValid = true;
        }
        else
        {
            isAgentValid = false;
            Debug.LogWarning($"{gameObject.name}: NavMeshAgent 无效或不在 NavMesh 上");
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

            if (isAgentValid)
            {
                agent.speed = baseSpeed;
                agent.stoppingDistance = enemyData.attackRange * 0.85f;
                agent.autoBraking = true;
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);

                // ⭐ 初始时停止移动
                agent.isStopped = true;
            }
        }
        else
        {
            Debug.LogWarning("EnemyData 未赋值！使用默认值");
            baseSpeed = 2f;
            baseHealth = 50f;
            baseAttackDamage = 10f;

            if (!isHealthInitialized)
            {
                currentHealth = 50f;
                isHealthInitialized = true;
            }

            if (isAgentValid)
            {
                agent.speed = 2f;
                agent.stoppingDistance = 1.5f * 0.85f;
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);

                // ⭐ 初始时停止移动
                agent.isStopped = true;
            }
        }

        CreateHealthBar();
        ApplyCurrentMultipliers();

        // 初始化为待机状态
        isChasing = false;

        // 设置初始随机旋转
        targetIdleRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        idleRotationInterval = Random.Range(2f, 5f);
        idleRotationTimer = 0f;
    }

    // 由 EnemySpawner 调用，设置倍率
    public void ApplyScalingMultipliers(float speedMult, float healthMult, float damageMult)
    {
        currentSpeedMultiplier = speedMult;
        currentHealthMultiplier = healthMult;
        currentDamageMultiplier = damageMult;

        ApplyCurrentMultipliers();
    }

    void ApplyCurrentMultipliers()
    {
        if (enemyData == null) return;

        UpdateHealthBar();

        if (isAgentValid)
        {
            agent.speed = baseSpeed * currentSpeedMultiplier;
        }
    }

    void Update()
    {
        if (isDead)
        {
            // ⭐ 安全停止 Agent
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
                animator.SetBool("IsAttacking", false);
                if (attackCoroutine != null)
                {
                    StopCoroutine(attackCoroutine);
                    attackCoroutine = null;
                }
            }
            // ⭐ 安全停止 Agent
            StopAgent();
            return;
        }

        if (player == null || player.IsDead())
        {
            // ⭐ 安全停止 Agent
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

        // 追击检测：进入 detectionRange 开始追击
        if (distance <= detectionRange)
        {
            isChasing = true;
        }
        // 离开 loseTargetRange 停止追击（比检测范围大，防止抖动）
        else if (distance > loseTargetRange)
        {
            isChasing = false;
        }

        // 不在追击状态：待机
        if (!isChasing)
        {
            // ⭐ 安全停止 Agent
            StopAgent();
            UpdateAnimations(0f, false);

            // 待机时缓慢旋转（更有生气）
            IdleRotation();

            // 更新血条位置（但血条一直显示）
            UpdateHealthBarPosition();
            return;
        }

        // ⭐ 追击状态：正常行为
        if (distance <= enemyData.attackRange)
        {
            // ⭐ 安全停止 Agent
            StopAgent();

            Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;
            directionToPlayer.y = 0;

            if (directionToPlayer != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
            }

            if (IsFacingPlayer())
            {
                if (canAttack && !isAttacking && !isDead)
                {
                    PerformAttack();
                }
            }

            UpdateAnimations(0f, false);
        }
        else
        {
            if (!isAttacking)
            {
                if (isAgentValid)
                {
                    agent.isStopped = false;
                    agent.SetDestination(player.transform.position);
                }

                float currentSpeed = isAgentValid ? agent.velocity.magnitude : 0f;
                UpdateAnimations(currentSpeed, true);
            }
            else
            {
                // ⭐ 安全停止 Agent
                StopAgent();
                UpdateAnimations(0f, false);
            }
        }

        UpdateHealthBarPosition();
    }

    /// <summary>
    /// ⭐ 安全地停止 NavMeshAgent
    /// </summary>
    private void StopAgent()
    {
        if (isAgentValid && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }
    }

    /// <summary>
    /// 待机时的随机旋转
    /// </summary>
    void IdleRotation()
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

    bool IsFacingPlayer()
    {
        if (player == null) return false;

        Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;
        directionToPlayer.y = 0;

        Vector3 forward = transform.forward;
        forward.y = 0;

        float angle = Vector3.Angle(forward, directionToPlayer);

        return angle <= facingAngleThreshold;
    }

    void PerformAttack()
    {
        if (!canAttack || isDead || isAttacking) return;

        canAttack = false;
        attackCooldownTimer = 0f;

        isAttacking = true;
        attackTimer = 0f;

        // ⭐ 安全停止 Agent
        StopAgent();

        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Attack");

        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(DelayedDamage());
    }

    IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);

        if (player != null && !player.IsDead() && !isDead)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);

            if (distance <= enemyData.attackRange && IsFacingPlayer())
            {
                float finalDamage = baseAttackDamage * currentDamageMultiplier;
                player.TakeDamage(Mathf.RoundToInt(finalDamage));
                Debug.Log(gameObject.name + " 攻击玩家，造成 " + finalDamage + " 伤害");
            }
            else
            {
                Debug.Log(gameObject.name + " 攻击失败");
            }
        }
    }

    void CreateHealthBar()
    {
        if (healthBarPrefab == null)
        {
            Debug.LogWarning("HealthBar Prefab 未设置");
            return;
        }

        healthBarInstance = Instantiate(healthBarPrefab, transform.position + healthBarOffset, Quaternion.identity);
        healthBarInstance.transform.SetParent(transform);

        healthSlider = healthBarInstance.GetComponent<Slider>();
        if (healthSlider == null)
        {
            healthSlider = healthBarInstance.GetComponentInChildren<Slider>();
        }

        if (healthSlider != null)
        {
            Transform fillTransform = healthSlider.transform.Find("Fill Area/Fill");
            if (fillTransform != null)
            {
                healthFillImage = fillTransform.GetComponent<Image>();
            }
        }

        if (healthFillImage == null)
        {
            Image[] images = healthBarInstance.GetComponentsInChildren<Image>();
            foreach (Image img in images)
            {
                if (img.transform != healthBarInstance.transform &&
                    img.transform.parent != null &&
                    img.transform.parent.name.Contains("Fill"))
                {
                    healthFillImage = img;
                    break;
                }
            }

            if (healthFillImage == null && healthSlider != null)
            {
                healthFillImage = healthSlider.GetComponentInChildren<Image>();
            }
        }

        UpdateHealthBar();
    }

    void UpdateHealthBarPosition()
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

    void UpdateHealthBar()
    {
        if (healthSlider == null || enemyData == null) return;

        float maxHealth = baseHealth * currentHealthMultiplier;
        float healthPercent = currentHealth / maxHealth;
        healthSlider.value = healthPercent;

        UpdateHealthBarColor(healthPercent);
    }

    void UpdateHealthBarColor(float healthPercent)
    {
        if (healthFillImage == null) return;

        Color targetColor;
        if (healthPercent >= 0.6f)
            targetColor = fullHealthColor;
        else if (healthPercent >= 0.3f)
            targetColor = midHealthColor;
        else
            targetColor = lowHealthColor;

        healthFillImage.color = targetColor;
    }

    void UpdateAnimations(float speed, bool isMoving)
    {
        if (animator == null || isAttacking) return;
        animator.SetFloat("Speed", speed);
        animator.SetBool("IsMoving", isMoving);
    }

    public void TakeDamageImmediate(int damage)
    {
        if (isDead || enemyData == null) return;

        StartCoroutine(SmoothDamage(damage));
    }

    IEnumerator SmoothDamage(int damage)
    {
        float duration = 0.2f;
        float elapsed = 0f;
        float startHealth = currentHealth;
        float targetHealth = Mathf.Max(currentHealth - damage, 0);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            currentHealth = Mathf.Lerp(startHealth, targetHealth, t);
            UpdateHealthBar();
            yield return null;
        }

        currentHealth = targetHealth;
        UpdateHealthBar();

        Debug.Log("敌人受到 " + damage + " 伤害，剩余血量: " + currentHealth);

        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        isDead = true;

        if (healthBarInstance != null)
            healthBarInstance.SetActive(false);

        // ⭐ 安全停止 Agent
        StopAgent();

        if (animator != null)
            animator.SetTrigger("Die");

        int baseCoinReward = enemyData != null ? enemyData.coinReward : 10;
        int finalCoinReward = baseCoinReward;

        bool isComboActive = false;
        if (ComboManager.Instance != null)
        {
            isComboActive = ComboManager.Instance.IsComboActive();
            if (isComboActive)
            {
                finalCoinReward = baseCoinReward * 2;
            }

            ComboManager.Instance.AddKill();
        }

        SpawnCoin(finalCoinReward);

        if (player != null)
        {
            player.AddKill();
        }

        Debug.Log($"击杀 {enemyData?.enemyName ?? "敌人"} | " +
                  $"基础金币: {baseCoinReward} | " +
                  $"{(isComboActive ? "🔥 Combo x2 → " : "")}最终: {finalCoinReward} 金币");

        float delay = enemyData != null ? enemyData.deathAnimationDelay : 2f;
        Destroy(gameObject, delay);
    }

    void SpawnCoin(int coinAmount)
    {
        if (coinPrefab == null)
        {
            Debug.LogWarning("coinPrefab 未设置，无法生成金币");
            return;
        }

        for (int i = 0; i < coinAmount; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-0.3f, 0.3f),
                0.5f,
                Random.Range(-0.3f, 0.3f)
            );

            GameObject coin = Instantiate(coinPrefab, transform.position + offset, Quaternion.identity);
            Coin coinScript = coin.GetComponent<Coin>();

            if (coinScript != null)
            {
                coinScript.SetValue(1);
            }
        }

        Debug.Log($"生成了 {coinAmount} 个金币");
    }

    public void SetMultipliersFromSpawner()
    {
        // 由 EnemySpawner 在生成后直接调用 ApplyScalingMultipliers
    }

    void OnDrawGizmosSelected()
    {
        if (enemyData == null || !enemyData.showGizmos) return;

        // 检测范围（绿色）
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // 失去目标范围（红色）
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, loseTargetRange);

        // 攻击范围（黄色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, enemyData.attackRange);

        // 面向角度
        Gizmos.color = Color.blue;
        Vector3 forward = transform.forward;
        Quaternion leftRotation = Quaternion.Euler(0, -facingAngleThreshold, 0);
        Quaternion rightRotation = Quaternion.Euler(0, facingAngleThreshold, 0);
        Vector3 leftBoundary = leftRotation * forward * 2f;
        Vector3 rightBoundary = rightRotation * forward * 2f;
        Gizmos.DrawLine(transform.position, transform.position + leftBoundary);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary);

#if UNITY_EDITOR
        if (enemyData != null)
        {
            float maxHealth = baseHealth * currentHealthMultiplier;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                enemyData.enemyName + "\nHP: " + currentHealth + "/" + maxHealth +
                "\n追击: " + (isChasing ? "是" : "否") +
                "\n距离: " + (player != null ? Vector3.Distance(transform.position, player.transform.position).ToString("F1") : "N/A"));
        }
#endif
    }
}