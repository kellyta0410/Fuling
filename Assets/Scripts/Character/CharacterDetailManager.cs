using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class CharacterDetailManager : MonoBehaviour
{
    [Header("===== 头像与信息 =====")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image fullBodyImage;
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private TextMeshProUGUI characterDescriptionText; 

    [Header("===== 普通攻击 =====")]
    [SerializeField] private Button normalAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI normalAttackStatText;      // "Lv: 10"(最高等级)
    [SerializeField] private TextMeshProUGUI normalAttackLevelText;     // "Lv.2 ? 3" ? "MAX"
    [SerializeField] private TextMeshProUGUI normalAttackDescriptionText;
    [SerializeField] private TextMeshProUGUI normalAttackCoinText;

    [Header("===== 技能攻击 =====")]
    [SerializeField] private Button skillAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI skillAttackStatText;       
    [SerializeField] private TextMeshProUGUI skillAttackLevelText;     
    [SerializeField] private TextMeshProUGUI skillAttackDescriptionText;
    [SerializeField] private TextMeshProUGUI skillAttackCoinText;

    [Header("===== 选择按钮 =====")]
    [SerializeField] private Button selectCharacterButton;
    [SerializeField] private TextMeshProUGUI selectButtonText;
    [SerializeField] private TextMeshProUGUI selectButtonShadowText;   // 阴影副本（自己在 Inspector 拖入，与原文字同字体、偏移一点即可）

    [Header("===== 字体 =====")]
    [Tooltip("游戏内统一使用的华文行楷字体（拖入 Assets/Font/LiyuXingkai SDF）")]
    [SerializeField] private TMP_FontAsset uiFont;

    [Header("===== manager =====")]
    [SerializeField] private GameDataManager gameDataManager;

    [Header("===== 提示气泡 =====")]
    [Tooltip("按钮不可用时点击弹出的提示文字，留空则仅打印日志")]
    public TextMeshProUGUI hintToastText;
    [Tooltip("提示文字后面的背景 Panel（可选），显示提示时一起打开")]
    public GameObject hintToastPanel;
    private Coroutine hintToastCoroutine;

    /* ==================== 3D 模型（暂时不需要，已注释） ==================== */
    [Header("===== 3D 模型=====")]
    [Tooltip("3D 角色的模型预制体")]
    [SerializeField] private Transform modelContainer;
    [Tooltip("模型相对相机前方摆放的距离（米），用于对齐到全身图位置")]
    [SerializeField] private float modelViewDistance = 2.5f;
    [Header("===== 专用模型相机（RenderTexture 方式，优先于 modelContainer）=====")]
    [Tooltip("照模型的专用相机；模型会放在它前方，相机输出给 RawImage")]
    [SerializeField] private Camera modelCamera;
    [Tooltip("接收 RenderTexture 的 UI 图（与全身图同大小同位置），有模型时显示")]
    [SerializeField] private RawImage modelRenderImage;
    [Tooltip("模型放在相机前方的距离（米）")]
    [SerializeField] private float modelCamDistance = 3f;
    [Header("===== 模型旋转预览 =====")]
    [Tooltip("开启后模型可随鼠标/触屏旋转")]
    [SerializeField] private bool allowRotate = true;
    [Tooltip("开=模型朝向跟随指针在 RawImage 上的水平位置（悬停即转）；关=按住拖动旋转")]
    [SerializeField] private bool rotateFollowPointer = false;
    [Tooltip("转动灵敏度（仅拖动模式使用）")]
    [SerializeField] private float modelRotateSpeed = 0.6f;
    [Tooltip("创建模型后自动把包围盒中心对齐到相机画面中线（解决偏高/偏低）")]
    [SerializeField] private bool autoCenterModel = true;
    [Tooltip("按相机画面比例自动放大模型，避免显示太小")]
    [SerializeField] private bool autoFitScale = true;
    [Tooltip("模型高度占整个画面高度的比例（0~1，越大模型越大）")]
    [SerializeField] private float fitRatio = 0.7f;
    private GameObject currentModelInstance;
    private float modelYaw;
    private float lastDragX = -1f;
    /* ==================== 3D 模型字段注释结束 ==================== */

    private CharacterData currentDisplayCharacter;

    /* ==================== 3D 模型旋转（暂时不需要，已注释） ==================== */
    void Update()
    {
        if (!allowRotate || currentModelInstance == null || currentDisplayCharacter == null)
            return;

        // 只在 RawImage 矩形范围内生效
        Vector2 local = Vector2.zero;
        Rect rect = new Rect();
        bool inside = false;
        if (modelRenderImage != null && modelRenderImage.rectTransform != null)
        {
            Camera uicam = null;
            Canvas canvas = modelRenderImage.canvas;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
                uicam = canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    modelRenderImage.rectTransform, Input.mousePosition, uicam, out local))
            {
                rect = modelRenderImage.rectTransform.rect;
                inside = rect.Contains(local);
            }
        }
        if (!inside)
        {
            lastDragX = -1f;
            return;
        }

        if (rotateFollowPointer)
        {
            float t = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
            modelYaw = Mathf.Lerp(-180f, 180f, t);
        }
        else if (Input.GetMouseButton(0))
        {
            if (lastDragX >= 0f)
                modelYaw += (local.x - lastDragX) * modelRotateSpeed;
            lastDragX = local.x;
        }
        else
        {
            lastDragX = -1f;
        }

        currentModelInstance.transform.localRotation =
            Quaternion.Euler(currentDisplayCharacter.modelSpawnRotation) * Quaternion.Euler(0f, modelYaw, 0f);
    }
    /* ==================== 3D 模型旋转注释结束 ==================== */

    void Start()
    {
        // 无条件使用单例，避免引用到场景里被销毁的 GameDataManager 对象
        gameDataManager = GameDataManager.Instance;

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！");
            return;
        }

        // 升级按钮的 onClick 已在场景中绑定（Inspector）
        selectCharacterButton?.onClick.AddListener(SelectCurrentCharacter);

        gameDataManager.OnDataChanged += RefreshPanel;

        ApplyGameFont();
        UpdateSelectButtonShadow();

        // 启动时默认显示当前角色
        if (gameDataManager.CurrentCharacter != null)
            ShowCharacterDetail(gameDataManager.CurrentCharacter);
        else
            RefreshPanel();
    }

    void OnDestroy()
    {
        if (gameDataManager != null)
            gameDataManager.OnDataChanged -= RefreshPanel;
    }

    // ==================== ?????? ====================

    private UpgradeConfigSO GetNormalAttackConfig(CharacterData character)
    {
        return character != null ? character.normalAttackConfig : null;
    }

    private UpgradeConfigSO GetSkillAttackConfig(CharacterData character)
    {
        return character != null ? character.skillAttackConfig : null;
    }

    // ==================== ???? ====================

    int GetNormalLevel(string name)
    {
        string id = $"NormalAttack_{name}";
        var entry = gameDataManager.skillLevels.Find(s => s.skillID == id);
        return entry != null && entry.level > 0 ? entry.level : 1;
    }

    int GetSkillLevel(string name)
    {
        string id = $"SkillAttack_{name}";
        var entry = gameDataManager.skillLevels.Find(s => s.skillID == id);
        return entry != null && entry.level > 0 ? entry.level : 1;
    }

    void SetNormalLevel(string name, int level)
    {
        string id = $"NormalAttack_{name}";
        var entry = gameDataManager.skillLevels.Find(s => s.skillID == id);
        if (entry != null) entry.level = level;
        else gameDataManager.skillLevels.Add(new GameDataManager.SkillSaveEntry { skillID = id, level = level });
        gameDataManager.SaveData();
    }

    void SetSkillLevel(string name, int level)
    {
        string id = $"SkillAttack_{name}";
        var entry = gameDataManager.skillLevels.Find(s => s.skillID == id);
        if (entry != null) entry.level = level;
        else gameDataManager.skillLevels.Add(new GameDataManager.SkillSaveEntry { skillID = id, level = level });
        gameDataManager.SaveData();
    }

    // ==================== ???? ====================

    /// <summary>
    /// ???????????
    /// </summary>
    public void ShowCharacterByAvatar(CharacterData character)
    {
        ShowCharacterDetail(character);
    }

    public void ShowCharacterDetail(CharacterData character)
    {
        if (character == null) { ClearPanel(); return; }
        currentDisplayCharacter = character;

        bool isUnlocked = gameDataManager.IsCharacterUnlocked(character);

        // ===== Avatar ??(????????) =====
        if (avatarImage != null)
        {
            if (isUnlocked && character.avatarSprite != null)
            {
                avatarImage.sprite = character.avatarSprite;
                avatarImage.color = Color.white;
            }
            else if (!isUnlocked && character.lockedAvatarSprite != null)
            {
                avatarImage.sprite = character.lockedAvatarSprite;
                avatarImage.color = Color.white;
            }
            else
            {
                avatarImage.sprite = character.avatarSprite;
                avatarImage.color = isUnlocked ? Color.white : Color.gray;
            }
            avatarImage.preserveAspect = true;
        }

        // ===== ?????(??????) =====
        if (fullBodyImage != null)
        {
            if (isUnlocked && character.fullBodySprite != null)
            {
                fullBodyImage.sprite = character.fullBodySprite;
                fullBodyImage.color = Color.white;
            }
            else
            {
                fullBodyImage.sprite = character.fullBodySprite;
                fullBodyImage.color = isUnlocked ? Color.white : Color.gray;
            }
            fullBodyImage.preserveAspect = true;
        }

        // ===== ???? =====
        if (characterNameText != null)
            characterNameText.text = isUnlocked ? character.characterName : "???";
        // ===== 描述 =====
        if (characterDescriptionText != null)
        {
            string desc = isUnlocked ? character.characterDescription : null;
            characterDescriptionText.text = string.IsNullOrEmpty(desc)
                ? (isUnlocked ? "暂无描述" : "未解锁角色")
                : desc;
        }

        // ===== ?????? =====
        UpdateCharacterStats(character);
        UpdateSkillDisplay(character);
        UpdateSelectButtonState(character);

        // ===== 3D ???? =====
        UpdateCharacterModel(character);
    }

    // ==================== 3D ???? ====================

    // 暂时不需要 3D 模型，直接显示全身图
    void UpdateCharacterModel(CharacterData character)
    {
        // 3D 模型相关物体全部停用，防止它们盖在全身图上
        //（尤其 Android 上 3D 相机/RawImage 会渲染成黑块，把贴图遮成一片黑）
        if (modelContainer != null) modelContainer.gameObject.SetActive(false);
        if (modelCamera != null) modelCamera.gameObject.SetActive(false);
        if (modelRenderImage != null) modelRenderImage.gameObject.SetActive(false);
        if (currentModelInstance != null)
        {
            Destroy(currentModelInstance);
            currentModelInstance = null;
        }

        if (fullBodyImage != null)
            fullBodyImage.gameObject.SetActive(true);
    }

    /* ==================== 3D 模型（暂时不需要，已注释） ====================
    void UpdateCharacterModel(CharacterData character)
    {
        if (currentModelInstance != null)
        {
            Destroy(currentModelInstance);
            currentModelInstance = null;
        }

        bool useCamMode = character != null && character.modelPrefab != null && modelCamera != null;
        bool useContainerMode = character != null && character.modelPrefab != null && modelContainer != null;
        bool hasModel = useCamMode || useContainerMode;

        if (hasModel)
        {
            if (useCamMode)
            {
                // 专用相机方式：模型放进相机前方，相机渲染到 RawImage
                SetModelRenderActive(true);
                currentModelInstance = Instantiate(character.modelPrefab, modelCamera.transform);
                currentModelInstance.transform.localPosition = Vector3.forward * modelCamDistance + character.modelSpawnPosition;
                currentModelInstance.transform.localRotation = Quaternion.Euler(character.modelSpawnRotation);
                currentModelInstance.transform.localScale = Vector3.one;
                CenterModelInView();
            }
            else
            {
                // 原方式：容器摆到全身图 UI 位置
                AlignModelToImage();
                currentModelInstance = Instantiate(character.modelPrefab, modelContainer);
                currentModelInstance.transform.localPosition = character.modelSpawnPosition;
                currentModelInstance.transform.localRotation = Quaternion.Euler(character.modelSpawnRotation);
                currentModelInstance.transform.localScale = Vector3.one;
            }

            Animator modelAnimator = currentModelInstance.GetComponentInChildren<Animator>();
            if (modelAnimator != null && !string.IsNullOrEmpty(character.defaultAnimation))
            {
                modelAnimator.Play(character.defaultAnimation);
            }
        }
        else
        {
            SetModelRenderActive(false);
        }

        // ?????????,???????????
        if (fullBodyImage != null)
            fullBodyImage.gameObject.SetActive(!hasModel);
    }

    // 开关专用模型相机 + RawImage（有模型才显示）
    void SetModelRenderActive(bool active)
    {
        if (modelCamera != null) modelCamera.gameObject.SetActive(active);
        if (modelRenderImage != null) modelRenderImage.gameObject.SetActive(active);
    }

    // 自动缩放模型到画面比例 + 垂直居中
    void CenterModelInView()
    {
        if (currentModelInstance == null || modelCamera == null)
            return;
        if (!autoCenterModel && !autoFitScale)
            return;

        // 先按画面高度放大模型
        if (autoFitScale)
        {
            float h = GetModelBounds().size.y;
            if (h > 0.001f)
            {
                float viewHeight;
                if (modelCamera.orthographic)
                    viewHeight = modelCamera.orthographicSize * 2f;
                else
                    viewHeight = 2f * modelCamDistance * Mathf.Tan(modelCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                currentModelInstance.transform.localScale = Vector3.one * (viewHeight * fitRatio / h);
            }
        }

        // 再让包围盒中心对齐画面中线
        if (autoCenterModel)
        {
            Vector3 camLocal = modelCamera.transform.InverseTransformPoint(GetModelBounds().center);
            Vector3 p = currentModelInstance.transform.localPosition;
            p.y -= camLocal.y;
            currentModelInstance.transform.localPosition = p;
        }
    }

    Bounds GetModelBounds()
    {
        Renderer[] renderers = currentModelInstance.GetComponentsInChildren<Renderer>(true);
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds();
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // 把 modelContainer 放到"全身图 UI"屏幕中心的相机前方，让模型显示在图片位置
    void AlignModelToImage()
    {
        if (modelContainer == null || fullBodyImage == null || fullBodyImage.rectTransform == null)
        {
            if (modelContainer != null) modelContainer.localPosition = Vector3.zero;
            return;
        }

        // 选相机：优先用 Canvas 的世界相机，其次 MainCamera，再退到任意相机
        Canvas canvas = fullBodyImage.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != null)
            cam = canvas.worldCamera;
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Camera[] all = Camera.allCameras;
            if (all.Length > 0) cam = all[0];
        }
        if (cam == null)
        {
            modelContainer.localPosition = Vector3.zero;
            return;
        }

        Vector3[] corners = new Vector3[4];
        fullBodyImage.rectTransform.GetWorldCorners(corners);
        Vector3 center = (corners[0] + corners[2]) * 0.5f;

        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // Overlay：corners 是屏幕像素，射线投到相机前方
            Ray r = cam.ScreenPointToRay(center);
            modelContainer.position = cam.transform.position + r.direction * modelViewDistance;
        }
        else
        {
            // ScreenSpace-Camera / World：corners 是世界坐标，沿相机>中心方向取一段
            Vector3 dir = (center - cam.transform.position).normalized;
            modelContainer.position = cam.transform.position + dir * modelViewDistance;
        }

        modelContainer.rotation = cam.transform.rotation;
    }
    // ==================== 3D 模型注释结束 ==================== */

    // 暂时不需要 3D 模型，只恢复显示全身图
    void DestroyCurrentModel()
    {
        if (fullBodyImage != null)
            fullBodyImage.gameObject.SetActive(true);
    }

    // ==================== ????/?????? ====================

    void UpdateCharacterStats(CharacterData character)
    {
        if (character == null) return;

        string name = character.characterName;

        var normalConfig = GetNormalAttackConfig(character);
        var skillConfig = GetSkillAttackConfig(character);

        var normalTotal = normalConfig?.GetTotalBonus(GetNormalLevel(name)) ?? new UpgradeLevelData();
        var skillTotal = skillConfig?.GetTotalBonus(GetSkillLevel(name)) ?? new UpgradeLevelData();

        // 普通攻击区域：显示当前实际数值（默认属性 + 升级累计，升级后更新）
        int totalAttack = character.baseAttack + normalTotal.attackBonus + skillTotal.attackBonus;
        float totalRange = character.baseRange + normalTotal.attackRangeBonus;
        if (normalAttackStatText != null)
            normalAttackStatText.text = $"攻击：{totalAttack}  范围：{totalRange:F1}";

        // 技能攻击区域：显示当前实际数值（基础攻击×3 + 技能伤害成长；冷却基准 15 秒）
        int skillAttack = character.baseAttack * 3 + skillTotal.skillDamageBonus;
        float skillRange = character.baseRange * 1.5f + skillTotal.attackRangeBonus;
        float skillCd = Mathf.Max(0f, 15f - skillTotal.cooldownReductionBonus);
        if (skillAttackStatText != null)
            skillAttackStatText.text = $"伤害：{skillAttack}  范围：{skillRange:F1}  冷却：{skillCd:F0}秒";
    }

    // ==================== ?????? ====================

    void UpdateSkillDisplay(CharacterData character)
    {
        string name = character.characterName;
        bool unlocked = gameDataManager.IsCharacterUnlocked(character);

        var normalConfig = GetNormalAttackConfig(character);
        var skillConfig = GetSkillAttackConfig(character);

        int normalMaxLevel = normalConfig != null ? normalConfig.maxLevel : 10;
        int skillMaxLevel = skillConfig != null ? skillConfig.maxLevel : 10;

        // ===== 普通攻击 =====
        int normalLevel = GetNormalLevel(name);
        bool normalMaxed = normalLevel >= normalMaxLevel;
        var nextNormal = normalConfig?.GetLevelData(normalLevel + 1);
        var currentNormal = normalConfig?.GetLevelData(normalLevel);

        // 等级显示（当前等级 > 下一等级，满级显示 MAX）
        if (normalAttackLevelText != null)
        {
            if (!unlocked)
                normalAttackLevelText.text = "Lv.--";
            else if (normalMaxed)
                normalAttackLevelText.text = "MAX";
            else
                normalAttackLevelText.text = $"Lv.{normalLevel} > {normalLevel + 1}";
        }

        // 升级所需金币（满级清空）
        if (normalAttackCoinText != null)
        {
            if (!unlocked || normalMaxed || nextNormal == null || normalConfig == null)
                normalAttackCoinText.text = "";
            else
                normalAttackCoinText.text = $"{normalConfig.GetLevelCost(normalLevel + 1)}";
        }

        // 简介显示当前等级这一级提升的内容（升级后更新）
        if (normalAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                normalAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else if (currentNormal == null)
            {
                normalAttackDescriptionText.text = normalConfig?.GetLevelData(1)?.description ?? "无描述";
            }
            else
            {
                normalAttackDescriptionText.text = currentNormal?.description ?? "无描述";
            }
        }

        // 按钮（始终可点用于弹提示，不可用时置灰）
        if (normalAttackUpgradeButton != null)
        {
            if (!unlocked)
            {
                SetButtonClickable(normalAttackUpgradeButton, false);
                Debug.Log($"[升级按钮] {name} 普通攻击禁用：未解锁");
            }
            else
            {
                if (normalMaxed || nextNormal == null || normalConfig == null)
                {
                    SetButtonClickable(normalAttackUpgradeButton, false);
                    Debug.Log($"[升级按钮] {name} 普通攻击禁用：maxed={normalMaxed} nextNull={nextNormal == null} configNull={normalConfig == null}");
                }
                else
                {
                    SetButtonClickable(normalAttackUpgradeButton, gameDataManager.TotalCoins >= normalConfig.GetLevelCost(normalLevel + 1));
                    Debug.Log($"[升级按钮] {name} 普通攻击 金币={gameDataManager.TotalCoins} 需要={normalConfig.GetLevelCost(normalLevel + 1)} => {normalAttackUpgradeButton.interactable}");
                }
            }
        }

        // ===== 技能攻击 =====
        int skillLevel = GetSkillLevel(name);
        bool skillMaxed = skillLevel >= skillMaxLevel;
        var nextSkill = skillConfig?.GetLevelData(skillLevel + 1);
        var currentSkill = skillConfig?.GetLevelData(skillLevel);

        // 等级显示（当前等级 > 下一等级，满级显示 MAX）
        if (skillAttackLevelText != null)
        {
            if (!unlocked)
                skillAttackLevelText.text = "Lv.--";
            else if (skillMaxed)
                skillAttackLevelText.text = "MAX";
            else
                skillAttackLevelText.text = $"Lv.{skillLevel} > {skillLevel + 1}";
        }

        // 升级所需金币（满级清空）
        if (skillAttackCoinText != null)
        {
            if (!unlocked || skillMaxed || nextSkill == null || skillConfig == null)
                skillAttackCoinText.text = "";
            else
                skillAttackCoinText.text = $"{skillConfig.GetLevelCost(skillLevel + 1)}";
        }

        // 简介显示当前等级这一级提升的内容（升级后更新）
        if (skillAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                skillAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else if (currentSkill == null)
            {
                skillAttackDescriptionText.text = skillConfig?.GetLevelData(1)?.description ?? "无描述";
            }
            else
            {
                skillAttackDescriptionText.text = currentSkill?.description ?? "无描述";
            }
        }

        // 按钮（只控制可点，文字由场景固定，不自动创建字体）
        if (skillAttackUpgradeButton != null)
        {
            if (!unlocked)
            {
                SetButtonClickable(skillAttackUpgradeButton, false);
                Debug.Log($"[升级按钮] {name} 技能攻击禁用：未解锁");
            }
            else
            {
                if (skillMaxed || nextSkill == null || skillConfig == null)
                {
                    SetButtonClickable(skillAttackUpgradeButton, false);
                    Debug.Log($"[升级按钮] {name} 技能攻击禁用：maxed={skillMaxed} nextNull={nextSkill == null} configNull={skillConfig == null}");
                }
                else
                {
                    SetButtonClickable(skillAttackUpgradeButton, gameDataManager.TotalCoins >= skillConfig.GetLevelCost(skillLevel + 1));
                    Debug.Log($"[升级按钮] {name} 技能攻击 金币={gameDataManager.TotalCoins} 需要={skillConfig.GetLevelCost(skillLevel + 1)} => {skillAttackUpgradeButton.interactable}");
                }
            }
        }
    }

    // ==================== ?? ====================

    public void UpgradeNormalAttack()
    {
        if (currentDisplayCharacter == null) return;
        var config = GetNormalAttackConfig(currentDisplayCharacter);
        if (config == null) return;

        string name = currentDisplayCharacter.characterName;
        int current = GetNormalLevel(name);
        if (current >= config.maxLevel)
        {
            ShowHint("普通攻击已满级");
            return;
        }

        int cost = config.GetLevelCost(current + 1);
        if (!gameDataManager.SpendCoins(cost))
        {
            ShowHint($"金币不足！升级需 {cost} 金币");
            return;
        }

        SetNormalLevel(name, current + 1);
        UpdateCharacterStats(currentDisplayCharacter);
        UpdateSkillDisplay(currentDisplayCharacter);
        gameDataManager.NotifyDataChanged();
    }

    public void UpgradeSkillAttack()
    {
        if (currentDisplayCharacter == null) return;
        var config = GetSkillAttackConfig(currentDisplayCharacter);
        if (config == null) return;

        string name = currentDisplayCharacter.characterName;
        int current = GetSkillLevel(name);
        if (current >= config.maxLevel)
        {
            ShowHint("技能攻击已满级");
            return;
        }

        int cost = config.GetLevelCost(current + 1);
        if (!gameDataManager.SpendCoins(cost))
        {
            ShowHint($"金币不足！升级需 {cost} 金币");
            return;
        }

        SetSkillLevel(name, current + 1);
        UpdateCharacterStats(currentDisplayCharacter);
        UpdateSkillDisplay(currentDisplayCharacter);
        gameDataManager.NotifyDataChanged();
    }

    // ==================== 华文字体（统一本面板所有文本） ====================

    // ==================== 按钮可点/置灰 ====================

    // 始终可点（用于不可用时点击弹提示），不可用时置灰，看起来像禁用
    void SetButtonClickable(Button btn, bool available)
    {
        if (btn == null) return;
        btn.interactable = true;
        if (btn.targetGraphic != null)
            btn.targetGraphic.color = available ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.8f);
    }

    // ==================== 提示气泡（从下往上弹入） ====================

    public void ShowHint(string message)
    {
        HintToast.Show(message, uiFont, hintToastText, hintToastPanel);
    }

    void ApplyGameFont()
    {
        if (uiFont == null) return;

        TextMeshProUGUI[] allTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in allTexts)
        {
            t.font = uiFont;
        }
    }

    // ==================== 选择按钮阴影（同步文字，阴影副本已在 Inspector 拖入） ====================

    void UpdateSelectButtonShadow()
    {
        if (selectButtonShadowText == null || selectButtonText == null) return;

        // 阴影与原文字完全一致（字体、字号、样式、对齐、内容），只靠 Inspector 里的颜色/偏移做区分
        selectButtonShadowText.text = selectButtonText.text;
        selectButtonShadowText.font = selectButtonText.font;
        selectButtonShadowText.fontSize = selectButtonText.fontSize;
        selectButtonShadowText.fontStyle = selectButtonText.fontStyle;
        selectButtonShadowText.alignment = selectButtonText.alignment;
        selectButtonShadowText.enableWordWrapping = selectButtonText.enableWordWrapping;
    }

    // ==================== ???? ====================

    void UpdateSelectButtonState(CharacterData character)
    {
        bool unlocked = gameDataManager.IsCharacterUnlocked(character);
        bool selected = gameDataManager.CurrentCharacter?.characterName == character.characterName;

        if (selectButtonText != null)
        {
            // 只同步内容，颜色由场景 Inspector 设置（不在此修改）
            if (selected)
                selectButtonText.text = "已选择";
            else if (unlocked)
                selectButtonText.text = "选择角色";
            else
                selectButtonText.text = $"解锁 ({character.unlockCost})";

            UpdateSelectButtonShadow();
        }

        if (selectCharacterButton != null)
        {
            if (selected)
                SetButtonClickable(selectCharacterButton, false);
            else if (unlocked)
                SetButtonClickable(selectCharacterButton, true);
            else
                SetButtonClickable(selectCharacterButton, gameDataManager.TotalCoins >= character.unlockCost);
        }
    }

    public void SelectCurrentCharacter()
    {
        if (currentDisplayCharacter == null) return;

        if (!gameDataManager.IsCharacterUnlocked(currentDisplayCharacter))
        {
            if (!gameDataManager.UnlockCharacter(currentDisplayCharacter))
            {
                ShowHint($"金币不足！解锁需 {currentDisplayCharacter.unlockCost} 金币");
                return;
            }
            gameDataManager.SelectCharacter(currentDisplayCharacter);
            gameDataManager.NotifyDataChanged();
            return;
        }

        gameDataManager.SelectCharacter(currentDisplayCharacter);
        gameDataManager.NotifyDataChanged();
    }

    public void RefreshPanel()
    {
        if (currentDisplayCharacter != null)
            ShowCharacterDetail(currentDisplayCharacter);
        else if (gameDataManager.CurrentCharacter != null)
            ShowCharacterDetail(gameDataManager.CurrentCharacter);
    }

    public void ClearPanel()
    {
        currentDisplayCharacter = null;

        DestroyCurrentModel();

        if (avatarImage != null) avatarImage.sprite = null;
        if (fullBodyImage != null) fullBodyImage.sprite = null;
        if (characterNameText != null) characterNameText.text = "";
        if (characterDescriptionText != null) characterDescriptionText.text = "";

        // 清空攻击/技能属性显示
        if (normalAttackStatText != null) normalAttackStatText.text = "攻击: --";
        if (skillAttackStatText != null) skillAttackStatText.text = "技能: --";

        if (normalAttackLevelText != null) normalAttackLevelText.text = "Lv.--";
        if (skillAttackLevelText != null) skillAttackLevelText.text = "Lv.--";
        if (normalAttackDescriptionText != null) normalAttackDescriptionText.text = "";
        if (skillAttackDescriptionText != null) skillAttackDescriptionText.text = "";
        if (normalAttackCoinText != null) normalAttackCoinText.text = "";
        if (skillAttackCoinText != null) skillAttackCoinText.text = "";

        // 按钮（文字由场景固定，只置灰不可用）
        if (normalAttackUpgradeButton != null)
        {
            SetButtonClickable(normalAttackUpgradeButton, false);
        }
        if (skillAttackUpgradeButton != null)
        {
            SetButtonClickable(skillAttackUpgradeButton, false);
        }

        if (selectButtonText != null)
        {
            selectButtonText.text = "选择角色";
            UpdateSelectButtonShadow();
        }
        if (selectCharacterButton != null)
            SetButtonClickable(selectCharacterButton, false);
    }

    public CharacterData GetCurrentDisplayCharacter()
    {
        return currentDisplayCharacter;
    }
}