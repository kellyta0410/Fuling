using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class CharacterDetailManager : MonoBehaviour
{
    [Header("===== 头像与信息 =====")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image fullBodyImage;
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private TextMeshProUGUI characterDescriptionText; // ??????

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

    [Header("===== 3D 模型=====")]
    [Tooltip("3D 角色的模型预制体")]
    [SerializeField] private Transform modelContainer;
    private GameObject currentModelInstance;

    private CharacterData currentDisplayCharacter;

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
        return entry != null ? entry.level : 0;
    }

    int GetSkillLevel(string name)
    {
        string id = $"SkillAttack_{name}";
        var entry = gameDataManager.skillLevels.Find(s => s.skillID == id);
        return entry != null ? entry.level : 0;
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

    void UpdateCharacterModel(CharacterData character)
    {
        if (currentModelInstance != null)
        {
            Destroy(currentModelInstance);
            currentModelInstance = null;
        }

        bool hasModel = character != null && character.modelPrefab != null && modelContainer != null;

        if (hasModel)
        {
            currentModelInstance = Instantiate(character.modelPrefab, modelContainer);
            currentModelInstance.transform.localPosition = character.modelSpawnPosition;
            currentModelInstance.transform.localRotation = Quaternion.Euler(character.modelSpawnRotation);
            currentModelInstance.transform.localScale = Vector3.one;

            Animator modelAnimator = currentModelInstance.GetComponentInChildren<Animator>();
            if (modelAnimator != null && !string.IsNullOrEmpty(character.defaultAnimation))
            {
                modelAnimator.Play(character.defaultAnimation);
            }
        }

        // ?????????,???????????
        if (fullBodyImage != null)
            fullBodyImage.gameObject.SetActive(!hasModel);
    }

    void DestroyCurrentModel()
    {
        if (currentModelInstance != null)
        {
            Destroy(currentModelInstance);
            currentModelInstance = null;
        }
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

        // 普通攻击区域：显示最终攻击力（默认属性，升级后更新数字）
        int totalAttack = character.baseAttack + normalTotal.attackBonus + skillTotal.attackBonus;
        if (normalAttackStatText != null)
            normalAttackStatText.text = $"攻击: {totalAttack}";

        // 技能攻击区域：显示技能攻击力（默认属性，升级后更新数字）
        int skillAttack = character.baseAttack + skillTotal.attackBonus;
        if (skillAttackStatText != null)
            skillAttackStatText.text = $"技能: {skillAttack}";
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

        // 等级显示（当前等级 → 下一等级，满级显示 MAX）
        if (normalAttackLevelText != null)
        {
            if (!unlocked)
                normalAttackLevelText.text = "Lv.--";
            else if (normalMaxed)
                normalAttackLevelText.text = "MAX";
            else
                normalAttackLevelText.text = $"Lv.{normalLevel} → {normalLevel + 1}";
        }

        // 升级所需金币（满级清空）
        if (normalAttackCoinText != null)
        {
            if (!unlocked || normalMaxed || nextNormal == null || normalConfig == null)
                normalAttackCoinText.text = "";
            else
                normalAttackCoinText.text = $"{nextNormal.cost}";
        }

        // 描述显示（当前等级的描述，0 级时显示下一级效果）
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

        // 按钮（只控制可点，文字由场景固定，不自动创建字体）
        if (normalAttackUpgradeButton != null)
        {
            if (!unlocked)
            {
                normalAttackUpgradeButton.interactable = false;
                Debug.Log($"[升级按钮] {name} 普通攻击禁用：未解锁");
            }
            else
            {
                if (normalMaxed || nextNormal == null || normalConfig == null)
                {
                    normalAttackUpgradeButton.interactable = false;
                    Debug.Log($"[升级按钮] {name} 普通攻击禁用：maxed={normalMaxed} nextNull={nextNormal == null} configNull={normalConfig == null}");
                }
                else
                {
                    normalAttackUpgradeButton.interactable = gameDataManager.TotalCoins >= nextNormal.cost;
                    Debug.Log($"[升级按钮] {name} 普通攻击 金币={gameDataManager.TotalCoins} 需要={nextNormal.cost} => {normalAttackUpgradeButton.interactable}");
                }
            }
        }

        // ===== 技能攻击 =====
        int skillLevel = GetSkillLevel(name);
        bool skillMaxed = skillLevel >= skillMaxLevel;
        var nextSkill = skillConfig?.GetLevelData(skillLevel + 1);
        var currentSkill = skillConfig?.GetLevelData(skillLevel);

        // 等级显示（当前等级 → 下一等级，满级显示 MAX）
        if (skillAttackLevelText != null)
        {
            if (!unlocked)
                skillAttackLevelText.text = "Lv.--";
            else if (skillMaxed)
                skillAttackLevelText.text = "MAX";
            else
                skillAttackLevelText.text = $"Lv.{skillLevel} → {skillLevel + 1}";
        }

        // 升级所需金币（满级清空）
        if (skillAttackCoinText != null)
        {
            if (!unlocked || skillMaxed || nextSkill == null || skillConfig == null)
                skillAttackCoinText.text = "";
            else
                skillAttackCoinText.text = $"{nextSkill.cost}";
        }

        // 描述显示（当前等级的描述，0 级时显示下一级效果）
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
                skillAttackUpgradeButton.interactable = false;
                Debug.Log($"[升级按钮] {name} 技能攻击禁用：未解锁");
            }
            else
            {
                if (skillMaxed || nextSkill == null || skillConfig == null)
                {
                    skillAttackUpgradeButton.interactable = false;
                    Debug.Log($"[升级按钮] {name} 技能攻击禁用：maxed={skillMaxed} nextNull={nextSkill == null} configNull={skillConfig == null}");
                }
                else
                {
                    skillAttackUpgradeButton.interactable = gameDataManager.TotalCoins >= nextSkill.cost;
                    Debug.Log($"[升级按钮] {name} 技能攻击 金币={gameDataManager.TotalCoins} 需要={nextSkill.cost} => {skillAttackUpgradeButton.interactable}");
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
        if (current >= config.maxLevel) return;

        var next = config.GetLevelData(current + 1);
        if (next == null || !gameDataManager.SpendCoins(next.cost)) return;

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
        if (current >= config.maxLevel) return;

        var next = config.GetLevelData(current + 1);
        if (next == null || !gameDataManager.SpendCoins(next.cost)) return;

        SetSkillLevel(name, current + 1);
        UpdateCharacterStats(currentDisplayCharacter);
        UpdateSkillDisplay(currentDisplayCharacter);
        gameDataManager.NotifyDataChanged();
    }

    // ==================== 华文字体（统一本面板所有文本） ====================

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
                selectCharacterButton.interactable = false;
            else if (unlocked)
                selectCharacterButton.interactable = true;
            else
                selectCharacterButton.interactable = gameDataManager.TotalCoins >= character.unlockCost;
        }
    }

    public void SelectCurrentCharacter()
    {
        if (currentDisplayCharacter == null) return;

        if (!gameDataManager.IsCharacterUnlocked(currentDisplayCharacter))
        {
            if (gameDataManager.UnlockCharacter(currentDisplayCharacter))
            {
                gameDataManager.SelectCharacter(currentDisplayCharacter);
                gameDataManager.NotifyDataChanged();
            }
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

        // 按钮（文字由场景固定，只禁用）
        if (normalAttackUpgradeButton != null)
        {
            normalAttackUpgradeButton.interactable = false;
        }
        if (skillAttackUpgradeButton != null)
        {
            skillAttackUpgradeButton.interactable = false;
        }

        if (selectButtonText != null)
        {
            selectButtonText.text = "选择角色";
            UpdateSelectButtonShadow();
        }
        if (selectCharacterButton != null)
            selectCharacterButton.interactable = false;
    }

    public CharacterData GetCurrentDisplayCharacter()
    {
        return currentDisplayCharacter;
    }
}