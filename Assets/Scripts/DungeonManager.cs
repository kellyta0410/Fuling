using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
//  Dungeon 房间制地牢系统
//  改造 Infinite 场景：玩家一次只在一个房间内，
//  清空敌人后才能前往下一个随机方向的房间，
//  每 5 个房间（5/10/15…）为商店房间，敌人随房间数递增变强。
// =============================================================

public enum DungeonRoomType
{
    Snake,      // 蛇房间
    Zombie,     // 僵尸房间
    Mixed,      // 蛇 + 僵尸
    Shop        // 商店房间
}

public class DungeonManager : MonoBehaviour
{
    [Header("房间尺寸")]
    public float roomSize = 10f;
    public float wallHeight = 4f;
    public float wallThickness = 1f;
    public float doorWidth = 3f;

    [Header("敌人生成")]
    public int roomEnemyCap = 30;       // 每间房敌人上限（打完这 30 只门才开）
    public float spawnInterval = 1f;    // 每隔多少秒生成一只（每秒一只）

    public float playerClearRadius = 4f;

    [Header("装饰随机摆放")]
    public int decorationMin = 3;
    public int decorationMax = 8;
    public float decorationClearRadius = 3f;

    [Header("玩家进入房间的落点偏移（房间中心朝入口方向退后）")]
    public float entryInset = 4f;

    [Header("装饰预制体（按敌人类型分类；留空则该类型用中性装饰）")]
    public List<GameObject> snakeDecorations = new List<GameObject>();
    public List<GameObject> zombieDecorations = new List<GameObject>();
    public List<GameObject> neutralDecorations = new List<GameObject>();

    [Header("商店 Buff（拖入 BuffDataSO 资产；留空自动在 Resources 找）")]
    public List<BuffDataSO> shopBuffs = new List<BuffDataSO>();

    [Header("玩家参考")]
    public Transform playerTarget;

    [Header("调试")]
    public bool showDebugLogs = true;

    [Header("预制体（留空则使用程序化基础体；稍后导入地板/带门墙/不带门墙后拖入对应槽）")]
    public List<GameObject> floorPrefabs = new List<GameObject>(); // 多种地板，每间房随机取一种（不同房间可能不同地板）；墙始终同一套
    public GameObject floorPrefab;          // 兼容：floorPrefabs 为空时用这个
    public GameObject wallPrefab;          // 不带门的墙（用作墙段）
    public GameObject wallWithDoorPrefab;  // 带门洞的墙（含可开关的门挡）
    public string doorBlockerChildName = "DoorBlocker";
    public string doorOpenAnimParam = "Open";     // DoorBlocker 上 Animator 的开/关参数（bool；也可用 trigger）

    [Header("墙体/门等级（蛇=lvl1，僵尸=lvl2，混合=随机）")]
    public GameObject wallPrefabLvl1;
    public GameObject wallWithDoorPrefabLvl1;
    public GameObject wallPrefabLvl2;
    public GameObject wallWithDoorPrefabLvl2;

    // ---------- 运行时状态 ----------
    private int roomIndex = 0;
    private DungeonRoomType currentType;
    private GameObject roomRoot;
    private List<GameObject> aliveEnemies = new List<GameObject>();

    private List<GameObject> doorBlockers = new List<GameObject>();
    private bool[] doorOpen = new bool[4];
    private bool roomCleared = false;
    private bool spawningDone = false;
    private int spawnedCount = 0;
    private bool advancing = false;
    private int entryDir = -1;

    private List<GameObject> snakeEnemyPrefabs = new List<GameObject>();
    private List<GameObject> zombieEnemyPrefabs = new List<GameObject>();
    private DifficultySettings difficulty;
    private NavMeshSurface navMeshSurface;
    private UIManager uiManager;

    // 方向常量：0=N(+Z) 1=E(+X) 2=S(-Z) 3=W(-X)
    private static readonly Vector3[] DirVec = new Vector3[]
    {
        new Vector3(0, 0, 1),
        new Vector3(1, 0, 0),
        new Vector3(0, 0, -1),
        new Vector3(-1, 0, 0),
    };

    void Start()
    {
        if (playerTarget == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTarget = p.transform;
        }
        uiManager = FindObjectOfType<UIManager>();
        difficulty = GameManager.Instance != null ? GameManager.Instance.currentDifficulty : null;

        ResolveEnemyPrefabs();
        EnsureNavMeshSurface();

        if (showDebugLogs) Debug.Log("[Dungeon] 地牢系统启动，生成第 1 个房间");
        StartCoroutine(GenerateRoomRoutine(Vector3.zero, -1));
    }

    void ResolveEnemyPrefabs()
    {
        snakeEnemyPrefabs.Clear();
        zombieEnemyPrefabs.Clear();

        List<GameObject> src = null;
        if (difficulty != null && difficulty.allowedEnemyPrefabs != null && difficulty.allowedEnemyPrefabs.Count > 0)
            src = difficulty.allowedEnemyPrefabs;
        if (src == null && GameManager.Instance != null)
        {
            var inf = GameManager.Instance.infiniteSpawner;
            if (inf != null && inf.enemyPrefabs != null) src = inf.enemyPrefabs;
        }
        if (src != null)
        {
            foreach (var prefab in src)
            {
                if (prefab == null) continue;
                string n = prefab.name.ToLower();
                if (n.Contains("snake")) snakeEnemyPrefabs.Add(prefab);
                else if (n.Contains("jiangshi") || n.Contains("zombie")) zombieEnemyPrefabs.Add(prefab);
                else
                {
                    // 通用敌人（如 Basic / Fast）两种房间都可出现
                    snakeEnemyPrefabs.Add(prefab);
                    zombieEnemyPrefabs.Add(prefab);
                }
            }
        }
        if (showDebugLogs)
            Debug.Log("[Dungeon] 敌人预制体：蛇 " + snakeEnemyPrefabs.Count + " / 僵尸 " + zombieEnemyPrefabs.Count);
    }

    void EnsureNavMeshSurface()
    {
        navMeshSurface = FindObjectOfType<NavMeshSurface>();
        if (navMeshSurface == null)
        {
            GameObject go = new GameObject("NavMeshSurface (Dungeon)");
            navMeshSurface = go.AddComponent<NavMeshSurface>();
            navMeshSurface.layerMask = ~0;
            navMeshSurface.collectObjects = CollectObjects.All;
            navMeshSurface.defaultArea = 0;
        }
    }

    // ============================================================
    //  房间生成
    // ============================================================
    IEnumerator GenerateRoomRoutine(Vector3 center, int entryDir)
    {
        advancing = true;
        this.entryDir = entryDir;

        if (roomRoot != null) Destroy(roomRoot);
        aliveEnemies.Clear();
        doorBlockers.Clear();
        roomCleared = false;

        roomIndex++;

        if (roomIndex % 5 == 0)
            currentType = DungeonRoomType.Shop;
        else
            currentType = (DungeonRoomType)Random.Range(0, 3);

        roomRoot = new GameObject("DungeonRoom_" + roomIndex + "_" + currentType);
        roomRoot.transform.position = center;

        BuildFloor(roomRoot.transform);
        BuildWalls(roomRoot.transform);
        PlaceDecorations(roomRoot.transform);

        yield return null;
        BuildNavMesh();
        yield return null;

        if (playerTarget != null)
        {
            Vector3 playerPos = center;
            if (entryDir >= 0)
                playerPos += DirVec[entryDir] * (roomSize * 0.5f - entryInset);
            playerPos.y = 0.1f;
            playerTarget.position = playerPos;
            CharacterController cc = playerTarget.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }

        if (uiManager != null) uiManager.SetRoomDisplay(roomIndex);
        if (GameManager.Instance != null) GameManager.Instance.SetDungeonRoom(roomIndex);

        if (showDebugLogs) Debug.Log("[Dungeon] 生成房间 #" + roomIndex + " 类型=" + currentType + " 中心=" + center);

        if (currentType == DungeonRoomType.Shop)
            SpawnShop();
        else
            SpawnEnemiesForRoom();

        if (currentType != DungeonRoomType.Shop)
        {
            for (int i = 0; i < 4; i++) { doorOpen[i] = false; SetDoorBlocker(i, true); }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == entryDir) { doorOpen[i] = false; SetDoorBlocker(i, true); } // 不开启玩家背后的入口门
                else { doorOpen[i] = true; SetDoorBlocker(i, false); }
            }
        }

        advancing = false;
    }

    void BuildFloor(Transform parent)
    {
        GameObject floorPrefabToUse = null;
        if (floorPrefabs != null && floorPrefabs.Count > 0)
            floorPrefabToUse = floorPrefabs[Random.Range(0, floorPrefabs.Count)]; // 每间房随机一种地板
        else if (floorPrefab != null)
            floorPrefabToUse = floorPrefab;

        GameObject floor;
        if (floorPrefabToUse != null)
        {
            floor = Instantiate(floorPrefabToUse, parent);
            floor.name = "Floor";
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localRotation = Quaternion.identity;
        }
        else
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(parent);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localRotation = Quaternion.identity;
            floor.transform.localScale = new Vector3(roomSize / 10f, 1f, roomSize / 10f);
        }
        floor.tag = "Ground";
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer != -1) floor.layer = groundLayer;
    }

    void BuildWalls(Transform parent)
    {
        float half = roomSize * 0.5f;
        // 顺序必须严格是 N(0) / E(1) / S(2) / W(3)，与 DirVec、doorOpen 的索引一致
        BuildWallWithDoor(parent, new Vector3(0, 0, half), Quaternion.identity, 0);
        BuildWallWithDoor(parent, new Vector3(half, 0, 0), Quaternion.Euler(0, 90, 0), 1);
        BuildWallWithDoor(parent, new Vector3(0, 0, -half), Quaternion.Euler(0, 180, 0), 2);
        BuildWallWithDoor(parent, new Vector3(-half, 0, 0), Quaternion.Euler(0, -90, 0), 3);
    }

    void BuildWallWithDoor(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex)
    {
        float segLen = (roomSize - doorWidth) * 0.5f;
        float segCenter = doorWidth * 0.5f + segLen * 0.5f;

        GameObject wallParent = new GameObject("Wall_" + dirIndex);
        wallParent.transform.SetParent(parent);
        wallParent.transform.localPosition = localPos;
        wallParent.transform.localRotation = localRot;

        // 按房间类型选择墙/门等级：蛇=lvl1，僵尸=lvl2，混合=随机
        GameObject wwd = wallWithDoorPrefab;
        GameObject w = wallPrefab;
        bool useLvl2;
        if (currentType == DungeonRoomType.Snake) useLvl2 = false;
        else if (currentType == DungeonRoomType.Zombie) useLvl2 = true;
        else useLvl2 = Random.value < 0.5f; // 混合房间：随机选 lvl1 / lvl2
        if (useLvl2)
        {
            if (wallWithDoorPrefabLvl2 != null) wwd = wallWithDoorPrefabLvl2;
            if (wallPrefabLvl2 != null) w = wallPrefabLvl2;
        }
        else
        {
            if (wallWithDoorPrefabLvl1 != null) wwd = wallWithDoorPrefabLvl1;
            if (wallPrefabLvl1 != null) w = wallPrefabLvl1;
        }

        // 优先：使用“带门墙”预制体
        if (wwd != null)
        {
            GameObject wall = Instantiate(wwd, wallParent.transform);
            wall.name = "WallMesh";
            Transform blockerT = wall.transform.Find(doorBlockerChildName);
            GameObject blockerObj = blockerT != null ? blockerT.gameObject : null;
            if (blockerObj == null)
            {
                blockerObj = new GameObject("DoorBlocker");
                blockerObj.transform.SetParent(wallParent.transform);
                blockerObj.transform.localPosition = new Vector3(0, wallHeight * 0.5f, 0);
                var bcInner = blockerObj.AddComponent<BoxCollider>();
                bcInner.size = new Vector3(doorWidth, wallHeight, wallThickness);
                bcInner.isTrigger = false;
            }
            doorBlockers.Add(blockerObj);
            return;
        }

        // 次选：用“不带门墙”预制体当两段墙段 + 程序化门挡
        if (w != null)
        {
            for (int s = 0; s < 2; s++)
            {
                float sign = (s == 0) ? -1f : 1f;
                GameObject seg = Instantiate(w, wallParent.transform);
                seg.name = "WallSeg";
                seg.transform.localPosition = new Vector3(sign * segCenter, wallHeight * 0.5f, 0);
                seg.transform.localRotation = Quaternion.identity;
                seg.tag = "Wall";
                NavMeshObstacle obs = seg.GetComponent<NavMeshObstacle>();
                if (obs == null)
                {
                    obs = seg.AddComponent<NavMeshObstacle>();
                    obs.size = new Vector3(segLen, wallHeight, wallThickness);
                }
                obs.carving = true;
                obs.center = Vector3.zero;
            }
        }
        else
        {
            // 兜底：完全程序化
            for (int s = 0; s < 2; s++)
            {
                float sign = (s == 0) ? -1f : 1f;
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "WallSeg";
                seg.transform.SetParent(wallParent.transform);
                seg.transform.localPosition = new Vector3(sign * segCenter, wallHeight * 0.5f, 0);
                seg.transform.localScale = new Vector3(segLen, wallHeight, wallThickness);
                seg.tag = "Wall";
                NavMeshObstacle obs = seg.AddComponent<NavMeshObstacle>();
                obs.carving = true;
                obs.size = new Vector3(segLen, wallHeight, wallThickness);
                obs.center = Vector3.zero;
            }
        }

        GameObject blocker = new GameObject("DoorBlocker");
        blocker.transform.SetParent(wallParent.transform);
        blocker.transform.localPosition = new Vector3(0, wallHeight * 0.5f, 0);
        var bc = blocker.AddComponent<BoxCollider>();
        bc.size = new Vector3(doorWidth, wallHeight, wallThickness);
        bc.isTrigger = false;
        doorBlockers.Add(blocker);
    }

    void SetDoorBlocker(int dirIndex, bool block)
    {
        if (dirIndex < 0 || dirIndex >= doorBlockers.Count) return;
        GameObject b = doorBlockers[dirIndex];
        if (b == null) return;

        // 物理阻挡：关门时启用碰撞体，开门时关闭（这样玩家/敌人能穿过去）
        var col = b.GetComponent<Collider>();
        if (col != null) col.enabled = block;

        // 视觉：优先用 DoorBlocker（及其子物体）上的 Animator 播放开/关动画；
        // 支持双开门——两侧门扇各自挂 Animator 也能同时驱动；
        // 没有任何 Animator 才退回原来的显隐方式（用于程序化兜底方块）
        var anims = b.GetComponentsInChildren<Animator>();
        if (anims != null && anims.Length > 0)
        {
            bool matchedAny = false;
            foreach (var anim in anims)
            {
                foreach (var p in anim.parameters)
                {
                    if (p.name != doorOpenAnimParam) continue;
                    matchedAny = true;
                    if (p.type == AnimatorControllerParameterType.Bool)
                        anim.SetBool(doorOpenAnimParam, !block);          // !block：开门=true
                    else if (p.type == AnimatorControllerParameterType.Trigger && !block)
                        anim.SetTrigger(doorOpenAnimParam);               // 仅开门时触发
                    break;
                }
            }
            if (!matchedAny) b.SetActive(block);
        }
        else
        {
            b.SetActive(block);
        }
    }

    // ============================================================
    //  装饰随机摆放（呼应敌人类型）
    // ============================================================
    void PlaceDecorations(Transform parent)
    {
        List<GameObject> pool = GetDecorationPool(currentType);
        if (pool.Count == 0) return;

        int count = Random.Range(decorationMin, decorationMax + 1);
        float half = roomSize * 0.5f;
        float limit = half - wallThickness; // 仅避开墙体；中心留白由下面的 magnitude 判断处理

        int placed = 0;
        int tries = 0;
        while (placed < count && tries < count * 8)
        {
            tries++;
            GameObject prefab = pool[Random.Range(0, pool.Count)];
            if (prefab == null) continue;

            float x = Random.Range(-limit, limit);
            float z = Random.Range(-limit, limit);
            Vector3 pos = new Vector3(x, 0, z);
            if (pos.magnitude < decorationClearRadius) continue;

            bool nearDoor = false;
            for (int d = 0; d < 4; d++)
            {
                Vector3 doorLocal = DirVec[d] * half;
                if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(doorLocal.x, 0, doorLocal.z)) < doorWidth * 0.6f)
                { nearDoor = true; break; }
            }
            if (nearDoor) continue;

            GameObject deco = Instantiate(prefab, parent);
            deco.transform.localPosition = pos;
            deco.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            if (deco.GetComponent<Collider>() == null) deco.AddComponent<BoxCollider>();
            NavMeshObstacle obs = deco.GetComponent<NavMeshObstacle>();
            if (obs == null) obs = deco.AddComponent<NavMeshObstacle>();
            obs.carving = true;

            placed++;
        }
        if (showDebugLogs) Debug.Log("[Dungeon] 摆放装饰 " + placed + " 个");
    }

    List<GameObject> GetDecorationPool(DungeonRoomType type)
    {
        List<GameObject> pool = new List<GameObject>();
        if (type == DungeonRoomType.Shop) return pool; // 商店房间不放装饰

        if (type == DungeonRoomType.Snake)
        {
            pool.AddRange(snakeDecorations);    // 仅蛇房出现的特定装饰
            pool.AddRange(neutralDecorations);  // 其余随机布置
        }
        else if (type == DungeonRoomType.Zombie)
        {
            pool.AddRange(zombieDecorations);   // 仅僵尸房出现的特定装饰
            pool.AddRange(neutralDecorations);
        }
        else if (type == DungeonRoomType.Mixed)
        {
            pool.AddRange(snakeDecorations);
            pool.AddRange(zombieDecorations);
            pool.AddRange(neutralDecorations);  // 混合房间：全部随机混合
        }
        else
        {
            pool.AddRange(neutralDecorations);
        }
        return pool;
    }

    void BuildNavMesh()
    {
        if (navMeshSurface == null) EnsureNavMeshSurface();
        if (navMeshSurface == null || roomRoot == null) return;
        // 只烘焙当前房间子树，避免敌人走到旧世界的大地面上
        navMeshSurface.collectObjects = CollectObjects.Children;
        navMeshSurface.transform.SetParent(roomRoot.transform, false);
        if (showDebugLogs) Debug.Log("[Dungeon] 烘焙 NavMesh…");
        navMeshSurface.BuildNavMesh();
    }

    // ============================================================
    //  敌人生成（按房间序号缩放）
    // ============================================================
    void ComputeScaling(int room, out float speed, out float health, out float damage)
    {
        if (difficulty != null)
        {
            speed = difficulty.enemySpeedMultiplier * Mathf.Min(1f + (room - 1) * difficulty.speedMultiplierStep, difficulty.speedMultiplierMax);
            health = difficulty.enemyHealthMultiplier * Mathf.Min(1f + (room - 1) * difficulty.healthMultiplierStep, difficulty.healthMultiplierMax);
            damage = difficulty.enemyDamageMultiplier * Mathf.Min(1f + (room - 1) * difficulty.damageMultiplierStep, difficulty.damageMultiplierMax);
        }
        else
        {
            speed = Mathf.Min(1f + (room - 1) * 0.03f, 2.5f);
            health = Mathf.Min(1f + (room - 1) * 0.04f, 3f);
            damage = Mathf.Min(1f + (room - 1) * 0.02f, 2f);
        }
    }

    void SpawnEnemiesForRoom()
    {
        StartCoroutine(SpawnRoomEnemies());
    }

    IEnumerator SpawnRoomEnemies()
    {
        spawningDone = false;
        spawnedCount = 0;

        ComputeScaling(roomIndex, out float speed, out float health, out float damage);

        List<GameObject> prefabs = new List<GameObject>();
        if (currentType == DungeonRoomType.Snake) prefabs.AddRange(snakeEnemyPrefabs);
        else if (currentType == DungeonRoomType.Zombie) prefabs.AddRange(zombieEnemyPrefabs);
        else { prefabs.AddRange(snakeEnemyPrefabs); prefabs.AddRange(zombieEnemyPrefabs); }

        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[Dungeon] 没有可用敌人预制体！");
            spawningDone = true;
            yield break;
        }

        float half = roomSize * 0.5f;
        float inset = Mathf.Max(1f, half - 1f);
        Vector3 center = roomRoot.transform.position;
        Vector3[] corners = new Vector3[]
        {
            center + new Vector3(inset, 0, inset),
            center + new Vector3(-inset, 0, inset),
            center + new Vector3(inset, 0, -inset),
            center + new Vector3(-inset, 0, -inset),
        };

        while (spawnedCount < roomEnemyCap)
        {
            yield return new WaitForSeconds(spawnInterval);
            if (playerTarget == null) break;

            GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
            if (prefab == null) continue;

            Vector3 corner = corners[Random.Range(0, 4)] + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
            NavMeshHit hit;
            Vector3 pos = corner;
            if (NavMesh.SamplePosition(corner, out hit, 3f, NavMesh.AllAreas)) pos = hit.position;

            GameObject enemy = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0, 360), 0));
            enemy.transform.parent = null;

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(pos);

            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai != null) ai.ApplyScalingMultipliers(speed, health, damage);

            aliveEnemies.Add(enemy);
            spawnedCount++;
        }

        spawningDone = true;
        if (showDebugLogs) Debug.Log("[Dungeon] 房间敌人生成完毕，共 " + spawnedCount + " 只");
    }



    // ============================================================
    //  商店房间：用金币购买 Buff
    // ============================================================
    List<BuffDataSO> ResolveShopBuffs()
    {
        if (shopBuffs != null && shopBuffs.Count > 0)
            return shopBuffs;
        // 兜底：尝试从 Resources 加载（若您把 BuffDataSO 放进了 Resources 文件夹）
        BuffDataSO[] found = Resources.LoadAll<BuffDataSO>("");
        if (found != null && found.Length > 0)
            return new List<BuffDataSO>(found);
        Debug.LogWarning("[Dungeon] 未配置商店 Buff，请在 DungeonManager 的 shopBuffs 中拖入 Heal/PowerUp/SpeedUp 资产");
        return new List<BuffDataSO>();
    }

    void SpawnShop()
    {
        List<BuffDataSO> buffs = ResolveShopBuffs();
        if (buffs.Count == 0) return;

        // 整间商店房只生成一个交互点：玩家靠近后点击，弹出随机 3 个 Buff 供选择购买
        Vector3 pos = roomRoot.transform.position + new Vector3(0, 1f, 0);
        GameObject shop = new GameObject("Shop");
        shop.transform.position = pos;
        shop.AddComponent<ShopItem>().Setup(buffs, this, Mathf.Max(5, 10 + roomIndex));
        if (showDebugLogs) Debug.Log("[Dungeon] 商店已生成（靠近后点击打开，随机提供 3 个 Buff）");
    }

    // ============================================================
    //  主循环：检测清空 / 出口
    // ============================================================
    void Update()
    {
        if (advancing || playerTarget == null) return;

        // 清理已死亡敌人
        aliveEnemies.RemoveAll(e => e == null);

        if (currentType != DungeonRoomType.Shop && !roomCleared)
        {
            if (spawningDone && aliveEnemies.Count == 0)
            {
                OnRoomCleared();
            }
        }

        if (roomCleared || currentType == DungeonRoomType.Shop)
        {
            CheckExit();
        }
    }

    void OnRoomCleared()
    {
        roomCleared = true;

        // 开启除入口方向以外的所有出口（入口门在玩家背后，保持关闭）
        // 这样保证每个房间至少有 3 个门可走，且不会出现“背后就是出口”的情况
        for (int i = 0; i < 4; i++)
        {
            if (i == entryDir) continue;
            doorOpen[i] = true;
            SetDoorBlocker(i, false);
        }

        if (uiManager != null) uiManager.ShowBuffToast("房间已清空！走向任意发光的出口前往下一间");
        if (showDebugLogs) Debug.Log("[Dungeon] 房间 #" + roomIndex + " 已清空，开启出口（不含入口方向 " + entryDir + "）");
    }

    void CheckExit()
    {
        Vector3 local = playerTarget.position - roomRoot.transform.position;
        float half = roomSize * 0.5f;

        for (int d = 0; d < 4; d++)
        {
            if (!doorOpen[d]) continue;
            float along = Vector3.Dot(local, DirVec[d]);
            if (along <= half - 1.0f) continue; // 还未走到门口
            Vector3 perp = local - DirVec[d] * along;
            if (perp.magnitude > doorWidth * 0.6f) continue; // 没对准门洞
            Advance(d);
            return;
        }
    }

    void Advance(int dir)
    {
        if (advancing) return;
        if (showDebugLogs) Debug.Log("[Dungeon] 玩家从 " + dir + " 方向前往新房间");
        Vector3 newCenter = roomRoot.transform.position + DirVec[dir] * (roomSize + 2f);
        int entryDir = (dir + 2) % 4; // 从对面墙进入新房间
        StartCoroutine(GenerateRoomRoutine(newCenter, entryDir));
    }

    public int GetRoomIndex() => roomIndex;

    void OnDestroy()
    {
        if (roomRoot != null) Destroy(roomRoot);
    }
}

