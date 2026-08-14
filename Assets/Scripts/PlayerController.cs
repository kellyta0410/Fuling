using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;

public class PlayerController : MonoBehaviour
{
    // ==================== 组件引用 ====================
    private CharacterController controller;
    private Animator animator;
    private UIManager uiManager;
    private GameDataManager dataManager;
    private Coroutine shakeRtn;
    private Coroutine hitCornerRtn;

    // ==================== 基础属性（从角色配置加载） ====================
    [Header("基础属性（运行时从角色配置加载）")]
    public float maxHealth = 100f;
    public float speed = 4f;
    [Tooltip("普通攻击期间可移动，但速度降为此倍率×speed（不锁死位移，边打边走）")]
    [Range(0f, 1f)]
    public float attackMoveSpeedFactor = 0.5f;
    [Tooltip("技能期间可移动，但速度降为此倍率×speed（不锁死位移）")]
    [Range(0f, 1f)]
    public float skillMoveSpeedFactor = 0.35f;
    public int attackDamage = 20;
    public float attackRange = 2f;
    public float attackCooldown = 1f;

    [Header("重力参数")]
    public float gravity = 3.5f;
    public float smoothRotation = 10f;

    [Header("攻击参数")]
    public float attackDuration = 0.5f;
    [Tooltip("普通攻击造成伤害的延迟（秒），对齐普通攻击动画命中那一刻")]
    public float attackDamageDelay = 0.35f;
    [Tooltip("普通攻击判定方位角(度)：只有玩家正前方 ±该角度 内的敌人才会收到伤害，背后/侧面打不到；技能不受此限制(全方位)")]
    public float attackFacingAngle = 90f;
    [Tooltip("技能造成伤害的延迟（秒），独立调整以对齐技能动画命中那一刻")]
    public float skillDamageDelay = 0.5f;
    [Header("攻击音效（Clip 放这里，音量读 SettingsManager）")]
    [Tooltip("普通攻击音效，每次攻击随机取一个播放")]
    public AudioClip[] attackSFX;
    [Tooltip("技能攻击音效（单独一条）")]
    public AudioClip skillSFX;
    [Tooltip("玩家被敌人打中的受击音效")]
    public AudioClip hitSFX;

    [Header("攻击特效")]
    [Tooltip("普通攻击时在武器位置生成的特效预制体（如 SlashVFX）")]
    public GameObject attackVFXPrefab;
    [Tooltip("技能攻击时生成的特效预制体（如 SlashVFX 横劈版）")]
    public GameObject skillVFXPrefab;
    [Tooltip("武器挂点（留空则运行时按名字 coin sword 2 自动查找）")]
    public Transform weaponPivot;
    [Tooltip("挥砍特效：前段从微缩撑到全尺寸所占动画时长的比例(0~1)。越小挥得越快")]
    public float slashGrowAmount = 0.45f;
    [Tooltip("挥出缓动指数：越大越接近\"命中瞬间才拉满\"，越小越线性")]
    public float slashGrowExponent = 1.5f;
    [Tooltip("挥砍甩出总角度(度)：弧光在挥出阶段绕竖直轴从 -角度/2 甩到 +角度/2，像被剑砍出来划开空气而不是贴在剑上")]
    public float slashSwingAngle = 70f;
    [Tooltip("挥砍甩出方向：0 = 随连招交替左右挥，1 = 固定从左到右，-1 = 从右到左")]
    public int slashSwingDirection = 0;
    [Tooltip("剑光出现位置在玩家前方的偏移量(米)：让剑光更靠前、落在攻击范围内而非贴在角色身上")]
    public float slashForwardOffset = 0.9f;

    [Header("穿墙兜底")]
    [Tooltip("每帧检查玩家是否与环境墙(实墙/X-Ray半透明墙)重叠，重叠就从墙里水平推出，保证玩家永不穿墙")]
    public bool wallResolveEnabled = true;

    [Header("调试")]
    [Tooltip("场景中选择玩家时是否显示攻击/技能范围 Gizmos")]
    public bool showGizmos = true;

    [Header("地面检测")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.5f;

    [Header("死亡参数")]
    public float deathDelay = 1.5f;

    [Header("复活参数")]
    [Tooltip("看广告复活后短暂无敌时长（秒），避免原地被围殴")]
    public float reviveInvincibleDuration = 3f;
    [Tooltip("复活时推开周围敌人的范围（米）")]
    public float reviveKnockbackRadius = 6f;
    [Tooltip("复活时周围敌人被推开的距离（米）")]
    public float reviveKnockbackDistance = 5f;

    [Header("受击反馈（镜头晃动 + 四角闪红）")]
    [Tooltip("受击时晃动的相机（留空自动用 Camera.main）")]
    public Camera hitCam;
    [Tooltip("屏幕四角的红色 Image（从上到下、从左到右，各自锚在四个角）")]
    public Image[] hitCornerImages;
    [Tooltip("镜头晃动强度（米）")]
    public float hitShakeStrength = 0.35f;
    [Tooltip("镜头晃动时长（秒）")]
    public float hitShakeDuration = 0.25f;
    [Tooltip("四角红色最大透明度（0~1）")]
    public float hitRedAlpha = 0.7f;
    [Tooltip("四角闪红时长（秒）")]
    public float hitRedDuration = 0.35f;
    [Tooltip("自动生成的受击红晕覆盖范围：小于它就全透明，越接近 1 红色范围越大（0~1）")]
    public float vignetteInnerRatio = 0.6f;
    // 受击镜头晃动（CinemachineImpulseSource，由相机 vcam 上的 ImpulseListener 接收）
    private CinemachineImpulseSource hitImpulse;

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
    [Tooltip("攻击命中后敌人被击退的距离（米）")]
    public float enemyKnockbackDistance = 2f;
    [Tooltip("闪避按钮（可不拖，运行时自动创建在普通攻击按钮左边）")]
    public Button dodgeButton;
    [Tooltip("闪避冷却时间（秒）")]
    public float dodgeCooldown = 5f;
    [Tooltip("闪避距离")]
    public float dodgeDistance = 8f;
    [Tooltip("闪避持续时间（秒，越短闪得越快）")]
    public float dodgeDuration = 0.25f;

    // ==================== 运行时数据 ====================
    private float currentHealth;
    private int coins = 0;
    private int kills = 0;
    private bool isDead = false;
    private bool isDying = false;
    private bool isInvincible = false;

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
    private static readonly string[] attackStateNames = { "Attack", "Attack2", "Attack3" };
    private float attackTimer = 0f;
    private bool isAttacking = false;
    private bool queuedAttack = false;   // 攻击排队：攻击中点按钮，等上一个播完再播下一个
    private float attackCooldownTimer = 0f;
    private bool canAttack = true;
    private int comboIndex = 0;

    // ==================== 技能相关 ====================
    private int skillDamage = 0;
    private float skillRange = 3f;
    private float skillCooldownTimer = 0f;
    private bool canUseSkill = true;
    private bool isUsingSkill = false;
    private float skillTimer = 0f;
    private float skillStartYaw = 0f;   // 技能开始瞬间的朝向，用于代码驱动 360° 转圈对齐动画
    public float skillDuration = 0.8f;
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
        // ⭐ root motion 全局关闭：角色位移一律由代码(CharacterController)驱动，动画只负责姿势，
        // 避免动画自带位移叠加造成滑步/贴墙抖动。技能 360° 转圈由 OnAnimatorMove 代码驱动旋转，
        // 但 deltaRotation 需要 applyRootMotion=true 才有值，故技能开启、其余关闭。
        if (animator != null)
            animator.applyRootMotion = false;
        uiManager = FindObjectOfType<UIManager>();
        dataManager = GameDataManager.Instance;

        if (weaponPivot == null)
        {
            weaponPivot = FindDeepChild(transform, "coin sword 2");
        }

        // 受击四角先设为透明（运行时总是重建，避免场景里残留的失效引用挡住红光）
        hitCornerImages = AutoCreateCornerImages();

        // 受击镜头晃动：相机被 CinemachineBrain 驱动（场景里有 vcam）时 transform 每帧被覆写，
        // 所以要把 ImpulseListener 挂在 vcam 上，用冲量做晃动；纯 transform 摇作兜底
        hitCam = hitCam != null ? hitCam : Camera.main;
        SetupHitImpulse();

        if (hitCornerImages != null)
        {
            for (int i = 0; i < hitCornerImages.Length; i++)
            {
                if (hitCornerImages[i] == null) continue;
                Color c = hitCornerImages[i].color;
                c.a = 0f;
                hitCornerImages[i].color = c;
            }
        }

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
        skillRange = currentCharacterData.baseRange * 2f;   // 初始技能范围 = 4（普攻2 × 2）

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
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            // ⭐ 以"攻击动画是否真的播完"为准解锁：
            // 还在攻击状态且 normalizedTime<1（动画未播完）时一直锁定位移/方向输入，
            // 不再被固定 attackDuration 过早解锁（动画 1.5x 播放时 0.5s 兜底会早于动画结束）。
            bool stillInAttack = false;
            for (int i = 0; i < attackStateNames.Length; i++)
            {
                if (stateInfo.shortNameHash == Animator.StringToHash(attackStateNames[i]))
                {
                    stillInAttack = true;
                    break;
                }
            }
            bool animFinished = !stillInAttack || stateInfo.normalizedTime >= 1f;
            float safeCap = stateInfo.length > 0.01f ? Mathf.Max(stateInfo.length, attackDuration) : attackDuration;
            if (animFinished || attackTimer >= safeCap)
            {
                isAttacking = false;
                attackTimer = 0f;
                animator.SetBool("IsAttacking", false);
                // 攻击结束恢复 root motion 关闭（位移由代码驱动；技能/死亡时再按需开启）
                animator.applyRootMotion = false;
                // ⭐ 攻击中允许转向，结束后保持玩家当前朝向（不做回正）。
            }

            // 攻击点排队：上一个播完，接着播下一个连击
            if (!isAttacking && queuedAttack)
            {
                queuedAttack = false;
                canAttack = true;
                attackCooldownTimer = 0f;
                BeginAttack();
            }
        }

        if (isUsingSkill)
        {
            skillTimer += Time.deltaTime;
            AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            bool skillActive = cur.IsName("Skill Attack") || next.IsName("Skill Attack");

            float safetyCap = skillDuration;
            if (cur.IsName("Skill Attack") && cur.length > 0.01f)
                safetyCap = cur.length + 0.5f;
            else if (next.IsName("Skill Attack") && next.length > 0.01f)
                safetyCap = next.length + 0.5f;

            if (!skillActive || skillTimer >= safetyCap)
            {
                isUsingSkill = false;
                skillTimer = 0f;
                // ⭐ 技能中允许转向，结束后保持玩家当前朝向（不做回正）。
            }
        }

        Vector3 moveDir = isDodging ? dodgeDirection : GetMoveDirection(inputVector);
        float inputMagnitude = isDodging ? 1f : Mathf.Clamp01(inputVector.magnitude);

        // 普通移动/攻击时都朝输入方向转向（跟手）。技能由动画自带旋转主导，不受输入转向影响。
        // 攻击中可转向但位移仍锁死；闪避锁死朝向。
        if (moveDir.magnitude > 0.1f && !isDodging && !isUsingSkill)
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

        // 攻击/技能期间都锁死水平位移（定位准确，配合动画判定；不叠加任何 root 位移）
        float currentSpeed = isDodging ? dodgeSpeed : (isAttacking || isUsingSkill ? 0f : speed);

        if (isAttacking || isUsingSkill)
        {
            velocity.x = 0f;
            velocity.z = 0f;
        }
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
                // 输入松开：水平速度快速归零（之前 *0.99 衰减太慢导致"跑后迟迟不 idle"）
                float decel = currentSpeed * Time.deltaTime * 8f;
                velocity.x = Mathf.MoveTowards(velocity.x, 0f, decel);
                velocity.z = Mathf.MoveTowards(velocity.z, 0f, decel);
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

    // 穿墙兜底：普通移动/闪避/击退/复活等无论怎么动，
    // 只要 CharacterController 与任何竖直墙(实墙或 X-Ray 半透明墙)重叠就把玩家水平推出墙外。
    // X-Ray 只是把墙渲染半透明，碰撞体一直都在，所以实墙/XRay 一视同仁。
    void LateUpdate()
    {
        if (!wallResolveEnabled) return;
        if (isDead || isDying || controller == null || !controller.enabled) return;

        WallPenetrationResolve.Resolve(controller, transform);
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
        if (isDead || isDying) return;

        // ⭐ 技能播放期间忽略普通攻击：防止 animator.Play 打断技能动画导致技能中途被切
        if (isUsingSkill) return;

        // 正在攻击：这次点击缓存起来，当前攻击播完后再自动播下一段（不打断、不吞掉输入）
        if (isAttacking)
        {
            queuedAttack = true;
            return;
        }

        if (!canAttack) return;

        BeginAttack();
    }

    void BeginAttack()
    {
        isAttacking = true;
        canAttack = false;
        attackTimer = 0f;
        attackCooldownTimer = 0f;
        animator.SetBool("IsAttacking", true);
        // 起手先面对玩家输入方向（若有输入），否则保持当前朝向；攻击中仍可转向，位置锁死不位移。
        Vector3 inputDir = GetMoveDirection(inputVector);
        if (inputDir.magnitude > 0.1f)
            transform.rotation = Quaternion.LookRotation(inputDir);

        // ⭐ 普通攻击时临时关闭 root motion：攻击需精确站位/判定，动画自带位移会拖走角色造成滑步。
        // 其余状态(移动/技能/闪避)保持全局开启，位移由 OnAnimatorMove 转交 CharacterController。
        // 攻击不锁死位移：保留当前 velocity，后续由 attackMoveSpeedFactor 减速带出移动。
        animator.applyRootMotion = false;

        string stateName = attackStateNames[comboIndex];
        animator.ResetTrigger("Action");
        animator.Play(stateName, 0, 0f);
        comboIndex = (comboIndex + 1) % attackStateNames.Length;

        PlayRandomAttackSFX();
        SpawnAttackVFX(stateName);
        StartCoroutine(DelayedDamage());
    }

    void SpawnAttackVFX(string stateName)
    {
        if (attackVFXPrefab == null || weaponPivot == null) return;

        // 剑光角度在发起攻击时确定（协程延迟后 comboIndex 可能已切到下一招）
        float yaw;
        switch (comboIndex)
        {
            case 0: yaw = -45f; break;
            case 1: yaw = 45f; break;
            default: yaw = 0f; break;
        }
        if (slashSwingDirection != 0) yaw = slashSwingDirection * 45f; // 手动覆盖方向

        // 剑光出现在动画中段（命中帧附近），位置在玩家正前方中间、对齐剑尖朝向。
        StartCoroutine(SpawnAttackVFXDelayed(stateName, yaw));
    }

    IEnumerator SpawnAttackVFXDelayed(string stateName, float yaw)
    {
        // 等一帧让 Animator 切到新攻击状态，再取动画总时长计算"中段"延迟
        yield return null;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        float total = info.shortNameHash == Animator.StringToHash(stateName)
            ? info.length
            : attackDuration;
        if (total <= 0.01f) total = attackDuration;

        // 动画中段出现（可让命中帧与剑光同步；如果动画还没切到该状态就按固定比例兜底）
        yield return new WaitForSeconds(Mathf.Max(total * 0.5f, attackDamageDelay));

        // 若攻击已经结束（例如连击被更快输入打断），就不再生成
        if (!isAttacking) yield break;

        Quaternion spawnRot = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);

        // 位置：玩家正前方中间，对齐剑尖（刀尖）的高度与朝向。
        Vector3 tip = GetWeaponTipPosition();
        Vector3 spawnPos = transform.position + Vector3.up * (tip.y - transform.position.y);
        spawnPos += transform.forward * slashForwardOffset;

        GameObject vfx = Instantiate(attackVFXPrefab, spawnPos, spawnRot);

        // 关键：挂到角色根而不是剑(weaponPivot)。挂在剑上会随剑的挥砍抖动旋转，
        // 读起来像"贴死在剑上"；挂到角色根后特效保持一次稳定挥砍轨迹。
        vfx.transform.SetParent(transform, true);

        // 完整尺寸直接出现，播放一次后随攻击结束销毁
        StartCoroutine(DespawnAttackVFX(vfx, stateName));
    }

    // 特效完整出现一次，等攻击动画播放完再销毁。不做"从零放大 + 甩出"动画。
    IEnumerator DespawnAttackVFX(GameObject vfx, string stateName)
    {
        yield return null; // 等一帧让 Animator 切换到新状态
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        float total = info.shortNameHash == Animator.StringToHash(stateName)
            ? info.length
            : attackDuration;
        if (total <= 0.01f) total = attackDuration;
        total = Mathf.Max(total, 0.05f);

        yield return new WaitForSeconds(total);
        if (vfx != null) Destroy(vfx);
    }

    // 用剑渲染网格包围盒，取离剑根(手柄)最远的角点当作刀尖
    Vector3 GetWeaponTipPosition()
    {
        Renderer[] renderers = weaponPivot.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Vector3 center = weaponPivot.position;
        Vector3 extents = Vector3.zero;

        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!has)
            {
                center = r.bounds.center;
                extents = r.bounds.extents;
                has = true;
            }
            else
            {
                center = Vector3.Lerp(center, r.bounds.center, 0.5f);
                extents = Vector3.Max(extents, r.bounds.extents);
            }
        }

        if (!has) return weaponPivot.position;

        Vector3 pivot = weaponPivot.position;
        Vector3 tip = center;
        float best = float.MinValue;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                    float d = (corner - pivot).sqrMagnitude;
                    if (d > best)
                    {
                        best = d;
                        tip = corner;
                    }
                }
            }
        }
        return tip;
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
                // ⭐ 普通攻击只打正前方扇形：判定命中点在玩家前方，背后的敌人即使进了球形范围也不掉血。
                // 技能(isUsingSkill 走 DelayedSkillDamage)始终保持全方位，不受此限制。
                Vector3 toEnemy = enemy.transform.position - transform.position;
                toEnemy.y = 0f;
                if (Vector3.Angle(transform.forward, toEnemy) > attackFacingAngle)
                    continue;

                // ⭐ 近身/贴脸（≤ attackRange*0.5）豁免墙挡判定：肉搏距离内必然命中，
                // 避免敌人被击退贴墙(碰撞体微嵌墙面)或玩家贴墙时，射线先撞墙误判"隔墙打不到"。
                float distToEnemy = toEnemy.magnitude;
                if (distToEnemy > attackRange * 0.5f &&
                    WallPenetrationResolve.IsBlockedBetween(transform.position, enemy.transform.position))
                    continue;

                enemy.TakeDamageImmediate(attackDamage);
                enemy.AddKnockback(transform.forward, enemyKnockbackDistance);
            }
        }
    }

    // ==================== 技能攻击 ====================

    void PerformSkillAttack()
    {
        if (!canUseSkill || isDead || isDying) return;

        canUseSkill = false;
        skillCooldownTimer = 0f;
        isUsingSkill = true;
        skillTimer = 0f;
        skillStartYaw = transform.eulerAngles.y;   // 记录技能起始朝向，供代码驱动转圈
        // ⭐ 技能需要动画旋转(360° 转圈)，强制开启 root motion：
        // 若玩家从普攻中切入技能，普攻已把 applyRootMotion 关掉，必须恢复，否则 deltaRotation 恒为零。
        animator.applyRootMotion = true;
        // 只用 CrossFade 强制切到技能动画：不再 SetTrigger。
        // SetTrigger 的 SkillAction 会被状态机 Idle→Skill Attack 过渡消费（或残留），
        // 导致播完回 Idle 时 trigger 残留再次触发 → 技能播放两次/中间被切。
        animator.ResetTrigger("SkillAction");
        animator.CrossFade("Skill Attack", 0.08f, 0);

        int finalDamage = skillDamage > 0 ? skillDamage : attackDamage * 2;
        PlaySkillSFX();
        StartCoroutine(DelayedSkillDamage(finalDamage));
        SpawnSkillVFX();
    }

    // 技能横劈特效：出现在玩家正前方中间，剑光横着（绕竖直轴 90°）劈向正前方。
    void SpawnSkillVFX()
    {
        if (skillVFXPrefab == null) return;

        // 延迟到技能动画中段出现（和命中帧对齐）
        StartCoroutine(SpawnSkillVFXDelayed());
    }

    IEnumerator SpawnSkillVFXDelayed()
    {
        yield return null;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        float total = info.IsName("Skill Attack") ? info.length : skillDuration;
        if (total <= 0.01f) total = skillDuration;
        yield return new WaitForSeconds(Mathf.Max(total * 0.5f, skillDamageDelay));

        if (!isUsingSkill) yield break;

        // 横劈：剑光平面绕竖直轴转 90°，从横向前方横向劈出
        Quaternion spawnRot = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);

        Vector3 tip = weaponPivot != null ? GetWeaponTipPosition() : transform.position + Vector3.up;
        Vector3 spawnPos = transform.position + Vector3.up * (tip.y - transform.position.y);
        spawnPos += transform.forward * slashForwardOffset;

        GameObject vfx = Instantiate(skillVFXPrefab, spawnPos, spawnRot);
        vfx.transform.SetParent(transform, true);

        // 技能播完销毁
        yield return new WaitForSeconds(Mathf.Max(total, 0.1f));
        if (vfx != null) Destroy(vfx);
    }

    // 接管 root motion：全局开启时把各状态动画自带位移转交给 CharacterController 应用，
    // 旋转丢弃（朝向由代码跟随输入控制）。仅普通攻击时（applyRootMotion 被临时关闭）丢弃位移，
    // 避免动画 root motion 拖走角色干扰攻击判定。技能(Skill Attack)是 360° 转圈动画，
    // 需应用动画自带旋转才能对齐 preview。
    private void OnAnimatorMove()
    {
        if (animator == null || controller == null) return;

        // ⭐ 普通攻击已临时关闭 root motion：本帧不应用动画位移（位置锁死，判定才准）。
        // 其余状态全局开启，水平位移跟动画；垂直由代码重力统一处理，避免动画 Y 干扰落地判定。
        if (isAttacking && !animator.applyRootMotion) return;

        // 技能 360° 转圈：不依赖 deltaRotation(导入烘焙到骨骼时恒为零导致不转)，
        // 改由代码按动画进度从起始朝向转满一整圈，保证与 preview 一致且位移锁死。
        if (isUsingSkill)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            float t = state.IsName("Skill Attack") ? state.normalizedTime
                    : next.IsName("Skill Attack") ? next.normalizedTime
                    : skillTimer / Mathf.Max(skillDuration, 0.01f);
            t = Mathf.Clamp01(t);
            transform.rotation = Quaternion.Euler(0f, skillStartYaw + 360f * t, 0f);
            return;
        }

        // 其余状态(移动/受击/死亡前等)：丢弃动画位移，位移一律由代码控制（防滑步/贴墙抖动）
        // 理由：角色移动由 Update 里的 controller.Move(velocity) 全权驱动，
        // 若把 deltaPosition 也叠进来会双重位移，贴墙时角色被反复顶挤，判定点飘移。
        // 因此这里不应用任何位移，只让 Animator 正常播放动画姿势。
    }

    IEnumerator DelayedSkillDamage(int damage)
    {
        yield return new WaitForSeconds(skillDamageDelay);

        // 以角色为中心的全方位球形判定（360° 旋转技能，前后左右对称）
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, skillRange);

        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                // ⭐ 近身/贴脸（≤ skillRange*0.4）豁免墙挡判定：近身必然命中，
                // 避免敌人贴墙/玩家贴墙时射线先撞墙误判"隔墙打不到"。
                Vector3 toEnemy = enemy.transform.position - transform.position;
                toEnemy.y = 0f;
                if (toEnemy.magnitude > skillRange * 0.4f &&
                    WallPenetrationResolve.IsBlockedBetween(transform.position, enemy.transform.position))
                    continue;

                enemy.TakeDamageImmediate(damage);
                enemy.AddKnockback(transform.forward, enemyKnockbackDistance * 1.5f);
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
        // 攻击/技能期间禁止闪避：闪避会覆盖 currentSpeed 造成位移，绕过攻击锁定
        if (!canDodge || isDead || isDying || isDodging || isAttacking || isUsingSkill) return;

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
        if (isDead || isDying || isInvincible) return;

        PlayPlayerHitSFX();
        PlayHitFeedback();
        StartCoroutine(SmoothDamage(damage));
    }

    // 受击反馈：镜头微微晃动 + 四角闪红
    void PlayHitFeedback()
    {
        if (hitCornerImages != null && hitCornerImages.Length > 0)
        {
            if (hitCornerRtn != null) StopCoroutine(hitCornerRtn);
            hitCornerRtn = StartCoroutine(CornerFlashRoutine());
        }
        if (shakeRtn != null) StopCoroutine(shakeRtn);
        shakeRtn = StartCoroutine(CameraShakeRoutine());
    }

    IEnumerator CornerFlashRoutine()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(hitRedDuration, 0.001f);
            float a = hitRedAlpha * Mathf.Sin(t * Mathf.PI); // 0→红→0
            for (int i = 0; i < hitCornerImages.Length; i++)
            {
                if (hitCornerImages[i] == null) continue;
                Color c = hitCornerImages[i].color;
                c.a = a;
                hitCornerImages[i].color = c;
            }
            yield return null;
        }
        for (int i = 0; i < hitCornerImages.Length; i++)
        {
            if (hitCornerImages[i] == null) continue;
            Color c = hitCornerImages[i].color;
            c.a = 0f;
            hitCornerImages[i].color = c;
        }
        hitCornerRtn = null;
    }

    // 相机是 CinemachineBrain 驱动的：把 ImpulseListener 挂到场景 vcam 上，用冲量做受击晃动
    void SetupHitImpulse()
    {
        if (hitCam == null) return;
        if (hitCam.GetComponent<CinemachineBrain>() == null) return;

        CinemachineVirtualCameraBase vcam = null;
        CinemachineVirtualCameraBase[] all = FindObjectsOfType<CinemachineVirtualCameraBase>();
        foreach (CinemachineVirtualCameraBase v in all)
        {
            if (v != null && v.isActiveAndEnabled) { vcam = v; break; }
        }
        if (vcam == null) return;

        if (vcam.GetComponent<CinemachineImpulseListener>() == null)
            vcam.gameObject.AddComponent<CinemachineImpulseListener>();

        hitImpulse = GetComponent<CinemachineImpulseSource>();
        if (hitImpulse == null)
        {
            hitImpulse = gameObject.AddComponent<CinemachineImpulseSource>();
            CinemachineImpulseDefinition d = hitImpulse.m_ImpulseDefinition;
            d.m_AmplitudeGain = 1f;
            d.m_FrequencyGain = 2f;
            d.m_TimeEnvelope.m_AttackTime = 0.02f;
            d.m_TimeEnvelope.m_SustainTime = 0f;
            d.m_TimeEnvelope.m_DecayTime = 0.18f;
        }
    }

    IEnumerator CameraShakeRoutine()
    {
        Camera cam = hitCam != null ? hitCam : Camera.main;
        if (cam == null) { shakeRtn = null; yield break; }

        // 两条并行：冲量（vcam 接管时生效）+ 纯 transform 晃动（没被接管时兜底）
        Vector3 basePos = cam.transform.localPosition;
        Quaternion baseRot = cam.transform.localRotation;
        float elapsed = 0f;
        float impulseTimer = 0f;
        while (elapsed < hitShakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / hitShakeDuration;
            float decay = 1f - t;
            float strength = hitShakeStrength * decay;

            Vector3 offset = new Vector3(
                (Mathf.PerlinNoise(elapsed * 60f, 0f) * 2f - 1f),
                (Mathf.PerlinNoise(0f, elapsed * 60f) * 2f - 1f),
                0f) * strength;

            cam.transform.localPosition = basePos + offset;
            cam.transform.localRotation = baseRot * Quaternion.Euler(
                (Mathf.PerlinNoise(elapsed * 60f, 5f) * 2f - 1f) * 2f * decay,
                (Mathf.PerlinNoise(8f, elapsed * 60f) * 2f - 1f) * 2f * decay, 0f);

            if (hitImpulse != null)
            {
                impulseTimer += Time.deltaTime;
                if (impulseTimer >= 0.045f)
                {
                    impulseTimer = 0f;
                    Vector3 dir = new Vector3(
                        Random.Range(-1f, 1f),
                        Random.Range(-0.7f, 0.7f),
                        Random.Range(-1f, 1f));
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                    dir.Normalize();
                    hitImpulse.GenerateImpulse(dir * hitShakeStrength * 2.5f * (0.4f + 0.6f * decay));
                }
            }

            yield return null;
        }

        cam.transform.localPosition = basePos;
        cam.transform.localRotation = baseRot;
        shakeRtn = null;
    }

    // 自动生成全屏径向渐晕红（中心透明、越靠边越红、柔和朦胧的受击效果）
    Image[] AutoCreateCornerImages()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("HitOverlayCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            canvas.sortingOrder = 999;
        }

        // 生成径向渐晕纹理：中心透明，越往边缘越不透明
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float half = size * 0.5f;
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half + 0.5f, half + 0.5f)) / half;
                float a = Mathf.SmoothStep(vignetteInnerRatio, 1f, d);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);

        GameObject go = new GameObject("HitVignette", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.sprite = sp;
        img.color = new Color(1f, 0.06f, 0.06f, 0f);
        img.raycastTarget = false;
        return new Image[] { img };
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
        animator.applyRootMotion = false;
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

        // 死亡动画播完立即弹面板，不再额外等待（原 deathDelay 已移除）
        isDead = true;
        isDying = false;
        velocity = Vector3.zero;

        if (controller != null) controller.enabled = false;

        // 交给 UIManager 决定：第一次死亡弹复活面板，其余直接结算
        if (uiManager != null)
        {
            uiManager.HandlePlayerDied(this);
        }
        else
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.GameOver(false);
            }
        }
    }

    public void AddCoin(int amount)
    {
        coins += amount;
        if (uiManager != null) uiManager.OnPlayerCoinChanged();
    }

    // ==================== 玩家音效（Clip 在 Inspector 上配，走 AudioManager 播放池） ====================

    void PlayRandomAttackSFX()
    {
        if (attackSFX == null || attackSFX.Length == 0) return;
        AudioClip clip = attackSFX[Random.Range(0, attackSFX.Length)];
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip, transform.position);
    }

    void PlaySkillSFX()
    {
        if (skillSFX == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(skillSFX, transform.position);
    }

    void PlayPlayerHitSFX()
    {
        if (hitSFX == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(hitSFX, transform.position);
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
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) PerformDodge();
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

    // ==================== 看广告原地复活 ====================

    /// <summary>
    /// 原地复活：满血、恢复控制、推开周围敌人、短暂无敌。
    /// 金币/击杀/计时等全部保留（由调用方保证未进入 GameOver 结算）。
    /// </summary>
    public void ReviveInPlace()
    {
        if (!isDead) return;

        isDead = false;
        isDying = false;
        isInvincible = true;
        currentHealth = maxHealth;
        velocity = Vector3.zero;

        if (controller != null) controller.enabled = true;

        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            if (col != null && !col.isTrigger) col.enabled = true;
        }

        // 动画复位：从死亡状态回到正常移动
        if (animator != null)
        {
            animator.ResetTrigger("Die");
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsAttacking", false);
            animator.Play("Idle", 0, 0f);
        }

        // 推开周围敌人，给复活留出安全空间
        Vector3 playerPos = transform.position;
        foreach (var enemy in FindObjectsOfType<EnemyAI>())
        {
            if (enemy == null || enemy.isDead) continue;
            Vector3 toEnemy = enemy.transform.position - playerPos;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > reviveKnockbackRadius * reviveKnockbackRadius) continue;
            if (toEnemy.sqrMagnitude < 0.0001f) toEnemy = Vector3.forward;
            enemy.AddKnockback(toEnemy.normalized, reviveKnockbackDistance);
        }

        if (uiManager != null)
        {
            uiManager.OnPlayerRevived();
        }

        // 无敌计时
        StartCoroutine(ReviveInvincibleRoutine(reviveInvincibleDuration));

        Debug.Log("⚡ 玩家原地复活（满血 + 短暂无敌 + 推开敌人）");
    }

    IEnumerator ReviveInvincibleRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        isInvincible = false;
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        Gizmos.color = Color.yellow;
        float radius = 0.3f;
        if (controller != null) radius = controller.radius * 0.9f;
        Vector3 sphereOrigin = transform.position + Vector3.up * (radius + 0.05f);
        Gizmos.DrawWireSphere(sphereOrigin - Vector3.up * (groundCheckDistance + 0.1f), radius);

        // ⭐ 普通攻击：正前方扇形（含方位角限制，与伤害判定一致）
        Gizmos.color = Color.red;
        DrawHorizontalArc(transform.position, attackRange, attackFacingAngle, 32);

        // 技能：全方位球形（不受 attackFacingAngle 限制）
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, skillRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, Vector3.down * 10f);
    }

    // 在水平面画一个以 transform 为中心、半径为 arcRadius、张开角 ±arcAngle 度的扇形（线段拼合）。
    void DrawHorizontalArc(Vector3 center, float arcRadius, float arcAngle, int segments)
    {
        Quaternion facing = Quaternion.LookRotation(transform.forward, Vector3.up);
        Vector3 left = facing * Quaternion.Euler(0f, -arcAngle, 0f) * Vector3.forward;
        Vector3 right = facing * Quaternion.Euler(0f, arcAngle, 0f) * Vector3.forward;

        // 两条边
        Gizmos.DrawLine(center, center + left * arcRadius);
        Gizmos.DrawLine(center, center + right * arcRadius);

        // 弧线（分段直线）
        Vector3 prev = center + left * arcRadius;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(-arcAngle, arcAngle, t);
            Vector3 dir = facing * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 cur = center + dir * arcRadius;
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }

    Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        if (parent.name == childName) return parent;

        foreach (Transform child in parent)
        {
            Transform result = FindDeepChild(child, childName);
            if (result != null) return result;
        }
        return null;
    }
}