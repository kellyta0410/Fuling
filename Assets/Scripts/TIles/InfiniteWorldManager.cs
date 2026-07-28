using UnityEngine;
using System.Collections;
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
    public int renderDistance = 3;
    public int preGenerateDistance = 3;
    public int seed = 42;

    [Header("生成概率")]
    [Range(0, 100)] public int grassChance = 40;
    [Range(0, 100)] public int waterChance = 30;
    [Range(0, 100)] public int stoneChance = 30;

    [Header("性能优化")]
    public int tilesPerFrame = 2;

    [Header("NavMesh")]
    public NavMeshSurface navMeshSurface;
    public bool enableDynamicNavMesh = false;

    [Header("玩家")]
    public Transform player;

    private Dictionary<Vector2Int, Tile> activeTiles = new Dictionary<Vector2Int, Tile>();
    private Dictionary<Vector2Int, List<GameObject>> tileEnemies = new Dictionary<Vector2Int, List<GameObject>>();
    private Vector2Int lastPlayerGrid;
    private System.Random random;
    private EnemySpawner enemySpawner;

    private Queue<Vector2Int> pendingTiles = new Queue<Vector2Int>();
    private bool isCreating = false;
    private int createdThisFrame = 0;
    private float lastCreateTime = 0f;

    private bool navMeshBaked = false;

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
            PreGenerateTiles();
            UpdateTiles(player.position);
        }

        if (navMeshSurface != null && !navMeshBaked && enableDynamicNavMesh)
        {
            navMeshSurface.BuildNavMesh();
            navMeshBaked = true;
            Debug.Log("✅ NavMesh 烘焙完成");
        }
        else
        {
            Debug.Log("ℹ️ NavMesh 动态烘焙已禁用（无限模式用直接追踪）");
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
        }

        if (pendingTiles.Count > 0 && !isCreating && Time.time - lastCreateTime > 0.1f)
        {
            lastCreateTime = Time.time;
            StartCoroutine(CreateTilesCoroutine());
        }
    }

    void PreGenerateTiles()
    {
        int preGenRange = preGenerateDistance;
        int count = 0;

        for (int x = -preGenRange; x <= preGenRange; x++)
        {
            for (int z = -preGenRange; z <= preGenRange; z++)
            {
                Vector2Int gridPos = new Vector2Int(x, z);
                if (!activeTiles.ContainsKey(gridPos))
                {
                    CreateTileInstant(gridPos);
                    count++;
                }
            }
        }

        Debug.Log($"✅ 预生成了 {count} 个Tile");
    }

    // ⭐ 修改：创建Tile时直接创建生成点
    void CreateTileInstant(Vector2Int gridPos)
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

        tileObj.SetActive(false);
        activeTiles.Add(gridPos, tile);
        tileEnemies.Add(gridPos, new List<GameObject>());

        // ⭐ 创建生成点（不管是否显示）
        CreateSpawnPointsOnTile(tile);
    }

    IEnumerator CreateTilesCoroutine()
    {
        isCreating = true;
        createdThisFrame = 0;

        while (pendingTiles.Count > 0)
        {
            Vector2Int gridPos = pendingTiles.Dequeue();

            if (!activeTiles.ContainsKey(gridPos))
            {
                CreateTileInstant(gridPos);
                createdThisFrame++;
            }

            if (createdThisFrame >= tilesPerFrame)
            {
                createdThisFrame = 0;
                yield return new WaitForEndOfFrame();
                yield return null;
            }
        }

        isCreating = false;
    }

    void UpdateTiles(Vector3 playerPosition)
    {
        Vector2Int playerGrid = WorldToGrid(playerPosition);

        int showMinX = playerGrid.x - renderDistance;
        int showMaxX = playerGrid.x + renderDistance;
        int showMinZ = playerGrid.y - renderDistance;
        int showMaxZ = playerGrid.y + renderDistance;

        int genMinX = playerGrid.x - preGenerateDistance;
        int genMaxX = playerGrid.x + preGenerateDistance;
        int genMinZ = playerGrid.y - preGenerateDistance;
        int genMaxZ = playerGrid.y + preGenerateDistance;

        HashSet<Vector2Int> shouldShow = new HashSet<Vector2Int>();
        for (int x = showMinX; x <= showMaxX; x++)
        {
            for (int z = showMinZ; z <= showMaxZ; z++)
            {
                shouldShow.Add(new Vector2Int(x, z));
            }
        }

        HashSet<Vector2Int> shouldGenerate = new HashSet<Vector2Int>();
        for (int x = genMinX; x <= genMaxX; x++)
        {
            for (int z = genMinZ; z <= genMaxZ; z++)
            {
                Vector2Int pos = new Vector2Int(x, z);
                if (!activeTiles.ContainsKey(pos) && !pendingTiles.Contains(pos))
                {
                    shouldGenerate.Add(pos);
                }
            }
        }

        foreach (Vector2Int gridPos in shouldGenerate)
        {
            pendingTiles.Enqueue(gridPos);
        }

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var tile in activeTiles)
        {
            bool inShow = tile.Key.x >= showMinX && tile.Key.x <= showMaxX &&
                          tile.Key.y >= showMinZ && tile.Key.y <= showMaxZ;
            bool inGen = tile.Key.x >= genMinX && tile.Key.x <= genMaxX &&
                         tile.Key.y >= genMinZ && tile.Key.y <= genMaxZ;

            if (!inShow && !inGen)
            {
                toRemove.Add(tile.Key);
            }
        }

        foreach (Vector2Int gridPos in toRemove)
        {
            DestroyTile(gridPos);
        }

        // ⭐ 确保所有显示的Tile都有生成点
        foreach (Vector2Int gridPos in shouldShow)
        {
            if (activeTiles.ContainsKey(gridPos))
            {
                Tile tile = activeTiles[gridPos];

                if (!tile.isActive)
                {
                    tile.Activate();
                    CreateSpawnPointsOnTile(tile);
                    Debug.Log($"✅ Tile {gridPos} 激活，创建生成点");
                }
                else
                {
                    // ⭐ 检查是否有生成点，没有就补创建
                    bool hasSpawnPoint = false;
                    if (enemySpawner != null)
                    {
                        foreach (var data in enemySpawner.spawnPoints)
                        {
                            if (data.point != null && data.point.parent == tile.transform)
                            {
                                hasSpawnPoint = true;
                                break;
                            }
                        }
                    }

                    if (!hasSpawnPoint)
                    {
                        CreateSpawnPointsOnTile(tile);
                        Debug.Log($"🔄 补创建Tile {gridPos} 的生成点");
                    }
                }
            }
        }
    }

    // ⭐ DestroyTile（不销毁敌人）
    void DestroyTile(Vector2Int gridPos)
    {
        if (activeTiles.ContainsKey(gridPos))
        {
            Tile tile = activeTiles[gridPos];

            if (enemySpawner != null)
            {
                enemySpawner.spawnPoints.RemoveAll(data =>
                    data.point != null && data.point.parent == tile.transform
                );
            }

            // ⭐ 不销毁敌人！让敌人独立存在
            // 敌人已经通过 EnemySpawner 设置为 parent = null
            // 所以不会随着 Tile 销毁

            Destroy(tile.gameObject);
            activeTiles.Remove(gridPos);
        }
    }

    void CreateSpawnPointsOnTile(Tile tile)
    {
        if (enemySpawner == null) return;

        GameObject spawnPoint = new GameObject($"SpawnPoint_0");
        spawnPoint.transform.parent = tile.transform;
        spawnPoint.transform.localPosition = Vector3.zero;

        EnemySpawner.SpawnPointData data = new EnemySpawner.SpawnPointData();
        data.point = spawnPoint.transform;
        data.activationRadius = 15f;
        data.deactivationRadius = 20f;
        data.spawnRadius = 2f;
        data.tileType = tile.tileType;

        enemySpawner.spawnPoints.Add(data);
    }

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