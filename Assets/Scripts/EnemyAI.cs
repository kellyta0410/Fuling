using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EnemyAI : MonoBehaviour
{
    // ==================== 敌人类型 ====================
    public enum EnemyType
    {
        Basic,
        Fast,
        Heavy
    }

    [Header("敌人类型 (切换后自动填充数值)")]
    public EnemyType enemyType = EnemyType.Basic;

    [Header("⭐ 当前数值 (可手动调整)")]
    public float maxHealth = 50f;
    public int damage = 10;
    public float attackRange = 1.5f;
    public float attackCooldown = 1.5f;
    public int coinDrop = 5;
    public float moveSpeed = 2.5f;
    public float detectionRange = 15f;
    public float stopDistance = 1.5f;

    [Header("追踪设置")]
    public Transform target;

    [Header("⭐ 实时状态 (只读)")]
    public float currentHealth = 50f;
    public string status = "Idle";
    public float distanceToPlayer = 0f;
    public bool isActive = false;
    public bool isAttacking = false;
    public bool isDead = false;

    [Header("受伤闪红")]
    public float flashDuration = 0.15f;
    public Color flashColor = Color.red;
    public Material defaultMaterial;
    public Material flashMaterial;

    [Header("金币预制体 (拖入)")]
    public GameObject coinPrefab;

    // Component references
    private NavMeshAgent agent;
    private Animator animator;
    private Renderer enemyRenderer;

    // State variables
    private float attackTimer = 0f;
    private bool canAttack = true;

    // ==================== ⭐ 编辑器自动更新 ====================
#if UNITY_EDITOR
    void OnValidate()
    {
        // 只在编辑模式下生效，切换类型时自动填充数值
        if (!Application.isPlaying)
        {
            ApplyTypeValues();
        }
    }
#endif

    void Start()
    {
        // Get components
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        enemyRenderer = GetComponentInChildren<Renderer>();

        // Save default material
        if (enemyRenderer != null)
        {
            defaultMaterial = enemyRenderer.material;
        }

        // Create flash material if not set manually
        if (flashMaterial == null)
        {
            flashMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            flashMaterial.color = flashColor;
        }

        // Apply values at start (for runtime)
        ApplyTypeValues();

        // Initialize health
        currentHealth = maxHealth;

        // Find target
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        // Configure NavMeshAgent
        if (agent != null)
        {
            agent.stoppingDistance = stopDistance;
            agent.speed = moveSpeed;
        }

        // Set tag
        gameObject.tag = "Enemy";
    }

    // ==================== 自动填充数值 ====================
    void ApplyTypeValues()
    {
        switch (enemyType)
        {
            case EnemyType.Basic:
                maxHealth = 50f;
                damage = 10;
                attackRange = 1.5f;
                attackCooldown = 1.5f;
                coinDrop = 5;
                moveSpeed = 2.5f;
                detectionRange = 15f;
                stopDistance = 1.5f;
                break;

            case EnemyType.Fast:
                maxHealth = 30f;
                damage = 8;
                attackRange = 1.2f;
                attackCooldown = 1.0f;
                coinDrop = 8;
                moveSpeed = 4.5f;
                detectionRange = 20f;
                stopDistance = 1.2f;
                break;

            case EnemyType.Heavy:
                maxHealth = 120f;
                damage = 20;
                attackRange = 2.0f;
                attackCooldown = 2.5f;
                coinDrop = 15;
                moveSpeed = 1.5f;
                detectionRange = 12f;
                stopDistance = 2.0f;
                break;
        }
    }

    void Update()
    {
        if (isDead || target == null)
        {
            status = "Dead";
            return;
        }

        // Update distance
        distanceToPlayer = Vector3.Distance(transform.position, target.position);
        isActive = distanceToPlayer <= detectionRange;

        // Attack cooldown
        if (!canAttack)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackCooldown)
            {
                canAttack = true;
                attackTimer = 0f;
            }
        }

        float distance = distanceToPlayer;

        // Attack if within range
        if (distance <= attackRange && canAttack && !isAttacking)
        {
            Attack();
        }
        // Chase if outside attack range
        else if (distance > attackRange && agent != null)
        {
            agent.SetDestination(target.position);
            if (animator != null) animator.SetBool("IsMoving", true);
            status = "Chasing";
        }
        else
        {
            if (agent != null && agent.hasPath)
            {
                agent.ResetPath();
                if (animator != null) animator.SetBool("IsMoving", false);
            }
            status = distance <= attackRange ? "Attack Range" : "Idle";
        }

        // Update animation speed
        if (animator != null && agent != null)
        {
            animator.SetFloat("Speed", agent.velocity.magnitude);
        }
    }

    // ==================== Attack ====================
    void Attack()
    {
        isAttacking = true;
        canAttack = false;
        status = "Attacking";
        if (animator != null) animator.SetTrigger("Attack");

        // Check for targets in attack range
        Collider[] hitTargets = Physics.OverlapSphere(transform.position, attackRange);
        foreach (Collider hit in hitTargets)
        {
            PlayerController player = hit.GetComponent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(damage);
                Debug.Log($"⚔️ {enemyType} enemy attacked player, dealt {damage} damage");
            }
        }

        StartCoroutine(ResetAttack());
    }

    IEnumerator ResetAttack()
    {
        yield return new WaitForSeconds(0.5f);
        isAttacking = false;
        status = "Idle";
    }

    // ==================== Take Damage ====================
    public void TakeDamage(float damage)
    {
        if (isDead) return;

        // Flash red effect
        StartCoroutine(FlashRed());

        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);
        if (animator != null) animator.SetTrigger("Hit");

        // Push back
        if (agent != null)
        {
            agent.velocity = Vector3.zero;
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ==================== Flash Red Effect ====================
    IEnumerator FlashRed()
    {
        if (enemyRenderer != null && flashMaterial != null)
        {
            enemyRenderer.material = flashMaterial;
            yield return new WaitForSeconds(flashDuration);
            if (defaultMaterial != null)
            {
                enemyRenderer.material = defaultMaterial;
            }
        }
    }

    // ==================== Death ====================
    protected virtual void Die()
    {
        isDead = true;
        status = "Dead";
        if (animator != null) animator.SetTrigger("Die");

        // Notify player of kill
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            player.AddKill();
        }

        // Drop coins
        DropCoins();

        // Disable NavMeshAgent
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Disable collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Destroy after delay
        Destroy(gameObject, 2f);
    }

    // ==================== Drop Coins ====================
    void DropCoins()
    {
        // Use assigned coinPrefab
        if (coinPrefab != null)
        {
            GameObject coin = Instantiate(coinPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            Coin coinScript = coin.GetComponent<Coin>();
            if (coinScript != null)
            {
                coinScript.SetValue(coinDrop);
            }
        }
        else
        {
            // Try to load from Resources
            GameObject loadedCoin = Resources.Load<GameObject>("Coin");
            if (loadedCoin != null)
            {
                GameObject coin = Instantiate(loadedCoin, transform.position + Vector3.up * 0.5f, Quaternion.identity);
                Coin coinScript = coin.GetComponent<Coin>();
                if (coinScript != null)
                {
                    coinScript.SetValue(coinDrop);
                }
            }
            else
            {
                // Fallback: give coins directly to player
                PlayerController player = FindObjectOfType<PlayerController>();
                if (player != null)
                {
                    player.AddCoin(coinDrop);
                    Debug.Log($"💰 Got {coinDrop} coins");
                }
                else
                {
                    Debug.LogWarning("⚠️ No Coin Prefab assigned and no Player found!");
                }
            }
        }
    }

    // ==================== Gizmos ====================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }

    // ==================== ⭐ 右键菜单：手动刷新数值 ====================
#if UNITY_EDITOR
    [ContextMenu("刷新数值 (Refresh Values)")]
    void RefreshValues()
    {
        ApplyTypeValues();
        Debug.Log($"✅ {enemyType} 数值已刷新！");
    }
#endif
}