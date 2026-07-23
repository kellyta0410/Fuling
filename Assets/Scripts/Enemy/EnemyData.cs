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

    [Header("奖励")]
    public int coinReward = 10;

    public float scale = 1f;

    [Header("动画参数")]
    public float deathAnimationDelay = 2f;

    [Header("调试")]
    public bool showGizmos = true;
}