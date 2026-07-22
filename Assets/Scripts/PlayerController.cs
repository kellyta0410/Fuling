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

    // ==================== 死亡参数 ====================
    [Header("死亡参数")]
    public float deathDelay = 1.5f; // 死亡后延迟显示 GameOver 的时间

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
    private int kills = 0;
    private bool isDead = false;
    private bool isDying = false; // 标记正在播放死亡动画

    private Vector3 lastGroundPosition = Vector3.zero;
    private bool wasGrounded = false;

    // 存储攻击命中的敌人列表（用于延迟闪红）
    private List<EnemyAI> hitEnemies = new List<EnemyAI>();

    // 标记是否正在等待闪红
    private bool isWaitingForFlash = false;

    // ==================== 属性（供外部访问） ====================
    public float GetHealthPercent() => currentHealth / maxHealth;
    public int GetCoins() => coins;
    public int GetKills() => kills;
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
        // 如果正在播放死亡动画，不执行任何移动逻辑
        if (isDead || isDying) return;

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
        if (!canAttack || isAttacking || isDead || isDying) return;

        isAttacking = true;
        canAttack = false;
        attackTimer = 0f;
        attackCooldownTimer = 0f;
        animator.SetBool("IsAttacking", true);
        animator.SetTrigger("Action");

        // 清空上次攻击的敌人列表
        hitEnemies.Clear();
        isWaitingForFlash = true;

        // 步骤1：检测攻击范围内的敌人，伤害立即生效
        Collider[] hitColliders = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange);
        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                // 伤害立即生效（不闪红）
                enemy.TakeDamageImmediate(attackDamage);
                hitEnemies.Add(enemy);
                Debug.Log($"⚔️ 攻击敌人 {enemy.name}，造成 {attackDamage} 伤害（立即）");
            }
        }

        // 步骤2：启动协程检测动画播放完成，然后触发闪红
        StartCoroutine(WaitForAttackAnimationEnd());
    }

    // ==================== 检测攻击动画播放完成 ====================
    IEnumerator WaitForAttackAnimationEnd()
    {
        // 等待动画播放完成
        while (true)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            // 检查是否在 Action 动画状态，且播放进度接近结束 (0.95 = 95%)
            if (stateInfo.IsName("Action") || stateInfo.IsName("Attack") || stateInfo.IsName("Attacking"))
            {
                if (stateInfo.normalizedTime >= 0.95f)
                {
                    Debug.Log($"💥 攻击动画播放完成！(进度: {stateInfo.normalizedTime:F2})");
                    break;
                }
            }
            else
            {
                // 如果不在攻击动画状态，可能是动画已经切换了，退出循环
                Debug.LogWarning("⚠️ 动画状态已切换，触发闪红");
                break;
            }

            yield return null;
        }

        // 触发闪红
        if (isWaitingForFlash)
        {
            foreach (EnemyAI enemy in hitEnemies)
            {
                if (enemy != null)
                {
                    enemy.FlashRedOnly();
                    Debug.Log($"💥 {enemy.name} 闪红！（攻击动画结束）");
                }
            }
            isWaitingForFlash = false;
        }

        // 重置攻击状态
        isAttacking = false;
        animator.SetBool("IsAttacking", false);
    }

    // ==================== 受伤 ====================
    public void TakeDamage(float damage)
    {
        if (isDead || isDying) return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

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

    // ==================== 死亡（完整版 - 带地面检测，不会悬空） ====================
    void Die()
    {
        if (isDead || isDying) return;

        isDying = true;
        isDead = false;

        // ===== 1. 停止所有移动 =====
        velocity = Vector3.zero;

        // ===== 2. 检测地面并放到地面（防止悬空） =====
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f; // 从角色中心偏上一点开始
        float rayDistance = 10f; // 检测距离
        bool foundGround = false;

        // 方法1：向下射线检测
        Debug.DrawRay(rayOrigin, Vector3.down * rayDistance, Color.red, 2f);
        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, groundLayer))
        {
            Vector3 newPos = transform.position;
            newPos.y = hit.point.y + 0.1f; // 稍微抬高一点防止穿地
            transform.position = newPos;
            foundGround = true;
            Debug.Log($"⬇️ 将玩家放到地面，高度: {newPos.y}");
        }

        // 方法2：如果射线没找到，用 SphereCast
        if (!foundGround)
        {
            float radius = controller != null ? controller.radius * 0.5f : 0.3f;
            if (Physics.SphereCast(rayOrigin, radius, Vector3.down, out hit, rayDistance, groundLayer))
            {
                Vector3 newPos = transform.position;
                newPos.y = hit.point.y + 0.1f;
                transform.position = newPos;
                foundGround = true;
                Debug.Log($"⬇️ 使用 SphereCast 放到地面，高度: {newPos.y}");
            }
        }

        // 方法3：如果还没找到，从角色脚底发射射线
        if (!foundGround)
        {
            float height = controller != null ? controller.height : 2f;
            Vector3 footPos = transform.position - Vector3.up * (height * 0.5f);
            if (Physics.Raycast(footPos, Vector3.down, out hit, rayDistance, groundLayer))
            {
                Vector3 newPos = transform.position;
                newPos.y = hit.point.y + 0.1f;
                transform.position = newPos;
                foundGround = true;
                Debug.Log($"⬇️ 从脚底找到地面，高度: {newPos.y}");
            }
        }

        // 如果还是找不到，设置为 y=0.1
        if (!foundGround)
        {
            Vector3 newPos = transform.position;
            newPos.y = 0.1f;
            transform.position = newPos;
            Debug.LogWarning("⚠️ 未找到地面，放到 y=0.1");
        }

        // ===== 3. 禁用 CharacterController =====
        if (controller != null)
        {
            controller.enabled = false;
        }

        // ===== 4. 禁用其他碰撞体 =====
        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger)
            {
                col.enabled = false;
            }
        }

        // ===== 5. 锁定动画参数，防止被其他代码修改 =====
        animator.SetBool("IsMoving", false);
        animator.SetBool("IsAttacking", false);
        animator.SetBool("IsJumping", false);

        // ===== 6. 播放死亡动画 =====
        animator.SetTrigger("Die");
        Debug.Log($"💀 玩家死亡，位置: {transform.position}");

        // ===== 7. 等待动画完成 =====
        StartCoroutine(WaitForDeathAnimationEnd());
    }

    // ==================== 等待死亡动画播放完成（带延迟） ====================
    IEnumerator WaitForDeathAnimationEnd()
    {
        // 等待一帧让动画状态更新
        yield return null;

        // 等待死亡动画播放完成
        while (true)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            // 检查是否在 Die 动画状态
            if (stateInfo.IsName("Die") || stateInfo.IsName("Death") || stateInfo.IsName("Dead"))
            {
                // 动画播放进度达到 95% 以上
                if (stateInfo.normalizedTime >= 0.95f)
                {
                    Debug.Log($"💀 死亡动画播放完成！(进度: {stateInfo.normalizedTime:F2})");
                    break;
                }
            }
            else
            {
                // 如果已经不在死亡动画状态，检查当前动画是否快结束了
                if (stateInfo.normalizedTime >= 0.95f && stateInfo.length > 0)
                {
                    Debug.Log($"💀 动画状态已切换，但当前动画进度 {stateInfo.normalizedTime:F2}，视为完成");
                    break;
                }
            }

            yield return null;
        }

        // 延迟显示 GameOver
        Debug.Log($"⏳ 等待 {deathDelay} 秒后显示 GameOver...");
        yield return new WaitForSeconds(deathDelay);

        // 死亡动画完成 + 延迟结束，现在显示 GameOver
        isDead = true;
        isDying = false;
        velocity = Vector3.zero;

        // 确保碰撞仍然禁用
        if (controller != null)
        {
            controller.enabled = false;
        }

        if (uiManager != null)
        {
            uiManager.OnPlayerDied();
            Debug.Log($"💀 显示游戏结束面板（死亡动画完成 + 延迟 {deathDelay} 秒）");
        }
    }

    // ==================== 金币收集 ====================
    public void AddCoin(int amount)
    {
        coins += amount;

        if (uiManager != null)
        {
            uiManager.OnPlayerCoinChanged();
        }
    }

    // ==================== 击杀统计 ====================
    public void AddKill()
    {
        kills++;
        Debug.Log($"💀 击杀数: {kills}");

        if (uiManager != null)
        {
            uiManager.OnPlayerKillChanged();
        }
    }

    // ==================== 动画更新 ====================
    void UpdateAnimations()
    {
        if (isDying || isDead) return;

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
        if (isDying || isDead) return;

        float h = 0f, v = 0f;

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h = 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) h = -1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v = 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v = -1f;

        keyboardInput = new Vector2(h, v);

        if (Input.GetKeyDown(KeyCode.Space)) PerformJump();
        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)) PerformAction();

        // 测试快捷键：按 K 键立即死亡
        if (Input.GetKeyDown(KeyCode.K))
        {
            TakeDamage(999f);
        }
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
        if (isDying || isDead) return;

        if (!isGrounded || !canJump || isJumping) return;

        velocity.y = jumpSpeed;
        isJumping = true;
        canJump = false;
        jumpCooldownTimer = 0f;
        animator.SetTrigger("Jump");
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
        isDying = false;
        currentHealth = maxHealth;
        transform.position = Vector3.zero;
        velocity = Vector3.zero;

        // 重新启用控制器
        if (controller != null)
        {
            controller.enabled = true;
            // 恢复原来的碰撞体大小（如果之前修改过）
            controller.radius = 0.5f; // 根据你的实际大小调整
            controller.height = 2f;   // 根据你的实际大小调整
        }

        // 重新启用碰撞体
        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger)
            {
                col.enabled = true;
            }
        }

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

    // ==================== Gizmos 可视化 ====================
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

        // 死亡地面检测可视化
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, Vector3.down * 10f);
    }
}