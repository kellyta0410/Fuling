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

        if (fullBodyImage != null)
            fullBodyImage.gameObject.SetActive(true);
    }

    
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

        // 技能攻击区域：显示当前实际数值（基础攻击×2 + 技能伤害成长；冷却基准 15 秒）
        int skillAttack = character.baseAttack * 2 + skillTotal.skillDamageBonus;
        float skillRange = character.baseRange * 2f + skillTotal.attackRangeBonus;
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

        // 简介显示即将升到下一级获得的提升（每级描述往前调一级，不再出现"初始属性"占位）
        if (normalAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                normalAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else
            {
                normalAttackDescriptionText.text = GetNextLevelDescription(normalConfig, normalLevel) ?? "无描述";
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

        // 简介显示即将升到下一级获得的提升（每级描述往前调一级，不再出现"初始属性"占位）
        if (skillAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                skillAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else
            {
                skillAttackDescriptionText.text = GetNextLevelDescription(skillConfig, skillLevel) ?? "无描述";
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

    // ⭐ 描述显示"升到下一级将获得"的提升：每级往前调一级。
    // manualLevels 的 level 1 是"初始属性"占位（无加成），跳过它直接显示第一个真实加成；
    // 满级（MAX）时返回空串，不显示任何描述。
    string GetNextLevelDescription(UpgradeConfigSO config, int currentLevel)
    {
        if (config == null) return null;

        // 下一级的下标（GetLevelData 是 1-based）
        int nextLevel = currentLevel + 1;

        // 跳过硬编码的"初始属性"占位级：等级 0/1 时都显示第一个真实升级的描述
        if (currentLevel <= 1) nextLevel = 2;

        var data = config.GetLevelData(nextLevel);
        if (data != null) return data.description;

        // 满级（下一级不存在）：不显示描述
        return "";
    }

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