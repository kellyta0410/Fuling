using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;

public class InfiniteWorldManager : MonoBehaviour
{
    [Header("Tile预制体")]
    public GameObject grassTilePrefab;
    public GameObject waterTilePrefab;
    public GameObject stoneTilePrefab;

    [Header("世界设置")]
    public float tileSize = 10f;
    public int renderDistance = 2;
    public int seed = 42;

    [Header("生成概率")]
    [Range(0, 100)] public int grassChance = 40;
    [Range(0, 100)] public int waterChance = 30;
    [Range(0, 100)] public int stoneChance = 30;

    [Header("延迟销毁设置")]
    public float destroyDelay = 30f;

    [Header("NavMesh")]
    public NavMeshSurface navMeshSurface;

    [Header("玩家")]
    public Transform player;

    private Dictionary<Vector2Int, Tile> activeTiles = new Dictionary<Vector2Int, Tile>();
    private Dictionary<Vector2Int, float> pendingDestroyTiles = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, List<GameObject>> tileEnemies = new Dictionary<Vector2Int, List<GameObject>>();

    private Vector2Int lastPlayerGrid;
    private System.Random random;
    private EnemySpawner enemySpawner;

    private float lastNavMeshBakeTime = 0f;
    private bool needsNavMeshBake = false;

    void Start()
    {
        random = new System.Random(seed);

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        enemySpawner = FindObjectOfType<EnemySpawner>();

        if (player != null)
        {
            UpdateTiles(player.position);
        }

        // ⭐ 初始烘焙
        if (navMeshSurface != null)
        {
            navMeshSurface.BuildNavMesh();
            Debug.Log("✅ 初始 NavMesh 烘焙完成");
        }
    }

    void Update()
    {
        if (player == null) return;

        Vector2Int currentGrid = WorldToGrid(player.position);
        if (currentGrid != lastPlayerGrid)
        {
            UpdateTiles(player.position);
            lastPlayerGrid = currentGrid;
            needsNavMeshBake = true;
            lastNavMeshBakeTime = Time.time;
        }

        UpdatePendingDestroyTiles();
    }

    void LateUpdate()
    {
        // ⭐ 延迟烘焙NavMesh（避免卡顿）
        if (needsNavMeshBake && Time.time - lastNavMeshBakeTime > 0.5f)
        {
            if (navMeshSurface != null)
            {
                navMeshSurface.BuildNavMesh();
                needsNavMeshBake = false;
                Debug.Log("🔄 NavMesh 已更新");
            }
        }
    }

    // ... 其他方法保持不变 ...

    TileType GetRandomTileType(Vector2Int gridPos)
    {
        int hash = (gridPos.x * 10000 + gridPos.y * 1000 + seed).GetHashCode();
        float randomValue = (float)((hash % 10000) / 10000.0);

        if (randomValue < grassChance / 100f)
            return TileType.Grass;
        else if (randomValue < (grassChance + waterChance) / 100f)
            return TileType.Water;
        else
            return TileType.Stone;
    }

    GameObject GetTilePrefab(TileType type)
    {
        switch (type)
        {
            case TileType.Grass: return grassTilePrefab;
            case TileType.Water: return waterTilePrefab;
            case TileType.Stone: return stoneTilePrefab;
            default: return grassTilePrefab;
        }
    }

    void UpdateTiles(Vector3 playerPosition)
    {
        Vector2Int playerGrid = WorldToGrid(playerPosition);

        int minX = playerGrid.x - renderDistance;
        int maxX = playerGrid.x + renderDistance;
        int minZ = playerGrid.y - renderDistance;
        int maxZ = playerGrid.y + renderDistance;

        HashSet<Vector2Int> shouldShow = new HashSet<Vector2Int>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                shouldShow.Add(new Vector2Int(x, z));
            }
        }

        foreach (Vector2Int gridPos in shouldShow)
        {
            if (!activeTiles.ContainsKey(gridPos) && !pendingDestroyTiles.ContainsKey(gridPos))
            {
                CreateTile(gridPos);
            }
            else if (pendingDestroyTiles.ContainsKey(gridPos))
            {
                pendingDestroyTiles.Remove(gridPos);
                if (activeTiles.ContainsKey(gridPos) && !activeTiles[gridPos].isActive)
                {
                    activeTiles[gridPos].Activate();
                    CreateSpawnPointsOnTile(activeTiles[gridPos]);
                }
            }
        }

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var tile in activeTiles)
        {
            if (!shouldShow.Contains(tile.Key))
            {
                RemoveSpawnPointsFromTile(tile.Value);

                if (!pendingDestroyTiles.ContainsKey(tile.Key))
                {
                    pendingDestroyTiles.Add(tile.Key, destroyDelay);
                    Debug.Log($"⏱️ Tile {tile.Key} 离开视野，{destroyDelay}秒后销毁");
                }
            }
        }
    }

    void CreateTile(Vector2Int gridPos)
    {
        Vector3 worldPos = GridToWorld(gridPos);
        TileType tileType = GetRandomTileType(gridPos);
        GameObject tilePrefab = GetTilePrefab(tileType);

        GameObject tileObj = Instantiate(tilePrefab, worldPos, Quaternion.identity);
        tileObj.transform.parent = transform;

        Tile tile = tileObj.GetComponent<Tile>();
        if (tile != null)
        {
            tile.Initialize(gridPos, tileType);
        }

        tileObj.SetActive(true);
        activeTiles.Add(gridPos, tile);
        tileEnemies.Add(gridPos, new List<GameObject>());

        CreateSpawnPointsOnTile(tile);

        Debug.Log($"✅ 创建Tile {gridPos} ({tileType})");
    }

    void UpdatePendingDestroyTiles()
    {
        List<Vector2Int> toDestroy = new List<Vector2Int>();

        foreach (var kvp in pendingDestroyTiles.ToList())
        {
            Vector2Int gridPos = kvp.Key;
            float timer = kvp.Value;

            timer -= Time.deltaTime;
            pendingDestroyTiles[gridPos] = timer;

            if (timer <= 0f)
            {
                toDestroy.Add(gridPos);
            }
        }

        foreach (Vector2Int gridPos in toDestroy)
        {
            DestroyTile(gridPos);
        }
    }

    void DestroyTile(Vector2Int gridPos)
    {
        if (activeTiles.ContainsKey(gridPos))
        {
            Tile tile = activeTiles[gridPos];

            RemoveSpawnPointsFromTile(tile);

            if (tileEnemies.ContainsKey(gridPos))
            {
                foreach (GameObject enemy in tileEnemies[gridPos])
                {
                    if (enemy != null)
                    {
                        Destroy(enemy);
                    }
                }
                tileEnemies[gridPos].Clear();
                tileEnemies.Remove(gridPos);
            }

            Destroy(tile.gameObject);
            activeTiles.Remove(gridPos);

            Debug.Log($"🗑️ 销毁Tile {gridPos}");
        }

        pendingDestroyTiles.Remove(gridPos);
    }

    void CreateSpawnPointsOnTile(Tile tile)
    {
        if (enemySpawner == null) return;

        int spawnPointCount = 1;

        float tileDisplayRadius = (renderDistance * tileSize) + (tileSize / 2f);

        GameObject spawnPoint = new GameObject($"SpawnPoint_0");
        spawnPoint.transform.parent = tile.transform;
        spawnPoint.transform.localPosition = Vector3.zero;

        EnemySpawner.SpawnPointData data = new EnemySpawner.SpawnPointData();
        data.point = spawnPoint.transform;
        data.activationRadius = tileDisplayRadius;
        data.deactivationRadius = tileDisplayRadius + 1f;
        data.spawnRadius = 2f;
        data.tileType = tile.tileType;

        enemySpawner.spawnPoints.Add(data);
    }

    void RemoveSpawnPointsFromTile(Tile tile)
    {
        if (enemySpawner == null) return;

        enemySpawner.spawnPoints.RemoveAll(data =>
            data.point != null && data.point.parent == tile.transform
        );
    }

    public void RegisterEnemyToTile(GameObject enemy, Vector2Int tileGridPos)
    {
        if (tileEnemies.ContainsKey(tileGridPos))
        {
            tileEnemies[tileGridPos].Add(enemy);
        }
    }

    Vector3 GridToWorld(Vector2Int grid)
    {
        return new Vector3(grid.x * tileSize, 0, grid.y * tileSize);
    }

    Vector2Int WorldToGrid(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.z / tileSize)
        );
    }
}