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
}