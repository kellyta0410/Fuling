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

    [Header("普通模式参数")]
    public float spawnInterval = 2f;
    public int spawnPerInterval = 1;
    public int initialSpawnCount = 2;
    public bool spawnOnStart = true;
    public float defaultSpawnRadius = 5f;

    [Header("普通模式 - 阶层式升级（时间驱动）")]
    public bool enableTieredDifficulty = true;
    public TierData[] difficultyTiers;

    [Header("普通模式 - 最终阶段")]
    public bool enableFinalPhase = true;
    public float finalPhaseThreshold = 0.8f;
    public int finalPhaseExtraEnemies = 3;

    [Header("NavMesh 设置")]
    public float navMeshSampleRadius = 5f;
    public int maxSpawnAttempts = 30;
    public float rebuildDelay = 1.5f;
    public bool autoRebuildOnTileGenerated = true;

    [Header("玩家避让")]
    [Tooltip("生成位置与玩家的最小水平距离；随机点落在玩家周围这段距离内会重试，避免生成在玩家身上/旁边引起推挤")]
    public float playerClearRadius = 4f;

    [Header("敌人权重")]
    public List<EnemyWeight> enemyWeights = new List<EnemyWeight>();

    [Header("调试")]
    public bool showDebugLogs = true;

    // 运行时参数
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
    private bool isFinalPhaseActive = false;
    private float lastSpeedMultiplier = 1f;

    // 阶层追踪
    private int currentTierIndex = -1;
    private int lastTierIndex = -1;
    private bool showTierMessage = false;
    private float tierMessageTimer = 0f;
    private string currentTierMessage = "";

    private CountdownManager countdownManager;
    private InfiniteWorldManager worldManager;
    private float cleanupTimer = 0f;

    private NavMeshSurface navMeshSurface;
    private bool pendingRebuild = false;
    private Coroutine rebuildCoroutine = null;

    private List<GameObject> allActiveEnemies = new List<GameObject>();
    private int globalTotalSpawned = 0;

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

    [System.Serializable]
    public class TierData
    {
        [Header("阶层配置")]
        public string tierName = "第1阶";

        [Tooltip("到达这个时间（秒）后激活此阶层")]
        public float timeThreshold = 0f;

        [Tooltip("生成间隔（秒）")]
        public float spawnInterval = 2f;

        [Tooltip("每次生成数量")]
        public int spawnCount = 1;

        [Tooltip("速度倍率")]
        public float speedMultiplier = 1f;

        [Tooltip("血量倍率")]
        public float healthMultiplier = 1f;

        [Tooltip("伤害倍率")]
        public float damageMultiplier = 1f;

        [Tooltip("是否启用冷却")]
        public bool enableCooldown = true;

        [Tooltip("升级时显示的提示文字（留空不显示）")]
        public string upgradeMessage = "";

        [Tooltip("此阶层的颜色（用于调试）")]
        public Color tierColor = Color.white;
    }

    void Awake()
    {
        canSpawn = false;

        // 如果没有配置阶层，创建默认
        if (difficultyTiers == null || difficultyTiers.Length == 0)
        {
            CreateDefaultTiers();
        }
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

        // 更新阶层（时间驱动）
        if (enableTieredDifficulty)
        {
            UpdateTier();
        }

        // 检查最终阶段
        if (enableFinalPhase)
        {
            CheckFinalPhase();
        }

        ProcessSpawnPoints();
    }

    void ProcessSpawnPoints()
    {
        // 清理无效生成点
        for (int i = spawnPoints.Count - 1; i >= 0; i--)
        {
            SpawnPointData spawnData = spawnPoints[i];
            if (spawnData.point == null)
            {
                spawnPoints.RemoveAt(i);
                continue;
            }

            // 检查父Tile是否激活
            if (spawnData.point.parent != null)
            {
                Tile parentTile = spawnData.point.parent.GetComponent<Tile>();
                if (parentTile != null && !parentTile.isActive)
                {
                    spawnData.isActive = false;
                    continue;
                }
            }

            // 根据距离激活/停用
            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);
            if (distance <= spawnData.activationRadius && !spawnData.isActive)
                spawnData.isActive = true;
            else if (distance > spawnData.deactivationRadius && spawnData.isActive)
                spawnData.isActive = false;

            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                    spawnData.isOnCooldown = false;
            }
        }

        // 正常模式生成逻辑
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;
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

    IEnumerator Routine_SpawnEnemies(SpawnPointData spawnData, int count)
    {
        float spawnRadius = spawnData.spawnRadius > 0 ? spawnData.spawnRadius : defaultSpawnRadius;

        for (int i = 0; i < count; i++)
        {
            if (spawnData.point == null) yield break;
            if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount) yield break;

            GameObject enemyPrefab = GetWeightedRandomEnemy();
            if (enemyPrefab == null) continue;

            Vector3 spawnPos = GetSpawnPositionAvoidingPlayer(spawnData.point.position, spawnRadius);

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
            enemy.transform.parent = null;

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(spawnPos);

            RegisterEnemyToTile(enemy, spawnPos);

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
        bool weightsReady = enemyWeights != null && enemyWeights.Count > 0;
        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
            return null;

        // 权重未就绪时兜底：从 enemyPrefabs 平权随机
        if (!weightsReady)
        {
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
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

    // ====================================================================
    //  ⭐ 核心：阶层式升级系统
    // ====================================================================

    void CreateDefaultTiers()
    {
        // 创建默认的6个阶层（300秒/5分钟一局）
        difficultyTiers = new TierData[]
        {
            new TierData {
                tierName = "🌱 平静期",
                timeThreshold = 0f,
                spawnInterval = 2.0f,
                spawnCount = 1,
                speedMultiplier = 1.0f,
                healthMultiplier = 1.0f,
                damageMultiplier = 1.0f,
                enableCooldown = true,
                upgradeMessage = "🌱 平静期开始..."
            },
            new TierData {
                tierName = "⚔️ 热身期",
                timeThreshold = 60f,
                spawnInterval = 1.8f,
                spawnCount = 1,
                speedMultiplier = 1.1f,
                healthMultiplier = 1.1f,
                damageMultiplier = 1.0f,
                enableCooldown = true,
                upgradeMessage = "⚔️ 敌人开始活跃！"
            },
            new TierData {
                tierName = "🔥 活跃期",
                timeThreshold = 120f,
                spawnInterval = 1.5f,
                spawnCount = 2,
                speedMultiplier = 1.3f,
                healthMultiplier = 1.2f,
                damageMultiplier = 1.1f,
                enableCooldown = true,
                upgradeMessage = "🔥 敌人越来越多！"
            },
            new TierData {
                tierName = "💥 激烈期",
                timeThreshold = 180f,
                spawnInterval = 1.0f,
                spawnCount = 3,
                speedMultiplier = 1.8f,
                healthMultiplier = 1.5f,
                damageMultiplier = 1.3f,
                enableCooldown = true,
                upgradeMessage = "💥 战斗白热化！"
            },
            new TierData {
                tierName = "🌪️ 狂暴期",
                timeThreshold = 240f,
                spawnInterval = 0.6f,
                spawnCount = 4,
                speedMultiplier = 2.5f,
                healthMultiplier = 2.0f,
                damageMultiplier = 1.6f,
                enableCooldown = false,
                upgradeMessage = "🌪️ 狂暴模式启动！"
            },
            new TierData {
                tierName = "☠️ 末日期",
                timeThreshold = 285f,
                spawnInterval = 0.3f,
                spawnCount = 5,
                speedMultiplier = 3.0f,
                healthMultiplier = 2.5f,
                damageMultiplier = 2.0f,
                enableCooldown = false,
                upgradeMessage = "☠️ 末日降临！！！"
            }
        };
    }

    void UpdateTier()
    {
        if (difficultyTiers == null || difficultyTiers.Length == 0) return;

        float elapsedTime = GetElapsedTime();

        // 从后往前找匹配的阶层
        TierData currentTier = difficultyTiers[0];
        int newTierIndex = 0;

        for (int i = difficultyTiers.Length - 1; i >= 0; i--)
        {
            if (elapsedTime >= difficultyTiers[i].timeThreshold)
            {
                currentTier = difficultyTiers[i];
                newTierIndex = i;
                break;
            }
        }

        // 检测是否升级了
        if (newTierIndex != lastTierIndex)
        {
            currentTierIndex = newTierIndex;
            OnTierUpgrade(currentTier, newTierIndex);
        }

        // 应用当前阶层参数（最终倍率 = 阶层倍率 × 难度基础倍率，让不同难度整体变强/变弱）
        float difficultySpeedBase = currentDifficulty != null ? currentDifficulty.enemySpeedMultiplier : 1f;
        float difficultyHealthBase = currentDifficulty != null ? currentDifficulty.enemyHealthMultiplier : 1f;
        float difficultyDamageBase = currentDifficulty != null ? currentDifficulty.enemyDamageMultiplier : 1f;

        currentSpawnInterval = currentTier.spawnInterval;
        currentSpawnPerInterval = currentTier.spawnCount;
        currentSpeedMultiplier = currentTier.speedMultiplier * difficultySpeedBase;
        currentHealthMultiplier = currentTier.healthMultiplier * difficultyHealthBase;
        currentDamageMultiplier = currentTier.damageMultiplier * difficultyDamageBase;
        enableCooldown = currentTier.enableCooldown;

        // 更新现有敌人属性
        if (Mathf.Abs(currentSpeedMultiplier - lastSpeedMultiplier) > 0.01f)
        {
            UpdateExistingEnemyMultipliers();
            lastSpeedMultiplier = currentSpeedMultiplier;
        }

        // 显示升级消息（在屏幕上）
        if (showTierMessage)
        {
            tierMessageTimer -= Time.deltaTime;
            if (tierMessageTimer <= 0f)
            {
                showTierMessage = false;
            }
        }

        // 调试日志
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"📊 [阶层 {newTierIndex + 1}/{difficultyTiers.Length}] {currentTier.tierName} | " +
                     $"时间: {elapsedTime:F0}s | 间隔: {currentSpawnInterval:F2}s | " +
                     $"每波: {currentSpawnPerInterval} | 速度: {currentSpeedMultiplier:F2}x | " +
                     $"血量: {currentHealthMultiplier:F2}x | 伤害: {currentDamageMultiplier:F2}x");
        }
    }

    void OnTierUpgrade(TierData newTier, int tierIndex)
    {
        lastTierIndex = tierIndex;

        // 显示升级消息
        if (!string.IsNullOrEmpty(newTier.upgradeMessage))
        {
            showTierMessage = true;
            tierMessageTimer = 2.5f;
            currentTierMessage = newTier.upgradeMessage;
            Debug.Log($"⬆️ [阶层升级] {newTier.tierName}: {newTier.upgradeMessage}");

            // TODO: 在这里触发UI显示
            // UIManager.Instance?.ShowTierMessage(newTier.upgradeMessage, newTier.tierColor);
        }
        else
        {
            Debug.Log($"⬆️ [阶层升级] {newTier.tierName}");
        }

        // 升级时额外生成一波敌人（给玩家一个"惊喜"）
        if (tierIndex > 0)
        {
            StartCoroutine(SpawnBonusWave());
        }
    }

    IEnumerator SpawnBonusWave()
    {
        yield return new WaitForSeconds(0.5f);

        int bonusCount = Mathf.Min(currentSpawnPerInterval * 2, 6);
        List<SpawnPointData> activePoints = new List<SpawnPointData>();

        foreach (var sp in spawnPoints)
        {
            if (sp.isActive && !sp.isOnCooldown)
                activePoints.Add(sp);
        }

        if (activePoints.Count > 0)
        {
            int pointsToUse = Mathf.Min(activePoints.Count, 3);
            for (int i = 0; i < pointsToUse; i++)
            {
                StartCoroutine(Routine_SpawnEnemies(activePoints[i], bonusCount / pointsToUse));
                yield return new WaitForSeconds(0.15f);
            }
        }
    }

    void UpdateExistingEnemyMultipliers()
    {
        foreach (GameObject enemy in allActiveEnemies)
        {
            if (enemy == null) continue;
            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.ApplyScalingMultipliers(currentSpeedMultiplier, currentHealthMultiplier, currentDamageMultiplier);
            }
        }
    }

    float GetElapsedTime()
    {
        if (GameManager.Instance != null)
            return GameManager.Instance.GetElapsedTime();
        return Time.time;
    }

    // ====================================================================
    //  最终阶段
    // ====================================================================

    void CheckFinalPhase()
    {
        if (!enableFinalPhase) return;

        float elapsedTime = GetElapsedTime();
        float totalTime = GetTotalTime();

        if (totalTime <= 0) return;

        float pressure = Mathf.Clamp01(elapsedTime / totalTime);
        bool shouldBeFinal = pressure >= finalPhaseThreshold;

        if (shouldBeFinal && !isFinalPhaseActive)
        {
            isFinalPhaseActive = true;
            OnFinalPhaseStart();
        }
    }

    void OnFinalPhaseStart()
    {
        Debug.Log("🔥🔥🔥 [普通模式] 最终阶段开始！敌人大军来袭！🔥🔥🔥");
        StartCoroutine(SpawnFinalWave());
    }

    IEnumerator SpawnFinalWave()
    {
        yield return new WaitForSeconds(1f);
        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.isActive)
            {
                StartCoroutine(Routine_SpawnEnemies(spawnData, finalPhaseExtraEnemies));
                yield return new WaitForSeconds(0.2f);
            }
        }
    }

    float GetTotalTime()
    {
        if (currentDifficulty != null)
            return currentDifficulty.timeLimit;
        return 90f; // 默认90秒
    }

    // ====================================================================
    //  清理
    // ====================================================================

    void CleanupDeadEnemies()
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

    public void RegisterEnemyToTile(GameObject enemy, Vector3 spawnPos)
    {
        if (worldManager == null)
        {
            worldManager = FindObjectOfType<InfiniteWorldManager>();
            if (worldManager == null) return;
        }

        Vector2Int gridPos = worldManager.WorldToGrid(spawnPos);
        Tile targetTile = worldManager.GetTileAtPosition(gridPos);
        if (targetTile != null)
        {
            targetTile.RegisterEnemy(enemy);
            if (showDebugLogs) Debug.Log($"✅ 敌人已注册到 Tile {gridPos}");
        }
        else
        {
            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai != null && ai.ownerTile != null)
            {
                ai.ownerTile.RegisterEnemy(enemy);
            }
        }
    }

    // ====================================================================
    //  NavMesh
    // ====================================================================

    void EnsureNavMeshSurfaceExists()
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
                navMeshSurface.collectObjects = CollectObjects.All;
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

    IEnumerator DelayedRebuild()
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

    // ====================================================================
    //  公共控制
    // ====================================================================

    public void EnableSpawning()
    {
        EnsureNavMeshSurfaceExists();
        if (navMeshSurface != null && !navMeshSurface.navMeshData)
            BuildNavMeshImmediate();

        canSpawn = true;
        if (showDebugLogs) Debug.Log("✅ 普通模式敌人生成已启用！");

        if (spawnOnStart && !initialSpawnDone)
        {
            PerformInitialSpawn();
            initialSpawnDone = true;
        }
    }

    public void DisableSpawning()
    {
        canSpawn = false;
        if (showDebugLogs) Debug.Log("⏸️ 普通模式敌人生成已禁用");
    }

    public bool IsSpawningEnabled() => canSpawn;

    public void StartSpawning() => EnableSpawning();
    public void StopSpawning() => DisableSpawning();

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

        if (showDebugLogs) Debug.Log($"📋 普通模式参数已更新: 间隔={spawnInterval}s, 每波={spawnPerInterval}, 速度={speedMultiplier}x");
    }

    public void ApplyDifficultySettings(DifficultySettings settings)
    {
        if (settings == null) return;

        enableMaxLimit = settings.enableMaxLimit;
        maxEnemyCount = settings.maxEnemyCount;
        enableCooldown = settings.enableCooldown;
        cooldownTime = settings.cooldownTime;

        if (settings.allowedEnemyPrefabs != null && settings.allowedEnemyPrefabs.Count > 0)
        {
            enemyPrefabs = new List<GameObject>(settings.allowedEnemyPrefabs);
            UpdateWeightList();
        }

        // 普通模式：生成节奏与属性倍率统一由 difficultyTiers 管理，不读取 SO 的生成参数
        // （避免多源冲突）。初始值取第一阶，之后 UpdateTier 每帧刷新。
        // 敌人硬度的"难度基础倍率"从 DifficultySettings 读取并叠加到阶层倍率上：
        //   最终倍率 = 阶层倍率 × 难度基础倍率（如困难血条 = 阶层 × 1.25）。
        if (enableTieredDifficulty && difficultyTiers != null && difficultyTiers.Length > 0)
        {
            currentSpawnInterval = difficultyTiers[0].spawnInterval;
            currentSpawnPerInterval = difficultyTiers[0].spawnCount;
            currentSpeedMultiplier = difficultyTiers[0].speedMultiplier * settings.enemySpeedMultiplier;
            currentHealthMultiplier = difficultyTiers[0].healthMultiplier * settings.enemyHealthMultiplier;
            currentDamageMultiplier = difficultyTiers[0].damageMultiplier * settings.enemyDamageMultiplier;
            enableCooldown = difficultyTiers[0].enableCooldown;
        }
        else
        {
            currentSpawnInterval = spawnInterval;
            currentSpawnPerInterval = spawnPerInterval;
            currentSpeedMultiplier = 1f;
            currentHealthMultiplier = 1f;
            currentDamageMultiplier = 1f;
        }

        if (showDebugLogs) Debug.Log($"📋 已应用难度设置: {settings.difficultyName}");
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

        ApplyTileSizeToSpawnPoints();
    }

    void ApplyTileSizeToSpawnPoints()
    {
        foreach (var spawnData in spawnPoints)
        {
            if (spawnData.point == null || spawnData.point.parent == null) continue;
            Tile tile = spawnData.point.parent.GetComponent<Tile>();
            if (tile == null) continue;

            float tileSize = spawnData.point.parent.lossyScale.x;
            if (tileSize > 0.1f)
            {
                spawnData.spawnRadius = tileSize * 0.5f;
                spawnData.activationRadius = tileSize * 1.0f;
                spawnData.deactivationRadius = tileSize * 1.2f;
            }
            else
            {
                spawnData.spawnRadius = defaultSpawnRadius;
            }
        }
    }

    public Vector3 GetRandomPositionAroundPoint(Vector3 center, float radius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(Random.Range(0f, 1f)) * radius;
        return new Vector3(center.x + Mathf.Cos(angle) * distance, center.y, center.z + Mathf.Sin(angle) * distance);
    }

    // 生成位置避开玩家：随机点必须落在 NavMesh 上，且与玩家保持至少 playerClearRadius 的水平距离，
    // 否则重试（避免生成在玩家身上/周围把玩家推开）。全部失败则回退，并把太靠近玩家的点沿远离方向推到安全距离。
    public Vector3 GetSpawnPositionAvoidingPlayer(Vector3 center, float radius)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            Vector3 candidate = GetRandomPositionAroundPoint(center, radius);
            candidate = GetValidNavMeshPosition(candidate, navMeshSampleRadius);
            if (!IsPositionValid(candidate)) continue;
            if (IsTooCloseToPlayer(candidate)) continue;
            return candidate;
        }

        Vector3 fallback = GetValidNavMeshPosition(center, navMeshSampleRadius);
        if (IsTooCloseToPlayer(fallback) && playerTarget != null)
        {
            Vector3 dir = fallback - playerTarget.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                fallback = playerTarget.position + dir.normalized * playerClearRadius;
                fallback = GetValidNavMeshPosition(fallback, navMeshSampleRadius);
            }
        }
        return fallback;
    }

    private bool IsTooCloseToPlayer(Vector3 pos)
    {
        if (playerTarget == null) return false;
        Vector3 a = pos; a.y = 0f;
        Vector3 b = playerTarget.position; b.y = 0f;
        return Vector3.Distance(a, b) < playerClearRadius;
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
        if (!NavMesh.SamplePosition(position, out hit, 1f, NavMesh.AllAreas)) return false;
        // ⭐ carve 挖洞是运行时才生效：烘烤期间洞口仍是蓝色可走，SamplePosition 会误判通过。
        // 若雕刻物(NavMeshObstacle)的洞尚未挖/刚挖，敌人可能生成在装饰物里原地卡死。
        // 直接查碰撞体——只要有 NavMeshObstacle 且非触发器与生成点重叠就拒绝。
        if (IsInsideNavMeshObstacle(position)) return false;
        return true;
    }

    // 生成点是否落在某个 NavMeshObstacle（装饰物 carve 挖洞）的碰撞体内
    private static Collider[] spawnObstacleBuffer = new Collider[32];
    private bool IsInsideNavMeshObstacle(Vector3 position, float checkRadius = 0.2f)
    {
        int count = Physics.OverlapSphereNonAlloc(position, checkRadius, spawnObstacleBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider c = spawnObstacleBuffer[i];
            if (c == null || c.isTrigger) continue;
            if (c.GetComponentInParent<NavMeshObstacle>() != null) return true;
        }
        return false;
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
        isFinalPhaseActive = false;
        currentTierIndex = -1;
        lastTierIndex = -1;
        showTierMessage = false;

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.isActive = false;
            spawnData.spawnTimer = 0f;
            spawnData.totalSpawned = 0;
            spawnData.isOnCooldown = false;
            spawnData.cooldownTimer = 0f;
            spawnData.hasSpawnedOnce = false;
            spawnData.activeEnemies.Clear();
        }
    }

    public void UpdateSpawnPoints(List<Transform> newSpawnPoints)
    {
        spawnPoints.Clear();
        foreach (Transform point in newSpawnPoints)
        {
            if (point == null) continue;
            SpawnPointData data = new SpawnPointData();
            data.point = point;
            if (point.parent != null)
            {
                Tile parentTile = point.parent.GetComponent<Tile>();
                if (parentTile != null)
                    data.tileType = parentTile.tileType;
            }
            spawnPoints.Add(data);
        }
        ApplyTileSizeToSpawnPoints();
        if (showDebugLogs) Debug.Log($"🔄 普通模式生成点已更新：{spawnPoints.Count} 个生成点");
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
        string tierInfo = (currentTierIndex >= 0 && currentTierIndex < difficultyTiers.Length) ?
                          difficultyTiers[currentTierIndex].tierName : "无";
        return $"阶层: {tierInfo} | 活跃: {activeCount}/{spawnPoints.Count} | 敌人: {limitInfo}";
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

        if (useDifficultySettings && currentDifficulty != null)
        {
            ApplyDifficultySettings(currentDifficulty);
        }

        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogError("没有敌人预制体！");
        }

        EnsureNavMeshSurfaceExists();
        ApplyTileSizeToSpawnPoints();
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