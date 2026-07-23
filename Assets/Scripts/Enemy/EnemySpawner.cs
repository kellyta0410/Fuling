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

    [Header("调试")]
    public bool showDebugLogs = true;

    // 当前生成参数（由 GameManager 更新）
    private float currentSpawnInterval = 2f;
    private int currentSpawnPerInterval = 1;
    private bool enableMaxLimit = false;
    private int maxEnemyCount = 30;
    private bool enableCooldown = true;
    private float cooldownTime = 10f;

    // 当前倍率（由 GameManager 更新）
    private float currentSpeedMultiplier = 1f;
    private float currentHealthMultiplier = 1f;
    private float currentDamageMultiplier = 1f;

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
            else Debug.LogWarning("未找到 Player");
        }

        if (spawnPoints.Count == 0)
        {
            AutoFindSpawnPoints();
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
            }
        }

        if (enemyPrefabs == null || enemyPrefabs.Count == 0)
        {
            Debug.LogError("没有敌人预制体！请在 DifficultySettings 中配置 allowedEnemyPrefabs");
        }

        string limitText = enableMaxLimit ? "上限: " + maxEnemyCount : "无限生成";
        string cooldownText = enableCooldown ? "冷却: " + cooldownTime + "秒" : "无冷却";
        Debug.Log("找到 " + spawnPoints.Count + " 个生成点 | " + limitText + " | " + cooldownText);
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
        }

        if (showDebugLogs)
        {
            Debug.Log("生成器参数已更新: 间隔=" + currentSpawnInterval + ", 每次=" + currentSpawnPerInterval);
            Debug.Log("  倍率: 速度=" + currentSpeedMultiplier + ", 血量=" + currentHealthMultiplier + ", 攻击=" + currentDamageMultiplier);
        }
    }

    void AutoFindSpawnPoints()
    {
        // 查找 Tag 为 "EnemySpawn" 的物体
        GameObject[] foundPoints = GameObject.FindGameObjectsWithTag("EnemySpawn");
        foreach (GameObject point in foundPoints)
        {
            SpawnPointData data = new SpawnPointData();
            data.point = point.transform;
            spawnPoints.Add(data);
        }

        // 如果没找到，尝试找 "SpawnPoints" 父物体下的子物体
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

        // 如果还是没有，使用自身位置
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
        if (enemyPrefabs == null || enemyPrefabs.Count == 0 || playerTarget == null) return;

        CleanupAllEnemies();

        foreach (SpawnPointData spawnData in spawnPoints)
        {
            if (spawnData.point == null) continue;

            float distance = Vector3.Distance(spawnData.point.position, playerTarget.position);

            if (spawnData.isOnCooldown)
            {
                spawnData.cooldownTimer -= Time.deltaTime;
                if (spawnData.cooldownTimer <= 0f)
                {
                    spawnData.isOnCooldown = false;
                    spawnData.hasSpawnedOnce = false;
                    if (showDebugLogs)
                    {
                        Debug.Log(spawnData.point.name + " 冷却结束");
                    }
                }
            }

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
                            Debug.Log(spawnData.point.name + " 激活，但处于" + reason);
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
                            Debug.Log(spawnData.point.name + " 已激活并立即生成，冷却 " + cooldownTime + "秒");
                        }
                    }
                    else
                    {
                        if (showDebugLogs)
                        {
                            Debug.Log(spawnData.point.name + " 已激活，立即生成敌人");
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
                    Debug.Log(spawnData.point.name + " 已停用");
                }
            }

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
                        SpawnEnemyAtPoint(spawnData, toSpawn);

                        if (enableCooldown)
                        {
                            spawnData.isOnCooldown = true;
                            spawnData.cooldownTimer = cooldownTime;
                            if (showDebugLogs)
                            {
                                Debug.Log(spawnData.point.name + " 定时生成后进入冷却 " + cooldownTime + "秒");
                            }
                        }
                    }
                }
            }
        }
    }

    void SpawnEnemyImmediately(SpawnPointData spawnData)
    {
        int toSpawn = currentSpawnPerInterval;

        if (enableMaxLimit)
        {
            int currentCount = allActiveEnemies.Count;
            int maxSpawn = maxEnemyCount - currentCount;
            toSpawn = Mathf.Min(currentSpawnPerInterval, maxSpawn);
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

            EnemyAI enemyScript = enemy.GetComponent<EnemyAI>();
            if (enemyScript != null)
            {
                enemyScript.ApplyScalingMultipliers(
                    currentSpeedMultiplier,
                    currentHealthMultiplier,
                    currentDamageMultiplier
                );

                if (enemyScript.enemyData != null)
                {
                    Debug.Log("生成 " + enemyScript.enemyData.enemyName +
                        " (速度x" + currentSpeedMultiplier +
                        ", 血量x" + currentHealthMultiplier +
                        ", 攻击x" + currentDamageMultiplier + ")");
                }
            }

            spawnData.activeEnemies.Add(enemy);
            allActiveEnemies.Add(enemy);
            spawnData.totalSpawned++;
            globalTotalSpawned++;

            if (showDebugLogs)
            {
                string type = enemyScript != null && enemyScript.enemyData != null ?
                    enemyScript.enemyData.enemyName : "Unknown";
                string limitInfo = enableMaxLimit ? "(" + allActiveEnemies.Count + "/" + maxEnemyCount + ")" : "";
                Debug.Log(spawnData.point.name + ": " + type + " " + limitInfo);
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

        Debug.Log("所有敌人已清除");
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

        Debug.Log("生成器已重置");
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

#if UNITY_EDITOR
            string status = spawnData.isOnCooldown ?
                "冷却中 (" + spawnData.cooldownTimer.ToString("F1") + "s)" :
                (spawnData.isActive ? "激活" : "停用");

            UnityEditor.Handles.Label(
                spawnData.point.position + Vector3.up * 2.5f,
                spawnData.point.name + "\n" + status + "\n激活: " + spawnData.activationRadius + "\n停用: " + spawnData.deactivationRadius
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