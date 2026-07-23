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
    public float gravity = 3.5f;
    public float smoothRotation = 10f;

    // ==================== 战斗参数 ====================
    [Header("战斗参数")]
    public float maxHealth = 100f;
    private float currentHealth;
    public float attackRange = 2f;
    public float attackCooldown = 1f;
    public int attackDamage = 20;
    public float attackDuration = 0.5f;
    public float attackDamageDelay = 0.3f;

    // ==================== 地面检测 ====================
    [Header("地面检测")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    // ==================== 死亡参数 ====================
    [Header("死亡参数")]
    public float deathDelay = 1.5f;

    // ==================== 摇杆 UI ====================
    [Header("摇杆 UI（拖拽赋值）")]
    public RectTransform joystickBg;
    public RectTransform joystickHandle;
    public float joystickRadius = 150f;

    // ==================== 按钮 UI ====================
    [Header("按钮 UI（拖拽赋值）")]
    public Button actionButton;

    // ==================== 私有变量 ====================
    private Vector3 velocity = Vector3.zero;
    private Vector2 inputVector = Vector2.zero;
    private Vector2 joystickInput = Vector2.zero;
    private Vector2 keyboardInput = Vector2.zero;
    private bool isDragging = false;
    private Vector2 touchStartPos;

    private bool isGrounded = false;

    private float attackTimer = 0f;
    private bool isAttacking = false;
    private float attackCooldownTimer = 0f;
    private bool canAttack = true;

    private int coins = 0;
    private int kills = 0;
    private bool isDead = false;
    private bool isDying = false;

    private Vector3 lastGroundPosition = Vector3.zero;
    private bool wasGrounded = false;

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

        if (controller != null)
        {
            controller.minMoveDistance = 0.001f;
            controller.enableOverlapRecovery = true;
        }

        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        if (groundLayerIndex != -1)
        {
            groundLayer = 1 << groundLayerIndex;
        }
        else
        {
            groundLayer = ~0;
        }

#if UNITY_ANDROID || UNITY_IOS
        if (actionButton != null) actionButton.onClick.AddListener(PerformAction);
        if (joystickBg != null) joystickBg.gameObject.SetActive(true);
#else
        if (joystickBg != null) joystickBg.gameObject.SetActive(false);
        if (actionButton != null) actionButton.gameObject.SetActive(false);
#endif
    }

    void Update()
    {
        if (isDead || isDying) return;

#if UNITY_ANDROID || UNITY_IOS
        HandleTouchInput();
        inputVector = joystickInput;
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

    // ==================== 攻击 ====================
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

        StartCoroutine(DelayedDamage());
    }

    IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);

        Collider[] hitColliders = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange);
        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                enemy.TakeDamageImmediate(attackDamage);
                Debug.Log($"⚔️ 攻击 {enemy.name}，造成 {attackDamage} 伤害");
            }
        }
    }

    // ==================== 受伤（平滑扣血） ====================
    public void TakeDamage(float damage)
    {
        if (isDead || isDying) return;

        animator.SetTrigger("Hit");

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

            if (uiManager != null)
            {
                uiManager.UpdateHealthUI();
            }

            yield return null;
        }

        currentHealth = targetHealth;

        if (uiManager != null)
        {
            uiManager.UpdateHealthUI();
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ==================== 死亡 ====================
    void Die()
    {
        if (isDead || isDying) return;

        isDying = true;
        isDead = false;

        velocity = Vector3.zero;

        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        float rayDistance = 10f;
        bool foundGround = false;

        Debug.DrawRay(rayOrigin, Vector3.down * rayDistance, Color.red, 2f);
        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, rayDistance, groundLayer))
        {
            Vector3 newPos = transform.position;
            newPos.y = hit.point.y + 0.1f;
            transform.position = newPos;
            foundGround = true;
            Debug.Log($"⬇️ 将玩家放到地面，高度: {newPos.y}");
        }

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

        if (!foundGround)
        {
            Vector3 newPos = transform.position;
            newPos.y = 0.1f;
            transform.position = newPos;
            Debug.LogWarning("⚠️ 未找到地面，放到 y=0.1");
        }

        if (controller != null)
        {
            controller.enabled = false;
        }

        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger)
            {
                col.enabled = false;
            }
        }

        animator.SetBool("IsMoving", false);
        animator.SetBool("IsAttacking", false);

        animator.SetTrigger("Die");
        Debug.Log($"💀 玩家死亡，位置: {transform.position}");

        StartCoroutine(WaitForDeathAnimationEnd());
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
                {
                    Debug.Log($"💀 死亡动画播放完成！(进度: {stateInfo.normalizedTime:F2})");
                    break;
                }
            }
            else
            {
                if (stateInfo.normalizedTime >= 0.95f && stateInfo.length > 0)
                {
                    Debug.Log($"💀 动画状态已切换，但当前动画进度 {stateInfo.normalizedTime:F2}，视为完成");
                    break;
                }
            }

            yield return null;
        }

        Debug.Log($"⏳ 等待 {deathDelay} 秒后显示 GameOver...");
        yield return new WaitForSeconds(deathDelay);

        isDead = true;
        isDying = false;
        velocity = Vector3.zero;

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

    public void AddKill()
    {
        kills++;
        Debug.Log($"💀 击杀数: {kills}");

        if (uiManager != null)
        {
            uiManager.OnPlayerKillChanged();
        }
    }

    void UpdateAnimations()
    {
        if (isDying || isDead) return;

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        animator.SetBool("IsMoving", currentSpeed > 0.05f);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetFloat("Speed", currentSpeed);
        animator.SetFloat("VerticalSpeed", velocity.y);
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

    public void Respawn()
    {
        isDead = false;
        isDying = false;
        currentHealth = maxHealth;
        transform.position = Vector3.zero;
        velocity = Vector3.zero;

        if (controller != null)
        {
            controller.enabled = true;
            controller.radius = 0.5f;
            controller.height = 2f;
        }

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