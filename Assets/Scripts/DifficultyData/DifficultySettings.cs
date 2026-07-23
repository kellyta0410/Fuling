using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewDifficulty", menuName = "Game/Difficulty Settings")]
public class DifficultySettings : ScriptableObject
{
    [Header("难度基本信息")]
    public string difficultyName = "普通";
    public string sceneName = "GameScene";

    [Header("无限模式")]
    public bool isInfiniteMode = false;

    [Header("时间限制")]
    public float timeLimit = 90f;

    [Header("生成参数")]
    public float spawnInterval = 2f;
    public int spawnPerInterval = 1;
    public float enemySpeedMultiplier = 1f;

    [Header("容量限制")]
    public bool enableMaxLimit = true;
    public int maxEnemyCount = 30;

    [Header("冷却")]
    public bool enableCooldown = true;
    public float cooldownTime = 10f;

    [Header("敌人类型")]
    public List<GameObject> allowedEnemyPrefabs;

    // ❌ 删除 timerColor
}