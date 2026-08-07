using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Enemy/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("基础属性")]
    public string enemyName = "敌人";
    public float health = 50f;
    public float speed = 2f;
    public float attackRange = 1.5f;
    public int attackDamage = 10;
    public float attackCooldown = 1f;

    [Header("攻击时机")]
    [Tooltip("敌人攻击造成伤害的延迟（秒），对齐到攻击动画命中玩家那一刻")]
    public float attackDamageDelay = 0.3f;

    [Header("奖励")]
    public int coinReward = 10;

    //public float scale = 1f;

    [Header("动画参数")]
    public float deathAnimationDelay = 2f;

    [Header("调试")]
    public bool showGizmos = true;
}