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
    public float attackFacingAngle = 150f;
    [Tooltip("技能造成伤害的延迟（秒），独立调整以对齐技能动画命中那一刻")]
    public float skillDamageDelay = 0.5f;        // 兜底：找不到“Skill Attack”动画时长时使用
    public float skillHitFraction = 0.5f;          // 伤害落在“Skill Attack”动画归一化时间的位置（0~1），跟动画对齐用
    [Header("攻击音效（Clip 放这里，音量读 SettingsManager）")]
    [Tooltip("普通攻击音效，每次攻击随机取一个播放")]
    public AudioClip attackSFX;
    [Tooltip("技能攻击音效（单独一条）")]
    public AudioClip skillSFX;
    [Tooltip("玩家被敌人打中的受击音效，每次随机取一个")]
    public AudioClip[] hitSFX;
    [Tooltip("闪避音效（闪避开始时播放一次）")]
    public AudioClip dashSFX;
    [Tooltip("奔跑脚步声：玩家跑步时按间隔随机取一个播放（放 4 个脚步音频，随机播）")]
    public AudioClip[] runSFX;
    [Tooltip("奔跑脚步间隔（秒）：越短步伐越快")]
    public float runFootstepInterval = 0.3f;
    [Tooltip("闪避特效存活时间（秒）：从闪避开始算总时长，闪避本体 0.2s + 残影 = 该值，到期自动隐藏")]
    public float dashEffectLifetime = 0.6f;

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
    [Tooltip("普通攻击命中后敌人被击退的距离（米）——比技能击退更短，避免普通攻击把敌人推太远/推进墙角")]
    public float normalAttackKnockbackDistance = 1.2f;
    [Tooltip("闪避撞开敌人的半径（米）：闪避冲刺路径附近这个范围里的敌人都被推开")]
    public float dodgePushRadius = 1.6f;
    [Tooltip("闪避撞开敌人的推力（米）：被闪避冲到的敌人沿闪避方向推开的距离")]
    public float dodgePushDistance = 2f;
    [Tooltip("闪避按钮（可不拖，运行时自动创建在普通攻击按钮左边）")]
    public Button dodgeButton;
    [Tooltip("闪避冷却时间（秒）")]
    public float dodgeCooldown = 5f;
    [Tooltip("闪避距离")]
    public float dodgeDistance = 8f;
    [Tooltip("闪避持续时间（秒，越短闪得越快）")]
    public float dodgeDuration = 0.25f;
    [Tooltip("玩家身上的 DashEffect 子物体（GameObject，把玩家下的 DashEffect 拖进来）；闪避期间激活，其余时间隐藏。留空则自动按名字查找名为 DashEffect 的子物体")]
    public GameObject dashEffect;

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
    // ⭐ 跑步脚步音计时器
    private float runFootstepTimer = 0f;
    // ⭐ 闪避特效延时隐藏协程（闪避太快，让残影多留一会儿更明显）
    private Coroutine dashEffectHideCoroutine;

    // ==================== 攻击相关 ====================
    private static readonly string[] attackStateNames = { "Attack", "Attack2", "Attack3" };
    private float attackTimer = 0f;
    private bool isAttacking = false;
    private bool queuedSkill = false;    // 技能排队：攻击中按技能，等当前攻击播完再放技能
    private bool queuedAttack = false;   // 攻击排队：技能中按攻击，等技能播完再接普通攻击
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
    public float skillDuration = 0.8f;
    private Image[] cooldownMasks = new Image[3];

    // ==================== 闪避相关 ====================
    private float dodgeCooldownTimer = 0f;
    private bool canDodge = true;
    private bool isDodging = false;
    private float dodgeTimer = 0f;
    private float dodgeSpeed = 10f;
    private Vector3 dodgeDirection = Vector3.zero;
    // ⭐ 闪避撞开去重：同一段闪避期间已撞过的敌人不再重复推，避免每帧重复 AddKnockback
    private readonly System.Collections.Generic.HashSet<EnemyAI> dodgePushedEnemies = new System.Collections.Generic.HashSet<EnemyAI>();
    // ⭐ 闪避期间被临时忽略碰撞的敌人（闪避结束恢复碰撞，保证"敌人挡住玩家"只在正常移动时生效）
    private readonly System.Collections.Generic.List<EnemyCollisionBlocker> dodgeIgnoredBlockers = new System.Collections.Generic.List<EnemyCollisionBlocker>();

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
        // Apply Root Motion 是否生效由 Inspector 决定，脚本不参与。
        uiManager = FindObjectOfType<UIManager>();
        dataManager = GameDataManager.Instance;

        // ⭐ 闪避特效默认隐藏，只在闪避期间激活（即使场景里某处设置成了激活，也强制关掉）
        // 同时兜底解析：字段没拖或 prefab 引用失效时，按名字自动找玩家身上的 DashEffect 子物体
        ResolveDashEffect();
        if (dashEffect != null) dashEffect.SetActive(false);

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
        // 桌面端也保留可点击的技能按钮（同时仍支持 Q / 空格 触发）
        if (skillButton == null) skillButton = CreateSkillButton();
        if (skillButton != null)
        {
            skillButton.gameObject.SetActive(true);
            skillButton.onClick.RemoveAllListeners();
            skillButton.onClick.AddListener(PerformSkillAttack);
        }
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
        // ⭐ 技能范围先按基础值给个初值；真正的技能范围在"普攻升级加成"应用后（见下方）按
        // 升级后的 attackRange 重新计算，保证 360° AoE 半径始终 ≥ 普攻触及距离。
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

        // ⭐ 技能范围随（含升级后的）普攻距离放大：skillRange = 升级后 attackRange × 2。
        // 这样 360° AoE 半径始终 ≥ 普攻触及距离，背后敌人也能稳定覆盖，
        // 避免"普攻打得到、技能打不到 / 技能能打到的反而更少"的问题。
        // ⚠ 兜底：用绝对下限 8m，防止 attackRange 在某些角色/加成下异常偏小导致技能只打到 1 个敌人。
        skillRange = Mathf.Max(attackRange * 2f, 8f);

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
            skillDamage = Mathf.RoundToInt(currentCharacterData.baseAttack * 2f + skillBonus.skillDamageBonus);
            skillCooldown -= skillBonus.cooldownReductionBonus;
            if (skillCooldown < 0.5f) skillCooldown = 0.5f;
        }
        else
        {
            skillDamage = Mathf.RoundToInt(currentCharacterData.baseAttack * 2f);
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
                EndDodge();
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
                canAttack = true;
                attackCooldownTimer = 0f;
                animator.SetBool("IsAttacking", false);
                // ⭐ 攻击中允许转向，结束后保持玩家当前朝向（不做回正）。
            }

            // 攻击播完：若攻击中排了技能，则攻击结束后自动放技能（否则停在回 idle，等下次点按）
            if (!isAttacking && queuedSkill)
            {
                queuedSkill = false;
                PerformSkillAttack();
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
                // 技能动画播完：若技能中排了普通攻击，自动接上，保证 skill 与 attack 互相衔接
                if (queuedAttack)
                {
                    queuedAttack = false;
                    PerformAttack();
                }
            }
        }

        Vector3 moveDir = isDodging ? dodgeDirection : GetMoveDirection(inputVector);
        float inputMagnitude = isDodging ? 1f : Mathf.Clamp01(inputVector.magnitude);

        // 普通移动/攻击时都朝输入方向转向（跟手）。技能由动画自带旋转主导，不受输入转向影响。
        // 攻击中可转向且可减速位移（0.5×）；闪避锁死朝向但沿闪避方向强制位移。
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

        // 攻击/技能期间代码位移=0：位移完全交给动画本身。
        float currentSpeed = isDodging ? dodgeSpeed
            : (isAttacking || isUsingSkill) ? 0f
            : speed;

        // 闪避时以闪避方向为准，其余情况位移交给下方输入速度处理（不再强制清零）
        if (isDodging)
        {
            velocity.x = 0f;
            velocity.z = 0f;
            // ⭐ 闪避撞开敌人：冲刺沿路把挡在前面的敌人按闪避方向推开（唯一保留的击退推力）
            ApplyDodgePush();
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

        // 技能播放期间按攻击：排队，技能动画播完后再自动接上普通攻击
        if (isUsingSkill)
        {
            queuedAttack = true;
            return;
        }

        // 攻击动画播完前忽略新攻击点击（手动单击衔接）：每按一次才推进到下一段连击；
        // 不按则当前段播完停在 idle，再按才进入下一段。
        if (isAttacking) return;

        if (!canAttack) return;

        BeginAttack();
    }

    // 攻击起手自动锁定：返回射程内最近的存活敌人（无则 null，保持当前朝向）
    EnemyAI FindNearestEnemy(float maxDist)
    {
        EnemyAI best = null;
        float bestSq = maxDist * maxDist;
        EnemyAI[] all = FindObjectsOfType<EnemyAI>();
        Vector3 p = transform.position;
        for (int i = 0; i < all.Length; i++)
        {
            EnemyAI e = all[i];
            if (e == null || e.isDead) continue;
            Vector3 d = e.transform.position - p;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = e; }
        }
        return best;
    }

    void BeginAttack()
    {
        isAttacking = true;
        canAttack = false;
        attackTimer = 0f;
        attackCooldownTimer = 0f;
        animator.SetBool("IsAttacking", true);
        // 起手朝向：有移动输入则朝输入方向（跟手）；
        // 无输入时自动面向最近敌人——否则玩家站着只按攻击，transform.forward 不会更新，
        // 命中判定(DelayedDamage 用 transform.forward 的 150° 扇形)会打空，
        // 出现"明明屏幕看着面向敌人却打不到 / 站桩完全打不到敌人"的情况。
        Vector3 inputDir = GetMoveDirection(inputVector);
        if (inputDir.magnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(inputDir);
        }
        else
        {
            EnemyAI nearest = FindNearestEnemy(attackRange * 1.5f + 2f);
            if (nearest != null)
            {
                Vector3 toEnemy = nearest.transform.position - transform.position;
                toEnemy.y = 0f;
                if (toEnemy.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toEnemy.normalized);
            }
        }

        string stateName = attackStateNames[comboIndex];
        animator.ResetTrigger("Action");
        animator.Play(stateName, 0, 0f);
        comboIndex = (comboIndex + 1) % attackStateNames.Length;

        PlayAttackSFX();
        StartCoroutine(DelayedDamage());
    }

    // ==================== 技能攻击 ====================

    IEnumerator DelayedDamage()
    {
        yield return new WaitForSeconds(attackDamageDelay);

        bool hitAny = false;
        // ⭐ 用敌人根节点(贴地)的水平距离收集候选，而非贴地的 3D 球形：
        // 僵尸"跳着走"时碰撞盒会随跳动抬升，贴地球形会漏掉升空瞬间导致打不到；
        // 改用 enemy.transform.position 的水平距离判定，彻底免疫垂直偏移。
        EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
        foreach (EnemyAI enemy in enemies)
        {
            if (enemy == null || enemy.isDead) continue;

            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            float distToEnemy = toEnemy.magnitude;
            // 保留原球形前向触及范围（球心前移 attackRange*0.5 + 半径 attackRange = 最大 attackRange*1.5）
            if (distToEnemy > attackRange * 1.5f) continue;

            // ⭐ 普通攻击只打正前方扇形：判定命中点在玩家前方，背后的敌人即使进了范围也不掉血。
            // 技能(isUsingSkill 走 DelayedSkillDamage)始终保持全方位，不受此限制。
            if (Vector3.Angle(transform.forward, toEnemy) > attackFacingAngle)
                continue;

            // ⭐ 近身/贴脸（≤ attackRange*0.75）豁免墙挡判定：墙角/贴墙肉搏距离内必然命中，
            // 避免玩家贴墙或敌人被击退贴墙时，中心连线的射线先撞到墙角障碍误判"隔墙打不到"。
            // 超过该阈值仍查真墙(IsBlockedBetween)，真隔墙时宁可不中也不穿墙攻击。
            if (distToEnemy > attackRange * 0.75f &&
                WallPenetrationResolve.IsBlockedBetween(transform.position, enemy.transform.position))
                continue;

            enemy.TakeDamageImmediate(attackDamage);
            enemy.AddKnockback(transform.forward, normalAttackKnockbackDistance);
            hitAny = true;
        }

        // ⭐ 兜底：若正前方扇形没命中任何敌人，再补一次"贴脸周身"判定（半径 1.6m，不限制朝向）。
        // 解决挥砍动画播放期间敌人绕到背后/侧后方、或起手朝向被移动输入覆盖导致"动画在播却完全打空"的情况。
        if (!hitAny)
        {
            foreach (EnemyAI enemy in FindObjectsOfType<EnemyAI>())
            {
                if (enemy == null || enemy.isDead) continue;
                Vector3 toEnemy = enemy.transform.position - transform.position;
                toEnemy.y = 0f;
                if (toEnemy.magnitude <= 1.6f)
                {
                    enemy.TakeDamageImmediate(attackDamage);
                    enemy.AddKnockback(transform.forward, normalAttackKnockbackDistance);
                }
            }
        }
    }

    // ==================== 技能攻击 ====================

    void PerformSkillAttack()
    {
        if (!canUseSkill || isDead || isDying) return;

        // 技能播放中再点：排队，等当前技能播完再放，避免“点击太快”导致技能动画被打断/重播
        if (isUsingSkill)
        {
            queuedSkill = true;
            return;
        }

        // 攻击动画没播完：技能排队，等当前攻击播完再自动放（Update 里触发），不打断攻击
        if (isAttacking)
        {
            queuedSkill = true;
            return;
        }

        canUseSkill = false;
        skillCooldownTimer = 0f;
        isUsingSkill = true;
        skillTimer = 0f;
        // 技能旋转完全交给动画本身：360° 旋转烘焙在 Hips 骨骼上。
        // 只用 CrossFade 强制切到技能动画：不再 SetTrigger。
        // SetTrigger 的 SkillAction 会被状态机 Idle→Skill Attack 过渡消费（或残留），
        // 导致播完回 Idle 时 trigger 残留再次触发 → 技能播放两次/中间被切。
        animator.ResetTrigger("SkillAction");
        animator.CrossFade("Skill Attack", 0.08f, 0);

        int finalDamage = skillDamage > 0 ? skillDamage : attackDamage * 2;
        PlaySkillSFX();
        // 在技能起手瞬间、以当时玩家位置为球心锁定范围内的敌人，
        // 避免延迟期间玩家移动 / 敌人被击退导致"有时只打到部分"。
        // 伤害延迟自动跟随“Skill Attack”动画时长（skillHitFraction 控制落在动画的哪一帧），不再与动画“对不上”。
        float hitDelay = GetSkillHitDelay();
        StartCoroutine(DelayedSkillDamage(finalDamage, Physics.OverlapSphere(transform.position, skillRange), hitDelay));
    }

    // 读取“Skill Attack”动画片段时长，按 skillHitFraction 算出伤害应延迟的秒数；拿不到片段则用 skillDamageDelay 兜底
    float GetSkillHitDelay()
    {
        if (animator == null) return skillDamageDelay;
        float len = 0f;
        var clips = animator.GetNextAnimatorClipInfo(0);
        if (clips.Length == 0) clips = animator.GetCurrentAnimatorClipInfo(0);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].clip != null && clips[i].clip.name.ToLower().Contains("skill"))
            {
                len = clips[i].clip.length;
                break;
            }
        }
        if (len <= 0.01f) return skillDamageDelay;
        return len * Mathf.Clamp01(skillHitFraction);
    }

    IEnumerator DelayedSkillDamage(int damage, Collider[] hitColliders, float delay)
    {
        yield return new WaitForSeconds(delay);

        // 命中所捕获（技能起手瞬间、以当时位置为球心）半径内的所有敌人，
        // 实现稳定的 360° 范围技：不再因延迟期间的移动/击退而漏掉部分敌人。
        foreach (Collider hit in hitColliders)
        {
            EnemyAI enemy = hit.GetComponentInParent<EnemyAI>();
            if (enemy != null && !enemy.isDead)
            {
                enemy.TakeDamageImmediate(damage);
                enemy.AddKnockback(transform.forward, enemyKnockbackDistance * 3f);
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
        if (!canDodge || isDead || isDying || isDodging || isUsingSkill) return;

        // ⭐ 攻击中按闪避：先取消当前攻击（清状态/动画/排队），再直接进入闪避 → 攻守一体，可取消攻击硬直后撤。
        if (isAttacking)
        {
            CancelAttack();
        }

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
        dodgePushedEnemies.Clear();

        // ⭐ 激活闪避特效（先取消上一次还没播完的生命周期隐藏，避免上一段残影提前把新特效关掉）
        if (dashEffectHideCoroutine != null)
        {
            StopCoroutine(dashEffectHideCoroutine);
            dashEffectHideCoroutine = null;
        }
        ResolveDashEffect();
        if (dashEffect != null)
        {
            dashEffect.SetActive(true);
            // ⭐ 特效存活时间：从闪避开始算总时长，到期自动隐藏（闪避就 0.2s，靠它留残影）
            dashEffectHideCoroutine = StartCoroutine(HideDashEffectAfterLifetime());
        }

        // ⭐ 闪避音效
        PlayDashSFX();
    }

    // ⭐ 闪避撞开敌人：以玩家为球心、闪避方向前段为探测范围，把范围内的非死亡敌人
    // 沿闪避方向推开 dodgePushDistance。同一段闪避只推每个敌人一次（去重）。
    // 全项目唯一的"玩家推动敌人"来源——普攻/技能已不推，只有闪避保留推力。
    void ApplyDodgePush()
    {
        if (dodgeDirection.sqrMagnitude < 0.0001f) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, dodgePushRadius);
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            EnemyAI enemy = hit.GetComponentInParent<EnemyAI>();
            if (enemy == null || enemy.isDead) continue;
            if (dodgePushedEnemies.Contains(enemy)) continue;

            dodgePushedEnemies.Add(enemy);

            // ⭐ 临时忽略玩家与该敌人的碰撞：闪避冲刺能冲开并穿过敌人（玩家实体不挡自己人撞开）。
            EnemyCollisionBlocker blocker = hit.GetComponentInParent<EnemyCollisionBlocker>();
            if (blocker != null)
            {
                blocker.SetIgnorePlayerCollision(true);
                dodgeIgnoredBlockers.Add(blocker);
            }

            enemy.AddKnockback(dodgeDirection, dodgePushDistance);
        }
    }

    // ⭐ 闪避结束：恢复对闪避期间撞开的敌人的碰撞（回到"敌人挡住玩家"），并清空去重记录
    void EndDodge()
    {
        isDodging = false;
        dodgeTimer = 0f;
        dodgePushedEnemies.Clear();

        foreach (EnemyCollisionBlocker blocker in dodgeIgnoredBlockers)
        {
            if (blocker != null) blocker.SetIgnorePlayerCollision(false);
        }
        dodgeIgnoredBlockers.Clear();

        // 特效隐藏由 PerformDodge 起的生命周期协程统一负责，EndDodge 不关
    }

    IEnumerator HideDashEffectAfterLifetime()
    {
        if (dashEffectLifetime > 0f)
            yield return new WaitForSeconds(dashEffectLifetime);
        if (dashEffect != null) dashEffect.SetActive(false);
        dashEffectHideCoroutine = null;
    }

    // ⭐ 解析闪避特效引用：字段没拖或引用失效时，按名字递归找玩家身上的 DashEffect 子物体。
    void ResolveDashEffect()
    {
        if (dashEffect != null) return;

        Transform found = FindChildRecursive(transform, "DashEffect");
        if (found != null)
        {
            dashEffect = found.gameObject;
            Debug.Log("[闪避特效] 按名字自动找到子物体: DashEffect");
        }
        else
        {
            Debug.LogWarning("[闪避特效] 玩家身上找不到名为 DashEffect 的子物体，闪避特效不会显示。请把玩家下的 DashEffect 子物体拖到 PlayerController 的 dashEffect 槽。");
        }
    }

    Transform FindChildRecursive(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform hit = FindChildRecursive(child, name);
            if (hit != null) return hit;
        }
        return null;
    }

    // 取消当前普通攻击：闪避触发时调用，避免攻击状态残留（仍锁位移/方向，闪避会覆盖 currentSpeed）
    void CancelAttack()
    {
        if (!isAttacking) return;
        isAttacking = false;
        attackTimer = 0f;
        queuedSkill = false;
        animator.SetBool("IsAttacking", false);
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
            t += Time.unscaledDeltaTime / Mathf.Max(hitRedDuration, 0.001f);
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

        // ⭐ 死亡时若正在闪避，恢复被临时忽略的敌人碰撞，避免敌人永久穿过玩家
        if (isDodging)
        {
            isDodging = false;
            dodgeTimer = 0f;
            dodgePushedEnemies.Clear();
            foreach (EnemyCollisionBlocker blocker in dodgeIgnoredBlockers)
            {
                if (blocker != null) blocker.SetIgnorePlayerCollision(false);
            }
            dodgeIgnoredBlockers.Clear();
            if (dashEffectHideCoroutine != null)
            {
                StopCoroutine(dashEffectHideCoroutine);
                dashEffectHideCoroutine = null;
            }
            if (dashEffect != null) dashEffect.SetActive(false);
        }

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

    void PlayAttackSFX()
    {
        if (attackSFX == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(attackSFX, transform.position);
    }

    void PlaySkillSFX()
    {
        if (skillSFX == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(skillSFX, transform.position);
    }

    void PlayPlayerHitSFX()
    {
        if (hitSFX == null || hitSFX.Length == 0) return;
        AudioClip clip = hitSFX[Random.Range(0, hitSFX.Length)];
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip, transform.position);
    }

    void PlayDashSFX()
    {
        if (dashSFX == null) return;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(dashSFX, transform.position);
    }

    void PlayRandomRunSFX()
    {
        if (runSFX == null || runSFX.Length == 0) return;
        AudioClip clip = runSFX[Random.Range(0, runSFX.Length)];
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip, transform.position);
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
        if (isDying || isDead)
        {
            runFootstepTimer = 0f;
            return;
        }

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        // ⭐ 跑步脚步声：贴地且正在移动时，按间隔随机播一个脚步音（闪避另走 dashSFX，不重复播脚步）
        bool moving = currentSpeed > 0.05f;
        if (moving && isGrounded && !isDodging)
        {
            runFootstepTimer -= Time.deltaTime;
            if (runFootstepTimer <= 0f)
            {
                runFootstepTimer = runFootstepInterval;
                PlayRandomRunSFX();
            }
        }
        else
        {
            runFootstepTimer = 0f;
        }

        animator.SetBool("IsMoving", moving);
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
}