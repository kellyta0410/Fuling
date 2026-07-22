using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private CharacterController controller;
    private Animator animator;
    private UIManager uiManager;

    // ==================== 移动参数 ====================
    [Header("移动参数")]
    public float speed = 4f;
    public float jumpSpeed = 6f;
    public float gravity = 3.5f;
    public float smoothRotation = 10f;
    public float airControl = 0.5f;

    // ==================== 台阶/斜坡参数 ====================
    [Header("台阶/斜坡参数")]
    public float stepOffset = 0.3f;
    public float slopeLimit = 45f;
    public float skinWidth = 0.08f;

    // ==================== 战斗参数 ====================
    [Header("战斗参数")]
    public float maxHealth = 100f;
    private float currentHealth;
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    public int attackDamage = 20;
    public float attackDuration = 0.5f;

    // ==================== 地面检测 ====================
    [Header("地面检测")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    // ==================== 摇杆 UI ====================
    [Header("摇杆 UI（拖拽赋值）")]
    public RectTransform joystickBg;
    public RectTransform joystickHandle;
    public float joystickRadius = 150f;

    // ==================== 按钮 UI ====================
    [Header("按钮 UI（拖拽赋值）")]
    public Button jumpButton;
    public Button actionButton;

    // ==================== 私有变量 ====================
    private Vector3 velocity = Vector3.zero;
    private Vector2 inputVector = Vector2.zero;
    private Vector2 joystickInput = Vector2.zero;
    private Vector2 keyboardInput = Vector2.zero;
    private bool isDragging = false;
    private Vector2 touchStartPos;

    private bool isJumping = false;
    private bool canJump = true;
    private float jumpCooldown = 0.5f;
    private float jumpCooldownTimer = 0f;
    private bool isGrounded = false;

    private float attackTimer = 0f;
    private bool isAttacking = false;
    private float attackCooldownTimer = 0f;
    private bool canAttack = true;

    private int coins = 0;
    private int kills = 0;              // ⭐ 击杀数
    private bool isDead = false;

    private Vector3 lastGroundPosition = Vector3.zero;
    private bool wasGrounded = false;

    // ==================== 属性（供外部访问） ====================
    public float GetHealthPercent() => currentHealth / maxHealth;
    public int GetCoins() => coins;
    public int GetKills() => kills;     // ⭐ 获取击杀数
    public bool IsDead() => isDead;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        uiManager = FindObjectOfType<UIManager>();

        currentHealth = maxHealth;

        // CharacterController 设置
        if (controller != null)
        {
            controller.skinWidth = skinWidth;
            controller.minMoveDistance = 0.001f;
            controller.enableOverlapRecovery = true;
            controller.stepOffset = stepOffset;
            controller.slopeLimit = slopeLimit;
        }

        // 地面检测设置
        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        if (groundLayerIndex != -1)
        {
            groundLayer = 1 << groundLayerIndex;
        }
        else
        {
            groundLayer = ~0;
        }

        // 平台适配
#if UNITY_ANDROID || UNITY_IOS
        if (jumpButton != null) jumpButton.onClick.AddListener(PerformJump);
        if (actionButton != null) actionButton.onClick.AddListener(PerformAction);
        if (joystickBg != null) joystickBg.gameObject.SetActive(true);
#else
        if (joystickBg != null) joystickBg.gameObject.SetActive(false);
        if (jumpButton != null) jumpButton.gameObject.SetActive(false);
        if (actionButton != null) actionButton.gameObject.SetActive(false);
#endif
    }

    void Update()
    {
        if (isDead) return;

        // ===== 输入 =====
#if UNITY_ANDROID || UNITY_IOS
        HandleTouchInput();
        inputVector = joystickInput;
#else
        HandleKeyboardInput();
        inputVector = keyboardInput;
#endif

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

        // ===== 攻击状态 =====
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

        // ===== 移动逻辑 =====
        Vector3 moveDir = GetMoveDirection(inputVector);
        float inputMagnitude = Mathf.Clamp01(inputVector.magnitude);

        // 旋转
        if (moveDir.magnitude > 0.1f && !isAttacking)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothRotation * Time.deltaTime);
        }

        // 地面检测
        isGrounded = IsGrounded();

        // 下落检测
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

        // 跳跃重置
        if (isGrounded && isJumping)
        {
            isJumping = false;
            canJump = true;
            jumpCooldownTimer = 0f;
        }

        // 跳跃冷却
        if (!canJump)
        {
            jumpCooldownTimer += Time.deltaTime;
            if (jumpCooldownTimer >= jumpCooldown)
            {
                canJump = true;
                jumpCooldownTimer = 0f;
            }
        }

        // 水平速度计算
        float currentSpeed = isAttacking ? speed * 0.3f : speed;
        if (isGrounded && !isJumping)
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
                Vector3 targetVelocity = moveDir * currentSpeed * airControl;
                velocity.x = Mathf.Lerp(velocity.x, targetVelocity.x, Time.deltaTime * 5f);
                velocity.z = Mathf.Lerp(velocity.z, targetVelocity.z, Time.deltaTime * 5f);

                Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
                if (horizontalVelocity.magnitude > currentSpeed * airControl)
                {
                    horizontalVelocity = horizontalVelocity.normalized * currentSpeed * airControl;
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

        // 重力
        velocity.y -= gravity * Time.deltaTime;

        // 移动
        controller.Move(velocity * Time.deltaTime);

        // 强制贴地
        if (isGrounded && !isJumping && velocity.y <= 0)
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

        // 动画
        UpdateAnimations();
    }

    // ==================== 攻击（原 Action）====================
    void PerformAction()
    {
        PerformAttack();
    }

    // ==================== 攻击逻辑 ====================
    void PerformAttack()
    {
        if (!canAttack || isAttacking || isDead) return;

        isAttacking = true;
        canAttack = false;
        attackTimer = 0f;
        attackCooldownTimer = 0f;
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Action");

        // 检测攻击范围内的敌人
        Collider[] hitEnemies = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange);
        foreach (Collider hit in hitEnemies)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                enemy.TakeDamage(attackDamage);
                Debug.Log($"⚔️ 攻击敌人 {enemy.name}，造成 {attackDamage} 伤害");
            }
        }

        StartCoroutine(AttackEffect());
    }

    IEnumerator AttackEffect()
    {
        yield return new WaitForSeconds(attackDuration);
        isAttacking = false;
        animator.SetBool("IsAttacking", false);
    }

    // ==================== 受伤 ====================
    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

        // 通知 UI 更新
        if (uiManager != null)
        {
            uiManager.OnPlayerDamaged();
        }

        animator.SetTrigger("Hit");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ==================== 死亡 ====================
    void Die()
    {
        isDead = true;
        animator.SetTrigger("Die");

        // 通知 UI 显示 GameOver
        if (uiManager != null)
        {
            uiManager.OnPlayerDied();
        }

        Debug.Log("💀 玩家死亡！");
    }

    // ==================== 金币收集 ====================
    public void AddCoin(int amount)
    {
        coins += amount;

        // 通知 UI 更新
        if (uiManager != null)
        {
            uiManager.OnPlayerCoinChanged();
        }
    }

    // ==================== ⭐ 击杀统计 ====================
    public void AddKill()
    {
        kills++;
        Debug.Log($"💀 击杀数: {kills}");

        // 通知 UI 更新（如果有击杀数显示）
        if (uiManager != null)
        {
            uiManager.OnPlayerKillChanged();
        }
    }

    // ==================== 动画更新 ====================
    void UpdateAnimations()
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        animator.SetBool("IsMoving", currentSpeed > 0.05f);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsJumping", isJumping);
        animator.SetFloat("Speed", currentSpeed);
        animator.SetFloat("VerticalSpeed", velocity.y);
    }

    // ==================== 输入处理 ====================
    void HandleKeyboardInput()
    {
        float h = 0f, v = 0f;

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h = 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) h = -1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v = 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v = -1f;

        keyboardInput = new Vector2(h, v);

        if (Input.GetKeyDown(KeyCode.Space)) PerformJump();
        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)) PerformAction();
    }

    void HandleTouchInput()
    {
        if (Input.touchCount == 0)
        {
            if (isDragging)
            {
                isDragging = false;
                joystickInput = Vector2.zero;
                if (joystickHandle != null)
                    joystickHandle.anchoredPosition = Vector2.zero;
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
                        if (joystickHandle != null)
                            joystickHandle.anchoredPosition = Vector2.zero;
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

                        if (joystickHandle != null)
                            joystickHandle.anchoredPosition = clampedDelta;

                        joystickInput = clampedDelta / joystickRadius;
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (isDragging && isLeftSide)
                    {
                        isDragging = false;
                        joystickInput = Vector2.zero;
                        if (joystickHandle != null)
                            joystickHandle.anchoredPosition = Vector2.zero;
                    }
                    break;
            }
        }
    }

    void PerformJump()
    {
        if (!isGrounded || !canJump || isJumping || isDead) return;

        velocity.y = jumpSpeed;
        isJumping = true;
        canJump = false;
        jumpCooldownTimer = 0f;
        animator.SetTrigger("Jump");
    }

    bool IsGrounded()
    {
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
        if (input.magnitude < 0.1f)
            return Vector3.zero;

        Vector3 forward = Camera.main.transform.forward;
        Vector3 right = Camera.main.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * input.y + right * input.x).normalized;
    }

    // ==================== 公共方法 ====================
    public void Respawn()
    {
        isDead = false;
        currentHealth = maxHealth;
        transform.position = Vector3.zero;
        velocity = Vector3.zero;
        animator.SetTrigger("Respawn");

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.UpdateHealthUI();
        }
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnDrawGizmosSelected()
    {
        // 地面检测可视化
        Gizmos.color = Color.yellow;
        float radius = 0.3f;
        if (controller != null) radius = controller.radius * 0.9f;
        Vector3 sphereOrigin = transform.position + Vector3.up * (radius + 0.05f);
        Gizmos.DrawWireSphere(sphereOrigin - Vector3.up * (groundCheckDistance + 0.1f), radius);

        // 攻击范围可视化
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange);
    }
}