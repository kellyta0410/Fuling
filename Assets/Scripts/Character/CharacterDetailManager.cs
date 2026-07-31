using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class CharacterDetailManager : MonoBehaviour
{
    [Header("===== 角色显示 =====")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image fullBodyImage;
    [SerializeField] private TextMeshProUGUI characterNameText;

    [Header("===== Stats（一行显示）=====")]
    [SerializeField] private TextMeshProUGUI statsText;

    [Header("===== 普通攻击升级=====")]
    [SerializeField] private Button normalAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI normalAttackLevelText;
    [SerializeField] private TextMeshProUGUI normalAttackDescriptionText;
    // ⭐ 移除 normalAttackCostText，费用直接显示在按钮上

    [Header("===== 技能攻击升级=====")]
    [SerializeField] private Button skillAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI skillAttackLevelText;
    [SerializeField] private TextMeshProUGUI skillAttackDescriptionText;
    // ⭐ 移除 skillAttackCostText，费用直接显示在按钮上

    [Header("===== 选择按钮 =====")]
    [SerializeField] private Button selectCharacterButton;
    [SerializeField] private TextMeshProUGUI selectButtonText;

    [Header("===== 数据管理 =====")]
    [SerializeField] private GameDataManager gameDataManager;

    private CharacterData currentDisplayCharacter;

    void Start()
    {
        if (gameDataManager == null)
            gameDataManager = GameDataManager.Instance;

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！");
            return;
        }

        normalAttackUpgradeButton?.onClick.AddListener(UpgradeNormalAttack);
        skillAttackUpgradeButton?.onClick.AddListener(UpgradeSkillAttack);
        selectCharacterButton?.onClick.AddListener(SelectCurrentCharacter);

        gameDataManager.OnDataChanged += RefreshPanel;
    }

    void OnDestroy()
    {
        if (gameDataManager != null)
            gameDataManager.OnDataChanged -= RefreshPanel;
    }

    // ==================== 获取角色配置 ====================

    private UpgradeConfigSO GetNormalAttackConfig(CharacterData character)
    {
        return character != null ? character.normalAttackConfig : null;
    }

    private UpgradeConfigSO GetSkillAttackConfig(CharacterData character)
    {
        return character != null ? character.skillAttackConfig : null;
    }

    // ==================== 等级读写 ====================

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

    // ==================== 显示角色 ====================

    public void ShowCharacterDetail(CharacterData character)
    {
        if (character == null) { ClearPanel(); return; }
        currentDisplayCharacter = character;

        if (avatarImage != null && character.avatarSprite != null)
        {
            avatarImage.sprite = character.avatarSprite;
            avatarImage.preserveAspect = true;
        }

        if (fullBodyImage != null && character.fullBodySprite != null)
        {
            fullBodyImage.sprite = character.fullBodySprite;
            fullBodyImage.preserveAspect = true;
        }

        if (characterNameText != null)
            characterNameText.text = character.characterName;

        UpdateStats(character);
        UpdateSkillDisplay(character);
        UpdateSelectButtonState(character);
    }

    // ==================== 更新 Stats（一行显示） ====================

    void UpdateStats(CharacterData character)
    {
        string name = character.characterName;

        var normalConfig = GetNormalAttackConfig(character);
        var skillConfig = GetSkillAttackConfig(character);

        var normalTotal = normalConfig?.GetTotalBonus(GetNormalLevel(name)) ?? new UpgradeLevelData();
        var skillTotal = skillConfig?.GetTotalBonus(GetSkillLevel(name)) ?? new UpgradeLevelData();

        int totalAttack = character.baseAttack + normalTotal.attackBonus + skillTotal.attackBonus;
        float totalSpeed = character.baseSpeed + normalTotal.speedBonus + skillTotal.speedBonus;

        if (statsText != null)
        {
            statsText.text = $"Health: {character.baseHealth}  |  Attack: {totalAttack}  |  Speed: {totalSpeed:F1}";
        }
    }

    // ==================== 更新技能显示 ====================

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

        if (normalAttackLevelText != null)
            normalAttackLevelText.text = $"Lv.{normalLevel}/{normalMaxLevel}";

        if (normalAttackDescriptionText != null)
            normalAttackDescriptionText.text = nextNormal?.description ?? "已满级";

        // ⭐ 费用直接显示在按钮上
        if (normalAttackUpgradeButton != null)
        {
            if (normalMaxed || nextNormal == null || normalConfig == null)
            {
                // 按钮显示 "MAX" 且不可交互
                var btnText = normalAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "MAX";
                    btnText.color = Color.green;
                }
                normalAttackUpgradeButton.interactable = false;
            }
            else
            {
                // 按钮显示费用
                var btnText = normalAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = $"{nextNormal.cost}";
                    btnText.color = gameDataManager.TotalCoins >= nextNormal.cost ? Color.yellow : Color.gray;
                }
                normalAttackUpgradeButton.interactable = gameDataManager.TotalCoins >= nextNormal.cost && unlocked;
            }
        }

        // ===== 技能攻击 =====
        int skillLevel = GetSkillLevel(name);
        bool skillMaxed = skillLevel >= skillMaxLevel;
        var nextSkill = skillConfig?.GetLevelData(skillLevel + 1);

        if (skillAttackLevelText != null)
            skillAttackLevelText.text = $"Lv.{skillLevel}/{skillMaxLevel}";

        if (skillAttackDescriptionText != null)
            skillAttackDescriptionText.text = nextSkill?.description ?? "已满级";

        // ⭐ 费用直接显示在按钮上
        if (skillAttackUpgradeButton != null)
        {
            if (skillMaxed || nextSkill == null || skillConfig == null)
            {
                var btnText = skillAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "MAX";
                    btnText.color = Color.green;
                }
                skillAttackUpgradeButton.interactable = false;
            }
            else
            {
                var btnText = skillAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = $"{nextSkill.cost}";
                    btnText.color = gameDataManager.TotalCoins >= nextSkill.cost ? Color.yellow : Color.gray;
                }
                skillAttackUpgradeButton.interactable = gameDataManager.TotalCoins >= nextSkill.cost && unlocked;
            }
        }
    }

    // ==================== 升级 ====================

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
        UpdateStats(currentDisplayCharacter);
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
        UpdateStats(currentDisplayCharacter);
        UpdateSkillDisplay(currentDisplayCharacter);
        gameDataManager.NotifyDataChanged();
    }

    // ==================== 选择角色 ====================

    void UpdateSelectButtonState(CharacterData character)
    {
        bool unlocked = gameDataManager.IsCharacterUnlocked(character);
        bool selected = gameDataManager.CurrentCharacter?.characterName == character.characterName;

        if (selectButtonText != null)
        {
            if (selected)
            {
                selectButtonText.text = "✓ 已选择";
                selectButtonText.color = Color.green;
            }
            else if (unlocked)
            {
                selectButtonText.text = "选择角色";
                selectButtonText.color = Color.white;
            }
            else
            {
                selectButtonText.text = $"解锁 ({character.unlockCost})";
                selectButtonText.color = gameDataManager.TotalCoins >= character.unlockCost ? Color.yellow : Color.gray;
            }
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
        if (avatarImage != null) avatarImage.sprite = null;
        if (fullBodyImage != null) fullBodyImage.sprite = null;
        if (characterNameText != null) characterNameText.text = "";
        if (statsText != null) statsText.text = "Health: --  |  Attack: --  |  Speed: --";
        if (normalAttackLevelText != null) normalAttackLevelText.text = "Lv.0/10";
        if (skillAttackLevelText != null) skillAttackLevelText.text = "Lv.0/10";
        if (normalAttackDescriptionText != null) normalAttackDescriptionText.text = "";
        if (skillAttackDescriptionText != null) skillAttackDescriptionText.text = "";

        // 重置按钮文字
        if (normalAttackUpgradeButton != null)
        {
            var btnText = normalAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "--";
            normalAttackUpgradeButton.interactable = false;
        }
        if (skillAttackUpgradeButton != null)
        {
            var btnText = skillAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "--";
            skillAttackUpgradeButton.interactable = false;
        }

        if (selectButtonText != null)
        {
            selectButtonText.text = "请选择角色";
            selectButtonText.color = Color.gray;
        }
        if (selectCharacterButton != null)
            selectCharacterButton.interactable = false;
    }

    public CharacterData GetCurrentDisplayCharacter()
    {
        return currentDisplayCharacter;
    }
}