using UnityEngine;
using System.Collections.Generic;

public class InfiniteWorldManager : MonoBehaviour
{
    [Header("核心设置")]
    public Tile[] tilePrefabs;           // 3种Tile预制体
    public Transform playerTarget;
    public float tileSize = 10f;         // 与预制体Scale一致

    [Header("生成范围")]
    public int spawnRadius = 5;          // 方形范围半径（格数）
    public int destroyRadius = 7;        // 销毁半径（必须大于 spawnRadius）

    [Header("性能限制")]
    public int maxTileCount = 100;

    [Header("Tile生成权重")]
    public float[] tileWeights = new float[] { 1f, 1f, 1f }; // 与预制体匹配的权重数组

    [Header("生成点过滤")]
    public int minSpawnDistance = 1;     // 跳过玩家周围区域

    [Header("调试")]
    public bool showDebugLogs = true;
    public bool showGizmos = true;

    // Tile管理
    private Dictionary<Vector2Int, Tile> activeTiles = new Dictionary<Vector2Int, Tile>();
    private Vector2Int lastPlayerGridPos;
    private bool isInitialized = false;

    // GC 性能优化缓存集合
    private HashSet<Vector2Int> neededPositionsCache = new HashSet<Vector2Int>();
    private List<Vector2Int> toRemoveCache = new List<Vector2Int>();

    // 引用
    private EnemySpawner enemySpawner;
    private NavMeshUpdater navUpdater;

    // 用于预测移动方向
    private Vector3 previousPlayerPos;

    void Start()
    {
        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTarget = player.transform;
        }

        enemySpawner = FindObjectOfType<EnemySpawner>();
        navUpdater = FindObjectOfType<NavMeshUpdater>();

        if (tilePrefabs == null || tilePrefabs.Length == 0)
        {
            Debug.LogError("❌ InfiniteWorldManager: 缺少Tile预制体数组！");
            return;
        }

        previousPlayerPos = playerTarget.position;
        // 延迟一帧执行初始生成，确保其他组件挂载就绪
        Invoke(nameof(InitialGenerate), 0.1f);
    }

    void InitialGenerate()
    {
        if (playerTarget == null) return;
        lastPlayerGridPos = WorldToGrid(playerTarget.position);
        UpdateTiles();
        isInitialized = true;
        if (showDebugLogs) Debug.Log($"✅ 初始生成完成，Tile 数量: {activeTiles.Count}");
    }

    void Update()
    {
        if (playerTarget == null || tilePrefabs == null || tilePrefabs.Length == 0 || !isInitialized)
            return;

        // 检测玩家是否移动到新网格
        Vector2Int currentGrid = WorldToGrid(playerTarget.position);
        if (currentGrid != lastPlayerGridPos)
        {
            UpdateTiles();
            lastPlayerGridPos = currentGrid;
            previousPlayerPos = playerTarget.position;
        }
    }

    void UpdateTiles()
    {
        Vector2Int playerGrid = WorldToGrid(playerTarget.position);

        // 1. 获取需要存在的 Tile 坐标集合（方形范围，零 GC 分配）
        GetPositionsInRadius(playerGrid, spawnRadius, neededPositionsCache);

        // 2. 移动方向预生成（修正：使用归一化方向向量计算相对网格偏移）
        Vector3 moveDelta = playerTarget.position - previousPlayerPos;
        if (moveDelta.sqrMagnitude > 0.01f)
        {
            Vector3 dir = moveDelta.normalized;
            Vector2Int gridDir = new Vector2Int(Mathf.RoundToInt(dir.x), Mathf.RoundToInt(dir.z));

            for (int i = 1; i <= 2; i++)
            {
                Vector2Int futurePos = playerGrid + gridDir * (spawnRadius + i);
                if (!neededPositionsCache.Contains(futurePos))
                    neededPositionsCache.Add(futurePos);
            }
        }

        // 3. 创建新 Tile
        foreach (var pos in neededPositionsCache)
        {
            if (!activeTiles.ContainsKey(pos))
            {
                if (activeTiles.Count >= maxTileCount)
                {
                    if (showDebugLogs) Debug.LogWarning($"⚠️ 达到Tile上限: {maxTileCount}");
                    break;
                }
                CreateTile(pos);
            }
        }

        // 4. 移除超出销毁范围的 Tile（统一采用切比雪夫/方形距离）
        toRemoveCache.Clear();
        foreach (var kvp in activeTiles)
        {
            int gridDistance = GetChebyshevDistance(kvp.Key, playerGrid);
            if (gridDistance > destroyRadius)
                toRemoveCache.Add(kvp.Key);
        }
        foreach (var pos in toRemoveCache)
            DestroyTile(pos);

        // 5. 更新 Tile 激活状态
        foreach (var kvp in activeTiles)
        {
            int gridDistance = GetChebyshevDistance(kvp.Key, playerGrid);
            if (gridDistance <= spawnRadius)
                kvp.Value.Activate();
            else
                kvp.Value.Deactivate();
        }

        // 6. 更新敌人生成点
        if (enemySpawner != null)
            enemySpawner.UpdateSpawnPoints(GetAllSpawnPoints());

        // 7. 请求 NavMesh 异步防抖更新
        if (navUpdater != null)
            navUpdater.RequestUpdate();
    }

    // ---------- 核心逻辑辅助方法 ----------

    void GetPositionsInRadius(Vector2Int center, int radius, HashSet<Vector2Int> result)
    {
        result.Clear();
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                result.Add(new Vector2Int(center.x + x, center.y + z));
            }
        }
    }

    int GetChebyshevDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }

    int GetRandomTileType()
    {
        if (tilePrefabs == null || tilePrefabs.Length == 0) return 0;

        float totalWeight = 0f;
        for (int i = 0; i < tilePrefabs.Length; i++)
        {
            float w = (i < tileWeights.Length) ? tileWeights[i] : 1f;
            totalWeight += Mathf.Max(0, w);
        }

        if (totalWeight <= 0) return Random.Range(0, tilePrefabs.Length);

        float randomValue = Random.Range(0f, totalWeight);
        float currentSum = 0f;

        for (int i = 0; i < tilePrefabs.Length; i++)
        {
            float w = (i < tileWeights.Length) ? tileWeights[i] : 1f;
            currentSum += Mathf.Max(0, w);
            if (randomValue <= currentSum)
                return i;
        }

        return 0;
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.x / tileSize);
        int z = Mathf.RoundToInt(worldPos.z / tileSize);
        return new Vector2Int(x, z);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(gridPos.x * tileSize, 0, gridPos.y * tileSize);
    }

    void CreateTile(Vector2Int gridPos)
    {
        Vector3 worldPos = GridToWorld(gridPos);
        int type = GetRandomTileType();
        Tile selectedPrefab = tilePrefabs[type];

        Tile newTile = Instantiate(selectedPrefab, worldPos, Quaternion.identity);
        newTile.Initialize(gridPos, type);
        newTile.Activate();
        activeTiles[gridPos] = newTile;

        if (showDebugLogs) Debug.Log($"✅ 创建Tile: {gridPos} (类型{type}) 位置{worldPos}");
    }

    void DestroyTile(Vector2Int gridPos)
    {
        if (activeTiles.TryGetValue(gridPos, out Tile tile))
        {
            Destroy(tile.gameObject);
            activeTiles.Remove(gridPos);
            if (showDebugLogs) Debug.Log($"🗑️ 销毁Tile: {gridPos}");
        }
    }

    // ---------- 供外部调用的公共接口 ----------

    public List<Transform> GetAllSpawnPoints()
    {
        List<Transform> allPoints = new List<Transform>();
        if (playerTarget == null) return allPoints;
        Vector2Int playerGrid = WorldToGrid(playerTarget.position);

        foreach (var kvp in activeTiles)
        {
            Tile tile = kvp.Value;
            Vector2Int tilePos = kvp.Key;
            int dist = GetChebyshevDistance(tilePos, playerGrid);

            if (dist < minSpawnDistance) continue;

            // ⭐ 优先返回 Tile 上的 spawnPoint 变换组件
            if (tile.isActive)
            {
                Transform sp = tile.spawnPoint != null ? tile.spawnPoint : tile.transform;
                allPoints.Add(sp);
            }
        }
        return allPoints;
    }

    public List<Tile> GetActiveTiles()
    {
        List<Tile> tiles = new List<Tile>();
        foreach (var kvp in activeTiles)
            if (kvp.Value.isActive) tiles.Add(kvp.Value);
        return tiles;
    }

    public int GetTotalEnemyCount()
    {
        int total = 0;
        foreach (var kvp in activeTiles)
            total += kvp.Value.GetEnemies().Count;
        return total;
    }

    public void ClearAll()
    {
        List<Vector2Int> keys = new List<Vector2Int>(activeTiles.Keys);
        foreach (var key in keys)
            DestroyTile(key);
        activeTiles.Clear();
    }

    public void ResetManager()
    {
        ClearAll();
        lastPlayerGridPos = Vector2Int.zero;
        isInitialized = false;
    }

    public Tile GetTileAtPosition(Vector2Int gridPos)
    {
        activeTiles.TryGetValue(gridPos, out Tile tile);
        return tile;
    }

    public void RegisterEnemyToTile(GameObject enemy, Vector2Int gridPos)
    {
        Tile targetTile = GetTileAtPosition(gridPos);
        if (targetTile != null)
            targetTile.RegisterEnemy(enemy);
        else if (showDebugLogs)
            Debug.LogWarning($"⚠️ 找不到 Tile {gridPos} 来注册敌人");
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmos || playerTarget == null) return;
        Vector2Int playerGrid = WorldToGrid(playerTarget.position);

        Gizmos.color = new Color(0f, 1f, 0f, 0.1f);
        DrawGridSquare(playerGrid, spawnRadius, tileSize);
        Gizmos.color = new Color(1f, 0f, 0f, 0.05f);
        DrawGridSquare(playerGrid, destroyRadius, tileSize);

        foreach (var kvp in activeTiles)
        {
            Vector3 center = GridToWorld(kvp.Key);
            Gizmos.color = kvp.Value.isActive ? Color.green : Color.gray;
            Gizmos.DrawWireCube(center, new Vector3(tileSize, 0.1f, tileSize));
        }
    }

    void DrawGridSquare(Vector2Int center, int radius, float size)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                Vector3 pos = GridToWorld(new Vector2Int(center.x + x, center.y + z));
                Gizmos.DrawWireCube(pos, new Vector3(size, 0.1f, size));
            }
        }
    }
#endif
}