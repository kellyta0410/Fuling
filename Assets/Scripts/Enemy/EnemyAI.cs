using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class EnemyAI : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private Animator animator;
    private PlayerController player;
    private NavMeshAgent agent;

    // ==================== 敌人数据 ====================
    [Header("敌人数据")]
    public EnemyData enemyData;

    // ==================== 金币 ====================
    [Header("金币")]
    public GameObject coinPrefab;

    // ==================== 血条 ====================
    [Header("血条")]
    public GameObject healthBarPrefab;
    public Vector3 healthBarOffset = new Vector3(0, 1.5f, 0);
    private GameObject healthBarInstance;
    private Slider healthSlider;
    private Image healthFillImage;
    private float currentHealth;

    // ==================== 血条颜色 ====================
    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;

    // ==================== 状态 ====================
    [Header("状态")]
    public bool isDead = false;

    // ==================== 攻击 ====================
    [Header("攻击")]
    private float attackCooldownTimer = 0f;
    public bool canAttack = true;
    private bool isAttacking = false;
    private float attackTimer = 0f;
    public float attackDuration = 0.5f;
    public float attackDamageDelay = 0.3f;
    private Coroutine attackCoroutine;

    void Start()
    {
        animator = GetComponent<Animator>();
        player = FindObjectOfType<PlayerController>();
        agent = GetComponent<NavMeshAgent>();

        if (enemyData != null)
        {
            currentHealth = enemyData.health;

            if (agent != null)
            {
                agent.speed = enemyData.speed;
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
            currentHealth = 50f;

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

            Vector3 direction = (player.transform.position - transform.position).normalized;
            direction.y = 0;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 5f * Time.deltaTime);
            }

            if (canAttack && !isAttacking)
            {
                PerformAttack();
            }

            if (!isAttacking)
            {
                UpdateAnimations(0f, false);
            }
        }
        else
        {
            if (agent != null)
            {
                agent.isStopped = false;
                agent.SetDestination(player.transform.position);
            }

            float currentSpeed = agent != null ? agent.velocity.magnitude : 0f;
            UpdateAnimations(currentSpeed, true);
        }

        UpdateHealthBarPosition();
    }

    // ==================== 攻击 ====================
    void PerformAttack()
    {
        if (!canAttack || isDead || isAttacking) return;

        canAttack = false;
        attackCooldownTimer = 0f;

        isAttacking = true;
        attackTimer = 0f;
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Attack");

        if (attackCoroutine != null) StopCoroutine(attackCoroutine);
        attackCoroutine = StartCoroutine(DelayedDamage());
    }

    IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);

        if (player != null && !player.IsDead())
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= enemyData.attackRange)
            {
                player.TakeDamage(enemyData.attackDamage);
                Debug.Log($"👊 {gameObject.name} 攻击玩家，造成 {enemyData.attackDamage} 伤害");
            }
        }
    }

    // ==================== 创建血条（Slider） ====================
    void CreateHealthBar()
    {
        if (healthBarPrefab == null)
        {
            Debug.LogWarning("HealthBar Prefab 未设置");
            return;
        }

        healthBarInstance = Instantiate(healthBarPrefab, transform.position + healthBarOffset, Quaternion.identity);
        healthBarInstance.transform.SetParent(transform);

        // 获取 Slider
        healthSlider = healthBarInstance.GetComponent<Slider>();
        if (healthSlider == null)
        {
            healthSlider = healthBarInstance.GetComponentInChildren<Slider>();
        }

        // 获取 Fill 图片
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
        }

        UpdateHealthBar();
    }

    // ==================== 更新血条位置 ====================
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

    // ==================== 更新血条 ====================
    void UpdateHealthBar()
    {
        if (healthSlider == null || enemyData == null) return;

        float healthPercent = currentHealth / enemyData.health;
        healthSlider.value = healthPercent;

        // 颜色变化
        UpdateHealthBarColor(healthPercent);
    }

    // ==================== 血条颜色 ====================
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

    // ==================== 动画 ====================
    void UpdateAnimations(float speed, bool isMoving)
    {
        if (animator == null || isAttacking) return;
        animator.SetFloat("Speed", speed);
        animator.SetBool("IsMoving", isMoving);
    }

    // ==================== 受伤（平滑扣血） ====================
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

        Debug.Log($"敌人受到 {damage} 伤害，剩余血量: {currentHealth}");

        if (currentHealth <= 0) Die();
    }

    // ==================== 死亡 ====================
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
            Debug.Log($"击杀 {enemyData.enemyName}，获得 {enemyData.coinReward} 金币");
        }

        float delay = enemyData != null ? enemyData.deathAnimationDelay : 2f;
        Destroy(gameObject, delay);
    }

    // ==================== 生成金币 ====================
    void SpawnCoin()
    {
        if (coinPrefab == null) return;

        GameObject coin = Instantiate(coinPrefab, transform.position, Quaternion.identity);
        Coin coinScript = coin.GetComponent<Coin>();
        if (coinScript != null) coinScript.SetValue(enemyData.coinReward);
    }

    // ==================== Gizmos ====================
    void OnDrawGizmosSelected()
    {
        if (enemyData == null || !enemyData.showGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 10f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, enemyData.attackRange);

#if UNITY_EDITOR
        if (enemyData != null)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                $"{enemyData.enemyName}\nHP: {currentHealth}/{enemyData.health}");
        }
#endif
    }
}