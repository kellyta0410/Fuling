using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("基础信息")]
    public string characterName;
    public string description;
    public Sprite portrait;
    public GameObject prefab;

    [Header("解锁条件")]
    public int unlockCost = 1500;

    [Header("基础属性")]
    public float baseHealth = 100f;
    public float baseSpeed = 4f;
    public int baseAttack = 20;
    public float baseAttackRange = 2f;
    public float baseAttackCooldown = 1f;
}