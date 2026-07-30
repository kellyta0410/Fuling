using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Game/CharacterData")]
public class CharacterData : ScriptableObject
{
    [Header("===== 基本信息 =====")]
    public string characterName;

    [Header("===== 美术资源 =====")]
    public Sprite avatarSprite;
    public Sprite fullBodySprite;

    [Header("===== 基础属性 =====")]
    public int baseHealth = 100;
    public int baseAttack = 10;
    public float baseSpeed = 5f;
    public int baseDefense = 5;
    public float baseAttackRange = 2f;
    public float baseAttackCooldown = 1f;

    [Header("===== 解锁条件 =====")]
    public int unlockCost = 100;

    [Header("===== 升级配置（每个角色独立） =====")]
    public UpgradeConfigSO normalAttackConfig;
    public UpgradeConfigSO skillAttackConfig;
}