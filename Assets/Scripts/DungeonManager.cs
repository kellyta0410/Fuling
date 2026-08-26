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
    [Tooltip("勾选：roomSize 自动取地板预制体的实测尺寸，无需手填；取消勾选则用上面的 roomSize 值")]
    public bool autoRoomSizeFromFloor = true;
    public float wallHeight = 4f;
    public float wallThickness = 1f;
    public float doorWidth = 3f;

    [Header("门动画参数名（需与门 Animator 的 Bool/Trigger 参数一致）")]
    [SerializeField] private string doorOpenAnimParam = "DoorOpen";

    [Header("敌人生成")]
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

    [Header("墙壁装饰（所有战斗房通用；随机上某面墙，正面朝向房间内；商店房不放）")]
    public int wallDecorationMin = 3;
    public int wallDecorationMax = 5;
    public float wallDecorationHeight = 2f;     // 离地高度
    public float wallDecorationInset = 0.25f;   // 距墙面往房内退一点，避免嵌进墙里
    public List<GameObject> wallDecorations = new List<GameObject>();

    [Header("房间中央顶灯（每间房生成一个，拖入你的 Point Light 预制体；Mode 设 Realtime）")]
    public GameObject roomLightPrefab;
    public float roomLightHeightOffset = 1f;   // 在 wallHeight 之上再抬一点

    [Header("商店 Buff（拖入 BuffDataSO 资产；留空自动在 Resources 找）")]
    public List<BuffDataSO> shopBuffs = new List<BuffDataSO>();

    [Header("玩家参考")]
    public Transform playerTarget;

    [Header("调试")]
    public bool showDebugLogs = true;

    [Header("门识别：在带门墙预制体的门子物体上挂 DoorMarker 组件即可（运行时自动查找）")]

    [Header("蛇房间（墙 + 地板）")]
    public GameObject snakeWallPrefab;
    public GameObject snakeWallWithDoorPrefab;
    public GameObject snakeFloorPrefab;

    [Header("僵尸房间（墙 + 地板）")]
    public GameObject zombieWallPrefab;
    public GameObject zombieWallWithDoorPrefab;
    public GameObject zombieFloorPrefab;

    [Header("混合房间（只用地板；墙随机取蛇房或僵尸房）")]
    public GameObject mixedFloorPrefab;

    [Header("商店房间（墙 + 地板）")]
    public GameObject shopWallPrefab;
    public GameObject shopWallWithDoorPrefab;
    public GameObject shopFloorPrefab;

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
    public DungeonDifficulty difficulty = new DungeonDifficulty();   // 地牢专用难度，直接在 Inspector 配（与普通/Buff 的 DifficultySettings 解耦）
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
        // 难度直接读取 Inspector 上挂的 DungeonDifficulty 资产（与 GameManager 的普通/Buff 难度解耦）

        ResolveEnemyPrefabs();
        EnsureNavMeshSurface();

        if (showDebugLogs) Debug.Log("[Dungeon] 地牢系统启动，生成第 1 个房间");
        StartCoroutine(GenerateRoomRoutine(Vector3.zero, -1));
    }

    void ResolveEnemyPrefabs()
    {
        snakeEnemyPrefabs.Clear();
        zombieEnemyPrefabs.Clear();

        if (difficulty != null)
        {
            if (difficulty.snakeEnemyPrefabs != null) snakeEnemyPrefabs.AddRange(difficulty.snakeEnemyPrefabs);
            if (difficulty.zombieEnemyPrefabs != null) zombieEnemyPrefabs.AddRange(difficulty.zombieEnemyPrefabs);
        }

        // 若 Inspector 没填任何敌人，回落到普通模式 EnemySpawner 的池子，保证不会空场
        if (snakeEnemyPrefabs.Count == 0 && zombieEnemyPrefabs.Count == 0 && GameManager.Instance != null
            && GameManager.Instance.normalSpawner != null && GameManager.Instance.normalSpawner.enemyPrefabs != null)
        {
            var inf = GameManager.Instance.normalSpawner.enemyPrefabs;
            foreach (var prefab in inf)
            {
                if (prefab == null) continue;
                string n = prefab.name.ToLower();
                if (n.Contains("snake")) snakeEnemyPrefabs.Add(prefab);
                else if (n.Contains("jiangshi") || n.Contains("zombie")) zombieEnemyPrefabs.Add(prefab);
                else
                {
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
        PlaceWallDecorations(roomRoot.transform);
        PlaceRoomLight(roomRoot.transform);

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

    GameObject GetFloorPrefab(DungeonRoomType type)
    {
        if (type == DungeonRoomType.Snake) return snakeFloorPrefab;
        if (type == DungeonRoomType.Zombie) return zombieFloorPrefab;
        if (type == DungeonRoomType.Mixed) return mixedFloorPrefab;
        if (type == DungeonRoomType.Shop) return shopFloorPrefab;
        return null;
    }

    void BuildFloor(Transform parent)
    {
        GameObject floorPrefabToUse = GetFloorPrefab(currentType);

        GameObject floor;
        if (floorPrefabToUse != null)
        {
            floor = Instantiate(floorPrefabToUse, parent);
            floor.name = "Floor";
            floor.transform.localPosition = Vector3.zero;
            // 不强制改旋转，保留预制体自身的朝向（你说 floor prefab 方向已正确）

            // 勾选时：用地板预制体实测尺寸推导 roomSize，墙面/门据此摆放，无需手填 roomSize
            if (autoRoomSizeFromFloor)
            {
                var rend = floor.GetComponent<Renderer>();
                if (rend != null) roomSize = Mathf.Max(rend.bounds.size.x, rend.bounds.size.z);
            }
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

        // 按房间类型直接选各自的墙/门预制体
        GameObject wwd = null;
        GameObject w = null;
        if (currentType == DungeonRoomType.Snake) { wwd = snakeWallWithDoorPrefab; w = snakeWallPrefab; }
        else if (currentType == DungeonRoomType.Zombie) { wwd = zombieWallWithDoorPrefab; w = zombieWallPrefab; }
        else if (currentType == DungeonRoomType.Mixed)
        {
            // 混合房墙随机取蛇房或僵尸房的墙（混合房自己只配地板）
            if (Random.value < 0.5f) { wwd = snakeWallWithDoorPrefab; w = snakeWallPrefab; }
            else { wwd = zombieWallWithDoorPrefab; w = zombieWallPrefab; }
        }
        else if (currentType == DungeonRoomType.Shop) { wwd = shopWallWithDoorPrefab; w = shopWallPrefab; }

        // 兜底：该类型未配墙时，借用蛇房墙，避免空墙
        if (wwd == null) wwd = snakeWallWithDoorPrefab;
        if (w == null) w = snakeWallPrefab;

        // 优先：使用“带门墙”预制体
        if (wwd != null)
        {
            GameObject wall = Instantiate(wwd, wallParent.transform);
            wall.name = "WallMesh";
            // 用预制体里自带的“门”物体（带 DoorMarker 组件、自带 BoxCollider）来开关；不再单独造 DoorBlocker。
            // 关门 = 碰撞体在门洞处挡玩家；清场开门 = 播动画把门移开门洞，碰撞体随之离开，玩家即可穿过。
            DoorMarker dm = wall.GetComponentInChildren<DoorMarker>();
            GameObject doorObj = dm != null ? dm.gameObject : null;
            if (doorObj == null)
            {
                // 兜底：预制体里没挂 DoorMarker 时，才补一个方块碰撞体挡住（正常不应走到这）
                doorObj = new GameObject("DoorColliderFallback");
                doorObj.transform.SetParent(wallParent.transform);
                doorObj.transform.localPosition = new Vector3(0, wallHeight * 0.5f, 0);
                var bcFallback = doorObj.AddComponent<BoxCollider>();
                bcFallback.size = new Vector3(doorWidth, wallHeight, wallThickness);
                bcFallback.isTrigger = false;
                if (showDebugLogs) Debug.LogWarning("[Dungeon] 带门墙预制体未找到 DoorMarker，已用兜底方块碰撞体（请在门的子物体上挂 DoorMarker）");
            }
            doorBlockers.Add(doorObj);
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

        // 优先用开门/关门动画：门在开启时会被动画移开门洞，其 BoxCollider 随之离开门洞，
        // 因此无需手动开关碰撞体（关门=碰撞体在门洞处挡人；开门=碰撞体已不在门洞处）。
        var anims = b.GetComponentsInChildren<Animator>();
        bool hasAnim = false;
        if (anims != null)
        {
            foreach (var anim in anims)
            {
                foreach (var p in anim.parameters)
                {
                    if (p.name != doorOpenAnimParam) continue;
                    hasAnim = true;
                    if (p.type == AnimatorControllerParameterType.Bool)
                        anim.SetBool(doorOpenAnimParam, !block);          // block=true 关门, false 开门
                    else if (p.type == AnimatorControllerParameterType.Trigger && !block)
                        anim.SetTrigger(doorOpenAnimParam);               // 触发器只在开门时触发
                    break;
                }
            }
        }

        // 无动画的兜底方块：只能用碰撞体的启用/停用来实现挡人 / 放行
        if (!hasAnim)
        {
            var col = b.GetComponentInChildren<Collider>();
            if (col != null) col.enabled = block;
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
        int maxTries = count * 40;   // 提高重试上限，保底能摆到 decorationMin 个
        while (placed < count && tries < maxTries)
        {
            tries++;
            GameObject prefab = pool[Random.Range(0, pool.Count)];
            if (prefab == null) continue;

            float x = Random.Range(-limit, limit);
            float z = Random.Range(-limit, limit);
            Vector3 pos = new Vector3(x, 0, z);
            // 前 70% 重试严格避让中心；后期放宽，确保数量达标
            if (pos.magnitude < decorationClearRadius && tries < maxTries * 0.7f) continue;

            bool nearDoor = false;
            for (int d = 0; d < 4; d++)
            {
                Vector3 doorLocal = DirVec[d] * half;
                if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(doorLocal.x, 0, doorLocal.z)) < doorWidth * 0.6f)
                { nearDoor = true; break; }
            }
            if (nearDoor && tries < maxTries * 0.7f) continue;

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

    List<GameObject> GetWallDecorationPool(DungeonRoomType type)
    {
        List<GameObject> pool = new List<GameObject>();
        if (type == DungeonRoomType.Shop) return pool; // 商店房不放墙壁装饰
        pool.AddRange(wallDecorations);                // 所有战斗房通用同一批墙壁装饰
        return pool;
    }

    void PlaceWallDecorations(Transform parent)
    {
        List<GameObject> pool = GetWallDecorationPool(currentType);
        if (pool.Count == 0) return;

        int count = Random.Range(wallDecorationMin, wallDecorationMax + 1);
        float half = roomSize * 0.5f;
        float limit = half - wallThickness - 0.2f;
        float off = half - wallThickness - wallDecorationInset; // 距房间中心到墙面的内退位置

        int placed = 0, tries = 0;
        int maxTries = count * 40;
        while (placed < count && tries < maxTries)
        {
            tries++;
            GameObject prefab = pool[Random.Range(0, pool.Count)];
            if (prefab == null) continue;

            int d = Random.Range(0, 4);                 // 随机一面墙
            float along = Random.Range(-limit, limit); // 沿墙随机位置
            Vector3 pos;
            if (d == 0)      pos = new Vector3(along, wallDecorationHeight, off);
            else if (d == 1) pos = new Vector3(off, wallDecorationHeight, along);
            else if (d == 2) pos = new Vector3(along, wallDecorationHeight, -off);
            else             pos = new Vector3(-off, wallDecorationHeight, along);

            // 确定方向：物体正面(+Z)朝向房间内（即 -DirVec[d]），保证上墙后不朝墙
            Quaternion rot = Quaternion.LookRotation(-DirVec[d]);

            GameObject deco = Instantiate(prefab, parent);
            deco.transform.localPosition = pos;
            deco.transform.localRotation = rot;
            if (deco.GetComponent<Collider>() == null) deco.AddComponent<BoxCollider>();
            placed++;
        }
        if (showDebugLogs) Debug.Log("[Dungeon] 摆放墙壁装饰 " + placed + " 个");
    }

    void PlaceRoomLight(Transform parent)
    {
        if (roomLightPrefab == null) return;
        GameObject lightObj = Instantiate(roomLightPrefab, parent);
        lightObj.name = "RoomLight";
        // 房间正中、略高于墙顶，向下照亮全房；随 roomRoot 销毁而一起销毁
        lightObj.transform.localPosition = new Vector3(0, wallHeight + roomLightHeightOffset, 0);
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

        int cap = difficulty.maxEnemiesPerRoom;

        ComputeScaling(roomIndex, out float speed, out float health, out float damage);

        // 每种房间固定池：蛇房全蛇、僵尸房全僵尸、混合房各一半（总数 = cap）
        int snakeCount = 0, zombieCount = 0;
        if (currentType == DungeonRoomType.Snake) snakeCount = cap;
        else if (currentType == DungeonRoomType.Zombie) zombieCount = cap;
        else { snakeCount = Mathf.FloorToInt(cap / 2); zombieCount = cap - snakeCount; }

        // 某类池为空时，把它的份额并入另一侧
        if (snakeEnemyPrefabs == null || snakeEnemyPrefabs.Count == 0) { zombieCount += snakeCount; snakeCount = 0; }
        if (zombieEnemyPrefabs == null || zombieEnemyPrefabs.Count == 0) { snakeCount += zombieCount; zombieCount = 0; }

        if (snakeCount == 0 && zombieCount == 0)
        {
            Debug.LogWarning("[Dungeon] 没有可用敌人预制体！");
            spawningDone = true;
            yield break;
        }

        // 生成顺序列表（混合房洗牌交错，避免一侧先刷完）
        List<GameObject> order = new List<GameObject>(cap);
        for (int i = 0; i < snakeCount; i++) order.Add(snakeEnemyPrefabs[Random.Range(0, snakeEnemyPrefabs.Count)]);
        for (int i = 0; i < zombieCount; i++) order.Add(zombieEnemyPrefabs[Random.Range(0, zombieEnemyPrefabs.Count)]);
        if (currentType == DungeonRoomType.Mixed) Shuffle(order);

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

        int idx = 0;
        while (idx < order.Count)
        {
            yield return new WaitForSeconds(spawnInterval);
            if (playerTarget == null) break;

            GameObject prefab = order[idx];
            if (prefab == null) { idx++; continue; }

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
            idx++;
        }

        spawningDone = true;
        if (showDebugLogs) Debug.Log("[Dungeon] 房间敌人生成完毕，共 " + spawnedCount + " 只");
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
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

