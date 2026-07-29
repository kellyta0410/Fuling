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

    [Header("NavMesh 烘焙设置")]
    public float rebuildDelay = 1.5f;
    public bool autoRebuildOnTileGenerated = true;

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

    // NavMeshSurface 引用
    private NavMeshSurface navMeshSurface;
    private bool pendingRebuild = false;
    private Coroutine rebuildCoroutine = null;

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
                if (parentTile != null && !parentTile.isActive) continue;
            }

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);

            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                {
                    spawnData.isOnCooldown = false;
                }
            }

            if (distance <= spawnData.activationRadius && !spawnData.isActive)
            {
                spawnData.isActive = true;
                spawnData.spawnTimer = 0f;
            }
            else if (distance > spawnData.deactivationRadius && spawnData.isActive)
            {
                spawnData.isActive = false;
                spawnData.spawnTimer = 0f;
            }

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

    IEnumerator Routine_SpawnEnemies(SpawnPointData spawnData, int count)
    {
        float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;

        for (int i = 0; i < count; i++)
        {
            if (spawnData.point == null) yield break;
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

                if (!IsPositionValid(spawnPos))
                {
                    Debug.LogWarning($"无法找到有效NavMesh位置，使用原始生成点 {spawnData.point.position}");
                    spawnPos = spawnData.point.position;
                }

                GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
                enemy.transform.parent = null;

                // ⭐ 强制吸附 NavMesh
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    agent.Warp(spawnPos);
                    if (!agent.isOnNavMesh)
                    {
                        Debug.LogWarning($"敌人 {enemy.name} 不在 NavMesh 上，尝试再次 Warp");
                        agent.Warp(spawnPos);
                    }
                }

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

    // ---------- NavMesh 烘焙相关 ----------
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
    }

    // 外部调用 - 在生成新 Tile 后触发
    public void OnTileGenerated()
    {
        if (autoRebuildOnTileGenerated)
        {
            RequestNavMeshRebuild();
        }
    }

    // ---------- 原有公共方法 ----------
    public void EnableSpawning()
    {
        EnsureNavMeshSurfaceExists();
        if (navMeshSurface != null && !navMeshSurface.navMeshData)
        {
            BuildNavMeshImmediate();
        }

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

        EnsureNavMeshSurfaceExists();
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