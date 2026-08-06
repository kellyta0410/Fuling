using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewUpgradeConfig", menuName = "Game/Upgrade Config")]
public class UpgradeConfigSO : ScriptableObject
{
    [Header("===== 基本信息 =====")]
    public string configName = "升级配置";
    public int maxLevel = 10;

    [Header("===== 金币花费（自动规律计算，逐级加速递增）=====")]
    [Tooltip("升到第 2 级的金币")]
    public int costBase = 25;
    [Tooltip("第 3 级比第 2 级多出的金币（每级的基础增量）")]
    public int costIncrease = 20;
    [Tooltip("每升一级，下一级增量再额外增加的数量（加速效果）")]
    public int costAcceleration = 10;

    [Header("===== 手动逐级加成（只填加成；金币由上方规律自动计算，成本区内 Cost 填 0 即可）=====")]
    public List<UpgradeLevelData> manualLevels = new List<UpgradeLevelData>();

    // ==================== 获取等级数据 ====================

    public UpgradeLevelData GetLevelData(int level)
    {
        if (manualLevels == null || manualLevels.Count == 0) return null;
        if (level <= 0 || level > manualLevels.Count) return null;
        return manualLevels[level - 1];
    }

    public List<UpgradeLevelData> GetAllLevels()
    {
        return manualLevels ?? new List<UpgradeLevelData>();
    }

    // ==================== 金币花费（规律自动计算）====================

    // 第 2 级 = costBase；之后每级的增量 = costIncrease + (级数-2) * costAcceleration
    // 例如：L2=25, L3=55, L4=95, L5=145, L6=205（costBase=25, costIncrease=30, costAcceleration=10）
    public int GetLevelCost(int level)
    {
        if (level <= 1 || level >= maxLevel) return 0; // 无上级(满级)免费
        int steps = level - 2;
        int cost = costBase;
        int stepInc = costIncrease;
        for (int i = 0; i < steps; i++)
        {
            cost += stepInc;
            stepInc += costAcceleration;
        }
        return Mathf.Max(0, cost);
    }

    // ==================== 获取累计加成 ====================

    public UpgradeLevelData GetTotalBonus(int level)
    {
        var total = new UpgradeLevelData();
        if (manualLevels == null || manualLevels.Count == 0) return total;

        int max = Mathf.Min(level, manualLevels.Count);
        for (int i = 0; i < max; i++)
        {
            var d = manualLevels[i];
            total.attackBonus += d.attackBonus;
            total.attackRangeBonus += d.attackRangeBonus;
            total.speedBonus += d.speedBonus;
            total.cooldownReductionBonus += d.cooldownReductionBonus;
            total.skillDamageBonus += d.skillDamageBonus;
        }
        return total;
    }
}