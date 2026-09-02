using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BuffType
{
    Heal,
    SpeedUp,            // 移速
    PowerUp,            // 普通攻击力
    AttackRangeUp,      // 普通攻击范围
    SkillPowerUp,       // 技能攻击力
    SkillRangeUp,       // 技能范围
    SkillCooldownUp,    // 技能冷却速度（缩短冷却）
    CoinMultUp,         // 金币掉落倍率
    MaxHealthUp         // 血量上限
}

[CreateAssetMenu(fileName = "Buff_New", menuName = "Game/Buff Data")]
public class BuffDataSO : ScriptableObject
{
    public BuffType buffType;
    public string buffName;
    public Sprite icon;
    [TextArea(2, 4)]
    public string description;

    [Header("效果分类")]
    [Tooltip("勾选 = 即时恢复(Heal)；不勾 = 商店永久叠层")]
    public bool isInstantEffect;

    [Header("数值参数")]
    [Tooltip("恢复类：恢复的血量（半血/满血忽略）。永久类：每一层的幅度。")]
    public float effectValue;

    [Header("恢复 Heal（地牢商店即时购买）")]
    public bool isFullRestore;
    public bool isHalfRestore;
    [Tooltip("恢复类在地牢商店里的价格")]
    public int shopCost = 40;

    [Header("商店永久叠层（地牢商店）")]
    [Tooltip("最多可购买的层数（默认 5）")]
    public int maxStack = 5;

    [Header("地图限时拾取（普通模式，可选）")]
    [Tooltip("作为地图掉落时的持续时长（秒）")]
    public float duration = 5f;
    public GameObject pickupPrefab;

    // 当资产未填写 description 时，按类型自动生成一句说明（供商店卡片展示），并列出数值/层数
    public string GetDefaultDescription()
    {
        // 加算型（固定数值）vs 乘算型（百分比），按类型判断
        bool isAdditive = buffType == BuffType.PowerUp
                       || buffType == BuffType.SkillPowerUp
                       || buffType == BuffType.AttackRangeUp
                       || buffType == BuffType.SkillRangeUp
                       || buffType == BuffType.SkillCooldownUp
                       || buffType == BuffType.MaxHealthUp;
        string unit = buffType == BuffType.SkillCooldownUp ? "秒" : "点";
        string perLayer = isAdditive
            ? ("每层+" + effectValue.ToString("G") + unit)
            : ("每层+" + (effectValue * 100f).ToString("F0") + "%");

        switch (buffType)
        {
            case BuffType.Heal:
                if (isFullRestore) return "立即生命值回满。";
                if (isHalfRestore) return "立即恢复一半生命值。";
                return effectValue > 0 ? ("立即恢复" + effectValue.ToString("F0") + "点生命值。") : "立即恢复生命值。";
        }

        string baseText;
        switch (buffType)
        {
            case BuffType.SpeedUp: baseText = "提升移动速度"; break;
            case BuffType.PowerUp: baseText = "提升普通攻击伤害"; break;
            case BuffType.AttackRangeUp: baseText = "扩大普通攻击范围"; break;
            case BuffType.SkillPowerUp: baseText = "提升技能攻击伤害"; break;
            case BuffType.SkillRangeUp: baseText = "扩大技能攻击范围"; break;
            case BuffType.SkillCooldownUp: baseText = "加快技能冷却恢复速度"; break;
            case BuffType.CoinMultUp: baseText = "提高金币掉落倍率"; break;
            case BuffType.MaxHealthUp: baseText = "提高生命值上限"; break;
            default: baseText = "未知增益"; break;
        }

        if (isInstantEffect) return baseText + "。";

        if (maxStack <= 1)
            return baseText + "(" + perLayer + ")。";

        string total;
        if (isAdditive)
            total = "，满级共+" + (effectValue * maxStack).ToString("G") + unit;
        else
            total = "，满级共+" + (effectValue * maxStack * 100f).ToString("F0") + "%";

        return baseText + "(最高" + maxStack + "层，" + perLayer + total + ")。";
    }

    // 返回指定层数的描述（商店卡片用），显示"第X层"及该层对应数值
    public string GetLayerDescription(int layer)
    {
        if (layer < 1) layer = 1;
        if (layer > maxStack) layer = maxStack;

        bool isAdditive = buffType == BuffType.PowerUp
                       || buffType == BuffType.SkillPowerUp
                       || buffType == BuffType.AttackRangeUp
                       || buffType == BuffType.SkillRangeUp
                       || buffType == BuffType.SkillCooldownUp
                       || buffType == BuffType.MaxHealthUp;
        string unit = buffType == BuffType.SkillCooldownUp ? "秒" : "点";

        string baseText;
        switch (buffType)
        {
            case BuffType.Heal:
                return GetDefaultDescription();
            case BuffType.SpeedUp: baseText = "提升移动速度"; break;
            case BuffType.PowerUp: baseText = "提升普通攻击伤害"; break;
            case BuffType.AttackRangeUp: baseText = "扩大普通攻击范围"; break;
            case BuffType.SkillPowerUp: baseText = "提升技能攻击伤害"; break;
            case BuffType.SkillRangeUp: baseText = "扩大技能攻击范围"; break;
            case BuffType.SkillCooldownUp: baseText = "加快技能冷却恢复速度"; break;
            case BuffType.CoinMultUp: baseText = "提高金币掉落倍率"; break;
            case BuffType.MaxHealthUp: baseText = "提高生命值上限"; break;
            default: baseText = "未知增益"; break;
        }

        float totalValue = layer * effectValue;
        string valueStr;
        if (isAdditive)
            valueStr = "Lv." + layer + " → +" + totalValue.ToString("G") + unit;
        else
            valueStr = "Lv." + layer + " → +" + (totalValue * 100f).ToString("F0") + "%";

        return baseText + "（" + valueStr + "）";
    }
}