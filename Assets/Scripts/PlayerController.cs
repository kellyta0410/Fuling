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
    [Tooltip("技能按钮（可不拖，运行时自动创建在普通攻击按钮上方）")]
    public Button skillButton;
    [Tooltip("技能冷却时间（秒）")]
    public float skillCooldown = 15f;
    [Tooltip("闪避按钮（可不拖，运行时自动创建在普通攻击按钮左边）")]
    public Button dodgeButton;
    [Tooltip("闪避冷却时间（秒）")]
    public float dodgeCooldown = 5f;
    [Tooltip("闪避距离")]
    public float dodgeDistance = 4f;
    [Tooltip("闪避持续时间（秒）")]
    public float dodgeDuration = 0.35f;

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

    // ==================== 技能相关 ====================
    private int skillDamage = 0;
    private float skillRange = 3f;
    private float skillCooldownTimer = 0f;
    private bool canUseSkill = true;
    private Image[] cooldownMasks = new Image[3];

    // ==================== 闪避相关 ====================
    private float dodgeCooldownTimer = 0f;
    private bool canDodge = true;
    private bool isDodging = false;
    private float dodgeTimer = 0f;
    private float dodgeSpeed = 10f;
    private Vector3 dodgeDirection = Vector3.zero;

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
        if (skillButton == null) skillButton = CreateSkillButton();
        if (skillButton != null) skillButton.onClick.AddListener(PerformSkillAttack);
        if (dodgeButton == null) dodgeButton = CreateDodgeButton();
        if (dodgeButton != null) dodgeButton.onClick.AddListener(PerformDodge);
        if (joystickBg != null) joystickBg.gameObject.SetActive(true);
        isJoystickEnabled = true;
#else
        if (joystickBg != null) joystickBg.gameObject.SetActive(false);
        if (actionButton != null) actionButton.gameObject.SetActive(false);
        if (skillButton != null) skillButton.gameObject.SetActive(false);
        if (dodgeButton != null) dodgeButton.gameObject.SetActive(false);
        isJoystickEnabled = false;
#endif

        // ⭐ 冷却效果：全暗遮罩 + 亮色从下往上填充，填满后才可点击
        CreateCooldownEffect(actionButton, 0);
        CreateCooldownEffect(skillButton, 1);
        CreateCooldownEffect(dodgeButton, 2);

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

        // ⭐ 从 CharacterData 加载属性
        maxHealth = currentCharacterData.baseHealth;
        speed = currentCharacterData.baseSpeed;              // ⭐ 保持不变
        attackDamage = currentCharacterData.baseAttack;
        attackRange = currentCharacterData.baseRange;
        attackCooldown = currentCharacterData.baseCooldown;
        skillRange = currentCharacterData.baseRange * 1.5f;   // 初始技能范围 = 3（普攻2 × 1.5）

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
            attackRange += normalBonus.attackRangeBonus;     // ⭐ 保持不变
            speed += normalBonus.speedBonus;                 // ⭐ 保持不变（移速加成）
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
            skillRange += skillBonus.attackRangeBonus;          // ⭐ 技能范围加成只加技能范围
            speed += skillBonus.speedBonus;                  // ⭐ 保持不变
            attackCooldown -= skillBonus.cooldownReductionBonus;
            if (attackCooldown < 0.1f) attackCooldown = 0.1f;

            baseAttack = attackDamage;

            // ⭐ 技能伤害 = 基础攻击 × 3 + 技能升级伤害加成；技能冷却受冷却缩减影响
            skillDamage = Mathf.RoundToInt(currentCharacterData.baseAttack * 3f + skillBonus.skillDamageBonus);
            skillCooldown -= skillBonus.cooldownReductionBonus;
            if (skillCooldown < 0.5f) skillCooldown = 0.5f;
        }
        else
        {
            skillDamage = Mathf.RoundToInt(currentCharacterData.baseAttack * 3f);
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

        if (!canUseSkill)
        {
            skillCooldownTimer += Time.deltaTime;
            if (skillCooldownTimer >= skillCooldown)
            {
                canUseSkill = true;
                skillCooldownTimer = 0f;
            }
        }

        if (!canDodge)
        {
            dodgeCooldownTimer += Time.deltaTime;
            if (dodgeCooldownTimer >= dodgeCooldown)
            {
                canDodge = true;
                dodgeCooldownTimer = 0f;
            }
        }

        if (isDodging)
        {
            dodgeTimer += Time.deltaTime;
            if (dodgeTimer >= dodgeDuration)
            {
                isDodging = false;
                dodgeTimer = 0f;
            }
        }

        UpdateCooldownOverlays();

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

        Vector3 moveDir = isDodging ? dodgeDirection : GetMoveDirection(inputVector);
        float inputMagnitude = isDodging ? 1f : Mathf.Clamp01(inputVector.magnitude);

        if (moveDir.magnitude > 0.1f && !isAttacking && !isDodging)
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

        float currentSpeed = isDodging ? dodgeSpeed : (isAttacking ? speed * 0.3f : speed);
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

    // ==================== 技能攻击 ====================

    void PerformSkillAttack()
    {
        if (!canUseSkill || isDead || isDying) return;

        canUseSkill = false;
        skillCooldownTimer = 0f;
        animator.SetTrigger("Action");

        int finalDamage = skillDamage > 0 ? skillDamage : attackDamage * 2;
        StartCoroutine(DelayedSkillDamage(finalDamage));
    }

    IEnumerator DelayedSkillDamage(int damage)
    {
        yield return new WaitForSeconds(attackDamageDelay);

        Collider[] hitColliders = Physics.OverlapSphere(
            transform.position + transform.forward * skillRange * 0.5f,
            skillRange
        );

        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                enemy.TakeDamageImmediate(damage);
                Debug.Log($"技能攻击 {enemy.name}，造成 {damage} 伤害");
            }
        }
    }

    // ⭐ 冷却进度：1 = 就绪，0 → 1 = 冷却推进
    public float GetNormalCooldownProgress()
    {
        if (canAttack) return 1f;
        return attackCooldown > 0 ? Mathf.Clamp01(attackCooldownTimer / attackCooldown) : 1f;
    }

    public float GetSkillCooldownProgress()
    {
        if (canUseSkill) return 1f;
        return skillCooldown > 0 ? Mathf.Clamp01(skillCooldownTimer / skillCooldown) : 1f;
    }

    public float GetDodgeCooldownProgress()
    {
        if (canDodge) return 1f;
        return dodgeCooldown > 0 ? Mathf.Clamp01(dodgeCooldownTimer / dodgeCooldown) : 1f;
    }

    // ==================== 闪避 ====================

    void PerformDodge()
    {
        if (!canDodge || isDead || isDying || isDodging) return;

        Vector3 dir = GetMoveDirection(inputVector);
        if (dir.magnitude < 0.1f) dir = transform.forward;
        dir.y = 0f;
        if (dir.magnitude < 0.1f) dir = Vector3.forward;

        dodgeDirection = dir.normalized;
        dodgeSpeed = dodgeDistance / Mathf.Max(dodgeDuration, 0.01f);
        dodgeTimer = 0f;
        isDodging = true;
        canDodge = false;
        dodgeCooldownTimer = 0f;
    }

    // ⭐ 在普通攻击按钮上方创建技能按钮
    Button CreateSkillButton()
    {
        if (actionButton == null) return null;

        RectTransform srcRt = actionButton.GetComponent<RectTransform>();
        if (srcRt == null || srcRt.parent == null) return null;

        GameObject obj = new GameObject("SkillButton", typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(srcRt.parent, false);

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta = srcRt.sizeDelta;
        rt.anchorMin = srcRt.anchorMin;
        rt.anchorMax = srcRt.anchorMax;
        rt.pivot = srcRt.pivot;
        rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0, srcRt.sizeDelta.y + 24f);

        Image srcImg = actionButton.GetComponent<Image>();
        Image img = obj.GetComponent<Image>();
        img.sprite = srcImg != null ? srcImg.sprite : null;
        img.color = srcImg != null ? srcImg.color : Color.white;
        img.raycastTarget = true;

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = actionButton.colors;
        btn.transition = actionButton.transition;

        Debug.Log("[技能按钮] 已自动创建在普通攻击按钮上方");
        return btn;
    }

    // ⭐ 在普通攻击按钮左边创建闪避按钮
    Button CreateDodgeButton()
    {
        if (actionButton == null) return null;

        RectTransform srcRt = actionButton.GetComponent<RectTransform>();
        if (srcRt == null || srcRt.parent == null) return null;

        GameObject obj = new GameObject("DodgeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(srcRt.parent, false);

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.sizeDelta = srcRt.sizeDelta;
        rt.anchorMin = srcRt.anchorMin;
        rt.anchorMax = srcRt.anchorMax;
        rt.pivot = srcRt.pivot;
        rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(-(srcRt.sizeDelta.x + 24f), 0);

        Image srcImg = actionButton.GetComponent<Image>();
        Image img = obj.GetComponent<Image>();
        img.sprite = srcImg != null ? srcImg.sprite : null;
        img.color = srcImg != null ? srcImg.color : Color.white;
        img.raycastTarget = true;

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = actionButton.colors;
        btn.transition = actionButton.transition;

        Debug.Log("[闪避按钮] 已自动创建在普通攻击按钮左边");
        return btn;
    }

    // ⭐ 创建冷却效果：圆形径向填充遮罩（跟随按钮形状），方形按钮自动退化回方形
    void CreateCooldownEffect(Button button, int index)
    {
        if (button == null) return;

        RectTransform btnRt = button.GetComponent<RectTransform>();
        if (btnRt == null) return;

        // 复用按钮自身的图片，保证遮罩形状和按钮一致（圆形按钮→圆形冷却）
        Sprite btnSprite = null;
        Image btnImg = button.GetComponent<Image>();
        if (btnImg != null && btnImg.sprite != null) btnSprite = btnImg.sprite;
        else if (button.transform.childCount > 0)
        {
            Image ci = button.transform.GetChild(0).GetComponent<Image>();
            if (ci != null) btnSprite = ci.sprite;
        }

        GameObject mask = new GameObject("CooldownMask", typeof(RectTransform), typeof(Image));
        mask.transform.SetParent(btnRt, false);

        RectTransform mrt = mask.GetComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;
        mrt.pivot = new Vector2(0.5f, 0.5f);

        Image mImg = mask.GetComponent<Image>();
        mImg.color = new Color(0f, 0f, 0f, 0.6f);
        mImg.raycastTarget = false;
        if (btnSprite != null)
        {
            mImg.sprite = btnSprite;
            mImg.type = Image.Type.Filled;
            mImg.fillMethod = Image.FillMethod.Radial360;
            mImg.fillOrigin = (int)Image.Origin360.Top;
            mImg.fillAmount = 1f;
        }
        mask.SetActive(false);
        cooldownMasks[index] = mImg;
    }

    // ⭐ 更新三个按钮的冷却效果
    void UpdateCooldownOverlays()
    {
        ApplyCooldown(0, GetNormalCooldownProgress());
        ApplyCooldown(1, GetSkillCooldownProgress());
        ApplyCooldown(2, GetDodgeCooldownProgress());
    }

    void ApplyCooldown(int index, float progress)
    {
        Image mask = cooldownMasks[index];
        if (mask == null) return;

        float p = Mathf.Clamp01(progress);
        mask.gameObject.SetActive(p < 1f);

        if (mask.type == Image.Type.Filled)
        {
            // 圆形/图形按钮：径向填充，冷却时全盖，随冷却减少一圈圈露出
            mask.fillAmount = 1f - p;
        }
        else
        {
            // 无图片时退化为方形遮罩
            RectTransform maskRt = mask.rectTransform;
            RectTransform btnRt = mask.transform.parent as RectTransform;
            float btnHeight = btnRt != null ? btnRt.rect.height : 100f;
            maskRt.sizeDelta = new Vector2(0f, btnHeight * (1f - p));
        }
    }

    public void TakeDamage(float damage)
    {
        if (isDead || isDying) return;

        StartCoroutine(SmoothDamage(damage));
    }

    // 击退：沿 dir 推 distance 米（用 CharacterController.Move，会被墙挡住）
    public void AddKnockback(Vector3 dir, float distance)
    {
        if (isDead || isDying || controller == null) return;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector3 mv = dir.normalized * distance;
        float left = distance;
        while (left > 0.001f)
        {
            float step = Mathf.Min(left, 0.1f);
            controller.Move(mv.normalized * step);
            left -= step;
        }
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
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Space)) PerformSkillAttack();
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) PerformDodge();
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