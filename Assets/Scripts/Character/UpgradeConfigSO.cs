using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewUpgradeConfig", menuName = "Game/Upgrade Config")]
public class UpgradeConfigSO : ScriptableObject
{
    [Header("===== 基本信息 =====")]
    public string configName = "升级配置";
    public int maxLevel = 10;

    [Header("===== 每级加成（每级固定增加）=====")]
    public int attackBonusPerLevel = 3;
    public float attackRangeBonusPerLevel = 0.2f;
    public float speedBonusPerLevel = 0f;
    public float cooldownReductionPerLevel = 0f;
    public int skillDamageBonusPerLevel = 0;

    [Header("===== 费用（每级增加）=====")]
    public int baseCost = 50;
    public int costIncreasePerLevel = 30;

    [Header("===== 生成的每级数据（只读）=====")]
    [SerializeField] private List<UpgradeLevelData> generatedLevels = new List<UpgradeLevelData>();

    // ==================== 获取等级数据 ====================

    public UpgradeLevelData GetLevelData(int level)
    {
        GenerateLevels();
        if (level <= 0 || level > generatedLevels.Count) return null;
        return generatedLevels[level - 1];
    }

    public List<UpgradeLevelData> GetAllLevels()
    {
        GenerateLevels();
        return generatedLevels;
    }

    // ==================== 获取累计加成 ====================

    public UpgradeLevelData GetTotalBonus(int level)
    {
        GenerateLevels();
        var total = new UpgradeLevelData();
        int max = Mathf.Min(level, generatedLevels.Count);
        for (int i = 0; i < max; i++)
        {
            var d = generatedLevels[i];
            total.attackBonus += d.attackBonus;
            total.attackRangeBonus += d.attackRangeBonus;
            total.speedBonus += d.speedBonus;
            total.cooldownReductionBonus += d.cooldownReductionBonus;
            total.skillDamageBonus += d.skillDamageBonus;
        }
        return total;
    }

    // ==================== 生成升级数据 ====================

    public void GenerateLevels()
    {
        if (generatedLevels.Count == maxLevel) return;

        generatedLevels.Clear();

        for (int i = 1; i <= maxLevel; i++)
        {
            var data = new UpgradeLevelData();
            data.level = i;

            data.attackBonus = attackBonusPerLevel;
            data.attackRangeBonus = attackRangeBonusPerLevel;
            data.speedBonus = speedBonusPerLevel;
            data.cooldownReductionBonus = cooldownReductionPerLevel;
            data.skillDamageBonus = skillDamageBonusPerLevel;

            data.cost = baseCost + (i - 1) * costIncreasePerLevel;

            data.description = GenerateDescription(data, i);
            generatedLevels.Add(data);
        }
    }

    // ==================== 自动生成描述 ====================

    string GenerateDescription(UpgradeLevelData data, int level)
    {
        List<string> parts = new List<string>();

        int totalAttack = data.attackBonus * level;
        float totalRange = data.attackRangeBonus * level;
        float totalSpeed = data.speedBonus * level;
        float totalCooldown = data.cooldownReductionBonus * level;
        int totalSkillDamage = data.skillDamageBonus * level;

        if (totalAttack > 0)
            parts.Add($"攻击+{totalAttack}");
        if (totalRange > 0)
            parts.Add($"范围+{totalRange:F1}");
        if (totalSpeed > 0)
            parts.Add($"移速+{totalSpeed:F1}");
        if (totalCooldown > 0)
            parts.Add($"冷却-{totalCooldown:F1}秒");
        if (totalSkillDamage > 0)
            parts.Add($"技能伤害+{totalSkillDamage}");

        return parts.Count > 0 ? string.Join("，", parts) : "无加成";
    }

    // ==================== Inspector 刷新 ====================

#if UNITY_EDITOR
    public void OnValidate()
    {
        GenerateLevels();
    }

    [ContextMenu("重新生成升级数据")]
    public void RegenerateLevels()
    {
        generatedLevels.Clear();
        GenerateLevels();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}