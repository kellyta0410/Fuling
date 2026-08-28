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
        float pct = effectValue * 100f;
        switch (buffType)
        {
            case BuffType.Heal:
                if (isFullRestore) return "立即将生命值回满。";
                if (isHalfRestore) return "立即恢复一半生命值。";
                return effectValue > 0 ? ("立即恢复 " + effectValue.ToString("F0") + " 点生命值。") : "立即恢复生命值。";
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
            return baseText + (pct > 0 ? ("（+" + pct.ToString("F0") + "%）。") : "。");

        string info = "（最高 " + maxStack + " 层";
        if (pct > 0) info += "，每层 +" + pct.ToString("F0") + "%，满级共 +" + (pct * maxStack).ToString("F0") + "%";
        info += "）。";
        return baseText + info;
    }
}