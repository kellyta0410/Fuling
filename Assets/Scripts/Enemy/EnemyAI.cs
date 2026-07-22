using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private Animator animator;
    private PlayerController player;
    private Renderer enemyRenderer;
    private Color originalColor;
    private NavMeshAgent agent;

    // ==================== 敌人数据 ====================
    [Header("敌人数据")]
    public EnemyData enemyData;

    // ==================== 状态 ====================
    [Header("状态")]
    public bool isDead = false;
    public float currentHealth;

    // ==================== 攻击 ====================
    private float attackCooldownTimer = 0f;
    public bool canAttack = true;

    // ==================== 玩家检测 ====================
    [Header("检测参数")]
    public float detectionRange = 8f;
    public float attackRange = 1.5f;
    public float speed = 2f;
    public int attackDamage = 10;
    public float attackCooldown = 1f;

    // ==================== 动画状态 ====================
    private bool isAttacking = false;
    private float attackAnimationTimer = 0f;
    public float attackAnimationDuration = 0.5f;

    // ==================== 停止距离 ====================
    private float stopDistance;

    void Start()
    {
        // 获取组件
        animator = GetComponent<Animator>();
        player = FindObjectOfType<PlayerController>();
        enemyRenderer = GetComponent<Renderer>();
        agent = GetComponent<NavMeshAgent>();

        // 从数据加载
        if (enemyData != null)
        {
            currentHealth = enemyData.health;
            speed = enemyData.speed;
            attackRange = enemyData.attackRange;
            attackDamage = enemyData.attackDamage;
            attackCooldown = enemyData.attackCooldown;

            stopDistance = attackRange * 0.85f;

            if (agent != null)
            {
                agent.speed = speed;
                agent.stoppingDistance = stopDistance;
                agent.autoBraking = true;

                // ⭐ 避障设置（正确的枚举值）
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; // ✅ 正确
                agent.avoidancePriority = Random.Range(0, 100);
            }

            if (enemyRenderer != null)
            {
                originalColor = enemyRenderer.material.color;
                enemyRenderer.material.color = enemyData.enemyColor;
            }

            transform.localScale = Vector3.one * enemyData.scale;
        }
        else
        {
            Debug.LogWarning("EnemyData 未赋值！使用默认值");
            currentHealth = 50f;
            stopDistance = attackRange * 0.85f;

            if (agent != null)
            {
                agent.speed = speed;
                agent.stoppingDistance = stopDistance;
                agent.radius = 0.4f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(0, 100);
            }
        }
    }

    void Update()
    {
        // ===== 死亡检查 =====
        if (isDead)
        {
            if (agent != null)
            {
                agent.isStopped = true;
            }
            return;
        }

        // ===== 攻击动画计时 =====
        if (isAttacking)
        {
            attackAnimationTimer += Time.deltaTime;
            if (attackAnimationTimer >= attackAnimationDuration)
            {
                isAttacking = false;
                attackAnimationTimer = 0f;
                animator.SetBool("IsAttacking", false);
            }
        }

        // ===== 玩家检查 =====
        if (player == null || player.IsDead())
        {
            if (agent != null)
            {
                agent.isStopped = true;
            }
            UpdateAnimations(0f, false);
            return;
        }

        // ===== 攻击冷却 =====
        if (!canAttack)
        {
            attackCooldownTimer += Time.deltaTime;
            if (attackCooldownTimer >= attackCooldown)
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        // ===== 计算与玩家的距离 =====
        float distance = Vector3.Distance(transform.position, player.transform.position);

        // ===== 状态切换 =====
        if (distance <= attackRange)
        {
            // ===== 攻击状态 =====
            if (agent != null)
            {
                agent.isStopped = true;
            }

            // 面向玩家
            Vector3 direction = (player.transform.position - transform.position).normalized;
            direction.y = 0;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 5f * Time.deltaTime);
            }

            // 攻击
            if (canAttack && !isAttacking)
            {
                PerformAttack();
            }

            // 动画
            if (!isAttacking)
            {
                UpdateAnimations(0f, false);
            }
        }
        else if (distance <= detectionRange)
        {
            // ===== 追逐状态 =====
            if (agent != null)
            {
                agent.isStopped = false;
                agent.SetDestination(player.transform.position);
            }

            // 动画：跑步
            float currentSpeed = agent != null ? agent.velocity.magnitude : 0f;
            UpdateAnimations(currentSpeed, true);
        }
        else
        {
            // ===== 空闲状态 =====
            if (agent != null)
            {
                agent.isStopped = true;
            }

            // 动画：空闲
            UpdateAnimations(0f, false);
        }
    }

    // ==================== 攻击 ====================
    void PerformAttack()
    {
        if (!canAttack || isDead || isAttacking) return;

        canAttack = false;
        attackCooldownTimer = 0f;

        isAttacking = true;
        attackAnimationTimer = 0f;
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Attack");

        if (player != null && !player.IsDead())
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= attackRange)
            {
                player.TakeDamage(attackDamage);
                Debug.Log($"👊 敌人攻击玩家，造成 {attackDamage} 伤害");
            }
        }
    }

    // ==================== 更新动画 ====================
    void UpdateAnimations(float speed, bool isMoving)
    {
        if (animator == null) return;
        if (isAttacking) return;

        animator.SetFloat("Speed", speed);
        animator.SetBool("IsMoving", isMoving);
    }

    // ==================== 受到伤害 ====================
    public void TakeDamageImmediate(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        Debug.Log($"敌人受到 {damage} 伤害，剩余血量: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ==================== 闪红效果 ====================
    public void FlashRedOnly()
    {
        StartCoroutine(FlashRed());
    }

    IEnumerator FlashRed()
    {
        if (enemyRenderer != null)
        {
            enemyRenderer.material.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            enemyRenderer.material.color = enemyData != null ? enemyData.enemyColor : originalColor;
        }
    }

    // ==================== 死亡 ====================
    void Die()
    {
        isDead = true;

        if (agent != null)
        {
            agent.isStopped = true;
        }

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        if (player != null)
        {
            player.AddKill();

            if (enemyData != null)
            {
                for (int i = 0; i < enemyData.coinReward; i++)
                {
                    player.AddCoin(1);
                }
                Debug.Log($"击杀 {enemyData.enemyName}，获得 {enemyData.coinReward} 金币");
            }
        }

        float delay = enemyData != null ? enemyData.deathAnimationDelay : 2f;
        Destroy(gameObject, delay);
    }

    // ==================== Gizmos 可视化 ====================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

#if UNITY_EDITOR
        if (enemyData != null)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                $"{enemyData.enemyName}\nHP: {currentHealth}");
        }
#endif
    }
}