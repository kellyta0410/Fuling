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
    [SerializeField] private TextMeshProUGUI characterDescriptionText; // 角色背景介绍

    [Header("===== 角色属性（横排显示）=====")]
    [SerializeField] private TextMeshProUGUI attackStatText;    // "攻击: 150"
    [SerializeField] private TextMeshProUGUI rangeStatText;     // "射程: 10.0"
    [SerializeField] private TextMeshProUGUI cooldownStatText;  // "冷却: 2.5s"
    // ⚠️ 移速不显示在 UI 上，但数据在后台计算（PlayerController 使用）

    [Header("===== 普通攻击升级=====")]
    [SerializeField] private Button normalAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI normalAttackLevelText;
    [SerializeField] private TextMeshProUGUI normalAttackDescriptionText;

    [Header("===== 技能攻击升级=====")]
    [SerializeField] private Button skillAttackUpgradeButton;
    [SerializeField] private TextMeshProUGUI skillAttackLevelText;
    [SerializeField] private TextMeshProUGUI skillAttackDescriptionText;

    [Header("===== 选择按钮 =====")]
    [SerializeField] private Button selectCharacterButton;
    [SerializeField] private TextMeshProUGUI selectButtonText;

    [Header("===== 数据管理 =====")]
    [SerializeField] private GameDataManager gameDataManager;

    [Header("===== 3D 模型显示（可选）=====")]
    [Tooltip("3D 模型挂载点（世界空间）。留空则回退到全身图显示")]
    [SerializeField] private Transform modelContainer;
    private GameObject currentModelInstance;

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

    /// <summary>
    /// 供场景头像按钮绑定调用
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

        // ===== Avatar 显示（根据解锁状态切换） =====
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

        // ===== 全身图显示（根据解锁状态） =====
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

        // ===== 角色名称 =====
        if (characterNameText != null)
            characterNameText.text = isUnlocked ? character.characterName : "???";

        // ===== 角色介绍 =====
        if (characterDescriptionText != null)
            characterDescriptionText.text = isUnlocked ? (character.characterDescription ?? "暂无介绍") : "未解锁此角色";

        // ===== 更新属性显示 =====
        UpdateCharacterStats(character);
        UpdateSkillDisplay(character);
        UpdateSelectButtonState(character);

        // ===== 3D 模型显示 =====
        UpdateCharacterModel(character);
    }

    // ==================== 3D 模型显示 ====================

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

        // 有模型时隐藏全身图，无模型时恢复全身图显示
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

    // ==================== 更新角色属性（横排显示：攻击、射程、冷却） ====================

    void UpdateCharacterStats(CharacterData character)
    {
        if (character == null) return;

        string name = character.characterName;

        var normalConfig = GetNormalAttackConfig(character);
        var skillConfig = GetSkillAttackConfig(character);

        var normalTotal = normalConfig?.GetTotalBonus(GetNormalLevel(name)) ?? new UpgradeLevelData();
        var skillTotal = skillConfig?.GetTotalBonus(GetSkillLevel(name)) ?? new UpgradeLevelData();

        // 计算最终属性
        int totalAttack = character.baseAttack + normalTotal.attackBonus + skillTotal.attackBonus;
        float totalRange = character.baseRange + normalTotal.attackRangeBonus + skillTotal.attackRangeBonus;
        float totalCooldown = character.baseCooldown - normalTotal.cooldownReductionBonus - skillTotal.cooldownReductionBonus;

        // 移速在后台计算（供 PlayerController 使用），但不显示在 UI 上
        float totalSpeed = character.baseSpeed + normalTotal.speedBonus + skillTotal.speedBonus;

        // 更新 UI（只显示攻击、射程、冷却）
        if (attackStatText != null)
            attackStatText.text = $"攻击: {totalAttack}";

        if (rangeStatText != null)
            rangeStatText.text = $"射程: {totalRange:F1}";

        if (cooldownStatText != null)
            cooldownStatText.text = $"冷却: {totalCooldown:F1}s";
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
        var currentNormal = normalConfig?.GetLevelData(normalLevel);

        // 等级显示（只显示当前等级）
        if (normalAttackLevelText != null)
        {
            if (unlocked)
                normalAttackLevelText.text = $"Lv.{normalLevel}";
            else
                normalAttackLevelText.text = "Lv.--";
        }

        // 描述显示（显示当前等级的描述）
        if (normalAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                normalAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else if (normalLevel == 0 && currentNormal == null)
            {
                normalAttackDescriptionText.text = normalConfig?.GetLevelData(0)?.description ?? "未解锁";
            }
            else
            {
                normalAttackDescriptionText.text = currentNormal?.description ?? "无描述";
            }
        }

        // 按钮显示
        if (normalAttackUpgradeButton != null)
        {
            if (!unlocked)
            {
                var btnText = normalAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "🔒";
                normalAttackUpgradeButton.interactable = false;
            }
            else if (normalMaxed || nextNormal == null || normalConfig == null)
            {
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
        var currentSkill = skillConfig?.GetLevelData(skillLevel);

        // 等级显示（只显示当前等级）
        if (skillAttackLevelText != null)
        {
            if (unlocked)
                skillAttackLevelText.text = $"Lv.{skillLevel}";
            else
                skillAttackLevelText.text = "Lv.--";
        }

        // 描述显示（显示当前等级的描述）
        if (skillAttackDescriptionText != null)
        {
            if (!unlocked)
            {
                skillAttackDescriptionText.text = "解锁角色以查看技能";
            }
            else if (skillLevel == 0 && currentSkill == null)
            {
                skillAttackDescriptionText.text = skillConfig?.GetLevelData(0)?.description ?? "未解锁";
            }
            else
            {
                skillAttackDescriptionText.text = currentSkill?.description ?? "无描述";
            }
        }

        // 按钮显示
        if (skillAttackUpgradeButton != null)
        {
            if (!unlocked)
            {
                var btnText = skillAttackUpgradeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = "🔒";
                skillAttackUpgradeButton.interactable = false;
            }
            else if (skillMaxed || nextSkill == null || skillConfig == null)
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

        DestroyCurrentModel();

        if (avatarImage != null) avatarImage.sprite = null;
        if (fullBodyImage != null) fullBodyImage.sprite = null;
        if (characterNameText != null) characterNameText.text = "";
        if (characterDescriptionText != null) characterDescriptionText.text = "";

        // 清空属性显示
        if (attackStatText != null) attackStatText.text = "攻击: --";
        if (rangeStatText != null) rangeStatText.text = "射程: --";
        if (cooldownStatText != null) cooldownStatText.text = "冷却: --";

        if (normalAttackLevelText != null) normalAttackLevelText.text = "Lv.--";
        if (skillAttackLevelText != null) skillAttackLevelText.text = "Lv.--";
        if (normalAttackDescriptionText != null) normalAttackDescriptionText.text = "";
        if (skillAttackDescriptionText != null) skillAttackDescriptionText.text = "";

        // 重置按钮
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