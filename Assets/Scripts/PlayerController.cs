using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerController : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private CharacterController controller;
    private Animator animator;

    // ==================== 移动参数 ====================
    [Header("移动参数")]
    public float speed = 4f;
    public float jumpSpeed = 6f;
    public float gravity = 3.5f;
    public float smoothRotation = 10f;
    public float airControl = 0.5f;

    // ==================== 台阶/斜坡参数 ====================
    [Header("台阶/斜坡参数")]
    public float stepOffset = 0.3f;      // 台阶高度
    public float slopeLimit = 45f;        // 最大斜坡角度
    public float skinWidth = 0.08f;       // 皮肤宽度

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

    // 🔥 用于平滑下台阶
    private Vector3 lastGroundPosition = Vector3.zero;
    private bool wasGrounded = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        // 🔥 CharacterController 优化 - 关键设置
        if (controller != null)
        {
            controller.skinWidth = skinWidth;
            controller.minMoveDistance = 0.001f;
            controller.enableOverlapRecovery = true;
            controller.stepOffset = stepOffset;
            controller.slopeLimit = slopeLimit;

            Vector3 pos = transform.position;
            pos.y = 0;
            transform.position = pos;
            Debug.Log($"✅ 角色已贴地: {transform.position}");
            Debug.Log($"✅ Step Offset: {stepOffset}, Slope Limit: {slopeLimit}");
        }

        // 🔥 地面检测设置
        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        if (groundLayerIndex != -1)
        {
            groundLayer = 1 << groundLayerIndex;
            Debug.Log($"✅ 地面检测: Ground 层 (Index: {groundLayerIndex})");
        }
        else
        {
            groundLayer = ~0;
            Debug.LogWarning("⚠️ 切换到 'Everything' 模式");
        }

        // 🔥 平台适配
#if UNITY_ANDROID || UNITY_IOS
        if (jumpButton != null) jumpButton.onClick.AddListener(PerformJump);
        if (actionButton != null) actionButton.onClick.AddListener(PerformAction);
        if (joystickBg != null) joystickBg.gameObject.SetActive(true);
#else
        if (joystickBg != null) joystickBg.gameObject.SetActive(false);
        if (jumpButton != null) jumpButton.gameObject.SetActive(false);
        if (actionButton != null) actionButton.gameObject.SetActive(false);
#endif

        Debug.Log("✅ PlayerController 已启动！");
        Debug.Log($"🏃 Speed: {speed}, Jump: {jumpSpeed}, Gravity: {gravity}, AirControl: {airControl}");
    }

    void Update()
    {
        // ===== 1. 输入 =====
#if UNITY_ANDROID || UNITY_IOS
        HandleTouchInput();
        inputVector = joystickInput;
#else
        HandleKeyboardInput();
        inputVector = keyboardInput;
#endif

        // ===== 2. 移动方向 =====
        Vector3 moveDir = GetMoveDirection(inputVector);
        float inputMagnitude = Mathf.Clamp01(inputVector.magnitude);

        // ===== 3. 旋转 =====
        if (moveDir.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothRotation * Time.deltaTime);
        }

        // ===== 4. 地面检测 =====
        isGrounded = IsGrounded();

        // 🔥 平滑下台阶 - 检测是否从高处落下
        if (isGrounded && !wasGrounded)
        {
            // 刚刚落地，检查是否有高度差
            float heightDifference = transform.position.y - lastGroundPosition.y;
            if (heightDifference > 0.5f) // 如果高度差大于0.5米
            {
                Debug.Log($"🔽 从 {heightDifference:F2} 米高处落下");
                // 触发落地动画
                animator.SetTrigger("Land");
            }
        }

        // 更新地面位置记录
        if (isGrounded)
        {
            lastGroundPosition = transform.position;
        }
        wasGrounded = isGrounded;

        // 🔥 落地时重置跳跃状态
        if (isGrounded && isJumping)
        {
            isJumping = false;
            canJump = true;
            jumpCooldownTimer = 0f;
            Debug.Log("✅ 落地，可以再次跳跃");
        }

        // 🔥 跳跃冷却计时
        if (!canJump)
        {
            jumpCooldownTimer += Time.deltaTime;
            if (jumpCooldownTimer >= jumpCooldown)
            {
                canJump = true;
                jumpCooldownTimer = 0f;
                Debug.Log("✅ 跳跃冷却结束");
            }
        }

        // ===== 5. 水平速度计算 =====
        if (isGrounded && !isJumping)
        {
            if (inputMagnitude > 0.1f)
            {
                velocity.x = moveDir.x * speed;
                velocity.z = moveDir.z * speed;
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
                Vector3 targetVelocity = moveDir * speed * airControl;
                velocity.x = Mathf.Lerp(velocity.x, targetVelocity.x, Time.deltaTime * 5f);
                velocity.z = Mathf.Lerp(velocity.z, targetVelocity.z, Time.deltaTime * 5f);

                Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
                if (horizontalVelocity.magnitude > speed * airControl)
                {
                    horizontalVelocity = horizontalVelocity.normalized * speed * airControl;
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

        // ===== 6. 重力（始终应用） =====
        velocity.y -= gravity * Time.deltaTime;

        // 🔥 7. 移动 - 使用 CharacterController 的 Move 方法
        // CharacterController 会自动处理台阶和斜坡
        controller.Move(velocity * Time.deltaTime);

        // 🔥 8. 强制贴地（当在斜坡上时）
        if (isGrounded && !isJumping && velocity.y <= 0)
        {
            // 检测地面并保持接触
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 1.5f, groundLayer))
            {
                float distanceToGround = hit.distance - 0.1f;
                if (distanceToGround > 0.01f && distanceToGround < 0.5f)
                {
                    // 轻微下拉，保持贴地
                    Vector3 snapDown = Vector3.down * distanceToGround * 0.5f;
                    controller.Move(snapDown);
                }
            }
        }

        // ===== 9. 动画 =====
        UpdateAnimations();
    }

    // ==================== 动画更新 ====================
    void UpdateAnimations()
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        bool isMoving = currentSpeed > 0.05f;
        animator.SetBool("IsMoving", isMoving);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsJumping", isJumping);
        animator.SetFloat("Speed", currentSpeed);

        // 🔥 传递垂直速度给动画（用于下落检测）
        animator.SetFloat("VerticalSpeed", velocity.y);
    }

    // ==================== 键盘输入 ====================
    void HandleKeyboardInput()
    {
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h = 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) h = -1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v = 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v = -1f;

        keyboardInput = new Vector2(h, v);

        if (Input.GetKeyDown(KeyCode.Space)) PerformJump();
        if (Input.GetKeyDown(KeyCode.E)) PerformAction();
    }

    // ==================== 触摸输入 ====================
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

    // ==================== 地面检测 ====================
    bool IsGrounded()
    {
        // 🔥 使用 SphereCast 更准确，特别是对于台阶边缘
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

    // ==================== 跳跃 ====================
    void PerformJump()
    {
        if (!isGrounded || !canJump || isJumping)
        {
            if (!isGrounded) Debug.Log("⚠️ 不在地面，无法跳跃！");
            if (!canJump) Debug.Log($"⚠️ 跳跃冷却中，等待 {jumpCooldownTimer}/{jumpCooldown} 秒");
            if (isJumping) Debug.Log("⚠️ 正在跳跃中，无法再次跳跃！");
            return;
        }

        velocity.y = jumpSpeed;
        isJumping = true;
        canJump = false;
        jumpCooldownTimer = 0f;
        animator.SetTrigger("Jump");

        Debug.Log($"✅ 跳跃！速度: {jumpSpeed}，状态: isJumping={isJumping}, canJump={canJump}");
    }

    // ==================== Action ====================
    void PerformAction()
    {
        Debug.Log("✅ Action！");
        animator.SetTrigger("Action");
    }

    // ==================== 计算移动方向 ====================
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

    void OnDrawGizmosSelected()
    {
        // 🔥 绘制地面检测
        Gizmos.color = Color.yellow;
        float radius = 0.3f; // 假设半径
        if (controller != null) radius = controller.radius * 0.9f;
        Vector3 sphereOrigin = transform.position + Vector3.up * (radius + 0.05f);
        Gizmos.DrawWireSphere(sphereOrigin - Vector3.up * (groundCheckDistance + 0.1f), radius);
        Gizmos.DrawLine(sphereOrigin, sphereOrigin - Vector3.up * (groundCheckDistance + 0.1f));

        // 🔥 绘制台阶高度
        Gizmos.color = Color.cyan;
        Vector3 stepPos = transform.position + Vector3.up * stepOffset;
        Gizmos.DrawWireCube(stepPos, new Vector3(0.5f, 0.02f, 0.5f));
    }
}