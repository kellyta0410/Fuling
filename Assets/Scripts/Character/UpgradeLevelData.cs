using UnityEngine;

[System.Serializable]
public class UpgradeLevelData
{
    public int level;
    public string description;
    public int cost;

    public int attackBonus = 0;
    public float attackRangeBonus = 0f;
    public float speedBonus = 0f;
    public float cooldownReductionBonus = 0f;
    public int skillDamageBonus = 0;
}