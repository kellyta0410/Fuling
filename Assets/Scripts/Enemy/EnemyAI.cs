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

    void Start()
    {
        animator = GetComponent<Animator>();
        player = FindObjectOfType<PlayerController>();
        agent = GetComponent<NavMeshAgent>();

        if (enemyData != null)
        {
            baseSpeed = enemyData.speed;
            baseHealth = enemyData.health;
            baseAttackDamage = enemyData.attackDamage;

            // ⭐ 初始化血量（只执行一次）
            if (!isHealthInitialized)
            {
                currentHealth = baseHealth;
                isHealthInitialized = true;
            }

            if (agent != null)
            {
                agent.speed = baseSpeed;
                agent.stoppingDistance = enemyData.attackRange * 0.85f;
                agent.autoBraking = true;
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);
            }

            transform.localScale = Vector3.one * enemyData.scale;
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

            if (agent != null)
            {
                agent.speed = 2f;
                agent.stoppingDistance = 1.5f * 0.85f;
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);
            }
        }

        CreateHealthBar();
        ApplyCurrentMultipliers();
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

        // ⭐ 血量只在初始化时设置，不重复重置
        // 只更新血条显示
        UpdateHealthBar();

        // 应用速度
        if (agent != null)
        {
            agent.speed = baseSpeed * currentSpeedMultiplier;
        }
    }

    void Update()
    {
        if (isDead)
        {
            if (agent != null) agent.isStopped = true;
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
            if (agent != null) agent.isStopped = true;
            return;
        }

        if (player == null || player.IsDead())
        {
            if (agent != null) agent.isStopped = true;
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

        if (distance <= enemyData.attackRange)
        {
            if (agent != null) agent.isStopped = true;

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
                if (agent != null)
                {
                    agent.isStopped = false;
                    agent.SetDestination(player.transform.position);
                }

                float currentSpeed = agent != null ? agent.velocity.magnitude : 0f;
                UpdateAnimations(currentSpeed, true);
            }
            else
            {
                if (agent != null) agent.isStopped = true;
                UpdateAnimations(0f, false);
            }
        }

        UpdateHealthBarPosition();
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

        if (agent != null) agent.isStopped = true;

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

            // 如果还没找到，尝试获取第一个子物体的Image
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

        if (animator != null) animator.SetTrigger("Hit");

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

        if (healthBarInstance != null) healthBarInstance.SetActive(false);
        if (agent != null) agent.isStopped = true;
        if (animator != null) animator.SetTrigger("Die");

        SpawnCoin();

        if (player != null)
        {
            player.AddKill();
            player.AddCoin(enemyData.coinReward);
            Debug.Log("击杀 " + enemyData.enemyName + "，获得 " + enemyData.coinReward + " 金币");
        }

        float delay = enemyData != null ? enemyData.deathAnimationDelay : 2f;
        Destroy(gameObject, delay);
    }

    void SpawnCoin()
    {
        if (coinPrefab == null) return;

        GameObject coin = Instantiate(coinPrefab, transform.position, Quaternion.identity);
        Coin coinScript = coin.GetComponent<Coin>();
        if (coinScript != null) coinScript.SetValue(enemyData.coinReward);
    }

    public void SetMultipliersFromSpawner()
    {
        // 由 EnemySpawner 在生成后直接调用 ApplyScalingMultipliers
    }

    void OnDrawGizmosSelected()
    {
        if (enemyData == null || !enemyData.showGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 10f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, enemyData.attackRange);

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
                enemyData.enemyName + "\nHP: " + currentHealth + "/" + maxHealth + "\nFacing: " + IsFacingPlayer());
        }
#endif
    }
}