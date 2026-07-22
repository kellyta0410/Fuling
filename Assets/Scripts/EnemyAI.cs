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

    [Header("敌人类型 (切换后不会自动填充，需右键手动应用)")]
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

    // ⭐ 标记是否等待闪红后死亡
    private bool pendingDeath = false;

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

    // ==================== 原有的 TakeDamage（保留兼容性） ====================
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

    // ==================== ⭐ 只造成伤害（不闪红，不立即死亡） ====================
    public void TakeDamageImmediate(float damage)
    {
        if (isDead) return;

        // ⭐ 伤害立即生效
        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

        if (animator != null) animator.SetTrigger("Hit");

        // Push back
        if (agent != null)
        {
            agent.velocity = Vector3.zero;
        }

        // ⭐ 如果血量归零，标记等待闪红，但不立即死亡
        if (currentHealth <= 0 && !pendingDeath)
        {
            pendingDeath = true;
            Debug.Log($"💀 {name} 血量归零，等待玩家攻击动画结束闪红后死亡");
        }
    }

    // ==================== ⭐ 只闪红（不造成伤害） ====================
    public void FlashRedOnly()
    {
        if (isDead) return;

        // ⭐ 如果有等待死亡的标记，闪红后死亡
        if (pendingDeath)
        {
            StartCoroutine(FlashRedAndDie());
        }
        else
        {
            StartCoroutine(FlashRed());
        }
    }

    // ==================== ⭐ 闪红然后死亡 ====================
    IEnumerator FlashRedAndDie()
    {
        // ⭐ 先闪红
        yield return StartCoroutine(FlashRed());

        // ⭐ 闪红结束后再死亡
        pendingDeath = false;
        Die();

        Debug.Log($"💀 {name} 闪红结束，死亡！");
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
        if (isDead) return;

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

        // ⭐ 延迟销毁，让死亡动画播放完
        float deathAnimLength = GetDeathAnimationLength();
        Destroy(gameObject, Mathf.Max(deathAnimLength, 1.5f));
    }

    // ==================== ⭐ 获取死亡动画长度 ====================
    float GetDeathAnimationLength()
    {
        if (animator != null)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName("Die") || stateInfo.IsName("Death"))
            {
                return stateInfo.length;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            foreach (AnimationClip clip in clips)
            {
                string clipName = clip.name.ToLower();
                if (clipName.Contains("die") || clipName.Contains("death"))
                {
                    return clip.length;
                }
            }
        }
        return 1.5f;
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

    // ==================== 右键菜单 ====================
#if UNITY_EDITOR
    [ContextMenu("应用类型数值 (Apply Type Values)")]
    void ApplyTypeValuesManually()
    {
        ApplyTypeValues();
        Debug.Log($"✅ 已应用 {enemyType} 的默认数值！");
    }
#endif
}