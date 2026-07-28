using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour
{
    [Header("核心设置")]
    [HideInInspector]
    public List<GameObject> enemyPrefabs;
    public Transform playerTarget;

    [Header("生成点设置")]
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();

    [Header("难度系统")]
    public DifficultySettings currentDifficulty;
    public bool useDifficultySettings = true;

    [Header("初始生成")]
    [Tooltip("开始时每个生成点生成的敌人数")]
    public int initialSpawnCount = 2;
    [Tooltip("是否在开始时立即生成")]
    public bool spawnOnStart = true;

    [Header("生成范围设置")]
    [Tooltip("默认生成半径（如果生成点未单独设置）")]
    public float defaultSpawnRadius = 5f;
    [Tooltip("NavMesh采样半径（用于寻找最近的NavMesh位置）")]
    public float navMeshSampleRadius = 5f;
    [Tooltip("生成位置尝试次数")]
    public int maxSpawnAttempts = 30;

    [Header("敌人权重（数值越大出现概率越高）")]
    public List<EnemyWeight> enemyWeights = new List<EnemyWeight>();

    [Header("调试")]
    public bool showDebugLogs = true;

    // 当前生成参数
    private float currentSpawnInterval = 2f;
    private int currentSpawnPerInterval = 1;
    private bool enableMaxLimit = false;
    private int maxEnemyCount = 30;
    private bool enableCooldown = true;
    private float cooldownTime = 10f;

    private float currentSpeedMultiplier = 1f;
    private float currentHealthMultiplier = 1f;
    private float currentDamageMultiplier = 1f;

    // 标记是否已完成初始生成
    private bool initialSpawnDone = false;

    [System.Serializable]
    public class SpawnPointData
    {
        public Transform point;
        public float activationRadius = 6f;    // ⭐ 从15改成6
        public float deactivationRadius = 10f;  // ⭐ 从22改成10
        [Tooltip("此生成点的生成半径（0则使用默认值）")]
        public float spawnRadius = 0f;
        public bool isActive = false;
        public float spawnTimer = 0f;
        public int totalSpawned = 0;
        public bool isOnCooldown = false;
        public float cooldownTimer = 0f;
        public bool hasSpawnedOnce = false;

        public TileType tileType = TileType.Grass;

        [System.NonSerialized]
        public List<GameObject> activeEnemies = new List<GameObject>();
    }

    [System.Serializable]
    public class EnemyWeight
    {
        public GameObject enemyPrefab;
        [Tooltip("权重值（越大出现概率越高）")]
        public float weight = 1f;
    }

    private List<GameObject> allActiveEnemies = new List<GameObject>();
    private int globalTotalSpawned = 0;

    void Awake()
    {
    }

    void Start()
    {
        InitializeSpawner();

        if (spawnOnStart)
        {
            PerformInitialSpawn();
        }
    }

    void InitializeSpawner()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
            else Debug.LogWarning("未找到 Player");
        }

        if (spawnPoints.Count == 0)
        {
            AutoFindSpawnPoints();
        }

        if (currentDifficulty == null && GameManager.Instance != null)
        {
            currentDifficulty = GameManager.Instance.currentDifficulty;
        }

        if (useDifficultySettings && currentDifficulty != null)
        {
            currentSpawnInterval = currentDifficulty.spawnInterval;
            currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
            enableMaxLimit = currentDifficulty.enableMaxLimit;
            maxEnemyCount = currentDifficulty.maxEnemyCount;
            enableCooldown = currentDifficulty.enableCooldown;
            cooldownTime = currentDifficulty.cooldownTime;

            if (currentDifficulty.allowedEnemyPrefabs != null && currentDifficulty.allowedEnemyPrefabs.Count > 0)
            {
                enemyPrefabs = new List<GameObject>(currentDifficulty.allowedEnemyPrefabs);

                if (enemyWeights.Count == 0)
                {
                    foreach (GameObject prefab in enemyPrefabs)
                    {
                        EnemyWeight ew = new EnemyWeight();
                        ew.enemyPrefab = prefab;
                        ew.weight = 1f;
                        enemyWeights.Add(ew);
                    }
                }
            }
        }

        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogError("没有敌人预制体！请在 DifficultySettings 中配置 allowedEnemyPrefabs");
        }

        string limitText = enableMaxLimit ? "上限: " + maxEnemyCount : "无限生成";
        string cooldownText = enableCooldown ? "冷却: " + cooldownTime + "秒" : "无冷却";
        Debug.Log("找到 " + spawnPoints.Count + " 个生成点 | " + limitText + " | " + cooldownText + " | 生成半径: " + defaultSpawnRadius);
    }

    void PerformInitialSpawn()
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogWarning("没有敌人预制体，无法执行初始生成");
            return;
        }

        Debug.Log($"=== 开始初始生成 ===");

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            int spawnCount = initialSpawnCount;

            if (enableMaxLimit)
            {
                int currentCount = allActiveEnemies.Count;
                int maxSpawn = maxEnemyCount - currentCount;
                spawnCount = Mathf.Min(spawnCount, maxSpawn);
            }

            if (spawnCount > 0)
            {
                SpawnEnemyAtPoint(spawnData, spawnCount);
                spawnData.hasSpawnedOnce = true;

                if (enableCooldown)
                {
                    spawnData.isOnCooldown = true;
                    spawnData.cooldownTimer = cooldownTime;
                }

                Debug.Log($"{spawnData.point.name}: 初始生成 {spawnCount} 个敌人 (冷却 {cooldownTime}s)");
            }
        }

        initialSpawnDone = true;
        Debug.Log($"=== 初始生成完成，共 {allActiveEnemies.Count} 个敌人 ===");
    }

    public void ApplyScalingParameters(
    float spawnInterval,
    int spawnPerInterval,
    float speedMultiplier,
    float healthMultiplier,
    float damageMultiplier,
    bool maxLimit,
    int maxCount,  // ⭐ 这个值会从 GameManager 传入动态上限
    bool cooldown,
    float cooldownTimeValue,
    List<GameObject> allowedPrefabs)
    {
        currentSpawnInterval = spawnInterval;
        currentSpawnPerInterval = spawnPerInterval;
        currentSpeedMultiplier = speedMultiplier;
        currentHealthMultiplier = healthMultiplier;
        currentDamageMultiplier = damageMultiplier;
        enableMaxLimit = maxLimit;
        maxEnemyCount = maxCount;  // ⭐ 动态上限
        enableCooldown = cooldown;
        cooldownTime = cooldownTimeValue;

        if (allowedPrefabs != null && allowedPrefabs.Count > 0)
        {
            enemyPrefabs = new List<GameObject>(allowedPrefabs);

            UpdateWeightList();
        }

        if (showDebugLogs)
        {
            Debug.Log("生成器参数已更新: 间隔=" + currentSpawnInterval + ", 每次=" + currentSpawnPerInterval);
            Debug.Log("  上限=" + maxEnemyCount + ", 倍率: 速度=" + currentSpeedMultiplier + ", 血量=" + currentHealthMultiplier + ", 攻击=" + currentDamageMultiplier);
        }
    }
    void UpdateWeightList()
    {
        enemyWeights.Clear();

        foreach (GameObject prefab in enemyPrefabs)
        {
            EnemyWeight ew = new EnemyWeight();
            ew.enemyPrefab = prefab;
            ew.weight = 1f;
            enemyWeights.Add(ew);
        }
    }

    void AutoFindSpawnPoints()
    {
        GameObject[] foundPoints = GameObject.FindGameObjectsWithTag("EnemySpawn");
        foreach (GameObject point in foundPoints)
        {
            SpawnPointData data = new SpawnPointData();
            data.point = point.transform;
            spawnPoints.Add(data);
        }

        if (spawnPoints.Count == 0)
        {
            GameObject parent = GameObject.Find("SpawnPoints");
            if (parent != null)
            {
                foreach (Transform child in parent.transform)
                {
                    SpawnPointData data = new SpawnPointData();
                    data.point = child;
                    spawnPoints.Add(data);
                }
            }
        }

        if (spawnPoints.Count == 0)
        {
            SpawnPointData data = new SpawnPointData();
            data.point = transform;
            spawnPoints.Add(data);
            Debug.LogWarning("没有生成点，使用自身位置");
        }
    }

    void Update()
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0 || playerTarget == null)
        {
            Debug.LogWarning("⚠️ 缺少敌人预制体或玩家目标");
            return;
        }

        CleanupAllEnemies();

        // ⭐ 每5秒打印一次状态
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"📊 状态: 生成点={spawnPoints.Count}, 敌人={allActiveEnemies.Count}, 间隔={currentSpawnInterval}, 每次={currentSpawnPerInterval}");
        }

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            // 检查父Tile
            bool parentTileActive = true;
            if (spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null && !parentTile.isActive)
                {
                    parentTileActive = false;
                }
            }

            if (!parentTileActive) continue;

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);

            // ⭐ 激活检测
            if (distance <= spawnData.activationRadius && !spawnData.isActive)
            {
                Debug.Log($"✅ 生成点激活！距离={distance:F1} <= {spawnData.activationRadius}");
                spawnData.isActive = true;
                spawnData.spawnTimer = 0f;

                if (!spawnData.isOnCooldown)
                {
                    int toSpawn = currentSpawnPerInterval;
                    if (enableMaxLimit)
                    {
                        int currentCount = allActiveEnemies.Count;
                        int maxSpawn = maxEnemyCount - currentCount;
                        toSpawn = Mathf.Min(toSpawn, maxSpawn);
                    }

                    if (toSpawn > 0)
                    {
                        SpawnEnemyAtPoint(spawnData, toSpawn);
                        spawnData.hasSpawnedOnce = true;

                        if (enableCooldown)
                        {
                            spawnData.isOnCooldown = true;
                            spawnData.cooldownTimer = cooldownTime;
                        }
                    }
                }
            }

            // 停用检测
            if (distance > spawnData.deactivationRadius && spawnData.isActive)
            {
                spawnData.isActive = false;
                spawnData.spawnTimer = 0f;
            }

            // 冷却处理
            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                {
                    spawnData.isOnCooldown = false;
                    Debug.Log($"✅ 冷却结束: {spawnData.point.name}");
                }
            }

            // ⭐ 定时生成
            if (spawnData.isActive && !spawnData.isOnCooldown)
            {
                if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount)
                {
                    continue;
                }

                spawnData.spawnTimer += Time.deltaTime;
                if (spawnData.spawnTimer >= currentSpawnInterval)
                {
                    spawnData.spawnTimer = 0f;

                    int toSpawn = currentSpawnPerInterval;
                    if (enableMaxLimit)
                    {
                        int currentCount = allActiveEnemies.Count;
                        int maxSpawn = maxEnemyCount - currentCount;
                        toSpawn = Mathf.Min(currentSpawnPerInterval, maxSpawn);
                    }

                    if (toSpawn > 0)
                    {
                        Debug.Log($"🔥 定时生成！{toSpawn}个敌人，位置={spawnData.point.position}");
                        SpawnEnemyAtPoint(spawnData, toSpawn);

                        if (enableCooldown)
                        {
                            spawnData.isOnCooldown = true;
                            spawnData.cooldownTimer = cooldownTime;
                        }
                    }
                }
            }
        }
    }

    GameObject GetWeightedRandomEnemy()
    {
        if (enemyWeights == null || enemyWeights.Count == 0 || enemyWeights.Count != enemyPrefabs.Count)
        {
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
        }

        float totalWeight = 0f;
        foreach (EnemyWeight ew in enemyWeights)
        {
            totalWeight += ew.weight;
        }

        if (totalWeight <= 0)
        {
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
        }

        float randomValue = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < enemyWeights.Count; i++)
        {
            cumulative += enemyWeights[i].weight;
            if (randomValue <= cumulative)
            {
                return enemyWeights[i].enemyPrefab;
            }
        }

        return enemyWeights[enemyWeights.Count - 1].enemyPrefab;
    }

    void SpawnEnemyAtPoint(SpawnPointData spawnData, int count)
    {
        float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;

        for (int i = 0; i < count; i++)
        {
            GameObject enemyPrefab = GetWeightedRandomEnemy();

            Vector3 spawnPos = GetRandomPositionAroundPoint(spawnData.point.position, spawnRadius);
            spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);

            if (!IsPositionValid(spawnPos))
            {
                spawnPos = spawnData.point.position;
                spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);
            }

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
            enemy.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            // ⭐ 记录敌人到Tile（无限模式专用）
            if (spawnData.point != null && spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null)
                {
                    InfiniteWorldManager worldManager = FindObjectOfType<InfiniteWorldManager>();
                    if (worldManager != null)
                    {
                        worldManager.RegisterEnemyToTile(enemy, parentTile.gridPosition);
                    }
                }
            }

            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null)
            {
                enemyScript.ApplyScalingMultipliers(
                    currentSpeedMultiplier,
                    currentHealthMultiplier,
                    currentDamageMultiplier
                );

                if (enemyScript.enemyData != null && showDebugLogs)
                {
                    Debug.Log("生成 " + enemyScript.enemyData.enemyName +
                        " (速度x" + currentSpeedMultiplier +
                        ", 血量x" + currentHealthMultiplier +
                        ", 攻击x" + currentDamageMultiplier +
                        ", 位置偏移: " + (spawnPos - spawnData.point.position).magnitude.ToString("F1") + "m)");
                }
            }

            spawnData.activeEnemies.Add(enemy);
            allActiveEnemies.Add(enemy);
            spawnData.totalSpawned++;
            globalTotalSpawned++;
        }

        if (showDebugLogs)
        {
            string limitInfo = enableMaxLimit ? "(" + allActiveEnemies.Count + "/" + maxEnemyCount + ")" : "";
            Debug.Log(spawnData.point.name + ": 生成 " + count + " 个敌人 " + limitInfo + " (半径: " + spawnRadius + "m)");
        }
    }

    Vector3 GetRandomPositionAroundPoint(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * radius;
        float yPos = center.y;

        return new Vector3(
            center.x + Mathf.Cos(angle) * distance,
            yPos,
            center.z + Mathf.Sin(angle) * distance
        );
    }

    Vector3 GetValidNavMeshPosition(Vector3 position, float sampleRadius)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius, NavMesh.AllAreas))
        {
            return hit.position;
        }

        if (NavMesh.SamplePosition(position, out hit, sampleRadius * 2f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return position;
    }

    bool IsPositionValid(Vector3 position)
    {
        NavMeshHit hit;
        return NavMesh.SamplePosition(position, out hit, 1f, NavMesh.AllAreas);
    }

    void CleanupAllEnemies()
    {
        allActiveEnemies.RemoveAll(e => e == null);

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.activeEnemies.RemoveAll(e => e == null);
        }
    }

    public void ClearAllEnemies()
    {
        foreach (GameObject enemy in allActiveEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        allActiveEnemies.Clear();

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.activeEnemies.Clear();
        }

        Debug.Log("所有敌人已清除");
    }

    public void ResetSpawner()
    {
        ClearAllEnemies();
        globalTotalSpawned = 0;
        initialSpawnDone = false;

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.isActive = false;
            spawnData.spawnTimer = 0f;
            spawnData.totalSpawned = 0;
            spawnData.isOnCooldown = false;
            spawnData.cooldownTimer = 0f;
            spawnData.hasSpawnedOnce = false;
        }

        Debug.Log("生成器已重置");

        if (spawnOnStart)
        {
            PerformInitialSpawn();
        }
    }

    public string GetStats()
    {
        int activeCount = 0;
        int coolingCount = 0;
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.isActive) activeCount++;
            if (spawnData.isOnCooldown) coolingCount++;
        }

        string limitInfo = enableMaxLimit ? allActiveEnemies.Count + "/" + maxEnemyCount : "无限 " + allActiveEnemies.Count;
        string cooldownInfo = enableCooldown ? " | 冷却中: " + coolingCount : "";
        return "活跃: " + activeCount + "/" + spawnPoints.Count + " | 敌人: " + limitInfo + cooldownInfo;
    }

    void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            if (spawnData.isOnCooldown)
            {
                Gizmos.color = Color.yellow;
            }
            else if (spawnData.isActive)
            {
                Gizmos.color = Color.green;
            }
            else
            {
                Gizmos.color = Color.blue;
            }
            Gizmos.DrawSphere(spawnData.point.position, 0.5f);

            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(spawnData.point.position, spawnData.activationRadius);

            Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
            Gizmos.DrawWireSphere(spawnData.point.position, spawnData.deactivationRadius);

            float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;
            Gizmos.color = new Color(1f, 0f, 1f, 0.15f);
            Gizmos.DrawWireSphere(spawnData.point.position, spawnRadius);

#if UNITY_EDITOR
            string status = spawnData.isOnCooldown ?
                "冷却中 (" + spawnData.cooldownTimer.ToString("F1") + "s)" :
                (spawnData.isActive ? "激活" : "停用");

            string tileInfo = spawnData.tileType.ToString();

            UnityEditor.Handles.Label(
                spawnData.point.position + Vector3.up * 2.5f,
                spawnData.point.name + "\n" + status +
                "\n激活: " + spawnData.activationRadius +
                "\n停用: " + spawnData.deactivationRadius +
                "\n生成半径: " + spawnRadius +
                "\nTile类型: " + tileInfo
            );
#endif
        }

#if UNITY_EDITOR
        string limitText = enableMaxLimit ? "上限: " + maxEnemyCount : "无限";
        string cooldownText = enableCooldown ? "冷却: " + cooldownTime + "s" : "无冷却";
        string difficultyText = currentDifficulty != null ? "难度: " + currentDifficulty.difficultyName : "未设置";
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 5f,
            difficultyText + "\n" + limitText + " | " + cooldownText + " | 当前: " + allActiveEnemies.Count
        );
#endif
    }
}