using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayerController : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private CharacterController controller;
    private Animator animator;
    private UIManager uiManager;
    private GameDataManager dataManager;

    // ==================== 基础属性（从角色配置加载） ====================
    [Header("基础属性（运行时从角色配置加载）")]
    public float maxHealth = 100f;
    public float speed = 4f;
    public int attackDamage = 20;
    public float attackRange = 2f;
    public float attackCooldown = 1f;

    [Header("重力参数")]
    public float gravity = 3.5f;
    public float smoothRotation = 10f;

    [Header("攻击参数")]
    public float attackDuration = 0.5f;
    public float attackDamageDelay = 0.3f;

    [Header("地面检测")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    [Header("死亡参数")]
    public float deathDelay = 1.5f;

    // ==================== 摇杆 UI ====================
    public RectTransform joystickBg;
    public RectTransform joystickHandle;
    public float joystickRadius = 150f;

    [Header("按钮 UI")]
    public Button actionButton;

    // ==================== 运行时数据 ====================
    private float currentHealth;
    private int coins = 0;
    private int kills = 0;
    private bool isDead = false;
    private bool isDying = false;

    // ==================== 移动相关 ====================
    private Vector3 velocity = Vector3.zero;
    private Vector2 inputVector = Vector2.zero;
    private Vector2 joystickInput = Vector2.zero;
    private Vector2 keyboardInput = Vector2.zero;
    private bool isDragging = false;
    private Vector2 touchStartPos;
    private bool isGrounded = false;
    private Vector3 lastGroundPosition = Vector3.zero;
    private bool wasGrounded = false;

    // ==================== 攻击相关 ====================
    private float attackTimer = 0f;
    private bool isAttacking = false;
    private float attackCooldownTimer = 0f;
    private bool canAttack = true;

    // ==================== 角色配置 ====================
    private CharacterData currentCharacterData;

    // ==================== 摇杆控制 ====================
    private bool isJoystickEnabled = true;

    // ⭐ 是否可以移动（倒计时控制）
    private bool canMove = false;

    // ==================== BUFF 系统支持 ====================
    private float baseSpeed;
    private int baseAttack;

    // ==================== 属性 ====================
    public float HealthPercent => currentHealth / maxHealth;
    public int GetCoins() => coins;
    public int GetKills() => kills;
    public bool IsDead() => isDead;
    public CharacterData GetCharacterData() => currentCharacterData;

    public float GetHealthPercent()
    {
        return currentHealth / maxHealth;
    }

    public void SetCanMove(bool canMove)
    {
        this.canMove = canMove;
        if (!canMove)
        {
            inputVector = Vector2.zero;
            joystickInput = Vector2.zero;
            keyboardInput = Vector2.zero;
            if (isDragging)
            {
                isDragging = false;
                if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
            }
        }
    }

    // ==================== Unity 生命周期 ====================

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        uiManager = FindObjectOfType<UIManager>();
        dataManager = GameDataManager.Instance;

        LoadCharacterData();

        currentHealth = maxHealth;

        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        if (groundLayerIndex != -1)
        {
            groundLayer = 1 << groundLayerIndex;
        }
        else
        {
            groundLayer = ~0;
        }

#if UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL
        if (actionButton != null) actionButton.onClick.AddListener(PerformAction);
        if (joystickBg != null) joystickBg.gameObject.SetActive(true);
        isJoystickEnabled = true;
#else
        if (joystickBg != null) joystickBg.gameObject.SetActive(false);
        if (actionButton != null) actionButton.gameObject.SetActive(false);
        isJoystickEnabled = false;
#endif

        if (uiManager != null)
        {
            uiManager.UpdateHealthUI();
            uiManager.UpdateCoinUI();
            uiManager.UpdateKillUI();
        }

        canMove = false;
    }

    void LoadCharacterData()
    {
        if (dataManager != null)
        {
            currentCharacterData = dataManager.CurrentCharacter;
        }
        else
        {
            Debug.LogWarning("GameDataManager 未找到，尝试从 PlayerPrefs 加载角色");
        }

        if (currentCharacterData == null)
        {
            currentCharacterData = LoadFallbackCharacter();
        }

        if (currentCharacterData == null)
        {
            Debug.LogWarning("未找到角色数据，使用 Inspector 默认属性");
            baseSpeed = speed;
            baseAttack = attackDamage;
            return;
        }

        maxHealth = currentCharacterData.baseHealth;
        speed = currentCharacterData.baseSpeed;
        attackDamage = currentCharacterData.baseAttack;
        attackRange = currentCharacterData.baseAttackRange;
        attackCooldown = currentCharacterData.baseAttackCooldown;

        baseSpeed = speed;
        baseAttack = attackDamage;

        if (dataManager == null) return;

        // ===== 应用普通攻击升级加成 =====
        string characterName = currentCharacterData.characterName;
        var normalConfig = currentCharacterData.normalAttackConfig;

        if (normalConfig != null)
        {
            var normalBonus = dataManager.GetSkillTotalBonus("NormalAttack", characterName, normalConfig);
            attackDamage += normalBonus.attackBonus;
            attackRange += normalBonus.attackRangeBonus;
            speed += normalBonus.speedBonus;
            attackCooldown -= normalBonus.cooldownReductionBonus;
            if (attackCooldown < 0.1f) attackCooldown = 0.1f;

            baseSpeed = speed;
            baseAttack = attackDamage;
        }

        // ===== 应用技能攻击升级加成 =====
        var skillConfig = currentCharacterData.skillAttackConfig;

        if (skillConfig != null)
        {
            var skillBonus = dataManager.GetSkillTotalBonus("SkillAttack", characterName, skillConfig);
            attackDamage += skillBonus.attackBonus;
            attackRange += skillBonus.attackRangeBonus;
            // 技能攻击也可以加速度、冷却等
            // speed += skillBonus.speedBonus;

            baseAttack = attackDamage;
        }

        Debug.Log($"加载角色: {currentCharacterData.characterName}，攻击: {attackDamage}，范围: {attackRange}，速度: {speed}，冷却: {attackCooldown}");
    }

    CharacterData LoadFallbackCharacter()
    {
        string name = PlayerPrefs.GetString("SelectedCharacter", "");
        if (string.IsNullOrEmpty(name)) return null;

        CharacterData[] characters = Resources.LoadAll<CharacterData>("CharacterData");
        foreach (var character in characters)
        {
            if (character != null && character.characterName == name)
                return character;
        }
        return null;
    }

    void Update()
    {
        if (isDead || isDying) return;

        if (!canMove)
        {
            inputVector = Vector2.zero;
            joystickInput = Vector2.zero;
            keyboardInput = Vector2.zero;
            if (isDragging)
            {
                isDragging = false;
                if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
            }
            return;
        }

#if UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL
        if (isJoystickEnabled)
        {
            HandleTouchInput();
            inputVector = joystickInput;
        }
        else
        {
            joystickInput = Vector2.zero;
            inputVector = Vector2.zero;
            if (isDragging)
            {
                isDragging = false;
                if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
            }
        }
#else
        HandleKeyboardInput();
        inputVector = keyboardInput;
#endif

        if (!canAttack)
        {
            attackCooldownTimer += Time.deltaTime;
            if (attackCooldownTimer >= attackCooldown)
            {
                canAttack = true;
                attackCooldownTimer = 0f;
            }
        }

        if (isAttacking)
        {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackDuration)
            {
                isAttacking = false;
                attackTimer = 0f;
                animator.SetBool("IsAttacking", false);
            }
        }

        Vector3 moveDir = GetMoveDirection(inputVector);
        float inputMagnitude = Mathf.Clamp01(inputVector.magnitude);

        if (moveDir.magnitude > 0.1f && !isAttacking)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothRotation * Time.deltaTime);
        }

        isGrounded = IsGrounded();

        if (isGrounded && !wasGrounded)
        {
            float heightDifference = transform.position.y - lastGroundPosition.y;
            if (heightDifference > 0.5f)
            {
                animator.SetTrigger("Land");
            }
        }

        if (isGrounded)
        {
            lastGroundPosition = transform.position;
        }
        wasGrounded = isGrounded;

        float currentSpeed = isAttacking ? speed * 0.3f : speed;
        if (isGrounded)
        {
            if (inputMagnitude > 0.1f)
            {
                velocity.x = moveDir.x * currentSpeed;
                velocity.z = moveDir.z * currentSpeed;
            }
            else
            {
                velocity.x = 0;
                velocity.z = 0;
            }
        }
        else
        {
            if (inputMagnitude > 0.1f)
            {
                Vector3 targetVelocity = moveDir * currentSpeed * 0.5f;
                velocity.x = Mathf.Lerp(velocity.x, targetVelocity.x, Time.deltaTime * 5f);
                velocity.z = Mathf.Lerp(velocity.z, targetVelocity.z, Time.deltaTime * 5f);

                Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
                if (horizontalVelocity.magnitude > currentSpeed * 0.5f)
                {
                    horizontalVelocity = horizontalVelocity.normalized * currentSpeed * 0.5f;
                    velocity.x = horizontalVelocity.x;
                    velocity.z = horizontalVelocity.z;
                }
            }
            else
            {
                velocity.x *= 0.99f;
                velocity.z *= 0.99f;
            }
        }

        velocity.y -= gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        if (isGrounded && velocity.y <= 0)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 1.5f, groundLayer))
            {
                float distanceToGround = hit.distance - 0.1f;
                if (distanceToGround > 0.01f && distanceToGround < 0.5f)
                {
                    Vector3 snapDown = Vector3.down * distanceToGround * 0.5f;
                    controller.Move(snapDown);
                }
            }
        }

        UpdateAnimations();
    }

    public void SetJoystickEnabled(bool enabled)
    {
#if UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL
        isJoystickEnabled = enabled;

        if (joystickBg != null)
        {
            joystickBg.gameObject.SetActive(enabled);
        }
        if (joystickHandle != null)
        {
            joystickHandle.gameObject.SetActive(enabled);
        }
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(enabled);
        }

        if (!enabled)
        {
            isDragging = false;
            joystickInput = Vector2.zero;
            if (joystickHandle != null)
            {
                joystickHandle.anchoredPosition = Vector2.zero;
            }
        }
#endif
    }

    void PerformAction()
    {
        PerformAttack();
    }

    void PerformAttack()
    {
        if (!canAttack || isAttacking || isDead || isDying) return;

        isAttacking = true;
        canAttack = false;
        attackTimer = 0f;
        attackCooldownTimer = 0f;
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Action");

        StartCoroutine(DelayedDamage());
    }

    IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);

        Collider[] hitColliders = Physics.OverlapSphere(
            transform.position + transform.forward * attackRange * 0.5f,
            attackRange
        );

        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                enemy.TakeDamageImmediate(attackDamage);
                Debug.Log($"攻击 {enemy.name}，造成 {attackDamage} 伤害");
            }
        }
    }

    public void TakeDamage(float damage)
    {
        if (isDead || isDying) return;

        StartCoroutine(SmoothDamage(damage));
    }

    IEnumerator SmoothDamage(float damage)
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

            if (uiManager != null) uiManager.UpdateHealthUI();

            yield return null;
        }

        currentHealth = targetHealth;
        if (uiManager != null) uiManager.UpdateHealthUI();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        if (isDead || isDying) return;

        isDying = true;
        velocity = Vector3.zero;

        PlaceOnGround();

        if (controller != null) controller.enabled = false;

        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger) col.enabled = false;
        }

        animator.SetBool("IsMoving", false);
        animator.SetBool("IsAttacking", false);
        animator.SetTrigger("Die");

        Debug.Log($"玩家死亡");

        StartCoroutine(WaitForDeathAnimationEnd());

        if (ComboManager.Instance != null)
        {
            ComboManager.Instance.OnPlayerDeath();
        }
    }

    void PlaceOnGround()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        float rayDistance = 10f;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, groundLayer))
        {
            Vector3 newPos = transform.position;
            newPos.y = hit.point.y + 0.1f;
            transform.position = newPos;
        }
        else
        {
            Vector3 newPos = transform.position;
            newPos.y = 0.1f;
            transform.position = newPos;
        }
    }

    IEnumerator WaitForDeathAnimationEnd()
    {
        yield return null;

        while (true)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.IsName("Die") || stateInfo.IsName("Death") || stateInfo.IsName("Dead"))
            {
                if (stateInfo.normalizedTime >= 0.95f)
                    break;
            }
            else
            {
                if (stateInfo.normalizedTime >= 0.95f && stateInfo.length > 0)
                    break;
            }

            yield return null;
        }

        yield return new WaitForSeconds(deathDelay);

        isDead = true;
        isDying = false;
        velocity = Vector3.zero;

        if (controller != null) controller.enabled = false;

        if (uiManager != null)
        {
            uiManager.OnPlayerDied();
        }

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            gm.GameOver(false);
        }
    }

    public void AddCoin(int amount)
    {
        coins += amount;
        if (uiManager != null) uiManager.OnPlayerCoinChanged();
    }

    public void AddKill()
    {
        kills++;
        if (uiManager != null) uiManager.OnPlayerKillChanged();
    }

    // ==================== BUFF 系统接口 ====================

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        if (uiManager != null) uiManager.UpdateHealthUI();
    }

    public void RestoreFullHealth()
    {
        currentHealth = maxHealth;
        if (uiManager != null) uiManager.UpdateHealthUI();
    }

    public void ApplySpeedMultiplier(float multiplier)
    {
        if (baseSpeed <= 0f) baseSpeed = speed;
        speed = baseSpeed * multiplier;
    }

    public void ApplyAttackAdditive(int additive)
    {
        if (baseAttack <= 0) baseAttack = attackDamage;
        attackDamage = baseAttack + additive;
    }

    // ==================== 原有方法 ====================

    void UpdateAnimations()
    {
        if (isDying || isDead) return;

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        animator.SetBool("IsMoving", currentSpeed > 0.05f);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetFloat("Speed", currentSpeed);
    }

    void HandleKeyboardInput()
    {
        if (isDying || isDead) return;

        float h = 0f, v = 0f;

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h = 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) h = -1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v = 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v = -1f;

        keyboardInput = new Vector2(h, v);

        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)) PerformAction();
        if (Input.GetKeyDown(KeyCode.K)) TakeDamage(999f);
    }

    void HandleTouchInput()
    {
        if (isDying || isDead) return;

        if (Input.touchCount == 0)
        {
            if (isDragging)
            {
                isDragging = false;
                joystickInput = Vector2.zero;
                if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
            }
            return;
        }

        foreach (Touch touch in Input.touches)
        {
            bool isLeftSide = touch.position.x < Screen.width / 2;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    if (isLeftSide && joystickBg != null)
                    {
                        isDragging = true;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            joystickBg, touch.position, null, out touchStartPos
                        );
                        if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
                    }
                    break;

                case TouchPhase.Moved:
                    if (isDragging && isLeftSide && joystickBg != null)
                    {
                        Vector2 localPoint;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            joystickBg, touch.position, null, out localPoint
                        );

                        Vector2 delta = localPoint - touchStartPos;
                        float distance = Mathf.Min(delta.magnitude, joystickRadius);
                        Vector2 clampedDelta = delta.normalized * distance;

                        if (joystickHandle != null) joystickHandle.anchoredPosition = clampedDelta;
                        joystickInput = clampedDelta / joystickRadius;
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (isDragging && isLeftSide)
                    {
                        isDragging = false;
                        joystickInput = Vector2.zero;
                        if (joystickHandle != null) joystickHandle.anchoredPosition = Vector2.zero;
                    }
                    break;
            }
        }
    }

    bool IsGrounded()
    {
        if (controller == null) return false;

        float radius = controller.radius * 0.9f;
        float checkDistance = groundCheckDistance + 0.1f;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);

        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, checkDistance, groundLayer))
        {
            Debug.DrawLine(origin, hit.point, Color.green);
            return true;
        }

        Debug.DrawRay(origin, Vector3.down * checkDistance, Color.red);
        return false;
    }

    Vector3 GetMoveDirection(Vector2 input)
    {
        if (input.magnitude < 0.1f) return Vector3.zero;

        Vector3 forward = Camera.main.transform.forward;
        Vector3 right = Camera.main.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * input.y + right * input.x).normalized;
    }

    public void ResetState()
    {
        isDead = false;
        isDying = false;
        currentHealth = maxHealth;
        velocity = Vector3.zero;
        coins = 0;
        kills = 0;

        if (controller != null) controller.enabled = true;

        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger) col.enabled = true;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        float radius = 0.3f;
        if (controller != null) radius = controller.radius * 0.9f;
        Vector3 sphereOrigin = transform.position + Vector3.up * (radius + 0.05f);
        Gizmos.DrawWireSphere(sphereOrigin - Vector3.up * (groundCheckDistance + 0.1f), radius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, Vector3.down * 10f);
    }
}