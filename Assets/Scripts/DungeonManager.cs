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

    [Header("进房相机就位等待（左右门进入时，等相机转到玩家背后再刷怪）")]
    public float camAlignMaxWait = 1.2f;   // 最长等待秒数：相机不跟随朝向时也只会卡这么久
    public float camAlignAngle = 12f;      // 相机前向与“看向房间内”夹角小于该值即认为就位

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

    [Header("外观：外面变黑，看不到房间外的 void")]
    public bool blackOutside = true;           // 把主相机背景设为纯黑，门外/墙外的虚空不可见

    [Header("商店 Buff（拖入 BuffDataSO 资产；留空自动在 Resources 找）")]
    public List<BuffDataSO> shopBuffs = new List<BuffDataSO>();

    [Header("玩家参考")]
    public Transform playerTarget;

    [Header("调试")]
    public bool showDebugLogs = true;

    [Header("门识别：在带门墙预制体的门子物体上挂 DoorMarker 组件即可（运行时自动查找）")]

    [Header("蛇房间（墙 + 地板）")]
    public GameObject snakeWallWithDoorPrefab;
    public GameObject snakeFloorPrefab;

    [Header("僵尸房间（墙 + 地板）")]
    public GameObject zombieWallWithDoorPrefab;
    public GameObject zombieFloorPrefab;

    [Header("混合房间（只用地板；墙随机取蛇房或僵尸房）")]
    public GameObject mixedFloorPrefab;

    [Header("商店房间（墙 + 地板）")]
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

        // X-Ray：把“相机与玩家之间”的墙变半透明（只影响挡在相机前的那面墙，不透明其余墙）。
        // 墙壁已在 BuildWallWithDoor 里打上 "Wall" 标签，配合 Custom/XRayWall 着色器生效。
        if (Camera.main != null && Camera.main.GetComponent<CinemachineWallXRay>() == null)
        {
            var xray = Camera.main.gameObject.AddComponent<CinemachineWallXRay>();
            xray.player = playerTarget;
        }
        // 难度直接读取 Inspector 上挂的 DungeonDifficulty 资产（与 GameManager 的普通/Buff 难度解耦）

        ResolveEnemyPrefabs();
        EnsureNavMeshSurface();

        // 外面变黑：主相机背景设纯黑，房间外的 void 不可见（仍看得到房间内部与灯光）
        if (blackOutside && Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.black;
        }

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

        // 先在本帧内把玩家传送进新房间的地面安全点，再 yield。
        // 否则“旧房已销毁、玩家还站在门口（新房边缘）”的那几帧里可能悬空/掉落。
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

        yield return null;
        BuildNavMesh();
        yield return null;

        if (uiManager != null) uiManager.SetRoomDisplay(roomIndex);
        if (GameManager.Instance != null) GameManager.Instance.SetDungeonRoom(roomIndex);

        if (showDebugLogs) Debug.Log("[Dungeon] 生成房间 #" + roomIndex + " 类型=" + currentType + " 中心=" + center);

        // 左右门进入时，先等相机转到玩家背面再开始刷怪/商店；正前/正后进入则直接进入。
        BeginRoomContent(entryDir);

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

    // 进入房间后启动内容（刷怪/商店）。左右门(entryDir 为 1=东 / 3=西)进入时，
    // 先把玩家转向房间内并等待相机转到玩家背面，避免一进门镜头还背对就开始战斗。
    void BeginRoomContent(int entryDir)
    {
        if (entryDir == 1 || entryDir == 3)
            StartCoroutine(SpawnAfterCameraAligned(entryDir));
        else
            StartRoomContent();
    }

    void StartRoomContent()
    {
        if (currentType == DungeonRoomType.Shop)
            SpawnShop();
        else
            SpawnEnemiesForRoom();
    }

    IEnumerator SpawnAfterCameraAligned(int entryDir)
    {
        // 玩家应面向房间内（-DirVec[entryDir]），相机随之转到玩家背面
        Vector3 intoRoom = -DirVec[entryDir];
        intoRoom.y = 0;
        if (intoRoom.sqrMagnitude > 1e-6f && playerTarget != null)
            playerTarget.rotation = Quaternion.LookRotation(intoRoom);

        float timer = 0f;
        bool aligned = false;
        while (timer < camAlignMaxWait)
        {
            timer += Time.deltaTime;
            if (Camera.main != null)
            {
                Vector3 fwd = Camera.main.transform.forward;
                fwd.y = 0;
                if (fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, intoRoom) < camAlignAngle)
                {
                    aligned = true;
                    break;
                }
            }
            yield return null;
        }
        if (showDebugLogs) Debug.Log("[Dungeon] 相机就位（" + (aligned ? "已对齐" : "超时") + "），开始房间内容");
        StartRoomContent();
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
                var rends = floor.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    Bounds fb = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) fb.Encapsulate(rends[i].bounds);
                    roomSize = Mathf.Max(fb.size.x, fb.size.z);
                }
            }
            // 把地板几何 x/z 居中，并让“顶面”落在房间地面高度(parent.position.y)，
            // 否则带厚度的地板会把墙/玩家/敌人埋进 slab 里（直接进到地板）
            {
                var rends = floor.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    Bounds fb = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) fb.Encapsulate(rends[i].bounds);
                    floor.transform.localPosition = new Vector3(
                        parent.position.x - fb.center.x,
                        parent.position.y - fb.center.y - fb.size.y * 0.5f,
                        parent.position.z - fb.center.z);
                }
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
        GameObject wallParent = new GameObject("Wall_" + dirIndex);
        wallParent.transform.SetParent(parent);
        wallParent.transform.localPosition = localPos;
        wallParent.transform.localRotation = localRot;

        // 按房间类型直接选各自的带门墙预制体（墙/门几何全部来自预制体）
        GameObject wwd = null;
        if (currentType == DungeonRoomType.Snake) wwd = snakeWallWithDoorPrefab;
        else if (currentType == DungeonRoomType.Zombie) wwd = zombieWallWithDoorPrefab;
        else if (currentType == DungeonRoomType.Mixed)
        {
            // 混合房墙随机取蛇房或僵尸房的带门墙（混合房自己只配地板）
            wwd = (Random.value < 0.5f) ? snakeWallWithDoorPrefab : zombieWallWithDoorPrefab;
        }
        else if (currentType == DungeonRoomType.Shop) wwd = shopWallWithDoorPrefab;

        // 兜底：该类型未配带门墙时，借用蛇房墙，避免空墙
        if (wwd == null) wwd = snakeWallWithDoorPrefab;

        // 优先：使用“带门墙”预制体
        if (wwd != null)
        {
            GameObject wall = Instantiate(wwd, wallParent.transform);
            wall.name = "WallMesh";
            // 给墙体所有带碰撞体的子物体打上 "Wall" 标签，供 CinemachineWallXRay
            // （相机与玩家之间的墙变透明）射线检测使用。
            foreach (var col in wall.GetComponentsInChildren<Collider>())
                col.gameObject.tag = "Wall";
            // 自动量墙高/墙厚：取带门墙预制体整体渲染包围盒（墙只绕 Y 旋转，Y 不受影响；
            // 墙宽沿墙长方向取 X/Z 较大者，墙厚取较小者），让顶灯高度、装饰内退都跟随预制体。
            Renderer[] wrs = wall.GetComponentsInChildren<Renderer>();
            float wallWidth = 0f;
            if (wrs.Length > 0)
            {
                Bounds rb = wrs[0].bounds;
                for (int i = 1; i < wrs.Length; i++) rb.Encapsulate(wrs[i].bounds);
                if (rb.size.y > 0.1f) wallHeight = rb.size.y;
                float wt = Mathf.Min(rb.size.x, rb.size.z);
                if (wt > 0.01f) wallThickness = wt;
                wallWidth = Mathf.Max(rb.size.x, rb.size.z);
            }
            // 让墙“贴”在地板边缘：把墙父物体从边线向内移半墙厚，使外墙正好落在 floor 边缘，
            // 而不是向外凸出半墙厚（之前墙几何中心被放在边线上，导致墙有一半悬在地板外）。
            {
                Vector3 outDir = localPos.normalized;
                if (outDir != Vector3.zero)
                    wallParent.transform.localPosition = localPos - outDir * (wallThickness * 0.5f);
            }
            // 墙体/门若缺 NavMeshObstacle 则按各自网格自动补（防止敌人穿墙/穿门寻路）
            bool addedObs = false;
            foreach (var r in wall.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponent<NavMeshObstacle>() != null) continue;
                var obs = r.gameObject.AddComponent<NavMeshObstacle>();
                obs.carving = true;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    obs.size = mf.sharedMesh.bounds.size;
                    obs.center = mf.sharedMesh.bounds.center;
                }
                else
                {
                    obs.size = r.bounds.size;
                    obs.center = r.transform.InverseTransformPoint(r.bounds.center);
                }
                addedObs = true;
            }
            if (addedObs && showDebugLogs) Debug.LogWarning("[Dungeon] 带门墙预制体部分网格缺少 NavMeshObstacle，已按网格自动补（建议预制体里给墙/门加 NavMeshObstacle）");
            // 用预制体里自带的“门”物体（带 DoorMarker 组件、自带 BoxCollider）来开关；不再单独造 DoorBlocker。
            // 关门 = 碰撞体在门洞处挡玩家；清场开门 = 播动画把门移开门洞，碰撞体随之离开，玩家即可穿过。
            DoorMarker dm = wall.GetComponentInChildren<DoorMarker>();
            // 自动量门洞宽度：门叶碰撞体在关门姿态下的包围盒跨度 ≈ 门洞宽，
            // 这样每间房的 doorWidth 跟着各自带门墙预制体走，无需手填（仅作兜底/装饰避让用）。
            if (dm != null)
            {
                var dcols = dm.GetComponentsInChildren<Collider>();
                if (dcols.Length > 0)
                {
                    Bounds db = dcols[0].bounds;
                    for (int i = 1; i < dcols.Length; i++) db.Encapsulate(dcols[i].bounds);
                    float measured = Mathf.Max(db.size.x, db.size.z); // 墙旋转0/90/180/270，取世界X或Z较大者
                    if (measured > 0.1f) doorWidth = measured;
                }
                // 门碰撞体必须是实心才能挡玩家：若全是 Trigger 自动改为实心；若一个都没有则补一个实心方块
                bool hasSolid = false;
                foreach (var c in dcols) if (!c.isTrigger) { hasSolid = true; break; }
                if (dcols.Length == 0)
                {
                    var bc = dm.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(doorWidth, wallHeight, wallThickness);
                    bc.isTrigger = false;
                    if (showDebugLogs) Debug.LogWarning("[Dungeon] 门未挂任何碰撞体，已自动补实心 BoxCollider（建议在门预制体上直接加）");
                }
                else if (!hasSolid)
                {
                    foreach (var c in dcols) c.isTrigger = false;
                    if (showDebugLogs) Debug.LogWarning("[Dungeon] 门的碰撞体是 Trigger，已自动改为实心(Is Trigger=false)以挡玩家；若需要触发检测请另加碰撞体");
                }
            }

            // 让墙严格对齐地板边缘：
            // 1) 统一缩放使墙“长度”= roomSize（轴无关，无论墙长沿本地 X 还是 Z 都能对齐，避免墙角对不上）；
            //    同时同步 wallHeight/wallThickness/doorWidth，保证灯光高度、装饰内退、门洞一致。
            // 2) 把墙几何中心重新对齐到边线中点（纠正预制体 pivot 不在几何中心导致的偏移）。
            // 若预制体长度与中心已正确，s≈1 且偏移≈0，无副作用。
            if (wallWidth > 0.001f)
            {
                float s = Mathf.Clamp(roomSize / wallWidth, 0.1f, 10f);
                if (Mathf.Abs(s - 1f) > 0.01f)
                {
                    wall.transform.localScale = new Vector3(s, s, s);
                    wallHeight *= s;
                    wallThickness *= s;
                    doorWidth *= s;
                    if (showDebugLogs) Debug.LogWarning("[Dungeon] 带门墙预制体长度(" + wallWidth.ToString("F2") + ")与房间尺寸(" + roomSize.ToString("F2") + ")不一致，已整体缩放对齐（建议把墙预制体长度直接做成房间尺寸）");
                }
            }
            // 重新对齐（pivot 无关）：水平把墙几何中心对到边线中点，垂直把墙“底面”落到地面高度
            // （prefab pivot 在几何中心时，几何中心会被放在地面高度导致半截埋进地板，这里纠正为底面贴地）
            {
                var rs2 = wall.GetComponentsInChildren<Renderer>();
                if (rs2.Length > 0)
                {
                    Bounds b2 = rs2[0].bounds;
                    for (int i = 1; i < rs2.Length; i++) b2.Encapsulate(rs2[i].bounds);
                    float groundY = wallParent.transform.position.y;
                    Vector3 desiredCenter = new Vector3(
                        wallParent.transform.position.x,
                        groundY + b2.size.y * 0.5f,
                        wallParent.transform.position.z);
                    wall.transform.position += (desiredCenter - b2.center);
                }
            }

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
                var noFallback = doorObj.AddComponent<NavMeshObstacle>();
                noFallback.carving = true;
                noFallback.size = new Vector3(doorWidth, wallHeight, wallThickness);
                noFallback.center = Vector3.zero;
                if (showDebugLogs) Debug.LogWarning("[Dungeon] 带门墙预制体未找到 DoorMarker，已用兜底方块碰撞体（请在门的子物体上挂 DoorMarker）");
            }
            doorBlockers.Add(doorObj);
            return;
        }


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

            // 把装饰底部贴到地面(parent.position.y)：兼容不同 pivot（中心/底部），避免悬空或下陷
            {
                Renderer r = deco.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    float groundY = parent.position.y;
                    float minY = r.bounds.min.y;
                    if (Mathf.Abs(minY - groundY) > 0.001f)
                        deco.transform.position += Vector3.up * (groundY - minY);
                }
            }

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

        int count = Mathf.Max(3, Random.Range(wallDecorationMin, wallDecorationMax + 1));
        float half = roomSize * 0.5f;
        float limit = half - wallThickness - 0.2f;
        float off = half - wallThickness - wallDecorationInset; // 距房间中心到墙面的内退位置

        // 每面墙都带门（门在墙正中 along≈0），避开门洞区域，避免装饰压在门前
        float doorClear = doorWidth * 0.5f + wallThickness + 0.4f;
        // 装饰中心目标高度：至少为墙高的 0.4 倍，避免贴地/过低
        float targetCenterY = parent.position.y + Mathf.Max(wallDecorationHeight, wallHeight * 0.4f);

        int placed = 0, tries = 0;
        int maxTries = count * 40;
        while (placed < count && tries < maxTries)
        {
            tries++;
            GameObject prefab = pool[Random.Range(0, pool.Count)];
            if (prefab == null) continue;

            int d = Random.Range(0, 4);                 // 随机一面墙
            float along = Random.Range(-limit, limit); // 沿墙随机位置
            if (Mathf.Abs(along) < doorClear) continue; // 避开门洞中心
            Vector3 pos;
            if (d == 0)      pos = new Vector3(along, 0, off);
            else if (d == 1) pos = new Vector3(off, 0, along);
            else if (d == 2) pos = new Vector3(along, 0, -off);
            else             pos = new Vector3(-off, 0, along);

            // 确定方向：物体正面(+Z)朝向房间内（即 -DirVec[d]），保证上墙后不朝墙
            Quaternion rot = Quaternion.LookRotation(-DirVec[d]);

            GameObject deco = Instantiate(prefab, parent);
            deco.transform.localPosition = pos;
            deco.transform.localRotation = rot;
            // 让装饰“垂直中心”落在目标高度（兼容不同 pivot，避免整体偏低）
            {
                Renderer r = deco.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    float dy = targetCenterY - r.bounds.center.y;
                    if (Mathf.Abs(dy) > 0.001f) deco.transform.position += Vector3.up * dy;
                }
            }
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
        navMeshSurface.enabled = true;
        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.layerMask = ~0;
        navMeshSurface.transform.SetParent(roomRoot.transform, false);
        navMeshSurface.RemoveData(); // 清掉上一间房残留的 NavMeshData，避免多房间 NavMesh 叠加导致寻路错乱
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
        Vector3 center = roomRoot.transform.position;

        int idx = 0;
        while (idx < order.Count)
        {
            yield return new WaitForSeconds(spawnInterval);
            if (playerTarget == null) break;

            GameObject prefab = order[idx];
            if (prefab == null) { idx++; continue; }

            // 在房间内部、离墙留余量的随机点；并校验 NavMesh 可达、不在装饰/门内、且不过近玩家，确保生成在墙内
            Vector3 pos = GetDungeonSpawnPos(center, half);

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

    // 在房间内部、离墙留余量的随机点；校验 NavMesh 可达、不在装饰/门等 NavMeshObstacle 内、且不过近玩家
    Vector3 GetDungeonSpawnPos(Vector3 center, float half)
    {
        float margin = Mathf.Max(wallThickness, 0.5f) + 1.0f; // 墙厚 + 约一个敌人碰撞半径
        float limit = Mathf.Max(1f, half - margin);
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Vector3 cand = center + new Vector3(Random.Range(-limit, limit), 0f, Random.Range(-limit, limit));
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(cand, out hit, 2f, NavMesh.AllAreas)) continue;
            Vector3 pos = hit.position;
            if (IsInsideObstacle(pos)) continue;
            if (playerTarget != null)
            {
                Vector3 a = pos; a.y = 0f;
                Vector3 b = playerTarget.position; b.y = 0f;
                if (Vector3.Distance(a, b) < playerClearRadius) continue;
            }
            return pos;
        }
        return center; // 兜底：房间正中
    }

    // 该点是否落在某个实心 NavMeshObstacle（装饰物 carve 挖洞 / 门）的碰撞体内
    bool IsInsideObstacle(Vector3 pos)
    {
        Collider[] buf = new Collider[8];
        int n = Physics.OverlapSphereNonAlloc(pos, 0.3f, buf);
        for (int i = 0; i < n; i++)
        {
            if (buf[i] == null || buf[i].isTrigger) continue;
            if (buf[i].GetComponentInParent<NavMeshObstacle>() != null) return true;
        }
        return false;
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
        // 新房间与当前房间“相邻拼接”（中心相距正好 roomSize），两间房的边线重合，
        // 门口不再留 2m 空隙，玩家踏出门即落在下一间地板，不会掉进 void。
        Vector3 newCenter = roomRoot.transform.position + DirVec[dir] * roomSize;
        int entryDir = (dir + 2) % 4; // 从对面墙进入新房间
        StartCoroutine(GenerateRoomRoutine(newCenter, entryDir));
    }

    public int GetRoomIndex() => roomIndex;

    void OnDestroy()
    {
        if (roomRoot != null) Destroy(roomRoot);
    }
}

