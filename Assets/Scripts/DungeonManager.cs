using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// =============================================================
//  Dungeon 房间制地牢系统
//  改造 Infinite 场景：玩家一次只在一个房间内，
//  清空敌人后才能前往下一个随机方向的房间，
    //  每 5 个战斗关之后插入一个商店房（物理房号 6/12/18…，商店不计入“关”序号），敌人随房间数递增变强。
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

    // 门开关完全由代码驱动（已移除 Animator 依赖，避免烘焙旋转在旋转后的父墙下摆错方向）

    [Header("敌人生成")]
    public float spawnInterval = 1f;    // 每隔多少秒生成一只（每秒一只）

    public float playerClearRadius = 4f;

    [Header("装饰随机摆放")]
    public int decorationMin = 3;
    public int decorationMax = 8;
    public float decorationClearRadius = 3f;
    public float decorationWallMargin = 0.8f;  // 装饰与墙体之间额外留边，避免插进墙里
    public float decorationDoorMargin = 1.2f;  // 装饰与门洞之间额外留边，避免卡住门/通行

    [Header("玩家进入房间的落点偏移（房间中心朝入口方向退后）")]
    public float entryInset = 4f;

    [Header("进房相机就位等待（左右门进入时，等相机转到玩家背后再刷怪）")]
    public float camAlignMaxWait = 1.2f;   // 最长等待秒数：相机不跟随朝向时也只会卡这么久
    public float camAlignAngle = 12f;      // 相机前向与“看向房间内”夹角小于该值即认为就位

    [Header("进房镜头接管（临时关掉 Cinemachine，把镜头转到玩家背后看向房内）")]
    public float entryCamDistance = 6f;    // 镜头退到玩家身后的距离
    public float entryCamHeight = 4f;      // 镜头高度
    public float entryCamTurnSpeed = 6f;   // 镜头转向插值速度（越大越快）

    [Header("装饰预制体（按敌人类型分类；留空则该类型用中性装饰）")]
    public List<GameObject> snakeDecorations = new List<GameObject>();
    public List<GameObject> zombieDecorations = new List<GameObject>();
    public List<GameObject> neutralDecorations = new List<GameObject>();

    [Header("墙壁装饰（所有战斗房通用；随机上某面墙，正面朝向房间内；商店房不放）")]
    public int wallDecorationMin = 3;
    public int wallDecorationMax = 5;
    public float wallDecorationHeight = 5f;     // 离地高度
    public float wallDecorationInset = 0.25f;   // 距墙面往房内退一点，避免嵌进墙里
    public List<GameObject> wallDecorations = new List<GameObject>();
    private HashSet<Light> decoLights = new HashSet<Light>();   // 墙饰自带的灯：只开关、亮度/范围/颜色保留预制体设定
    private List<Bounds> placedBounds = new List<Bounds>();    // 已摆放装饰的世界包围盒，墙壁装饰与地面装饰共用，防穿模
    private Material blackPillarMat = null;                    // 墙角柱共用的纯黑无贴图材质

    [Tooltip("周围暗房（邻居）放到的 Layer 索引；该层会被房间灯光排除，使邻居保持黑暗。请确保该 Layer 在 Layer 设置里存在（如 Layer 8）。当前活动房间仍用原层，不影响碰撞。")]
    public int darkLayer = 8;                 // 邻居暗房专用层：灯光不照这一层，故保持黑暗

    [Header("外观：外面变黑，看不到房间外的 void")]
    public bool blackOutside = true;           // 把主相机背景设为纯黑，门外/墙外的虚空不可见

    [Header("商店 Buff（拖入 BuffDataSO 资产；留空自动在 Resources 找）")]
    public List<BuffDataSO> shopBuffs = new List<BuffDataSO>();

    [Header("商店物体预制体（在预制体上挂 ShopItem 组件即可；留空则运行时自动生成一个简单 Shop 物体）")]
    public GameObject shopPrefab;
    [Header("商店按钮预制体（拖入你的按钮图片，需有 Button 组件）")]
    public GameObject shopButtonPrefab;
    [Header("商店按钮父物体（拖入你想放按钮的 Panel）")]
    public Transform shopButtonParent;

    [Header("玩家参考")]
    public Transform playerTarget;

    [Header("调试")]
    public bool showDebugLogs = true;

    [Header("门识别：在带门墙预制体的门子物体上挂 DoorMarker 组件即可（运行时自动查找）")]

    [Header("墙壁（所有房间统一用同一套）")]
    public GameObject wallWithDoorPrefab;
    public GameObject solidWallPrefab;

    [Header("地板（按房间类型区分）")]
    public GameObject snakeFloorPrefab;
    public GameObject zombieFloorPrefab;
    public GameObject mixedFloorPrefab;
    public GameObject shopFloorPrefab;

    // ============================================================
    //  房间实例（持久存在于房间图，不再随切换销毁）
    // ============================================================
    public class Room
    {
        public Vector2Int coord;
        public DungeonRoomType type;
        public GameObject root;
        public List<List<GameObject>> doorBlockers = new List<List<GameObject>>(); // 按方向索引，本房间“拥有”的门（共享墙所在方向为 null）
        public bool visited = false;
        public bool cleared = false;
        public bool spawningDone = false;
        public int spawnedCount = 0;
        public List<GameObject> aliveEnemies = new List<GameObject>();
        public int roomIndex;     // 物理序号（每建一间 +1）
        public int levelIndex;    // 显示关号（仅由 visitedRoomNumber 赋值，保证与进入顺序一致）
        public int entryDir = -1;
        public bool[] deadWallDirs = new bool[4]; // 本房间哪些方向是"死路"（实心墙、且不再向外生房）
        public GameObject shopObject; // 商店交互点
    }

    // ---------- 运行时状态（连通式持久房间图） ----------
    private int roomCounter = 0;          // 物理房间序号累加器（每建一间 +1）
    private int battleLevel = 0;          // 战斗关序号（商店房不计入，用于 UI 显示）
    private int visitedRoomNumber = 0;    // 玩家“第几次进入房间”的序号（用于决定第 5/10/15… 间为商店）
    private HashSet<Vector2Int> pendingShopCoords = new HashSet<Vector2Int>(); // 预标记为商店、待生成的房间坐标
    private DungeonRoomType currentType;  // 构建当前房间时临时使用（选地板/墙/装饰）
    private DungeonRoomType roomTheme;     // 混合房视觉主题：随机取蛇或僵尸，整间房（地板+墙）统一
    private Vector2Int currentCoord = Vector2Int.zero;
    private Room currentRoom = null;
    private Dictionary<Vector2Int, Room> rooms = new Dictionary<Vector2Int, Room>();
    private bool advancing = false;

    private Color _origAmbient = Color.white;
    private UnityEngine.Rendering.AmbientMode _origAmbientMode = UnityEngine.Rendering.AmbientMode.Flat;
    private Dictionary<Light, int> _dirLightMasks = new Dictionary<Light, int>(); // 记录方向光原始 cullingMask，退出时还原

    // 门开关状态按“门叶 GameObject”索引，持久房间不会在切换时清空
    private Dictionary<GameObject, float> swingAngleFor = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, Vector3> swingAxisFor = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Coroutine> doorSwingRoutine = new Dictionary<GameObject, Coroutine>();
    private Dictionary<GameObject, Quaternion> doorClosedRot = new Dictionary<GameObject, Quaternion>();
    [Tooltip("代码驱动开门：门绕本地 Y 轴旋转的角度")]
    public float doorSwingAngle = 90f;
    [Tooltip("门旋转绕的轴（门的本地空间）。普通竖直门绕 Y 轴；若模型朝向不同则设为 Z。")]
    public Vector3 doorSwingAxis = Vector3.forward;

    private List<GameObject> snakeEnemyPrefabs = new List<GameObject>();
    private List<GameObject> zombieEnemyPrefabs = new List<GameObject>();
    public DungeonDifficulty difficulty = new DungeonDifficulty();
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
        // 墙壁已在 BuildSolidWall 里打上 "Wall" 标签，配合 Custom/XRayWall 着色器生效。
        if (Camera.main != null && Camera.main.GetComponent<CinemachineWallXRay>() == null)
        {
            var xray = Camera.main.gameObject.AddComponent<CinemachineWallXRay>();
            xray.player = playerTarget;
            Debug.Log("[Dungeon] XRay added to Camera.main, player=" + (playerTarget != null ? playerTarget.name : "NULL"));
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

        // 遮盖隔壁暗房：不关闭任何灯光（保留其他 prefab 自带光），只把“邻居暗房层”从方向光照射范围里剔除，
        // 再配合环境光纯黑 + 房间灯光只照当前房(cullingMask 排除 darkLayer)，邻居即保持黑暗。
        _dirLightMasks.Clear();
        Light[] allLights = FindObjectsOfType<Light>();
        foreach (var l in allLights)
        {
            if (l.type == LightType.Directional)
            {
                _dirLightMasks[l] = l.cullingMask;
                l.cullingMask &= ~(1 << darkLayer); // 方向光不再照亮邻居暗房层
            }
        }
        _origAmbientMode = RenderSettings.ambientMode;
        _origAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.12f); // 浅灰，背光墙面有基本可见度

        // darkLayer 仅用于把邻居暗房排除出房间灯光；强制它与所有层碰撞，避免暗房碰撞失效
        if (darkLayer > 0)
            for (int L = 0; L < 32; L++)
                Physics.IgnoreLayerCollision(darkLayer, L, false);

        // 地牢专属 BGM：覆盖普通游戏内 BGM
        if (AudioManager.Instance != null) AudioManager.Instance.PlayDungeonBGM();

        if (showDebugLogs) Debug.Log("[Dungeon] 地牢系统启动，生成第 1 个房间（连通式，未开启房间保持黑暗）");
        Room start = BuildRoom(Vector2Int.zero, true);
        start.entryDir = -1; // 起始房无入口方向
        visitedRoomNumber = 1; // 起始房算作玩家第 1 次进入（战斗房），第 5/10/15… 次进入才是商店
        battleLevel = 1;
        start.levelIndex = battleLevel;
        currentRoom = start;
        currentCoord = Vector2Int.zero;
        BeginRoomContent(start, -1);
        ExpandRoom(start, new Vector2Int(int.MinValue, int.MinValue));
        BuildNavMesh();
        RefreshRoomUI(start);
        if (playerTarget != null)
        {
            Vector3 p = CoordToWorld(Vector2Int.zero) + Vector3.up * 0.5f;
            playerTarget.position = p;
            CharacterController cc = playerTarget.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }
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
    // ============================================================
    //  连通式房间图：房间持久存在，不销毁；相邻房共用一面墙（墙由先建的一方“拥有”）
    // ============================================================
    Room BuildRoom(Vector2Int coord, bool visited)
    {
        if (rooms.ContainsKey(coord)) return rooms[coord];

        Room room = new Room();
        room.coord = coord;
        roomCounter++;
        room.roomIndex = roomCounter;
        // 商店通常由 EnterRoom 在玩家第 4/9/14… 间时，把“下一间”周围坐标预标进 pendingShopCoords，
        // 这里命中则直接以商店主题（墙/地板/装饰）生成；否则一律按战斗房生成（避免提前标商店导致跳关）。
        bool makeShop = pendingShopCoords.Contains(coord);
        pendingShopCoords.Remove(coord);
        if (makeShop) room.type = DungeonRoomType.Shop;
        else room.type = (DungeonRoomType)Random.Range(0, 3);

        currentType = room.type;
        // 混合房随机取“蛇”或“僵尸”作为整间房的视觉主题（地板+墙统一），其余类型主题即自身
        roomTheme = (currentType == DungeonRoomType.Mixed)
            ? (Random.value < 0.5f ? DungeonRoomType.Snake : DungeonRoomType.Zombie)
            : currentType;
        GameObject root = new GameObject("Room_" + coord.x + "_" + coord.y);
        room.root = root;
        rooms[coord] = room;

        BuildFloor(root.transform);
        root.transform.position = CoordToWorld(coord);
        if (showDebugLogs) Debug.Log("[Dungeon] roomSize=" + roomSize.ToString("F2") + " 房间世界坐标=" + root.transform.position);
        BuildRoomWalls(room);
        placedBounds.Clear();
        PlaceDecorations(root.transform);
        PlaceWallDecorations(root.transform);
        PlaceRoomLight(root.transform);

        if (visited) { room.visited = true; SetRoomLightUp(room); }
        else { room.visited = false; SetRoomDark(room); }

        if (showDebugLogs) Debug.Log("[Dungeon] 生成房间 #" + room.roomIndex + " 类型=" + room.type + " 坐标=" + coord + (visited ? " (已点亮)" : " (黑暗)"));
        // 默认让本房“拥有”的门处于关闭（锁门）状态；只有清场(OnRoomCleared)或进商店时才开门。
        // 这样第 1 关（战斗房）一开始就是关门的，玩家清场后才解锁。
        CloseRoomDoors(room);
        return room;
    }

    // 仅在本房间“拥有”的边界建墙+门：邻居已存在的边界由邻居建过，这里跳过（两房之间只有一堵共用墙）。
    void BuildRoomWalls(Room room)
    {
        float half = roomSize * 0.5f;
        while (room.doorBlockers.Count < 4) room.doorBlockers.Add(null);
        bool builtAny = false;
        for (int d = 0; d < 4; d++)
        {
            Vector2Int nc = room.coord + DirVec2D(d);
            Vector3 localPos; Quaternion localRot;
            if (d == 0) { localPos = new Vector3(0, 0, half); localRot = Quaternion.identity; }
            else if (d == 1) { localPos = new Vector3(half, 0, 0); localRot = Quaternion.Euler(0, 90, 0); }
            else if (d == 2) { localPos = new Vector3(0, 0, -half); localRot = Quaternion.Euler(0, 180, 0); }
            else { localPos = new Vector3(-half, 0, 0); localRot = Quaternion.Euler(0, -90, 0); }

            Room nb;
            bool hasNeighbor = rooms.TryGetValue(nc, out nb) && nb != null && nb != room;
            if (hasNeighbor)
            {
                int opposite = (d + 2) % 4;
                DestroyNeighborSolidWall(nb, opposite);
                BuildWallWithDoor(room.root.transform, localPos, localRot, d, room.doorBlockers);
            }
            else if (room.type == DungeonRoomType.Shop)
            {
                // 商店：所有方向建门墙，确保出口可打开
                BuildWallWithDoor(room.root.transform, localPos, localRot, d, room.doorBlockers);
            }
            else
            {
                builtAny = true;
                BuildWall(room.root.transform, localPos, localRot, d, room.doorBlockers);
            }
        }
        if (builtAny) BuildCornerPillars(room);
    }

    void DestroyNeighborSolidWall(Room neighbor, int dir)
    {
        if (neighbor == null || neighbor.root == null) return;
        string wallName = "SolidWall_" + dir;
        Transform wall = neighbor.root.transform.Find(wallName);
        if (wall != null) Destroy(wall.gameObject);
    }

    // 在每个房间 4 个墙角放一根 墙厚×墙高 的方柱，补齐因墙缩短而在墙角留下的空洞，
    // 同时让墙与墙之间不再交叠。柱子沿用墙的材质但调成黑色，并打上 "Wall" 标签供 XRay 与寻路。
    void BuildCornerPillars(Room room)
    {
        float half = roomSize * 0.5f;
        float wt = wallThickness > 0.01f ? wallThickness : 0.3f;
        float h = wallHeight > 0.01f ? wallHeight : 3f;
        // 墙角柱：沿用墙的材质（保留原本的 shader/贴图观感，避免和房间风格割裂），只是把颜色调成黑色
        Material wallMat = null;
        foreach (var r in room.root.GetComponentsInChildren<Renderer>())
        {
            if (r.CompareTag("Wall")) { wallMat = r.sharedMaterial; break; }
        }
        if (wallMat == null)
        {
            var rs = room.root.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) wallMat = rs[0].sharedMaterial;
        }
        if (blackPillarMat == null)
        {
            blackPillarMat = wallMat != null ? new Material(wallMat) : new Material(Shader.Find("Standard"));
            blackPillarMat.color = Color.black;
        }
        float inset = wt * 0.5f;
        Vector3[] corners = new Vector3[]
        {
            new Vector3( half - inset, 0,  half - inset),
            new Vector3(-(half - inset), 0,  half - inset),
            new Vector3( half - inset, 0, -(half - inset)),
            new Vector3(-(half - inset), 0, -(half - inset)),
        };
        foreach (var c in corners)
        {
            // 共享墙角处相邻房间会在同一世界坐标各放一根柱，多根重合会互相 z-fighting 疯狂闪烁。
            // 放置前先清掉该位置已有的 CornerPillar（邻居已放 / 本房重建都覆盖），保证每个角只有一根。
            Vector3 worldXZ = new Vector3(room.root.transform.position.x + c.x, 0f, room.root.transform.position.z + c.z);
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                if (go.name == "CornerPillar" &&
                    Mathf.Abs(go.transform.position.x - worldXZ.x) < wt * 0.5f &&
                    Mathf.Abs(go.transform.position.z - worldXZ.z) < wt * 0.5f)
                {
                    Destroy(go);
                }
            }

            GameObject pil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pil.name = "CornerPillar";
            pil.transform.SetParent(room.root.transform);
            pil.transform.localPosition = new Vector3(c.x, h * 0.5f, c.z);
            // 比墙角缺口(wt)略大，把四面墙的端面“包”进柱体内部，
            // 彻底消除柱面与墙端面共面 z-fighting，且不留裂缝（避免邻居墙从缝里穿出）。
            pil.transform.localScale = new Vector3(wt * 1.06f, h, wt * 1.06f);
            pil.GetComponent<Renderer>().sharedMaterial = blackPillarMat;
            pil.tag = "Wall";
            var mf = pil.GetComponent<MeshFilter>();
            var obs = pil.AddComponent<NavMeshObstacle>();
            obs.carving = true;
            if (mf != null && mf.sharedMesh != null)
            {
                obs.size = mf.sharedMesh.bounds.size;
                obs.center = mf.sharedMesh.bounds.center;
            }
            else
            {
                obs.size = Vector3.one;
                obs.center = Vector3.zero;
            }
        }
    }

    // 玩家走进一间房：点亮、刷怪/商店、展开相邻黑暗房、刷新导航与 UI
    void EnterRoom(Room room, Vector2Int fromCoord, int entryDir)
    {
        if (advancing) return;
        advancing = true;
        try
        {
            // 进新房间时隐藏通关提示
            if (uiManager != null) uiManager.HidePersistentToast();

            currentCoord = room.coord;
            currentRoom = room;
            room.entryDir = entryDir;

            if (!room.visited)
            {
                room.visited = true;

                // 用“玩家进入顺序”决定商店：第 5/10/15… 次进入的房间才是商店房，
                // 与生成顺序无关，避免提前在暗房邻居里生成商店导致一进去跳关。
                visitedRoomNumber++;
                int vn = visitedRoomNumber;

                // 提前判定：玩家处在第 4/9/14… 间时，把其四周邻居（含已存在的暗房与尚未生成的）
                // 全部标为商店，确保"下一步进入的第 5/10/15… 间"必为商店，且以商店主题正确生成。
                if (vn % 5 == 4)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int nc = room.coord + DirVec2D(d);
                        Room nb;
                        if (rooms.TryGetValue(nc, out nb))
                        {
                            if (!nb.visited && nb.type != DungeonRoomType.Shop) ConvertToShop(nb);
                        }
                        else
                        {
                            pendingShopCoords.Add(nc);
                        }
                    }
                }

                if (vn % 5 == 0) room.type = DungeonRoomType.Shop;

                // 战斗关号仅在战斗房递增，商店不计入，UI 显示连续关号
                if (room.type != DungeonRoomType.Shop)
                {
                    battleLevel++;
                    room.levelIndex = battleLevel;
                }

                SetRoomLightUp(room);
                if (room.type == DungeonRoomType.Shop)
                {
                    // 商店：出口门永开，入口门延迟关闭（等玩家完全进入）
                    OpenRoomDoors(room);
                    StartCoroutine(DelayCloseEntryDoor(room));
                }
                else
                {
                    // 战斗房：锁出口门，延迟关入口门（等玩家完全进入）
                    CloseRoomDoors(room);
                    StartCoroutine(DelayCloseEntryDoor(room));
                }
                BeginRoomContent(room, entryDir);
                ExpandRoom(room, fromCoord);
                CleanupFarRooms(room.coord, 3);
                // 商店：ExpandRoom 可能新建了邻居（共享边自动建带门墙），再次开门确保所有出口畅通。
                if (room.type == DungeonRoomType.Shop) OpenRoomDoors(room);
                BuildNavMesh();
            }

            // 玩家刚离开的上一间房：入口门已在身后关上（DelayCloseEntryDoor），这里只关灯（变暗但不全黑）。
            // 玩家只点亮“当前所在关卡”；进入新关卡、门关之后，前关卡才变暗。
            Room prev;
            if (rooms.TryGetValue(fromCoord, out prev) && prev != null && prev != room)
            {
                SetRoomDim(prev);
                for (int d = 0; d < 4; d++)
                {
                    if (prev.doorBlockers.Count <= d) continue;
                    if (prev.doorBlockers[d] == null) continue;
                    ApplyDoorBlocker(prev.doorBlockers[d], true);
                }
            }
            // 确保当前所在房间处于点亮状态（也覆盖重新进入已离开过的房间）
            SetRoomLightUp(room);
            RefreshRoomUI(room);
        }
        finally { advancing = false; }
    }

    // 把一间已存在（暗房）的房间按商店主题重建：清掉旧地板/墙/装饰/灯，用商店预制体重新生成，
    // 保证“提前标成商店”的邻居拥有正确的商店外观（而非战斗主题）。
    void ConvertToShop(Room room)
    {
        if (room == null || room.root == null) return;
        List<GameObject> children = new List<GameObject>();
        foreach (Transform c in room.root.transform) children.Add(c.gameObject);
        foreach (var c in children) Destroy(c);

        room.doorBlockers = new List<List<GameObject>>();
        room.type = DungeonRoomType.Shop;
        currentType = DungeonRoomType.Shop;
        roomTheme = currentType;

        BuildFloor(room.root.transform);
        BuildRoomWalls(room);
        PlaceDecorations(room.root.transform);
        PlaceWallDecorations(room.root.transform);
        PlaceRoomLight(room.root.transform);

        if (room.visited) SetRoomLightUp(room); else SetRoomDark(room);
    }

    // 展开：把尚未存在的相邻房间建为黑暗房（保留“未开启关卡保持黑暗”），它们会跳过与本房共用的那面墙
    void ExpandRoom(Room room, Vector2Int fromCoord)
    {
        List<int> candidates = new List<int>();
        for (int d = 0; d < 4; d++)
        {
            Vector2Int nc = room.coord + DirVec2D(d);
            if (nc == fromCoord) continue;
            Room nb;
            if (rooms.TryGetValue(nc, out nb) && nb != null)
            {
                if (nb.cleared && nb.aliveEnemies.Count == 0 && nb != currentRoom)
                {
                    DestroyRoom(nb);
                    candidates.Add(d);
                }
            }
            else
            {
                candidates.Add(d);
            }
        }

        if (candidates.Count == 0)
        {
            List<int> forced = new List<int>();
            for (int d = 0; d < 4; d++)
            {
                Vector2Int nc = room.coord + DirVec2D(d);
                if (nc == fromCoord) continue;
                Room nb;
                if (rooms.TryGetValue(nc, out nb) && nb != null && nb != currentRoom
                    && nb.aliveEnemies.Count == 0)
                    forced.Add(d);
            }
            if (forced.Count > 0)
            {
                int pick = forced[Random.Range(0, forced.Count)];
                Vector2Int nc = room.coord + DirVec2D(pick);
                Room nb;
                if (rooms.TryGetValue(nc, out nb) && nb != null) DestroyRoom(nb);
                candidates.Add(pick);
            }
        }

        if (candidates.Count == 0) return;

        int exitCount = Random.Range(1, Mathf.Min(candidates.Count, 3) + 1);

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        for (int i = 0; i < exitCount; i++)
        {
            Vector2Int nc = room.coord + DirVec2D(candidates[i]);
            if (!rooms.ContainsKey(nc)) BuildRoom(nc, false);
        }
    }

    // 安全销毁一间已清完的房间：清除视觉对象、邻居悬空引用、字典条目、门动画缓存
    void DestroyRoom(Room room)
    {
        if (room == null) return;
        if (room == currentRoom) return; // 不销毁当前所在房间
        if (room.root == null) return;

        // 1. 清理所有邻居朝向本房间的 doorBlockers（防止悬空引用）
        for (int d = 0; d < 4; d++)
        {
            Vector2Int nc = room.coord + DirVec2D(d);
            Room nb;
            if (!rooms.TryGetValue(nc, out nb) || nb == null || nb == room) continue;
            int opposite = (d + 2) % 4;
            if (nb.doorBlockers.Count <= opposite) continue;
            List<GameObject> leaves = nb.doorBlockers[opposite];
            if (leaves == null) continue;
            // 停止该门的旋转动画
            foreach (var leaf in leaves)
            {
                if (leaf == null) continue;
                if (doorSwingRoutine.ContainsKey(leaf) && doorSwingRoutine[leaf] != null)
                    StopCoroutine(doorSwingRoutine[leaf]);
                doorSwingRoutine.Remove(leaf);
                doorClosedRot.Remove(leaf);
                swingAngleFor.Remove(leaf);
                swingAxisFor.Remove(leaf);
            }
            // 重建邻居该方向的实心墙（替换被销毁房间留下的门洞）
            int dir = opposite;
            Transform parent = nb.root.transform;
            float half = roomSize * 0.5f;
            Vector3 localPos; Quaternion localRot;
            if (dir == 0) { localPos = new Vector3(0, 0, half); localRot = Quaternion.identity; }
            else if (dir == 1) { localPos = new Vector3(half, 0, 0); localRot = Quaternion.Euler(0, 90, 0); }
            else if (dir == 2) { localPos = new Vector3(0, 0, -half); localRot = Quaternion.Euler(0, 180, 0); }
            else { localPos = new Vector3(-half, 0, 0); localRot = Quaternion.Euler(0, -90, 0); }
            // 销毁旧门物体
            string wallName = "Wall_" + dir;
            Transform oldWall = parent.Find(wallName);
            if (oldWall != null) Destroy(oldWall.gameObject);
            // 建实心墙
            BuildSolidWall(parent, localPos, localRot, dir);
            nb.doorBlockers[opposite] = null;
        }

        // 2. 清理本房间自身的门动画缓存
        for (int d = 0; d < 4; d++)
        {
            if (room.doorBlockers.Count <= d) continue;
            List<GameObject> leaves = room.doorBlockers[d];
            if (leaves == null) continue;
            foreach (var leaf in leaves)
            {
                if (leaf == null) continue;
                if (doorSwingRoutine.ContainsKey(leaf) && doorSwingRoutine[leaf] != null)
                    StopCoroutine(doorSwingRoutine[leaf]);
                doorSwingRoutine.Remove(leaf);
                doorClosedRot.Remove(leaf);
                swingAngleFor.Remove(leaf);
                swingAxisFor.Remove(leaf);
            }
        }

        // 3. 销毁视觉对象、从字典移除
        if (room.shopObject != null) Destroy(room.shopObject);
        rooms.Remove(room.coord);
        Destroy(room.root);

        Debug.Log("[Dungeon] DestroyRoom: destroyed room at " + room.coord);
    }

    void CleanupFarRooms(Vector2Int current, int maxDist)
    {
        List<Vector2Int> toDestroy = new List<Vector2Int>();
        foreach (var kvp in rooms)
        {
            if (kvp.Value == currentRoom) continue;
            int dist = Mathf.Abs(kvp.Key.x - current.x) + Mathf.Abs(kvp.Key.y - current.y);
            if (dist > maxDist && kvp.Value.cleared && kvp.Value.aliveEnemies.Count == 0)
                toDestroy.Add(kvp.Key);
        }
        foreach (var c in toDestroy)
        {
            Room r;
            if (rooms.TryGetValue(c, out r)) DestroyRoom(r);
        }
    }

    void SetRoomDark(Room room)
    {
        if (room.root == null) return;
        SetLayerRecursively(room.root, darkLayer);
        SetRoomLights(room, false);
    }
    void SetRoomLightUp(Room room)
    {
        if (room.root == null) return;
        SetLayerRecursively(room.root, 0);
        SetRoomLights(room, true);
    }
    // 仅关灯、不切暗房层：房间仍受方向光(环境光为黑，仅有方向光)照射，呈“变暗”而非“全黑”。
    // 用于玩家刚离开的已通关房——按需求“只是关灯，不用整个变黑”。
    void SetRoomDim(Room room)
    {
        if (room.root == null) return;
        SetRoomLights(room, false);
    }
    void SetRoomLights(Room room, bool on)
    {
        int decoCount = 0, roomCount = 0;
        foreach (var lt in room.root.GetComponentsInChildren<Light>(true))
        {
            if (decoLights.Contains(lt))
            {
                lt.enabled = on;
                decoCount++;
            }
            else
            {
                lt.intensity = on ? 30f : 0f;
                roomCount++;
            }
        }
        Debug.Log("[Lights] SetRoomLights room=" + room.root.name
            + " on=" + on
            + " decoLights=" + decoCount
            + " roomLights=" + roomCount
            + " decoHashSetTotal=" + decoLights.Count);
    }

    void RefreshRoomUI(Room room)
    {
        if (uiManager != null) uiManager.SetRoomDisplay(room.levelIndex, room.type == DungeonRoomType.Shop);
        if (GameManager.Instance != null) GameManager.Instance.SetDungeonRoom(room.roomIndex);
    }

    Vector2Int WorldToCoord(Vector3 p)
        => new Vector2Int(Mathf.RoundToInt(p.x / roomSize), Mathf.RoundToInt(p.z / roomSize));

    int DirFromDelta(Vector2Int delta)
    {
        if (delta.x == 1) return 1;   // E
        if (delta.x == -1) return 3;  // W
        if (delta.y == 1) return 0;   // N
        if (delta.y == -1) return 2;  // S
        return -1;
    }

    // 进入房间后的内容启动（刷怪/商店）。不做相机转场，直接开始。
    void BeginRoomContent(Room room, int entryDir)
    {
        StartRoomContent(room);
    }

    void StartRoomContent(Room room)
    {
        if (room.type == DungeonRoomType.Shop) SpawnShop(room);
        else StartCoroutine(SpawnRoomEnemies(room));
    }

    GameObject GetFloorPrefab(DungeonRoomType type)
    {
        if (type == DungeonRoomType.Snake) return snakeFloorPrefab;
        if (type == DungeonRoomType.Zombie) return zombieFloorPrefab;
        if (type == DungeonRoomType.Mixed) return (roomTheme == DungeonRoomType.Zombie) ? zombieFloorPrefab : snakeFloorPrefab;
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
            EnsureLightsEnabled(floor);
            // 不强制改旋转，保留预制体自身的朝向（你说 floor prefab 方向已正确）

            // 勾选时：仅在第 1 个房间用地板预制体实测尺寸推导 roomSize（建立基准尺寸）。
            // 之后不再随地板尺寸变化，避免“把邻居暗房转商店/用不同尺寸商店地板”时改掉全局 roomSize，
            // 导致后续房间尺寸错乱、墙/门错位甚至穿模坠落。
            if (autoRoomSizeFromFloor && roomCounter == 1)
            {
                var rends = floor.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    Bounds fb = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) fb.Encapsulate(rends[i].bounds);
                    float measured = Mathf.Max(fb.size.x, fb.size.z);
                    // 地板包围盒退化（size 为 0）时不能清零 roomSize，否则所有房间塌缩到原点、
                    // 地面碰撞体变 0 尺寸 -> 表现为“第2关叠在第1关位置上并穿门坠落”。
                    if (measured > 0.01f) roomSize = measured;
                    else Debug.LogWarning("[Dungeon] 地板(" + floor.name + ")包围盒尺寸为 0，沿用上次 roomSize=" + roomSize.ToString("F2"));
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
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer != -1) floor.layer = groundLayer;

        // 保险：确保地板有“实心”碰撞体，否则 CharacterController 会穿透地板掉进虚空
        // （表现为“第2关直接坠落”）。对每个带网格的子物体：无碰撞体则就地补 MeshCollider，
        // 已有碰撞体则强制非 Trigger。MeshCollider 必须加在“带 MeshFilter 的那个物体”上，否则碰撞体偏移。
        foreach (var mf in floor.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Collider col = mf.GetComponent<Collider>();
            if (col == null) mf.gameObject.AddComponent<MeshCollider>();
            else col.isTrigger = false;
        }
        // 兜底：地板整体没有任何网格时，给根物体补一个 BoxCollider 占位，避免完全无碰撞
        if (floor.GetComponentsInChildren<MeshFilter>().Length == 0)
        {
            BoxCollider bc = floor.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(roomSize, 0.1f), 0.1f, Mathf.Max(roomSize, 0.1f));
            bc.isTrigger = false;
        }

        // 永远保留一块与房间等大的实心地面碰撞体（挂在 roomRoot 下，独立于地板预制体），
        // 确保玩家/敌人无论地板预制体碰撞体是否正常，都绝不穿透坠落。
        GameObject groundCol = new GameObject("GroundCollider");
        groundCol.transform.SetParent(parent);
        groundCol.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        BoxCollider gbc = groundCol.AddComponent<BoxCollider>();
        gbc.size = new Vector3(Mathf.Max(roomSize, 0.1f), 0.1f, Mathf.Max(roomSize, 0.1f));
        gbc.center = Vector3.zero;
        gbc.isTrigger = false;
    }

    private Vector2Int DirVec2D(int dir)
    {
        if (dir < 0 || dir > 3) return Vector2Int.zero;
        return new Vector2Int((int)Mathf.Round(DirVec[dir].x), (int)Mathf.Round(DirVec[dir].z));
    }
    private Vector3 CoordToWorld(Vector2Int c) => new Vector3(c.x * roomSize, 0f, c.y * roomSize);

    void BuildWall(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex, List<List<GameObject>> doorOut)
    {
        // 死路（无邻居的边）一律封实心墙；玩家改从有邻居的共享边开口通行
        BuildSolidWall(parent, localPos, localRot, dirIndex);
    }

    // 死墙：完全封死的实心墙，没有门洞，也不会开/关，更不会生成暗房或出口。
    // 按房间类型取不同外观的实心墙预制体（蛇/僵尸/商店/混合）。
    void BuildSolidWall(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex)
    {
        GameObject wallParent = new GameObject("SolidWall_" + dirIndex);
        wallParent.transform.SetParent(parent);
        wallParent.transform.localPosition = localPos;
        wallParent.transform.localRotation = localRot;

        // 统一用同一套墙壁预制体，避免相邻房间材质不同
        GameObject sp = solidWallPrefab;

        GameObject mesh = null;
        if (sp != null)
        {
            mesh = Instantiate(sp, wallParent.transform);
            mesh.name = "WallMesh";
        }
        else
        {
            mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "WallMesh";
            mesh.transform.SetParent(wallParent.transform);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = new Vector3(roomSize, wallHeight, wallThickness);
        }
        foreach (var col in mesh.GetComponentsInChildren<Collider>())
            col.gameObject.tag = "Wall";

        // 自动量墙高/墙厚（与带门墙一致逻辑，供顶灯、墙角柱、装饰内退使用）
        Renderer[] wrs = mesh.GetComponentsInChildren<Renderer>();
        float wallWidth = 0f;
        float wt = 0f;
        if (wrs.Length > 0)
        {
            Bounds rb = wrs[0].bounds;
            for (int i = 1; i < wrs.Length; i++) rb.Encapsulate(wrs[i].bounds);
            if (rb.size.y > 0.1f) wallHeight = rb.size.y;
            wt = Mathf.Min(rb.size.x, rb.size.z);
            if (wt > 0.01f) wallThickness = wt;
            wallWidth = Mathf.Max(rb.size.x, rb.size.z);
        }

        // 墙体若缺 NavMeshObstacle 则按网格自动补（防止敌人穿墙寻路）
        bool addedObs = false;
        foreach (var r in mesh.GetComponentsInChildren<Renderer>())
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
        if (addedObs && showDebugLogs) Debug.LogWarning("[Dungeon] 实心墙预制体部分网格缺少 NavMeshObstacle，已按网格自动补");

        // 统一缩放使墙“长度”= 整条边 roomSize：不管用哪种墙预制体(snake/zombie/shop/mixed)，
        // 四面墙都精确抵达墙角，避免不同预制体缩放后长度不一（有的对齐柱子、有的超出柱子）。
        // 墙角上交叠的端部由墙角柱(略大于墙厚)整体包住，既无穿模也无闪烁。
        if (wallWidth > 0.001f)
        {
            float s = Mathf.Clamp(roomSize / wallWidth, 0.1f, 10f);
            if (Mathf.Abs(s - 1f) > 0.01f)
            {
                mesh.transform.localScale = new Vector3(s, s, s);
                wallHeight *= s;
                wallThickness *= s;
                if (showDebugLogs) Debug.LogWarning("[Dungeon] 实心墙预制体长度(" + wallWidth.ToString("F2") + ")与房间尺寸(" + roomSize.ToString("F2") + ")不一致，已整体缩放对齐");
            }
        }

        // 居中并把墙几何底面贴到地面高度
        {
            var rs = mesh.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                float groundY = wallParent.transform.position.y;
                Vector3 desired = new Vector3(wallParent.transform.position.x, groundY + b.size.y * 0.5f, wallParent.transform.position.z);
                mesh.transform.position += (desired - b.center);
            }
        }
        // 整面墙向内移半墙厚，使外墙落在外边缘
        {
            Vector3 outDir = localPos.normalized;
            if (outDir != Vector3.zero)
                wallParent.transform.localPosition = localPos - outDir * (wallThickness * 0.5f);
        }
        // 确保墙是“实心可挡”的：
        // ① 预制体自带碰撞体一律设为非触发器（触发器不挡 CharacterController，会被敌人推穿墙）；
        // ② 若预制体完全没有碰撞体，按网格包围盒补一个尺寸正确的盒子（默认 1×1×1 小盒挡不住整面墙）。
        // 确保墙是"实心可挡"的
        var wallCols = mesh.GetComponentsInChildren<Collider>();
        if (wallCols.Length == 0)
        {
            var bc = mesh.AddComponent<BoxCollider>();
            Bounds wb = new Bounds();
            bool has = false;
            foreach (var r in mesh.GetComponentsInChildren<Renderer>())
            {
                if (!has) { wb = r.bounds; has = true; }
                else wb.Encapsulate(r.bounds);
            }
            if (has)
            {
                Vector3 s = mesh.transform.localScale;
                s.x = Mathf.Abs(s.x) < 1e-5f ? 1f : s.x;
                s.y = Mathf.Abs(s.y) < 1e-5f ? 1f : s.y;
                s.z = Mathf.Abs(s.z) < 1e-5f ? 1f : s.z;
                bc.center = mesh.transform.InverseTransformPoint(wb.center);
                bc.size = new Vector3(wb.size.x / s.x, wb.size.y / s.y, wb.size.z / s.z);
            }
            bc.isTrigger = false;
        }
        else
        {
            foreach (var c in wallCols) c.isTrigger = false;
        }
        // 实心墙无门：不注册 doorBlockers（门机制对死路墙停用）
    }

    void BuildWallWithDoor(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex, List<List<GameObject>> doorOut)
    {
        Debug.Log("[Dungeon] BuildWallWithDoor: dirIndex=" + dirIndex + " prefab=" + (wallWithDoorPrefab != null) + " parent=" + parent.name);
        GameObject wallParent = new GameObject("Wall_" + dirIndex);
        wallParent.transform.SetParent(parent);
        wallParent.transform.localPosition = localPos;
        wallParent.transform.localRotation = localRot;

        // 统一用同一套带门墙预制体
        GameObject wwd = wallWithDoorPrefab;

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
            float wt = 0f;
            if (wrs.Length > 0)
            {
                Bounds rb = wrs[0].bounds;
                for (int i = 1; i < wrs.Length; i++) rb.Encapsulate(wrs[i].bounds);
                if (rb.size.y > 0.1f) wallHeight = rb.size.y;
                wt = Mathf.Min(rb.size.x, rb.size.z);
                if (wt > 0.01f) wallThickness = wt;
                wallWidth = Mathf.Max(rb.size.x, rb.size.z);
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
            // 支持单开门与双开门：预制体里可挂多个带 DoorMarker 的门叶，每叶独立旋转
            DoorMarker[] dms = wall.GetComponentsInChildren<DoorMarker>();
            List<GameObject> leaves = new List<GameObject>();

            // 自动量门洞宽度：所有门叶碰撞体包围盒合并后的跨度 ≈ 门洞宽，
            // 这样每间房的 doorWidth 跟着各自带门墙预制体走，无需手填（仅作兜底/装饰避让用）。
            if (dms.Length > 0)
            {
                Bounds db = new Bounds();
                bool firstBounds = true;
                bool forcedSolid = false;
                foreach (var dm in dms)
                {
                    var dcols = dm.GetComponentsInChildren<Collider>();
                    bool leafSolid = false;
                    foreach (var c in dcols)
                    {
                        if (!c.isTrigger) leafSolid = true;
                        if (firstBounds) { db = c.bounds; firstBounds = false; }
                        else db.Encapsulate(c.bounds);
                    }
                    if (dcols.Length == 0)
                    {
                        var bc = dm.gameObject.AddComponent<BoxCollider>();
                        bc.size = new Vector3(doorWidth, wallHeight, wallThickness);
                        bc.isTrigger = false;
                        forcedSolid = true;
                    }
                    else if (!leafSolid)
                    {
                        foreach (var c in dcols) c.isTrigger = false;
                        forcedSolid = true;
                    }
                }
                if (!firstBounds)
                {
                    float measured = Mathf.Max(db.size.x, db.size.z); // 墙旋转0/90/180/270，取世界X或Z较大者
                    if (measured > 0.1f) doorWidth = measured;
                }
                if (forcedSolid && showDebugLogs) Debug.LogWarning("[Dungeon] 门的碰撞体已是实心(Is Trigger=false)以挡玩家；若需要触发检测请另加碰撞体");
            }

            // 让墙严格对齐地板边缘：
            // 1) 统一缩放使墙“长度”= roomSize（轴无关，无论墙长沿本地 X 还是 Z 都能对齐，避免墙角对不上）；
            //    同时同步 wallHeight/wallThickness/doorWidth，保证灯光高度、装饰内退、门洞一致。
            // 2) 把墙几何中心重新对齐到边线中点（纠正预制体 pivot 不在几何中心导致的偏移）。
            // 若预制体长度与中心已正确，s≈1 且偏移≈0，无副作用。
            if (wallWidth > 0.001f)
            {
                // 墙长 = 整条边 roomSize：不管用哪种墙预制体，四面墙都精确抵达墙角，
                // 避免不同预制体缩放后长度不一（有的对齐柱子、有的超出柱子）。墙角上交叠的端部由墙角柱(略大于墙厚)整体包住。
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
            // 让墙“贴”在地板边缘：把墙父物体从边线向内移半墙厚，使外墙正好落在 floor 边缘，
            // 而不是向外凸出半墙厚（之前墙几何中心被放在边线上，导致墙有一半悬在地板外）。
            // 必须在缩放之后做，这样用的是缩放后的世界墙厚，与已缩短的墙长一致、墙角由墙角柱补齐而不留洞。
            {
                Vector3 outDir = localPos.normalized;
                if (outDir != Vector3.zero)
                    wallParent.transform.localPosition = localPos - outDir * (wallThickness * 0.5f);
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
                    if (showDebugLogs) Debug.Log("[Dungeon] 墙 dir=" + dirIndex
                        + " wallParent=" + wallParent.transform.position.ToString("F2")
                        + " 墙世界中心=" + b2.center.ToString("F2")
                        + " 尺寸=" + b2.size.ToString("F2"));
                }
            }

            if (dms.Length > 0)
            {
                foreach (var dm in dms)
                {
                    GameObject doorObj = (dm.swingTarget != null) ? dm.swingTarget.gameObject : dm.gameObject;
                    if (doorObj == null) continue;
                    leaves.Add(doorObj);
                    doorClosedRot[doorObj] = doorObj.transform.localRotation;
                    swingAngleFor[doorObj] = dm.overrideSwing ? dm.swingAngle : doorSwingAngle;
                    swingAxisFor[doorObj] = dm.overrideSwing ? dm.swingAxis : doorSwingAxis;
                }
            }
            else
            {
                // 兜底：预制体里没挂 DoorMarker 时，才补一个方块碰撞体挡住（正常不应走到这）
                GameObject doorObj = new GameObject("DoorColliderFallback");
                doorObj.transform.SetParent(wallParent.transform);
                doorObj.transform.localPosition = new Vector3(0, wallHeight * 0.5f, 0);
                var bcFallback = doorObj.AddComponent<BoxCollider>();
                bcFallback.size = new Vector3(doorWidth, wallHeight, wallThickness);
                bcFallback.isTrigger = false;
                var noFallback = doorObj.AddComponent<NavMeshObstacle>();
                noFallback.carving = true;
                noFallback.size = new Vector3(doorWidth, wallHeight, wallThickness);
                noFallback.center = Vector3.zero;
                leaves.Add(doorObj);
                doorClosedRot[doorObj] = doorObj.transform.localRotation;
                swingAngleFor[doorObj] = doorSwingAngle;
                swingAxisFor[doorObj] = doorSwingAxis;
                if (showDebugLogs) Debug.LogWarning("[Dungeon] 带门墙预制体未找到 DoorMarker，已用兜底方块碰撞体（请在门的子物体上挂 DoorMarker）");
            }
            while (doorOut.Count <= dirIndex) doorOut.Add(null);
            doorOut[dirIndex] = leaves;

            // 门不必强制居中：只保证四面墙各自按边线中点对齐（见上方“重新对齐”逻辑），
            // 墙角/墙边自然闭合。门留在预制体原本的位置即可，不再整体平移墙体，避免墙被推歪。
            return;
        }


    }

    // 通用的门开/关：给定一组门的叶子列表，绕门“本地轴”旋转并开关碰撞/导航。
    void ApplyDoorBlocker(List<GameObject> leaves, bool block)
    {
        if (leaves == null) return;
        foreach (var b in leaves)
        {
            if (b == null) continue;

            // 代码驱动开门：协程绕门“本地轴”旋转（轴/角度每扇门叶可独立设置，支持双开门左右相反）。
            if (!doorClosedRot.ContainsKey(b))
                doorClosedRot[b] = b.transform.localRotation;

            if (doorSwingRoutine.ContainsKey(b) && doorSwingRoutine[b] != null)
                StopCoroutine(doorSwingRoutine[b]);

            float ang = swingAngleFor.ContainsKey(b) ? swingAngleFor[b] : doorSwingAngle;
            Vector3 ax = swingAxisFor.ContainsKey(b) ? swingAxisFor[b] : doorSwingAxis;
            Quaternion from = b.transform.localRotation;
            Quaternion to = block ? doorClosedRot[b] : doorClosedRot[b] * Quaternion.AngleAxis(ang, ax);
            doorSwingRoutine[b] = StartCoroutine(DoorSwing(b.transform, from, to));

            // 碰撞体 / 导航障碍：关门启用（挡人），开门禁用（放行）
            foreach (var col in b.GetComponentsInChildren<Collider>())
                col.enabled = block;
            foreach (var obs in b.GetComponentsInChildren<NavMeshObstacle>())
                obs.enabled = block;
        }
    }

    // 开启/关闭某房间在指定方向“拥有”的门（共享墙方向 doorBlockers[d] 为 null，由邻居控制）
    void OpenDoor(Room room, int dir, bool open)
    {
        if (room.doorBlockers.Count <= dir) return;
        var list = room.doorBlockers[dir];
        if (list == null) return;
        ApplyDoorBlocker(list, !open); // block = !open
    }

    // 开启某房间“拥有”的所有门（清场或进入商店时调用）
    void OpenRoomDoors(Room room)
    {
        for (int d = 0; d < 4; d++)
        {
            if (room.doorBlockers.Count <= d) continue;
            if (room.doorBlockers[d] == null) continue;
            ApplyDoorBlocker(room.doorBlockers[d], false);
        }
    }

    // 关闭某房间“拥有”的所有门（战斗进入时锁房）
    void CloseRoomDoors(Room room)
    {
        for (int d = 0; d < 4; d++)
        {
            if (d == room.entryDir) continue;
            if (room.deadWallDirs[d]) continue;
            if (room.doorBlockers.Count <= d) continue;
            if (room.doorBlockers[d] == null) continue;
            ApplyDoorBlocker(room.doorBlockers[d], true);
        }
    }

    // 关闭/开启“入口那扇门”：该门位于本房 entryDir 侧，归属邻居(fromCoord)所有，
    // 对应邻居的 (entryDir+2)%4 方向。玩家进场后锁住入口，清场后再打开。
    void CloseEntryDoor(Vector2Int fromCoord, int entryDir)
    {
        if (entryDir < 0 || currentRoom == null) return;
        if (currentRoom.doorBlockers.Count > entryDir && currentRoom.doorBlockers[entryDir] != null)
            ApplyDoorBlocker(currentRoom.doorBlockers[entryDir], true);
    }
    void OpenEntryDoor(Vector2Int fromCoord, int entryDir)
    {
        if (entryDir < 0 || currentRoom == null) return;
        if (currentRoom.doorBlockers.Count > entryDir && currentRoom.doorBlockers[entryDir] != null)
            ApplyDoorBlocker(currentRoom.doorBlockers[entryDir], false);
    }

    IEnumerator DelayCloseEntryDoor(Room room)
    {
        if (room.entryDir < 0) yield break;
        Vector3 center = room.root.transform.position;
        float half = roomSize * 0.5f;
        float margin = 1.5f;
        while (true)
        {
            yield return null;
            if (room == null || playerTarget == null) yield break;
            Vector3 pp = playerTarget.position;
            bool pastDoor = false;
            switch (room.entryDir)
            {
                case 0: pastDoor = pp.z < center.z + half - margin; break;
                case 1: pastDoor = pp.x < center.x + half - margin; break;
                case 2: pastDoor = pp.z > center.z - half + margin; break;
                case 3: pastDoor = pp.x > center.x - half + margin; break;
            }
            if (pastDoor)
            {
                if (room.doorBlockers.Count > room.entryDir && room.doorBlockers[room.entryDir] != null)
                    ApplyDoorBlocker(room.doorBlockers[room.entryDir], true);
                yield break;
            }
        }
    }

    IEnumerator DoorSwing(Transform t, Quaternion from, Quaternion to)
    {
        float dur = 0.4f;
        float el = 0f;
        while (el < dur)
        {
            el += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, el / dur);
            t.localRotation = Quaternion.Slerp(from, to, k);
            yield return null;
        }
        t.localRotation = to;
        if (doorSwingRoutine.ContainsKey(t.gameObject)) doorSwingRoutine[t.gameObject] = null;
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
        float limit = half - wallThickness - decorationWallMargin; // 额外留边，避免装饰体积插进墙体

        int placed = 0;
        int tries = 0;
        int maxTries = count * 100;   // 提高重试上限，保底能摆到 decorationMin 个
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
                if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(doorLocal.x, 0, doorLocal.z)) < doorWidth * 0.5f + decorationDoorMargin)
                { nearDoor = true; break; }
            }
            if (nearDoor && tries < maxTries * 0.7f) continue;

            GameObject deco = Instantiate(prefab, parent);
            deco.transform.localPosition = pos;
            deco.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            EnsureLightsEnabled(deco);

            // 把装饰底部贴到地面(parent.position.y)：兼容不同 pivot（中心/底部），避免悬空或下陷
            Renderer r = deco.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                float groundY = parent.position.y;
                float minY = r.bounds.min.y;
                if (Mathf.Abs(minY - groundY) > 0.001f)
                    deco.transform.position += Vector3.up * (groundY - minY);
            }

            // 避免与已摆放的装饰互相穿模：用世界包围盒做相交检测，重叠则重摆
            // 垂直方向扩展到覆盖整个房间高度，确保墙壁装饰(Y≈5)与地面装饰(Y≈0)也能检测 XZ 重叠
            if (r != null)
            {
                Bounds b = r.bounds;
                float roomMinY = parent.position.y - 1f;
                float roomMaxY = parent.position.y + wallHeight + 2f;
                b.Encapsulate(new Vector3(b.center.x, roomMinY, b.center.z));
                b.Encapsulate(new Vector3(b.center.x, roomMaxY, b.center.z));
                bool overlap = false;
                foreach (var pb in placedBounds)
                {
                    if (b.Intersects(pb)) { overlap = true; break; }
                }
                if (overlap)
                {
                    Destroy(deco);
                    continue;
                }
                placedBounds.Add(b);
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

        int count = Mathf.Max(4, Random.Range(wallDecorationMin, wallDecorationMax + 1));
        float half = roomSize * 0.5f;
        float limit = half - wallThickness - 0.2f;

        float doorClear = doorWidth * 0.5f + wallThickness + 2.5f;
        float targetCenterY = parent.position.y + wallDecorationHeight;

        // 先保证每面墙（0~3）至少放一个，剩余随机分配
        List<int> wallOrder = new List<int> { 0, 1, 2, 3 };
        int extraCount = Mathf.Max(0, count - 4);
        for (int i = 0; i < extraCount; i++)
            wallOrder.Add(Random.Range(0, 4));

        int placed = 0;
        foreach (int d in wallOrder)
        {
            bool done = false;
            for (int t = 0; t < 40 && !done; t++)
            {
                GameObject prefab = pool[Random.Range(0, pool.Count)];
                if (prefab == null) continue;

                float cornerMargin = wallThickness + 1.0f;
                float alongMax = Mathf.Max(0.2f, limit - cornerMargin);
                float along = Random.Range(-alongMax, alongMax);
                if (Mathf.Abs(along) < doorClear) continue;

                float yaw = (d == 0) ? 180f : (d == 1) ? -90f : (d == 2) ? 0f : 90f;
                Quaternion rot = Quaternion.Euler(0, yaw, 0);

                GameObject deco = Instantiate(prefab, parent);
                Quaternion authored = deco.transform.localRotation;
                deco.transform.localRotation = rot * authored;
                EnsureLightsEnabled(deco);
                foreach (var dl in deco.GetComponentsInChildren<Light>(true))
                {
                    dl.renderMode = LightRenderMode.ForcePixel;
                    Vector3 toCenter = (Vector3.zero - deco.transform.localPosition).normalized;
                    dl.transform.position += toCenter * 0.4f;
                    decoLights.Add(dl);
                }

                Renderer r = deco.GetComponentInChildren<Renderer>();
                float extentAlongNormal = 0f;
                if (r != null)
                    extentAlongNormal = (d == 0 || d == 2) ? r.bounds.extents.z : r.bounds.extents.x;
                float wallInner = half - wallThickness;
                float backDist = wallInner - extentAlongNormal - 0.05f;
                if (backDist < 0.05f) backDist = 0.05f;

                Vector3 pos;
                if (d == 0)      pos = new Vector3(along, 0, backDist);
                else if (d == 1) pos = new Vector3(backDist, 0, along);
                else if (d == 2) pos = new Vector3(along, 0, -backDist);
                else             pos = new Vector3(-backDist, 0, along);
                deco.transform.localPosition = pos;

                if (r != null)
                {
                    float dy = targetCenterY - r.bounds.center.y;
                    if (Mathf.Abs(dy) > 0.001f) deco.transform.position += Vector3.up * dy;
                }

                if (r != null)
                {
                    Bounds b = r.bounds;
                    b.Expand(1.5f); // 每个装饰周围留 1.5m 间距，避免贴太近
                    float roomMinY = parent.position.y - 1f;
                    float roomMaxY = parent.position.y + wallHeight + 2f;
                    b.Encapsulate(new Vector3(b.center.x, roomMinY, b.center.z));
                    b.Encapsulate(new Vector3(b.center.x, roomMaxY, b.center.z));
                    bool overlap = false;
                    foreach (var pb in placedBounds)
                    {
                        if (b.Intersects(pb)) { overlap = true; break; }
                    }
                    if (overlap) { Destroy(deco); continue; }
                    placedBounds.Add(b);
                }
                if (deco.GetComponent<Collider>() == null) deco.AddComponent<BoxCollider>();
                placed++;
                done = true;
            }
        }
        if (showDebugLogs) Debug.Log("[Dungeon] 摆放墙壁装饰 " + placed + " 个（每面墙至少1个）");
    }

    void PlaceRoomLight(Transform parent)
    {
        // 灯光照“除 darkLayer(邻居暗房) 之外”的所有层，保证只点亮当前活动房间
        int lightMask = ~(1 << darkLayer);

        // 房间照明完全由生成的吸顶灯阵列负责（不再用 roomLightPrefab 中央顶灯）：
        // 多排点光从房间内部打亮每面墙，避免背光墙变纯黑；邻居在 darkLayer 被排除 → 依旧黑暗。
        float half = roomSize * 0.5f;
        float h = wallHeight * 0.75f;
        int colsX = 3;
        float[] rowZ = { -half * 0.45f, half * 0.45f };
        for (int r = 0; r < rowZ.Length; r++)
        {
            float z = rowZ[r];
            for (int c = 0; c < colsX; c++)
            {
                float x = (colsX == 1) ? 0f : (c / (float)(colsX - 1) * 2f - 1f) * half * 0.45f;
                GameObject pl = new GameObject("RoomLamp_" + r + "_" + c);
                pl.transform.SetParent(parent, false);
                pl.transform.localPosition = new Vector3(x, h, z);
                Light pll = pl.AddComponent<Light>();
                pll.type = LightType.Point;
                pll.color = new Color(1f, 0.82f, 0.55f);
                pll.cullingMask = lightMask;
                pll.intensity = 30f;
                pll.range = roomSize * 0.9f;
                pll.shadows = LightShadows.None;
            }
        }
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // 确保实例化出来的预制体里自带的灯光是开启的（很多预制体作者把灯放进去却忘了 Enable，
    // 导致“prefab 自带的灯不出现”）。这里统一兜底打开，并保留其原有 cullingMask。
    void EnsureLightsEnabled(GameObject go)
    {
        if (go == null) return;
        var lights = go.GetComponentsInChildren<Light>(true);
        foreach (var lt in lights)
        {
            lt.enabled = true;
            Debug.Log("[Lights] EnsureLightsEnabled: " + go.name
                + " light=" + lt.gameObject.name
                + " type=" + lt.type
                + " intensity=" + lt.intensity
                + " range=" + lt.range
                + " color=" + lt.color
                + " enabled=" + lt.enabled
                + " cullingMask=" + lt.renderingLayerMask
                + " layer=" + lt.gameObject.layer);
        }
    }

    void BuildNavMesh()
    {
        if (navMeshSurface == null) EnsureNavMeshSurface();
        if (navMeshSurface == null) return;
        // 房间地板在烘焙时可能位于 Default(0)（已点亮房）或 darkLayer（邻居暗房）或 Ground 层，
        // 必须把这些都纳入烘焙层，否则 NavMesh 找不到可行走地面（敌人无法寻路）。
        int groundLay = LayerMask.NameToLayer("Ground");
        int mask = (1 << 0);
        if (groundLay != -1) mask |= (1 << groundLay);
        mask |= (1 << darkLayer);
        navMeshSurface.layerMask = mask;
        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.defaultArea = 0;
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
            damage = Mathf.Min(1f + (room - 1) * 0.0278f, 1.5f);
        }
    }

    // 敌人生成间隔随房间数递减：前 4 间 3s，5~9 间 2.5s，10 间起 2s
    float GetSpawnInterval(int room)
    {
        if (room >= 6) return 1.75f;  // 6 起（含 10+）= 1.75s
        return 2f;                      // 1-5 = 2s
    }

    void SpawnEnemiesForRoom(Room room)
    {
        StartCoroutine(SpawnRoomEnemies(room));
    }

    IEnumerator SpawnRoomEnemies(Room room)
    {
        room.spawningDone = false;
        room.spawnedCount = 0;
        currentType = room.type;

        // 无限(地牢)模式敌人房间上限：1-10 房最多 20 只，11 房起最多 30 只（商店房不走这里）
        int cap = (room.roomIndex <= 10) ? 20 : 30;

        ComputeScaling(room.roomIndex, out float speed, out float health, out float damage);

        // 每种房间固定池：蛇房全蛇、僵尸房全僵尸、混合房各一半（总数 = cap）
        int snakeCount = 0, zombieCount = 0;
        if (room.type == DungeonRoomType.Snake) snakeCount = cap;
        else if (room.type == DungeonRoomType.Zombie) zombieCount = cap;
        else { snakeCount = Mathf.FloorToInt(cap / 2); zombieCount = cap - snakeCount; }

        // 某类池为空时，把它的份额并入另一侧
        if (snakeEnemyPrefabs == null || snakeEnemyPrefabs.Count == 0) { zombieCount += snakeCount; snakeCount = 0; }
        if (zombieEnemyPrefabs == null || zombieEnemyPrefabs.Count == 0) { snakeCount += zombieCount; zombieCount = 0; }

        if (snakeCount == 0 && zombieCount == 0)
        {
            Debug.LogWarning("[Dungeon] 没有可用敌人预制体！");
            room.spawningDone = true;
            yield break;
        }

        // 生成顺序列表（混合房洗牌交错，避免一侧先刷完）
        List<GameObject> order = new List<GameObject>(cap);
        for (int i = 0; i < snakeCount; i++) order.Add(snakeEnemyPrefabs[Random.Range(0, snakeEnemyPrefabs.Count)]);
        for (int i = 0; i < zombieCount; i++) order.Add(zombieEnemyPrefabs[Random.Range(0, zombieEnemyPrefabs.Count)]);
        if (room.type == DungeonRoomType.Mixed) Shuffle(order);

        float half = roomSize * 0.5f;
        Vector3 center = room.root.transform.position;

        int idx = 0;
        while (idx < order.Count)
        {
            yield return new WaitForSeconds(GetSpawnInterval(room.roomIndex));
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
            if (ai != null)
            {
                ai.ApplyScalingMultipliers(speed, health, damage);
                ai.ForceDirectChase(true);
                ai.infiniteDetectionRange = roomSize * 1.5f;
                ai.infiniteLoseTargetRange = roomSize * 2f;
            }

            room.aliveEnemies.Add(enemy);
            room.spawnedCount++;
            idx++;
        }

        room.spawningDone = true;
        if (showDebugLogs) Debug.Log("[Dungeon] 房间 #" + room.roomIndex + " 敌人生成完毕，共 " + room.spawnedCount + " 只");
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

    void SpawnShop(Room room)
    {
        // 销毁旧的商店对象（每次进入刷新）
        if (room.shopObject != null) Destroy(room.shopObject);

        List<BuffDataSO> buffs = ResolveShopBuffs();
        if (buffs.Count == 0) return;

        // 整间商店房只生成一个交互点：玩家靠近后点击，弹出随机 3 个 Buff 供选择购买
        Vector3 pos = room.root.transform.position;
        GameObject shop;
        if (shopPrefab != null)
        {
            // 使用你提供的商店预制体，并在房间中央生成
            shop = Instantiate(shopPrefab, pos, Quaternion.identity);
            shop.name = "Shop";
            ShopItem item = shop.GetComponent<ShopItem>();
            if (item != null) item.Setup(buffs, this, 100, shopButtonPrefab, shopButtonParent);
            else Debug.LogWarning("[Dungeon] shopPrefab 上没有 ShopItem 组件，请在其上挂 ShopItem.cs");
        }
        else
        {
            // 兜底：运行时自动创建简单的 Shop 物体
            shop = new GameObject("Shop");
            shop.transform.position = pos;
            shop.AddComponent<ShopItem>().Setup(buffs, this, 100, shopButtonPrefab, shopButtonParent);
        }
        room.shopObject = shop;
        if (showDebugLogs) Debug.Log("[Dungeon] 商店已生成（靠近后点击打开，随机提供 3 个 Buff）");
    }

    // ============================================================
    //  主循环：检测清空 / 出口
    // ============================================================
    void Update()
    {
        if (advancing || playerTarget == null) return;

        // 检测玩家当前所在房间（按世界坐标取整到网格），跨房即“走进”新房间
        Vector2Int pc = WorldToCoord(playerTarget.position);
        if (pc != currentCoord)
        {
            if (rooms.ContainsKey(pc))
            {
                Vector2Int from = currentCoord;
                int entryDir = -1;
                if (rooms.ContainsKey(from))
                {
                    int d = DirFromDelta(pc - from);
                    if (d >= 0) entryDir = (d + 2) % 4;
                }
                EnterRoom(rooms[pc], from, entryDir);
            }
            // 若不在任何房间（异常掉出），不处理
        }

        if (currentRoom == null) return;

        currentRoom.aliveEnemies.RemoveAll(e => e == null);

        if (currentRoom.type != DungeonRoomType.Shop && !currentRoom.cleared)
        {
            if (currentRoom.spawningDone && currentRoom.aliveEnemies.Count == 0)
            {
                OnRoomCleared(currentRoom);
            }
        }
    }

    void OnRoomCleared(Room room)
    {
        room.cleared = true;

        if (GameManager.Instance != null) GameManager.Instance.AddRoomCleared();
        Debug.Log("[Dungeon] OnRoomCleared room=" + room.roomIndex + " entryDir=" + room.entryDir);

        bool openedAny = false;

        for (int d = 0; d < 4; d++)
        {
            if (d == room.entryDir) continue;
            if (room.doorBlockers.Count <= d) continue;
            if (room.doorBlockers[d] == null) continue;
            ApplyDoorBlocker(room.doorBlockers[d], false);
            openedAny = true;
            Debug.Log("[Dungeon] OnRoomCleared: open OWN door room=" + room.roomIndex + " d=" + d);
        }

        for (int d = 0; d < 4; d++)
        {
            if (d == room.entryDir) continue;
            if (room.doorBlockers.Count > d && room.doorBlockers[d] != null) continue;
            Vector2Int nc = room.coord + DirVec2D(d);
            Room nb;
            if (!rooms.TryGetValue(nc, out nb) || nb == null) continue;
            int opposite = (d + 2) % 4;
            if (nb.doorBlockers.Count > opposite && nb.doorBlockers[opposite] != null)
            {
                Debug.Log("[Dungeon] OnRoomCleared: open NB door room=" + room.roomIndex + " d=" + d + " nb=" + nb.roomIndex);
                ApplyDoorBlocker(nb.doorBlockers[opposite], false);
                openedAny = true;
            }
        }

        if (!openedAny && room.entryDir >= 0)
        {
            Vector2Int nc = room.coord + DirVec2D(room.entryDir);
            Room nb;
            if (rooms.TryGetValue(nc, out nb) && nb != null)
            {
                int opposite = (room.entryDir + 2) % 4;
                if (nb.doorBlockers.Count > opposite && nb.doorBlockers[opposite] != null)
                {
                    Debug.Log("[Dungeon] OnRoomCleared: no exits, fallback open entry nb=" + nb.roomIndex);
                    ApplyDoorBlocker(nb.doorBlockers[opposite], false);
                }
            }
        }

        if (uiManager != null) uiManager.ShowPersistentToast("已通关！走向任意出口前往下一间");
        if (showDebugLogs) Debug.Log("[Dungeon] 房间 #" + room.roomIndex + " 已清空，开启出口");
    }

    public int GetRoomIndex() => currentRoom != null ? currentRoom.roomIndex : 0;

    void OnDestroy()
    {
        // 还原环境光与方向光
        RenderSettings.ambientMode = _origAmbientMode;
        RenderSettings.ambientLight = _origAmbient;
        foreach (var kv in _dirLightMasks) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
        _dirLightMasks.Clear();

        foreach (var kv in rooms)
        {
            if (kv.Value != null)
            {
                foreach (var e in kv.Value.aliveEnemies) if (e != null) Destroy(e);
                if (kv.Value.shopObject != null) Destroy(kv.Value.shopObject);
                if (kv.Value.root != null) Destroy(kv.Value.root);
            }
        }
        rooms.Clear();
        decoLights.Clear();
    }
}

