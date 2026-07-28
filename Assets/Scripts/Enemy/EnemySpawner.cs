using UnityEngine;
using System.Collections;
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
    public int initialSpawnCount = 2;
    public bool spawnOnStart = true;

    [Header("生成范围设置")]
    public float defaultSpawnRadius = 5f;
    public float navMeshSampleRadius = 5f;
    public int maxSpawnAttempts = 30;

    [Header("敌人权重")]
    public List<EnemyWeight> enemyWeights = new List<EnemyWeight>();

    [Header("调试")]
    public bool showDebugLogs = true;

    private float currentSpawnInterval = 2f;
    private int currentSpawnPerInterval = 1;
    private bool enableMaxLimit = false;
    private int maxEnemyCount = 30;
    private bool enableCooldown = true;
    private float cooldownTime = 10f;

    private float currentSpeedMultiplier = 1f;
    private float currentHealthMultiplier = 1f;
    private float currentDamageMultiplier = 1f;

    private bool initialSpawnDone = false;
    private bool canSpawn = false;

    // 缓存组件引用，避免 Update 中频繁 Find 开销
    private CountdownManager countdownManager;
    private InfiniteWorldManager worldManager;
    private float cleanupTimer = 0f;

    [System.Serializable]
    public class SpawnPointData
    {
        public Transform point;
        public float activationRadius = 15f;
        public float deactivationRadius = 22f;
        public float spawnRadius = 0f;
        public bool isActive = false;
        public float spawnTimer = 0f;
        public int totalSpawned = 0;
        public bool isOnCooldown = false;
        public float cooldownTimer = 0f;
        public bool hasSpawnedOnce = false;
        public TileType tileType = TileType.Type0;

        [System.NonSerialized]
        public List<GameObject> activeEnemies = new List<GameObject>();
    }

    [System.Serializable]
    public class EnemyWeight
    {
        public GameObject enemyPrefab;
        public float weight = 1f;
    }

    private List<GameObject> allActiveEnemies = new List<GameObject>();
    private int globalTotalSpawned = 0;

    void Awake()
    {
        canSpawn = false;
    }

    void Start()
    {
        // 预先获取并缓存全局管理器
        countdownManager = FindObjectOfType<CountdownManager>();
        worldManager = FindObjectOfType<InfiniteWorldManager>();

        InitializeSpawner();
    }

    void Update()
    {
        // 1. 如果游戏正在倒计时，直接跳过逻辑
        if (countdownManager != null && countdownManager.IsCountingDown()) return;
        if (!canSpawn || enemyPrefabs == null || enemyPrefabs.Count == 0 || playerTarget == null) return;

        // 2. 降低清理频率：每 1 秒才遍历清理一次已被销毁的敌人对象，避免每帧 GC
        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= 1.0f)
        {
            cleanupTimer = 0f;
            CleanupDeadEnemies();
        }

        // 3. 处理每个生成点的激活、冷却与生成逻辑
        ProcessSpawnPoints();
    }

    void ProcessSpawnPoints()
    {
        // 倒序遍历，方便中途安全移除失效的生成点
        for (int i = spawnPoints.Count - 1; i >= 0; i--)
        {
            SpawnPointData spawnData = spawnPoints[i];

            // 自动清理被销毁的动态 Tile 生成点
            if (spawnData.point == null)
            {
                spawnPoints.RemoveAt(i);
                continue;
            }

            // 父级 Tile 停用状态检查
            if (spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null && !parentTile.isActive) continue;
            }

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);

            // 处理生成点冷却计时
            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                {
                    spawnData.isOnCooldown = false;
                }
            }

            // 进入/离开范围的状态切换
            if (distance <= spawnData.activationRadius && !spawnData.isActive)
            {
                spawnData.isActive = true;
                spawnData.spawnTimer = 0f; // 重置计时器，统一由后续定时逻辑控制
            }
            else if (distance > spawnData.deactivationRadius && spawnData.isActive)
            {
                spawnData.isActive = false;
                spawnData.spawnTimer = 0f;
            }

            // 激活且未冷却时的生成逻辑
            if (spawnData.isActive && !spawnData.isOnCooldown)
            {
                if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) continue;

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
                        // 开始协程分帧生成
                        StartCoroutine(Routine_SpawnEnemies(spawnData, toSpawn));

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

    /// <summary>
    /// 🌟 核心优化：使用协程进行错峰生成，避免单帧 Instantiate 过多导致瞬间卡顿
    /// </summary>
    IEnumerator Routine_SpawnEnemies(SpawnPointData spawnData, int count)
    {
        float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;

        for (int i = 0; i < count; i++)
        {
            // 如果生成点中途被销毁，中断生成
            if (spawnData.point == null) yield break;

            // 再次检查上限限制（防止协程等待期间场上敌人超标）
            if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) yield break;

            GameObject enemyPrefab = GetWeightedRandomEnemy();
            if (enemyPrefab != null)
            {
                Vector3 spawnPos = GetRandomPositionAroundPoint(spawnData.point.position, spawnRadius);
                spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);

                if (!IsPositionValid(spawnPos))
                {
                    spawnPos = spawnData.point.position;
                    spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);
                }

                GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
                enemy.transform.parent = null;

                // 注册敌人到对应的 Tile
                if (spawnData.point != null && spawnData.point.parent != null)
                {
                    Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                    if (parentTile != null)
                    {
                        RegisterEnemyToTile(enemy, parentTile.gridPosition);
                    }
                }
                else if (worldManager != null)
                {
                    Vector2Int gridPos = worldManager.WorldToGrid(spawnPos);
                    RegisterEnemyToTile(enemy, gridPos);
                }

                // 统一应用难度属性加成
                EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
                if (enemyScript != null)
                {
                    enemyScript.ApplyScalingMultipliers(
                        currentSpeedMultiplier,
                        currentHealthMultiplier,
                        currentDamageMultiplier
                    );
                }

                spawnData.activeEnemies.Add(enemy);
                allActiveEnemies.Add(enemy);
                spawnData.totalSpawned++;
                globalTotalSpawned++;
            }

            // 关键：每生成一个敌人，微小停顿一帧/几毫秒，把 CPU 压力平摊开
            if (count > 1)
            {
                yield return new WaitForSeconds(0.05f);
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
            if (ew.enemyPrefab != null) totalWeight += Mathf.Max(0, ew.weight);
        }

        if (totalWeight <= 0f) return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];

        float randomValue = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < enemyWeights.Count; i++)
        {
            if (enemyWeights[i].enemyPrefab == null) continue;

            cumulative += enemyWeights[i].weight;
            if (randomValue <= cumulative)
            {
                return enemyWeights[i].enemyPrefab;
            }
        }

        return enemyPrefabs[0];
    }

    void PerformInitialSpawn()
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0) return;

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
                StartCoroutine(Routine_SpawnEnemies(spawnData, spawnCount));
                spawnData.hasSpawnedOnce = true;
                if (enableCooldown)
                {
                    spawnData.isOnCooldown = true;
                    spawnData.cooldownTimer = cooldownTime;
                }
            }
        }
        initialSpawnDone = true;
    }

    private void CleanupDeadEnemies()
    {
        // 倒序遍历清理，不使用匿名 Lambda 表达式，零额外的 GC 垃圾
        for (int i = allActiveEnemies.Count - 1; i >= 0; i--)
        {
            if (allActiveEnemies[i] == null)
            {
                allActiveEnemies.RemoveAt(i);
            }
        }

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            for (int i = spawnData.activeEnemies.Count - 1; i >= 0; i--)
            {
                if (spawnData.activeEnemies[i] == null)
                {
                    spawnData.activeEnemies.RemoveAt(i);
                }
            }
        }
    }

    public void RegisterEnemyToTile(GameObject enemy, Vector2Int gridPos)
    {
        if (worldManager == null)
        {
            worldManager = FindObjectOfType<InfiniteWorldManager>();
            if (worldManager == null) return;
        }

        Tile targetTile = worldManager.GetTileAtPosition(gridPos);
        if (targetTile != null)
        {
            targetTile.RegisterEnemy(enemy);
            if (showDebugLogs) Debug.Log($"✅ 敌人已注册到 Tile {gridPos}");
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning($"⚠️ 找不到 Tile {gridPos} 来注册敌人");
        }
    }

    public void EnableSpawning()
    {
        canSpawn = true;
        if (showDebugLogs) Debug.Log("✅ 敌人生成已启用！");

        if (spawnOnStart && !initialSpawnDone)
        {
            PerformInitialSpawn();
        }
    }

    public void DisableSpawning()
    {
        canSpawn = false;
        if (showDebugLogs) Debug.Log("⏸️ 敌人生成已禁用");
    }

    public bool IsSpawningEnabled() => canSpawn;

    void InitializeSpawner()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
            else Debug.LogWarning("未找到 Tag 为 Player 的对象");
        }

        if (spawnPoints.Count == 0) AutoFindSpawnPoints();

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
                    UpdateWeightList();
                }
            }
        }

        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogError("没有敌人预制体！");
        }
    }

    public void ApplyScalingParameters(
        float spawnInterval,
        int spawnPerInterval,
        float speedMultiplier,
        float healthMultiplier,
        float damageMultiplier,
        bool maxLimit,
        int maxCount,
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
        maxEnemyCount = maxCount;
        enableCooldown = cooldown;
        cooldownTime = cooldownTimeValue;

        if (allowedPrefabs != null && allowedPrefabs.Count > 0)
        {
            enemyPrefabs = new List<GameObject>(allowedPrefabs);
            UpdateWeightList();
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
        }
    }

    Vector3 GetRandomPositionAroundPoint(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * radius;
        return new Vector3(center.x + Mathf.Cos(angle) * distance, center.y, center.z + Mathf.Sin(angle) * distance);
    }

    Vector3 GetValidNavMeshPosition(Vector3 position, float sampleRadius)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius, NavMesh.AllAreas)) return hit.position;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius * 2f, NavMesh.AllAreas)) return hit.position;
        return position;
    }

    bool IsPositionValid(Vector3 position)
    {
        NavMeshHit hit;
        return NavMesh.SamplePosition(position, out hit, 1f, NavMesh.AllAreas);
    }

    public void ClearAllEnemies()
    {
        StopAllCoroutines(); // 停止所有生成协程

        foreach (GameObject enemy in allActiveEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        allActiveEnemies.Clear();

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.activeEnemies.Clear();
        }
    }

    public void ResetSpawner()
    {
        ClearAllEnemies();
        globalTotalSpawned = 0;
        initialSpawnDone = false;
        canSpawn = false;
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.isActive = false;
            spawnData.spawnTimer = 0f;
            spawnData.totalSpawned = 0;
            spawnData.isOnCooldown = false;
            spawnData.cooldownTimer = 0f;
            spawnData.hasSpawnedOnce = false;
        }
    }

    public void StartSpawning() => EnableSpawning();
    public void StopSpawning() => DisableSpawning();

    public void UpdateSpawnPoints(List<Transform> newSpawnPoints)
    {
        spawnPoints.Clear();

        foreach (Transform point in newSpawnPoints)
        {
            if (point != null)
            {
                SpawnPointData data = new SpawnPointData();
                data.point = point;
                data.activationRadius = 15f;
                data.deactivationRadius = 22f;
                data.spawnRadius = 5f;

                if (point.parent != null)
                {
                    Tile parentTile = point.parent.GetComponent<Tile>();
                    if (parentTile != null)
                    {
                        data.tileType = parentTile.tileType;
                    }
                }

                spawnPoints.Add(data);
            }
        }

        if (showDebugLogs) Debug.Log($"🔄 生成点已更新：{spawnPoints.Count} 个生成点");
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

            if (spawnData.isOnCooldown) Gizmos.color = Color.yellow;
            else if (spawnData.isActive) Gizmos.color = Color.green;
            else Gizmos.color = Color.blue;
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
            UnityEditor.Handles.Label(
                spawnData.point.position + Vector3.up * 2.5f,
                spawnData.point.name + "\n" + status +
                "\n激活: " + spawnData.activationRadius +
                "\n停用: " + spawnData.deactivationRadius +
                "\n生成半径: " + spawnRadius +
                "\nTile类型: " + spawnData.tileType.ToString()
            );
#endif
        }
    }
}