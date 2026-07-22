using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour
{
    [Header("核心设置")]
    public List<GameObject> enemyPrefabs;
    public Transform playerTarget;

    [Header("⭐ 生成限制（可选择）")]
    public bool enableMaxLimit = false;
    public int maxEnemyCount = 30;

    [Header("生成点设置")]
    public List<SpawnPointData> spawnPoints = new List<SpawnPointData>();

    [Header("全局生成设置")]
    public float spawnInterval = 2f;
    public int spawnPerInterval = 1;

    [Header("⭐ 冷却设置")]
    public float cooldownTime = 10f;
    public bool enableCooldown = true;

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
        public bool isOnCooldown = false;
        public float cooldownTimer = 0f;
        public bool hasSpawnedOnce = false;

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
                    spawnData.hasSpawnedOnce = false;
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

                bool shouldSpawnImmediately = true;

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
                if (enableCooldown && spawnData.isOnCooldown)
                {
                    continue;
                }

                if (enableMaxLimit && allActiveEnemies.Count >= maxEnemyCount)
                {
                    continue;
                }

                spawnData.spawnTimer += Time.deltaTime;
                if (spawnData.spawnTimer >= spawnInterval)
                {
                    spawnData.spawnTimer = 0f;

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

    void SpawnEnemyImmediately(SpawnPointData spawnData)
    {
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

            // ⭐ 获取 EnemyAI 并应用数据（如果有 EnemyData）
            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null && enemyScript.enemyData != null)
            {
                // 如果预制体已经有 EnemyData，使用预制体的
                Debug.Log($"✅ 生成敌人，使用 {enemyScript.enemyData.enemyName} 数据");
            }
            else if (enemyScript != null)
            {
                Debug.LogWarning("⚠️ 敌人没有 EnemyData，使用默认值");
            }

            spawnData.activeEnemies.Add(enemy);
            allActiveEnemies.Add(enemy);
            spawnData.totalSpawned++;
            globalTotalSpawned++;

            if (showDebugLogs)
            {
                string type = enemyScript != null && enemyScript.enemyData != null ?
                    enemyScript.enemyData.enemyName : "Unknown";
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

    public void ForceSpawnAtPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= spawnPoints.Count)
        {
            Debug.LogWarning("⚠️ 无效的生成点索引");
            return;
        }

        SpawnPointData spawnData = spawnPoints[pointIndex];
        spawnData.isOnCooldown = false;
        spawnData.hasSpawnedOnce = false;
        SpawnEnemyImmediately(spawnData);

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