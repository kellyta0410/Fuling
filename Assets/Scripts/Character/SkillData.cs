using UnityEngine;

public enum SkillType
{
    MeleeAOE,
    Projectile,
    Heal,
    Shield,
}

[CreateAssetMenu(fileName = "NewSkill", menuName = "Game/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("基础信息")]
    public string skillID;
    public string skillName;
    public string description;
    public Sprite icon;
    public SkillType skillType;

    [Header("技能效果参数")]
    public float damageMultiplier = 1.5f;
    public float range = 3f;
    public float cooldown = 8f;
    public float duration = 0.5f;

    [Header("投射物专用")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    [Header("升级参数")]
    public int maxLevel = 5;
    public int baseUpgradeCost = 50;
    public int costIncreasePerLevel = 25;

    [Header("每级成长")]
    public float damagePerLevel = 0.1f;
    public float cooldownReductionPerLevel = 0.5f;

    public float GetDamageMultiplier(int level)
    {
        return damageMultiplier + (damagePerLevel * level);
    }

    public float GetCooldown(int level)
    {
        return Mathf.Max(2f, cooldown - (cooldownReductionPerLevel * level));
    }

    public int GetUpgradeCost(int currentLevel)
    {
        if (currentLevel >= maxLevel) return 99999;
        return baseUpgradeCost + (currentLevel * costIncreasePerLevel);
    }

    public string GetDescription(int level)
    {
        if (level <= 0) return description + "\n<color=#888888>未解锁</color>";
        if (level >= maxLevel) return description + $"\n<color=#FFD700>★ MAX</color>";

        float dmg = GetDamageMultiplier(level);
        float cd = GetCooldown(level);
        return description +
               $"\n<color=#FF6666>伤害: {dmg:F1}x</color>" +
               $"\n<color=#66FF66>冷却: {cd:F1}s</color> (Lv.{level})";
    }
}