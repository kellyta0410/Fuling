using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地牢（Infinite）模式专用难度配置，独立于普通/Buff 模式的 DifficultySettings。
/// 作为可序列化字段直接内嵌在 DungeonManager 的 Inspector 中，无需单独创建资产。
/// </summary>
[System.Serializable]
public class DungeonDifficulty
{
    [Header("敌人池（按房间类型取用）")]
    public List<GameObject> snakeEnemyPrefabs = new List<GameObject>();
    public List<GameObject> zombieEnemyPrefabs = new List<GameObject>();

    [Header("每房间数量上限")]
    public int maxEnemiesPerRoom = 30;            // 每房最多 30 个（含混合）后停刷

    [Header("属性缩放：按房间序号递增，封顶")]
    public float enemySpeedMultiplier  = 1f;
    public float speedMultiplierStep  = 0.03f;
    public float speedMultiplierMax   = 2.5f;

    public float enemyHealthMultiplier = 1f;
    public float healthMultiplierStep  = 0.04f;
    public float healthMultiplierMax   = 3f;

    public float enemyDamageMultiplier = 1f;
    public float damageMultiplierStep  = 0.02f;
    public float damageMultiplierMax   = 2f;
}
