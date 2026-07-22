using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour
{
    [Header("核心设置")]
    public List<GameObject> enemyPrefabs;
    public Transform playerTarget;

    [Header("⭐ 生成限制（可选择）")]
    public bool enableMaxLimit = false;         // false = 无限生成，true = 有上限
    public int maxEnemyCount = 30;              // 最大敌人数量

    [Header("生成点设置")]
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();

    [Header("全局生成设置")]
    public float spawnInterval = 2f;
    public int spawnPerInterval = 1;

    [Header("⭐ 冷却设置")]
    public float cooldownTime = 10f;            // 每个生成点的冷却时间（秒）
    public bool enableCooldown = true;          // 是否启用冷却系统

    [Header("调试")]
    public bool showDebugLogs = true;

    [System.Serializable]
    public class SpawnPointData
    {
        public Transform point;
        public float activationRadius = 15f;
        public float deactivationRadius = 22f;
        public bool isActive = false;
        public float spawnTimer = 0f;
        public int totalSpawned = 0;

        // ⭐ 新增冷却相关字段
        public bool isOnCooldown = false;        // 是否在冷却中
        public float cooldownTimer = 0f;         // 冷却计时器
        public bool hasSpawnedOnce = false;      // 是否已经生成过（用于防止重复立即生成）

        [System.NonSerialized]
        public List<GameObject> activeEnemies = new List<GameObject>();
    }

    private List<GameObject> allActiveEnemies = new List<GameObject>();
    private int globalTotalSpawned = 0;

    void Start()
    {
        InitializeSpawner();
    }

    void InitializeSpawner()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
            else Debug.LogWarning("⚠️ 未找到 Player！");
        }

        if (spawnPoints.Count == 0)
        {
            AutoFindSpawnPoints();
        }

        if (enemyPrefabs.Count == 0)
        {
            Debug.LogError("❌ 没有敌人预制体！");
        }

        string limitText = enableMaxLimit ? $"上限: {maxEnemyCount}" : "♾️ 无限生成";
        string cooldownText = enableCooldown ? $"冷却: {cooldownTime}秒" : "无冷却";
        Debug.Log($"✅ 找到 {spawnPoints.Count} 个生成点 | {limitText} | {cooldownText}");
    }

    void AutoFindSpawnPoints()
    {
        GameObject[] foundPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");
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
            Debug.LogWarning("⚠️ 没有生成点，使用自身位置");
        }
    }

    void Update()
    {
        if (enemyPrefabs.Count == 0 || playerTarget == null) return;

        CleanupAllEnemies();

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);

            // ---- 更新冷却计时器 ----
            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                {
                    spawnData.isOnCooldown = false;
                    spawnData.hasSpawnedOnce = false; // 重置标记，允许再次立即生成
                    if (showDebugLogs)
                    {
                        Debug.Log($"⏰ {spawnData.point.name} 冷却结束，可以再次生成");
                    }
                }
            }

            // ---- 激活检测 ----
            if (distance <= spawnData.activationRadius && !spawnData.isActive)
            {
                spawnData.isActive = true;
                spawnData.spawnTimer = 0f;

                // ⭐ 检查是否应该立即生成
                bool shouldSpawnImmediately = true;

                // 如果启用了冷却，并且已经生成过（或在冷却中），则不立即生成
                if (enableCooldown)
                {
                    if (spawnData.isOnCooldown || spawnData.hasSpawnedOnce)
                    {
                        shouldSpawnImmediately = false;
                        if (showDebugLogs)
                        {
                            string reason = spawnData.isOnCooldown ? "冷却中" : "已生成过";
                            Debug.Log($"⏳ {spawnData.point.name} 激活，但处于{reason}，等待定时生成");
                        }
                    }
                }

                if (shouldSpawnImmediately)
                {
                    SpawnEnemyImmediately(spawnData);
                    // 标记已生成，并开始冷却（如果启用）
                    if (enableCooldown)
                    {
                        spawnData.hasSpawnedOnce = true;
                        spawnData.isOnCooldown = true;
                        spawnData.cooldownTimer = cooldownTime;
                        if (showDebugLogs)
                        {
                            Debug.Log($"🟢 {spawnData.point.name} 已激活并立即生成！冷却 {cooldownTime}秒");
                        }
                    }
                    else
                    {
                        if (showDebugLogs)
                        {
                            Debug.Log($"🟢 {spawnData.point.name} 已激活！立即生成敌人");
                        }
                    }
                }
            }

            if (distance > spawnData.deactivationRadius && spawnData.isActive)
            {
                spawnData.isActive = false;
                spawnData.spawnTimer = 0f;
                if (showDebugLogs)
                {
                    Debug.Log($"🔴 {spawnData.point.name} 已停用");
                }
            }

            // ---- 后续定时生成逻辑 ----
            if (spawnData.isActive)
            {
                // ⭐ 如果启用了冷却，且在冷却中，跳过定时生成
                if (enableCooldown && spawnData.isOnCooldown)
                {
                    continue;
                }

                // ⭐ 检查是否达到上限（如果启用了上限）
                if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount)
                {
                    continue;
                }

                spawnData.spawnTimer += Time.deltaTime;
                if (spawnData.spawnTimer >= spawnInterval)
                {
                    spawnData.spawnTimer = 0f;

                    // ⭐ 计算可生成数量
                    int toSpawn = spawnPerInterval;

                    // 如果启用了上限，限制数量
                    if (enableMaxLimit)
                    {
                        int currentCount = allActiveEnemies.Count;
                        int maxSpawn = maxEnemyCount - currentCount;
                        toSpawn = Mathf.Min(spawnPerInterval, maxSpawn);
                    }

                    if (toSpawn > 0)
                    {
                        SpawnEnemyAtPoint(spawnData, toSpawn);

                        // ⭐ 生成后进入冷却（如果启用）
                        if (enableCooldown)
                        {
                            spawnData.isOnCooldown = true;
                            spawnData.cooldownTimer = cooldownTime;
                            if (showDebugLogs)
                            {
                                Debug.Log($"⏳ {spawnData.point.name} 定时生成后进入冷却 {cooldownTime}秒");
                            }
                        }
                    }
                }
            }
        }
    }

    // ==================== 立即生成方法 ====================
    void SpawnEnemyImmediately(SpawnPointData spawnData)
    {
        // 计算可生成数量
        int toSpawn = spawnPerInterval;

        if (enableMaxLimit)
        {
            int currentCount = allActiveEnemies.Count;
            int maxSpawn = maxEnemyCount - currentCount;
            toSpawn = Mathf.Min(spawnPerInterval, maxSpawn);
        }

        if (toSpawn > 0)
        {
            SpawnEnemyAtPoint(spawnData, toSpawn);
        }
    }

    // ==================== 生成敌人 ====================
    void SpawnEnemyAtPoint(SpawnPointData spawnData, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject enemyPrefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];

            Vector2 randomOffset = Random.insideUnitCircle * 2f;
            Vector3 spawnPos = new Vector3(
                spawnData.point.position.x + randomOffset.x,
                0f,
                spawnData.point.position.z + randomOffset.y
            );

            if (!IsPositionOnNavMesh(spawnPos))
            {
                spawnPos = GetNearestNavMeshPoint(spawnPos);
            }

            GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
            enemy.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null)
            {
                EnemyAI.EnemyType[] types = (EnemyAI.EnemyType[])System.Enum.GetValues(typeof(EnemyAI.EnemyType));
                enemyScript.enemyType = types[Random.Range(0, types.Length)];
                enemyScript.Invoke("ConfigureByType", 0.01f);
            }

            spawnData.activeEnemies.Add(enemy);
            allActiveEnemies.Add(enemy);
            spawnData.totalSpawned++;
            globalTotalSpawned++;

            if (showDebugLogs)
            {
                string type = enemyScript != null ? enemyScript.enemyType.ToString() : "Unknown";
                string limitInfo = enableMaxLimit ? $"({allActiveEnemies.Count}/{maxEnemyCount})" : "(♾️)";
                string cooldownInfo = enableCooldown && spawnData.isOnCooldown ? " [冷却中]" : "";
                Debug.Log($"📦 {spawnData.point.name}: {type} {limitInfo}{cooldownInfo}");
            }
        }
    }

    bool IsPositionOnNavMesh(Vector3 position)
    {
        NavMeshHit hit;
        return NavMesh.SamplePosition(position, out hit, 2f, NavMesh.AllAreas);
    }

    Vector3 GetNearestNavMeshPoint(Vector3 position)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, 10f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return position;
    }

    void CleanupAllEnemies()
    {
        allActiveEnemies.RemoveAll(e => e == null);

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.activeEnemies.RemoveAll(e => e == null);
        }
    }

    // ==================== 公共方法 ====================
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

        Debug.Log("🧹 所有敌人已清除");
    }

    public void ResetSpawner()
    {
        ClearAllEnemies();
        globalTotalSpawned = 0;

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            spawnData.isActive = false;
            spawnData.spawnTimer = 0f;
            spawnData.totalSpawned = 0;
            spawnData.isOnCooldown = false;
            spawnData.cooldownTimer = 0f;
            spawnData.hasSpawnedOnce = false;
        }

        Debug.Log("🔄 生成器已重置");
    }

    // ⭐ 新增：手动触发特定生成点（跳过冷却）
    public void ForceSpawnAtPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= spawnPoints.Count)
        {
            Debug.LogWarning("⚠️ 无效的生成点索引");
            return;
        }

        SpawnPointData spawnData = spawnPoints[pointIndex];
        // 强制重置冷却状态
        spawnData.isOnCooldown = false;
        spawnData.hasSpawnedOnce = false;
        SpawnEnemyImmediately(spawnData);

        // 生成后进入冷却
        if (enableCooldown)
        {
            spawnData.isOnCooldown = true;
            spawnData.cooldownTimer = cooldownTime;
        }

        Debug.Log($"⚡ 强制生成 {spawnData.point.name}");
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

        string limitInfo = enableMaxLimit ? $"{allActiveEnemies.Count}/{maxEnemyCount}" : $"♾️ {allActiveEnemies.Count}";
        string cooldownInfo = enableCooldown ? $" | 冷却中: {coolingCount}" : "";
        return $"活跃: {activeCount}/{spawnPoints.Count} | 敌人: {limitInfo}{cooldownInfo}";
    }

    // ==================== Gizmos ====================
    void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            // 根据状态改变颜色
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

#if UNITY_EDITOR
            string status = spawnData.isOnCooldown ?
                $"🟡 冷却中 ({spawnData.cooldownTimer:F1}s)" :
                (spawnData.isActive ? "🟢 激活" : "🔴 停用");

            UnityEditor.Handles.Label(
                spawnData.point.position + Vector3.up * 2.5f,
                $"{spawnData.point.name}\n{status}\n激活: {spawnData.activationRadius}\n停用: {spawnData.deactivationRadius}"
            );
#endif
        }

        // 显示全局状态
#if UNITY_EDITOR
        string limitText = enableMaxLimit ? $"上限: {maxEnemyCount}" : "♾️ 无限";
        string cooldownText = enableCooldown ? $"冷却: {cooldownTime}s" : "无冷却";
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 5f,
            $"全局: {limitText} | {cooldownText} | 当前: {allActiveEnemies.Count}"
        );
#endif
    }
}