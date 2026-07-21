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

    private float jumpBufferTime = 0.1f;
    private float jumpBufferTimer = 0f;
    private bool hasJumped = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        // ==========================================
        // 🔥 CharacterController 优化（防止卡住/穿模）
        // ==========================================
        if (controller != null)
        {
            controller.skinWidth = 0.01f;
            controller.minMoveDistance = 0.001f;
            controller.enableOverlapRecovery = true;
            Debug.Log("✅ CharacterController 已优化");
            Debug.Log($"   Skin Width: {controller.skinWidth}");
            Debug.Log($"   Min Move Distance: {controller.minMoveDistance}");
        }

        // ==========================================
        // 🔥 地面检测设置
        // ==========================================
        groundLayer = 1 << LayerMask.NameToLayer("Ground");

        if (groundLayer == 0)
        {
            Debug.LogError("❌ 没有找到 'Ground' Layer！");
            groundLayer = ~0;
            Debug.LogWarning("⚠️ 切换到 'Everything' 模式");
        }
        else
        {
            Debug.Log($"✅ 地面检测: {LayerMask.LayerToName(groundLayer)} 层");
        }

        // ==========================================
        // 🔥 平台适配
        // ==========================================
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
        Debug.Log($"🏃 Speed: {speed}, Jump: {jumpSpeed}, Gravity: {gravity}");
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
        bool grounded = IsGrounded();

        if (hasJumped)
        {
            jumpBufferTimer += Time.deltaTime;
            if (jumpBufferTimer > jumpBufferTime)
            {
                hasJumped = false;
                jumpBufferTimer = 0f;
            }
            grounded = false;
        }

        // ===== 5. 速度计算 =====
        if (grounded)
        {
            if (inputMagnitude > 0.1f)
            {
                velocity = moveDir * speed;
            }
            else
            {
                velocity.x = 0;
                velocity.z = 0;
            }

            if (velocity.y < 0)
                velocity.y = 0;
        }
        else
        {
            velocity.x *= 0.98f;
            velocity.z *= 0.98f;
        }

        // ===== 6. 重力 =====
        velocity.y -= gravity * Time.deltaTime;

        // ===== 7. 移动 =====
        controller.Move(velocity * Time.deltaTime);

        // ===== 8. 动画 =====
        UpdateAnimations();
    }

    // ==================== 动画更新 ====================
    void UpdateAnimations()
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        bool isMoving = currentSpeed > 0.05f;
        animator.SetBool("IsMoving", isMoving);
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
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;
        float rayLength = groundCheckDistance + 0.1f;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayLength, groundLayer))
        {
            Debug.DrawRay(rayStart, Vector3.down * rayLength, Color.green);
            return true;
        }

        Debug.DrawRay(rayStart, Vector3.down * rayLength, Color.red);
        return false;
    }

    // ==================== 跳跃 ====================
    void PerformJump()
    {
        if (!IsGrounded())
        {
            Debug.Log("⚠️ 不在地面！");
            return;
        }

        velocity.y = jumpSpeed;
        hasJumped = true;
        jumpBufferTimer = 0f;
        animator.SetTrigger("Jump");
        Debug.Log("✅ 跳跃！");
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
        Gizmos.color = Color.yellow;
        Vector3 rayStart = transform.position + Vector3.up * 0.1f;
        Gizmos.DrawRay(rayStart, Vector3.down * (groundCheckDistance + 0.1f));
    }
}