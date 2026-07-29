using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
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

    [Header("初始生成（普通模式）")]
    public int initialSpawnCount = 2;
    public bool spawnOnStart = true;

    [Header("初始生成（无限模式）")]
    public int initialWaveCount = 50;                 // 初始波次生成数量（调大）
    public float minSpawnDistance = 8f;
    public float maxSpawnDistance = 20f;

    [Header("生成范围设置")]
    public float defaultSpawnRadius = 5f;             // 若无法获取 Tile 尺寸时的后备值
    public float navMeshSampleRadius = 5f;
    public int maxSpawnAttempts = 30;

    [Header("敌人权重")]
    public List<EnemyWeight> enemyWeights = new List<EnemyWeight>();

    [Header("调试")]
    public bool showDebugLogs = true;

    [Header("无限模式范围放大系数（仅用于 Gizmos 显示）")]
    public float infiniteRangeMultiplier = 2.5f;

    [Header("NavMesh 烘焙设置")]
    public float rebuildDelay = 1.5f;
    public bool autoRebuildOnTileGenerated = true;

    // 运行时参数（由 DifficultySettings 或 GameManager 控制）
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

    private CountdownManager countdownManager;
    private InfiniteWorldManager worldManager;
    private float cleanupTimer = 0f;
    private float globalSpawnTimer = 0f;

    private NavMeshSurface navMeshSurface;
    private bool pendingRebuild = false;
    private Coroutine rebuildCoroutine = null;

    private bool _isInfiniteMode = false;

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
        countdownManager = FindObjectOfType<CountdownManager>();
        worldManager = FindObjectOfType<InfiniteWorldManager>();
        InitializeSpawner();
    }

    void Update()
    {
        if (countdownManager != null && countdownManager.IsCountingDown()) return;
        if (!canSpawn || enemyPrefabs == null || enemyPrefabs.Count == 0 || playerTarget == null) return;

        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= 1.0f)
        {
            cleanupTimer = 0f;
            CleanupDeadEnemies();
        }

        ProcessSpawnPoints();
    }

    void ProcessSpawnPoints()
    {
        for (int i = spawnPoints.Count - 1; i >= 0; i--)
        {
            SpawnPointData spawnData = spawnPoints[i];
            if (spawnData.point == null)
            {
                spawnPoints.RemoveAt(i);
                continue;
            }

            if (spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null && !parentTile.isActive)
                {
                    spawnData.isActive = false;
                    continue;
                }
            }

            if (_isInfiniteMode) spawnData.isActive = true;

            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                    spawnData.isOnCooldown = false;
            }
        }

        if (_isInfiniteMode)
            ProcessInfiniteMode();
        else
            ProcessNormalMode();
    }

    void ProcessNormalMode()
    {
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);
            if (distance <= spawnData.activationRadius && !spawnData.isActive)
                spawnData.isActive = true;
            else if (distance > spawnData.deactivationRadius && spawnData.isActive)
                spawnData.isActive = false;

            if (!spawnData.isActive || spawnData.isOnCooldown) continue;
            if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) continue;

            spawnData.spawnTimer += Time.deltaTime;
            if (spawnData.spawnTimer >= currentSpawnInterval)
            {
                spawnData.spawnTimer = 0f;
                int toSpawn = currentSpawnPerInterval;
                if (enableMaxLimit)
                {
                    int maxSpawn = maxEnemyCount - allActiveEnemies.Count;
                    toSpawn = Mathf.Min(toSpawn, maxSpawn);
                }
                if (toSpawn > 0)
                {
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

    void ProcessInfiniteMode()
    {
        List<SpawnPointData> availablePoints = new List<SpawnPointData>();
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;
            if (!spawnData.isActive || spawnData.isOnCooldown) continue;
            if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) break;
            availablePoints.Add(spawnData);
        }
        if (availablePoints.Count == 0) return;

        availablePoints.Sort((a, b) =>
        {
            float da = Vector3.Distance(a.point.position, playerTarget.position);
            float db = Vector3.Distance(b.point.position, playerTarget.position);
            return da.CompareTo(db);
        });

        globalSpawnTimer += Time.deltaTime;
        if (globalSpawnTimer >= currentSpawnInterval)
        {
            globalSpawnTimer = 0f;
            int toSpawn = currentSpawnPerInterval;
            if (enableMaxLimit)
                toSpawn = Mathf.Min(toSpawn, maxEnemyCount - allActiveEnemies.Count);
            if (toSpawn > 0)
            {
                int count = Mathf.Min(toSpawn, availablePoints.Count);
                for (int i = 0; i < count; i++)
                {
                    SpawnPointData spawnData = availablePoints[i];
                    // ⭐ 每个生成点生成 2 个敌人（提高生成效率）
                    StartCoroutine(Routine_SpawnEnemies(spawnData, 2));
                    if (enableCooldown)
                    {
                        spawnData.isOnCooldown = true;
                        spawnData.cooldownTimer = cooldownTime;
                    }
                }
            }
        }
    }

    IEnumerator Routine_SpawnEnemies(SpawnPointData spawnData, int count)
    {
        float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;

        for (int i = 0; i < count; i++)
        {
            if (spawnData.point == null) yield break;
            if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) yield break;

            GameObject enemyPrefab = GetWeightedRandomEnemy();
            if (enemyPrefab == null) continue;

            Vector3 spawnPos = GetRandomPositionAroundPoint(spawnData.point.position, spawnRadius);
            spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);
            if (!IsPositionValid(spawnPos))
            {
                spawnPos = GetValidNavMeshPosition(spawnData.point.position, navMeshSampleRadius);
                if (!IsPositionValid(spawnPos))
                {
                    Debug.LogWarning($"无法找到有效 NavMesh 位置，使用原始生成点 {spawnData.point.position}");
                    spawnPos = spawnData.point.position;
                }
            }

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
            enemy.transform.parent = null;

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(spawnPos);

            if (spawnData.point != null && spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null)
                    RegisterEnemyToTile(enemy, parentTile.gridPosition);
            }
            else if (worldManager != null)
            {
                Vector2Int gridPos = worldManager.WorldToGrid(spawnPos);
                RegisterEnemyToTile(enemy, gridPos);
            }

            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null)
                enemyScript.ApplyScalingMultipliers(currentSpeedMultiplier, currentHealthMultiplier, currentDamageMultiplier);

            spawnData.activeEnemies.Add(enemy);
            allActiveEnemies.Add(enemy);
            spawnData.totalSpawned++;
            globalTotalSpawned++;

            if (count > 1) yield return new WaitForSeconds(0.05f);
        }
    }

    GameObject GetWeightedRandomEnemy()
    {
        if (enemyWeights == null || enemyWeights.Count == 0 || enemyWeights.Count != enemyPrefabs.Count)
        {
            return enemyPrefabs.Count > 0 ? enemyPrefabs[Random.Range(0, enemyPrefabs.Count)] : null;
        }

        float totalWeight = 0f;
        foreach (EnemyWeight ew in enemyWeights)
        {
            if (ew.enemyPrefab != null) totalWeight += Mathf.Max(0, ew.weight);
        }

        if (totalWeight <= 0f)
            return enemyPrefabs.Count > 0 ? enemyPrefabs[Random.Range(0, enemyPrefabs.Count)] : null;

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

        return enemyPrefabs.Count > 0 ? enemyPrefabs[0] : null;
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
                int maxSpawn = maxEnemyCount - allActiveEnemies.Count;
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

    IEnumerator SpawnInitialWave(int count)
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0 || playerTarget == null)
        {
            yield break;
        }

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = count * 10;

        while (spawned < count && attempts < maxAttempts)
        {
            attempts++;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
            Vector3 spawnPos = playerTarget.position + offset;

            spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius);
            if (!IsPositionValid(spawnPos))
            {
                spawnPos = playerTarget.position + offset;
                spawnPos = GetValidNavMeshPosition(spawnPos, navMeshSampleRadius * 2f);
                if (!IsPositionValid(spawnPos))
                    continue;
            }

            float distToPlayer = Vector3.Distance(spawnPos, playerTarget.position);
            if (distToPlayer < minSpawnDistance) continue;

            GameObject enemyPrefab = GetWeightedRandomEnemy();
            if (enemyPrefab == null) continue;

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
            enemy.transform.parent = null;

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(spawnPos);

            if (worldManager != null)
            {
                Vector2Int gridPos = worldManager.WorldToGrid(spawnPos);
                RegisterEnemyToTile(enemy, gridPos);
            }

            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null)
                enemyScript.ApplyScalingMultipliers(currentSpeedMultiplier, currentHealthMultiplier, currentDamageMultiplier);

            allActiveEnemies.Add(enemy);
            globalTotalSpawned++;
            spawned++;

            if (spawned % 3 == 0)
                yield return null;
        }

        if (showDebugLogs) Debug.Log($"🎯 无限模式初始波次生成完成：{spawned} 个敌人");
        yield return null;
    }

    private void CleanupDeadEnemies()
    {
        for (int i = allActiveEnemies.Count - 1; i >= 0; i--)
        {
            if (allActiveEnemies[i] == null)
                allActiveEnemies.RemoveAt(i);
        }

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            for (int i = spawnData.activeEnemies.Count - 1; i >= 0; i--)
            {
                if (spawnData.activeEnemies[i] == null)
                    spawnData.activeEnemies.RemoveAt(i);
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

    // ---------- NavMesh 烘焙 ----------
    private void EnsureNavMeshSurfaceExists()
    {
        if (navMeshSurface == null)
        {
            navMeshSurface = FindObjectOfType<NavMeshSurface>();
            if (navMeshSurface == null)
            {
                Debug.Log("未找到 NavMeshSurface，正在创建...");
                GameObject surfaceObj = new GameObject("NavMeshSurface (Runtime)");
                navMeshSurface = surfaceObj.AddComponent<NavMeshSurface>();
                navMeshSurface.layerMask = ~0;
                navMeshSurface.collectObjects = CollectObjects.Children;
                navMeshSurface.defaultArea = 0;
            }
        }
    }

    public void BuildNavMeshImmediate()
    {
        EnsureNavMeshSurfaceExists();
        if (navMeshSurface != null)
        {
            Debug.Log("🔄 立即烘焙 NavMesh...");
            navMeshSurface.BuildNavMesh();
            Debug.Log("✅ NavMesh 烘焙完成！");
        }
    }

    public void RequestNavMeshRebuild()
    {
        if (pendingRebuild) return;

        pendingRebuild = true;
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        rebuildCoroutine = StartCoroutine(DelayedRebuild());
    }

    private IEnumerator DelayedRebuild()
    {
        yield return new WaitForSeconds(rebuildDelay);

        pendingRebuild = false;
        rebuildCoroutine = null;

        EnsureNavMeshSurfaceExists();
        if (navMeshSurface != null)
        {
            Debug.Log("🔄 延迟烘焙 NavMesh...");
            navMeshSurface.BuildNavMesh();
            Debug.Log("✅ NavMesh 烘焙完成！");
        }
        yield return null;
    }

    public void OnTileGenerated()
    {
        if (autoRebuildOnTileGenerated)
            RequestNavMeshRebuild();
    }

    public void RemoveSpawnPointsForTile(Tile tile)
    {
        if (tile == null) return;
        spawnPoints.RemoveAll(sp => sp.point != null && sp.point.parent == tile.transform);
        if (showDebugLogs) Debug.Log($"🧹 已移除 Tile {tile.name} 的所有生成点");
    }

    // ---------- 公共控制 ----------
    public void EnableSpawning()
    {
        EnsureNavMeshSurfaceExists();
        if (navMeshSurface != null && !navMeshSurface.navMeshData)
            BuildNavMeshImmediate();

        canSpawn = true;
        if (showDebugLogs) Debug.Log("✅ 敌人生成已启用！");

        if (spawnOnStart && !initialSpawnDone)
        {
            if (_isInfiniteMode)
                StartCoroutine(SpawnInitialWave(initialWaveCount));
            else
                PerformInitialSpawn();
            initialSpawnDone = true;
        }
    }

    public void DisableSpawning() => canSpawn = false;
    public bool IsSpawningEnabled() => canSpawn;

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

    public void UpdateWeightList()
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

    public void AutoFindSpawnPoints()
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

    public Vector3 GetRandomPositionAroundPoint(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * radius;
        return new Vector3(center.x + Mathf.Cos(angle) * distance, center.y, center.z + Mathf.Sin(angle) * distance);
    }

    public Vector3 GetValidNavMeshPosition(Vector3 position, float sampleRadius)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius, NavMesh.AllAreas)) return hit.position;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius * 2f, NavMesh.AllAreas)) return hit.position;
        return position;
    }

    public bool IsPositionValid(Vector3 position)
    {
        NavMeshHit hit;
        return NavMesh.SamplePosition(position, out hit, 1f, NavMesh.AllAreas);
    }

    public void ClearAllEnemies()
    {
        StopAllCoroutines();
        foreach (GameObject enemy in allActiveEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        allActiveEnemies.Clear();
        foreach (SpawnPointData spawnData in spawnPoints)
            spawnData.activeEnemies.Clear();
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
            if (point == null) continue;
            SpawnPointData data = new SpawnPointData();
            data.point = point;
            data.activationRadius = 15f;   // 占位值，稍后会被覆盖
            data.deactivationRadius = 22f; // 占位值，稍后会被覆盖
            data.spawnRadius = 5f;         // 占位值，稍后会被覆盖

            if (point.parent != null)
            {
                Tile parentTile = point.parent.GetComponent<Tile>();
                if (parentTile != null)
                    data.tileType = parentTile.tileType;
            }
            spawnPoints.Add(data);
        }

        ApplyTileSizeToSpawnPoints(); // 自动适配 Tile 尺寸
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

    // ⭐ 自动适配：根据父 Tile 尺寸设置生成半径、激活半径、停用半径
    private void ApplyTileSizeToSpawnPoints()
    {
        foreach (var spawnData in spawnPoints)
        {
            if (spawnData.point == null || spawnData.point.parent == null) continue;
            Tile tile = spawnData.point.parent.GetComponent<Tile>();
            if (tile == null) continue;

            // 获取 Tile 尺寸（优先使用 tile.tileSize 字段，否则用 lossyScale.x）
            float tileSize = spawnData.point.parent.lossyScale.x;
            // 如果您的 Tile 有公开的 tileSize 字段，可改为：
            // float tileSize = tile.tileSize;

            if (tileSize > 0.1f)
            {
                // 设置生成半径为 Tile 尺寸的一半
                spawnData.spawnRadius = tileSize * 0.5f;
                // 设置激活/停用半径也适配 Tile 尺寸（普通模式生效）
                spawnData.activationRadius = tileSize * 1.0f;   
                spawnData.deactivationRadius = tileSize * 1.2f; 
            }
            else
            {
                // 后备：使用默认值
                spawnData.spawnRadius = defaultSpawnRadius;
                // 保持原有值不变（或使用默认的 15/22）
            }
        }
    }

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
            currentDifficulty = GameManager.Instance.currentDifficulty;

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
                if (enemyWeights.Count == 0) UpdateWeightList();
            }
        }

        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogError("没有敌人预制体！");
        }

        EnsureNavMeshSurfaceExists();

        // 自动适配 Tile 尺寸（启动时）
        ApplyTileSizeToSpawnPoints();

        if (GameManager.Instance != null)
        {
            _isInfiniteMode = GameManager.Instance.IsInfiniteMode();
            if (_isInfiniteMode)
                Debug.Log($"♾️ 无限模式激活，初始波次将生成 {initialWaveCount} 个敌人");
        }
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

            float activation = spawnData.activationRadius;
            float deactivation = spawnData.deactivationRadius;

            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(spawnData.point.position, activation);
            Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
            Gizmos.DrawWireSphere(spawnData.point.position, deactivation);

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
                "\n激活: " + activation +
                "\n停用: " + deactivation +
                "\n生成半径: " + spawnRadius.ToString("F2") +
                "\nTile类型: " + spawnData.tileType.ToString()
            );
#endif
        }
    }
}