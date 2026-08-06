using UnityEngine;
using System.Collections.Generic;

public enum GameMode
{
    Normal,
    Infinite
}

[CreateAssetMenu(fileName = "NewDifficulty", menuName = "Game/Difficulty Settings")]
public class DifficultySettings : ScriptableObject
{
    [Header("难度基本信息")]
    public string difficultyName = "普通";
    public string sceneName = "GameScene";

    [Header("模式选择")]
    public GameMode mode = GameMode.Normal;

    [Header("普通模式 - 时间限制")]
    public float timeLimit = 90f;

    [Header("无限模式 - 成长设置")]
    public bool enableScaling = true;
    public float scalingInterval = 30f;

    public float spawnIntervalMin = 0.3f;
    public int spawnPerIntervalMax = 5;
    public float speedMultiplierMax = 2.5f;
    public float healthMultiplierMax = 3f;
    public float damageMultiplierMax = 2f;

    public float spawnIntervalStep = 0.05f;
    public int spawnPerIntervalStep = 1;
    public float speedMultiplierStep = 0.03f;
    public float healthMultiplierStep = 0.04f;
    public float damageMultiplierStep = 0.02f;

    [Header("生成参数（基础值）")]
    public float spawnInterval = 2f;
    public int spawnPerInterval = 1;
    public float enemySpeedMultiplier = 1f;

    [Header("容量限制")]
    public bool enableMaxLimit = true;
    public int maxEnemyCount = 30;

    [Header("容量限制成长（无限模式）")]
    public bool enableMaxLimitScaling = true;
    public int maxEnemyCountStep = 2;
    public int maxEnemyCountMax = 100;

    [Header("冷却")]
    public bool enableCooldown = true;
    public float cooldownTime = 10f;

    [Header("敌人类型")]
    public List<GameObject> allowedEnemyPrefabs;

    [Header("无限模式 - 成长上限")]
    public int maxScalingLevel = 100;

    public bool IsInfiniteMode()
    {
        return mode == GameMode.Infinite;
    }
}