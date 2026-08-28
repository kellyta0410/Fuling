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
    public float wallDecorationHeight = 2f;     // 离地高度
    public float wallDecorationInset = 0.25f;   // 距墙面往房内退一点，避免嵌进墙里
    public List<GameObject> wallDecorations = new List<GameObject>();

    [Header("房间中央顶灯（每间房生成一个，拖入你的 Point Light 预制体；Mode 设 Realtime）")]
    public GameObject roomLightPrefab;
    public float roomLightHeightOffset = 1f;   // 在 wallHeight 之上再抬一点
    [Tooltip("周围暗房（邻居）放到的 Layer 索引；该层会被房间灯光排除，使邻居保持黑暗。请确保该 Layer 在 Layer 设置里存在（如 Layer 8）。当前活动房间仍用原层，不影响碰撞。")]
    public int darkLayer = 8;                 // 邻居暗房专用层：灯光不照这一层，故保持黑暗

    [Header("外观：外面变黑，看不到房间外的 void")]
    public bool blackOutside = true;           // 把主相机背景设为纯黑，门外/墙外的虚空不可见

    [Header("商店 Buff（拖入 BuffDataSO 资产；留空自动在 Resources 找）")]
    public List<BuffDataSO> shopBuffs = new List<BuffDataSO>();

    [Header("商店物体预制体（在预制体上挂 ShopItem 组件即可；留空则运行时自动生成一个简单 Shop 物体）")]
    public GameObject shopPrefab;

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

    [Header("死墙（封死的实心墙，无门；留空则退化为程序生成的实心方块）")]
    public GameObject solidWallPrefab;

    // ---------- 运行时状态 ----------
    private int roomIndex = 0;           // 物理房间序号（含商店房）
    private int levelIndex = 0;          // 战斗关序号（商店房不计入，用于顶部“第N关”显示）
    private DungeonRoomType currentType;
    private GameObject roomRoot;
    private Vector2Int currentCoord = Vector2Int.zero;
    private Dictionary<Vector2Int, GameObject> roomRoots = new Dictionary<Vector2Int, GameObject>();
    private bool[] deadWallDirs = new bool[4]; // 本房间哪些方向是“死墙”（无暗房、永久封死、不可走）
    private List<GameObject> aliveEnemies = new List<GameObject>();
    private Color _origAmbient = Color.white;
    private UnityEngine.Rendering.AmbientMode _origAmbientMode = UnityEngine.Rendering.AmbientMode.Flat;
    private Dictionary<Light, int> _dirLightMasks = new Dictionary<Light, int>(); // 记录方向光原始 cullingMask，退出时还原

    private List<List<GameObject>> doorBlockers = new List<List<GameObject>>();
    private Dictionary<GameObject, float> swingAngleFor = new Dictionary<GameObject, float>();
    private Dictionary<GameObject, Vector3> swingAxisFor = new Dictionary<GameObject, Vector3>();
    private Dictionary<GameObject, Coroutine> doorSwingRoutine = new Dictionary<GameObject, Coroutine>();
    private Dictionary<GameObject, Quaternion> doorClosedRot = new Dictionary<GameObject, Quaternion>();
    [Tooltip("代码驱动开门：门绕本地 Y 轴旋转的角度（父墙只绕 Y 旋转，本地 Y=世界 Y，四种墙方向一致）")]
    public float doorSwingAngle = 90f;
    [Tooltip("门旋转绕的轴（门的本地空间）。普通竖直门绕 Y 轴；若模型朝向不同（如导入后铰链在 Z）则设为 Z。")]
    public Vector3 doorSwingAxis = Vector3.forward;
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
        RenderSettings.ambientLight = Color.black;

        // darkLayer 仅用于把邻居暗房排除出房间灯光；强制它与所有层碰撞，避免暗房碰撞失效
        if (darkLayer > 0)
            for (int L = 0; L < 32; L++)
                Physics.IgnoreLayerCollision(darkLayer, L, false);

        // 地牢专属 BGM：覆盖普通游戏内 BGM
        if (AudioManager.Instance != null) AudioManager.Instance.PlayDungeonBGM();

        if (showDebugLogs) Debug.Log("[Dungeon] 地牢系统启动，生成第 1 个房间");
        StartCoroutine(TransitionToCoord(Vector2Int.zero, -1));
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
    IEnumerator TransitionToCoord(Vector2Int newCoord, int entryDir)
    {
        advancing = true;
        try
        {
            this.entryDir = entryDir;

        // 清掉所有已有房间（当前房 + 周围暗房），旧房即被销毁，之后封闭不可返回
        foreach (var kv in roomRoots) if (kv.Value != null) Destroy(kv.Value);
        roomRoots.Clear();
        if (roomRoot != null) Destroy(roomRoot);
        roomRoot = null;
        // NavMeshSurface 是上一间房 roomRoot 的子物体，随房销毁；必须清空引用，
        // 否则下一间 BuildNavMesh 会对“已销毁对象”调用 RemoveData 而抛异常，导致后续房间全部中断生成。
        navMeshSurface = null;
        foreach (var kv in doorSwingRoutine) if (kv.Value != null) StopCoroutine(kv.Value);
        // 敌人未挂到 roomRoot 下（parent=null），必须显式销毁，否则会残留在旧房间坐标
        foreach (var e in aliveEnemies) if (e != null) Destroy(e);
        aliveEnemies.Clear();
        doorBlockers.Clear();
        doorSwingRoutine.Clear();
        doorClosedRot.Clear();
        swingAngleFor.Clear();
        swingAxisFor.Clear();
        roomCleared = false;
        doorOpen = new bool[4];

        roomIndex++;

        // 每 6 个物理房间插入一个商店房（商店不计入“关”序号）
        if (roomIndex % 6 == 0)
            currentType = DungeonRoomType.Shop;
        else
        {
            currentType = (DungeonRoomType)Random.Range(0, 3);
            levelIndex++;
        }

        // 随机把部分“非入口”方向做成死墙（无暗房、永久封死），但保证至少留一个可开门
        deadWallDirs = new bool[4];
        if (entryDir >= 0)
        {
            List<int> cand = new List<int>();
            for (int i = 0; i < 4; i++) if (i != entryDir) cand.Add(i);
            int maxDead = Mathf.Min(2, cand.Count - 1); // 至多封死 2 个，至少留 1 个可开门
            int deadCount = Random.Range(1, maxDead + 1); // 至少封死 1 个，确保“死墙”可见（不是每面都带门）
            for (int k = 0; k < deadCount; k++)
            {
                int pick = Random.Range(0, cand.Count);
                deadWallDirs[cand[pick]] = true;
                cand.RemoveAt(pick);
            }
        }

        // —— 建当前（活动）房间 ——
        GameObject root = new GameObject("DungeonRoom_" + roomIndex + "_" + currentType);
        roomRoot = root;
        roomRoots[newCoord] = root;

        BuildFloor(root.transform);                    // 先建地板（会按地板尺寸确定 roomSize）
        root.transform.position = CoordToWorld(newCoord); // 再按当前 roomSize 摆放房间中心
        BuildWalls(root.transform, doorBlockers, -1, true);
        PlaceDecorations(root.transform);
        PlaceWallDecorations(root.transform);
        PlaceRoomLight(root.transform);

        // 当前活动房间保持原层（碰撞/导航不受影响）；邻居暗房才放到 darkLayer

        // 门：
        // 战斗/普通房：入口（你进来的那扇）打开（可回头），其余门先关，清完怪后由 OnRoomCleared 全部打开；
        // 商店房：入口（通过的门）关闭，其余门一开始即开。
        for (int i = 0; i < 4; i++)
        {
            if (deadWallDirs[i]) continue; // 死墙保持封死，不参与开关
            if (currentType == DungeonRoomType.Shop)
            {
                if (i == entryDir) { doorOpen[i] = false; SetDoorBlocker(i, true); }  // 关通过的门
                else { doorOpen[i] = true; SetDoorBlocker(i, false); }                // 其余开着
            }
            else
            {
                if (i == entryDir) { doorOpen[i] = true; SetDoorBlocker(i, false); }  // 入口打开（可回头）
                else { doorOpen[i] = false; SetDoorBlocker(i, true); }                // 其余先关，清怪后开
            }
        }

        // 先在本帧内把玩家传送进新房间的地面安全点，再 yield，避免悬空/掉落
        if (playerTarget != null)
        {
            Vector3 playerPos = CoordToWorld(newCoord);
            if (entryDir >= 0)
                playerPos += DirVec[entryDir] * (roomSize * 0.5f - entryInset);
            playerPos.y = 0.5f;
            playerTarget.position = playerPos;
            CharacterController cc = playerTarget.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }

        yield return null;
        BuildNavMesh();
        yield return null;

        currentCoord = newCoord;

        if (uiManager != null) uiManager.SetRoomDisplay(levelIndex, currentType == DungeonRoomType.Shop);
        if (GameManager.Instance != null) GameManager.Instance.SetDungeonRoom(roomIndex);

        if (showDebugLogs) Debug.Log("[Dungeon] 生成房间 #" + roomIndex + " 类型=" + currentType + " 坐标=" + newCoord);

        // 生成四周暗房（仍黑暗、无怪），仅朝向当前房的门打开，便于玩家走过去后再激活
        for (int d = 0; d < 4; d++)
        {
            int back = (entryDir + 2) % 4;
            if (entryDir >= 0 && d == back) continue; // 来时方向是已销毁的旧房，不建暗房
            if (deadWallDirs[d]) continue;            // 死墙方向：只是一堵封死的墙，不建暗房
            Vector2Int nc = newCoord + DirVec2D(d);
            if (roomRoots.ContainsKey(nc)) continue;
            BuildShell(nc, (d + 2) % 4);
        }

        // 左右门进入时，先等相机转到玩家背面再开始刷怪/商店；正前/正后进入则直接进入
        BeginRoomContent(entryDir);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Dungeon] 房间切换异常，已重置状态避免卡死：" + ex.Message);
            Debug.LogException(ex);
        }
        finally
        {
            advancing = false;
        }
    }

    // 暗房：相邻拼接的“背景房间”，保持黑暗、不刷怪、不装饰。
    // 朝向当前房的这面墙不建——由当前房提供，两房之间只有一堵共用墙，门自然对齐。
    void BuildShell(Vector2Int coord, int facingDir)
    {
        GameObject root = new GameObject("DarkRoom_" + coord.x + "_" + coord.y);
        roomRoots[coord] = root;

        var doors = new List<List<GameObject>>();
        BuildFloor(root.transform);
        root.transform.position = CoordToWorld(coord);
        BuildWalls(root.transform, doors, facingDir); // 跳过朝向当前房的那面墙
        PlaceWallDecorations(root.transform); // 周围暗房也预先摆好墙面装饰

        // 暗房其余三面墙封闭（纯背景墙，被墙挡住看不见里面，呈黑暗）
        for (int d = 0; d < 4; d++)
        {
            if (d == facingDir) continue;
            ApplyDoorBlocker(doors, d, true);
        }

        // 注意：必须在地板/墙/装饰都建完之后再把整棵子树放到 darkLayer，
        // 否则后建的几何仍留在默认层 → 会被房间灯光照亮，暗房就不黑了。
        SetLayerRecursively(root, darkLayer);
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
        // 玩家面向房间内（-DirVec[entryDir]），这是房间内容的基准朝向
        Vector3 intoRoom = -DirVec[entryDir];
        intoRoom.y = 0;

        CinemachineBrain brain = null;
        Camera cam = Camera.main;
        if (cam != null) brain = cam.GetComponent<CinemachineBrain>();
        bool tookOver = false;

        // 固定偏移的 Cinemachine 镜头不会随玩家转身而转，因此进房时临时接管相机：
        // 关掉 Brain，把 Camera.main 平滑转到玩家背后、看向房间内，到位后再把控制权交还。
        if (cam != null && brain != null)
        {
            brain.enabled = false;
            tookOver = true;
        }

        // 先让玩家面向房内（仅当其未操作时；有输入则交由 PlayerController 控制）
        if (intoRoom.sqrMagnitude > 1e-6f && playerTarget != null)
            playerTarget.rotation = Quaternion.LookRotation(intoRoom);

        float timer = 0f;
        bool aligned = false;
        while (timer < camAlignMaxWait)
        {
            timer += Time.deltaTime;

            if (playerTarget != null) playerTarget.rotation = Quaternion.LookRotation(intoRoom);

            if (tookOver && playerTarget != null)
            {
                Vector3 playerPos = playerTarget.position;
                // 目标：站在玩家身后（沿 -intoRoom）、抬高的位置，看向房间内
                Vector3 targetPos = playerPos - intoRoom * entryCamDistance + Vector3.up * entryCamHeight;
                Vector3 lookAt = playerPos + intoRoom * 2f;
                cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, entryCamTurnSpeed * Time.deltaTime);
                Quaternion targetRot = Quaternion.LookRotation((lookAt - cam.transform.position).normalized);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, entryCamTurnSpeed * Time.deltaTime);

                Vector3 fwd = cam.transform.forward; fwd.y = 0;
                if (fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, intoRoom) < camAlignAngle)
                {
                    aligned = true;
                    break;
                }
            }
            else if (cam != null)
            {
                // 没接管相机时（理论上不会走到），仍按前向判断对齐
                Vector3 fwd = cam.transform.forward; fwd.y = 0;
                if (fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, intoRoom) < camAlignAngle)
                {
                    aligned = true;
                    break;
                }
            }
            yield return null;
        }

        // 让转好的镜头停留片刻，确保玩家看清房内，再交还 Cinemachine（交还瞬间会切回常规固定镜头）
        if (tookOver && playerTarget != null)
        {
            float hold = 0.35f;
            while (hold > 0f)
            {
                hold -= Time.deltaTime;
                Vector3 playerPos = playerTarget.position;
                Vector3 targetPos = playerPos - intoRoom * entryCamDistance + Vector3.up * entryCamHeight;
                Vector3 lookAt = playerPos + intoRoom * 2f;
                cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, entryCamTurnSpeed * Time.deltaTime);
                Quaternion targetRot = Quaternion.LookRotation((lookAt - cam.transform.position).normalized);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, entryCamTurnSpeed * Time.deltaTime);
                yield return null;
            }
        }

        // 无论对齐与否，都把相机控制权还给 Cinemachine，避免影响正常游玩镜头
        if (tookOver && brain != null) brain.enabled = true;

        if (showDebugLogs) Debug.Log("[Dungeon] 相机就位（" + (aligned ? "已对齐" : "超时") + (tookOver ? "，已接管并交还" : "") + "），开始房间内容");
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
            EnsureLightsEnabled(floor);
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
            bc.size = new Vector3(roomSize, 0.1f, roomSize);
            bc.isTrigger = false;
        }

        // 永远保留一块与房间等大的实心地面碰撞体（挂在 roomRoot 下，独立于地板预制体），
        // 确保玩家/敌人无论地板预制体碰撞体是否正常，都绝不穿透坠落。
        GameObject groundCol = new GameObject("GroundCollider");
        groundCol.transform.SetParent(parent);
        groundCol.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        BoxCollider gbc = groundCol.AddComponent<BoxCollider>();
        gbc.size = new Vector3(roomSize, 0.1f, roomSize);
        gbc.center = Vector3.zero;
        gbc.isTrigger = false;
    }

    private Vector2Int DirVec2D(int dir) => new Vector2Int((int)Mathf.Round(DirVec[dir].x), (int)Mathf.Round(DirVec[dir].z));
    private Vector3 CoordToWorld(Vector2Int c) => new Vector3(c.x * roomSize, 0f, c.y * roomSize);

    void BuildWalls(Transform parent, List<List<GameObject>> doorOut, int skipDir = -1, bool applyDead = false)
    {
        float half = roomSize * 0.5f;
        // 顺序必须严格是 N(0) / E(1) / S(2) / W(3)，与 DirVec、doorOpen 的索引一致
        if (skipDir != 0) BuildWall(parent, new Vector3(0, 0, half), Quaternion.identity, 0, doorOut, applyDead);
        if (skipDir != 1) BuildWall(parent, new Vector3(half, 0, 0), Quaternion.Euler(0, 90, 0), 1, doorOut, applyDead);
        if (skipDir != 2) BuildWall(parent, new Vector3(0, 0, -half), Quaternion.Euler(0, 180, 0), 2, doorOut, applyDead);
        if (skipDir != 3) BuildWall(parent, new Vector3(-half, 0, 0), Quaternion.Euler(0, -90, 0), 3, doorOut, applyDead);
    }

    void BuildWall(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex, List<List<GameObject>> doorOut, bool applyDead)
    {
        if (applyDead && deadWallDirs[dirIndex])
            BuildSolidWall(parent, localPos, localRot, dirIndex);
        else
            BuildWallWithDoor(parent, localPos, localRot, dirIndex, doorOut);
    }

    // 死墙：完全封死的实心墙，没有门洞，也不会开/关，更不会生成暗房或出口。
    void BuildSolidWall(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex)
    {
        GameObject wallParent = new GameObject("SolidWall_" + dirIndex);
        wallParent.transform.SetParent(parent);
        wallParent.transform.localPosition = localPos;
        wallParent.transform.localRotation = localRot;

        GameObject mesh = null;
        if (solidWallPrefab != null)
        {
            mesh = Instantiate(solidWallPrefab, wallParent.transform);
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
        // 补碰撞与导航障碍（实心，完全挡住，但无门可开关）
        if (mesh.GetComponent<Collider>() == null)
            mesh.AddComponent<BoxCollider>();
        var obs = mesh.AddComponent<NavMeshObstacle>();
        obs.carving = true;
    }

    void BuildWallWithDoor(Transform parent, Vector3 localPos, Quaternion localRot, int dirIndex, List<List<GameObject>> doorOut)
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

            // 让门洞精确居中：若预制体里的门偏离几何中心，相邻房间的门会左右错开对不上，
            // 玩家从这一侧走出去会落进隔壁暗房（黑屏）。这里把整面墙平移，使门对齐墙中线，
            // 两边房间的门就重合了，且退出检测(CheckExit)的“门口中心”也正好对上视觉门口。
            if (leaves.Count > 0)
            {
                Bounds dbc = new Bounds();
                bool f = true;
                foreach (var lf in leaves)
                {
                    var lrs = lf.GetComponentsInChildren<Renderer>();
                    foreach (var r in lrs)
                    {
                        if (f) { dbc = r.bounds; f = false; }
                        else dbc.Encapsulate(r.bounds);
                    }
                }
                if (!f)
                {
                    Vector3 localOffset = wallParent.transform.InverseTransformPoint(dbc.center);
                    localOffset.y = 0f;
                    wall.transform.localPosition -= localOffset;
                }
            }
            return;
        }


    }

    // 通用的门开/关：给定一组门的叶子列表（某房间/暗房的某方向），绕门“本地轴”旋转并开关碰撞/导航。
    void ApplyDoorBlocker(List<List<GameObject>> blocks, int dirIndex, bool block)
    {
        if (dirIndex < 0 || dirIndex >= blocks.Count) return;
        var list = blocks[dirIndex];
        if (list == null) return;
        foreach (var b in list)
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

    void SetDoorBlocker(int dirIndex, bool block)
    {
        ApplyDoorBlocker(doorBlockers, dirIndex, block);
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
        List<Bounds> placedBounds = new List<Bounds>(); // 已摆放装饰的世界包围盒，用于互相避让（防穿模）
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
            if (r != null)
            {
                Bounds b = r.bounds;
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

        int count = Mathf.Max(3, Random.Range(wallDecorationMin, wallDecorationMax + 1));
        float half = roomSize * 0.5f;
        float limit = half - wallThickness - 0.2f;

        // 每面墙都带门（门在墙正中 along≈0），避开门洞区域，避免装饰压在门前
        float doorClear = doorWidth * 0.5f + wallThickness + 1.5f;
        // 装饰中心目标高度：至少为墙高的 0.4 倍，避免贴地/过低
        float targetCenterY = parent.position.y + Mathf.Max(wallDecorationHeight, wallHeight * 0.6f); // 调高一点，装饰更靠墙上部

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

            // 确定方向：物体正面(+Z)朝向房间内（即 -DirVec[d]），保证上墙后不朝墙。
            // 保留预制体里作者调好的额外旋转：最终朝向 = 房间朝向 × 预制体自身旋转，避免被覆盖成 (0,0,0)。
            Quaternion rot = Quaternion.LookRotation(-DirVec[d]);

            GameObject deco = Instantiate(prefab, parent);
            Quaternion authored = deco.transform.localRotation; // 预制体里设好的旋转
            deco.transform.localRotation = rot * authored;
            EnsureLightsEnabled(deco);

            // 按装饰自身实际深度，把中心往房内退，使“背面正好贴在内墙面内侧”，避免插进墙里（穿模）
            Renderer r = deco.GetComponentInChildren<Renderer>();
            float extentAlongNormal = 0f;
            if (r != null)
            {
                // 旋转为 0/90/180 倍数，AABB 与世界轴对齐：沿法线的半深 = 对应世界轴 extents
                extentAlongNormal = (d == 0 || d == 2) ? r.bounds.extents.z : r.bounds.extents.x;
            }
            float wallInner = half - wallThickness;
            float backDist = wallInner - (extentAlongNormal + 0.1f); // 中心到房心的距离
            if (backDist < 0.05f) backDist = 0.05f;

            Vector3 pos;
            if (d == 0)      pos = new Vector3(along, 0, backDist);
            else if (d == 1) pos = new Vector3(backDist, 0, along);
            else if (d == 2) pos = new Vector3(along, 0, -backDist);
            else             pos = new Vector3(-backDist, 0, along);

            deco.transform.localPosition = pos;

            // 让装饰“垂直中心”落在目标高度（兼容不同 pivot，避免整体偏低）
            if (r != null)
            {
                float dy = targetCenterY - r.bounds.center.y;
                if (Mathf.Abs(dy) > 0.001f) deco.transform.position += Vector3.up * dy;
            }
            if (deco.GetComponent<Collider>() == null) deco.AddComponent<BoxCollider>();
            placed++;
        }
        if (showDebugLogs) Debug.Log("[Dungeon] 摆放墙壁装饰 " + placed + " 个");
    }

    void PlaceRoomLight(Transform parent)
    {
        // 灯光照“除 darkLayer(邻居暗房) 之外”的所有层，保证只点亮当前活动房间
        int lightMask = ~(1 << darkLayer);

        // 1) 中央顶灯（Point）：主光 + 阴影，强度 50、范围覆盖全房；靠 cullingMask 隔绝邻居
        if (roomLightPrefab != null)
        {
            GameObject lightObj = Instantiate(roomLightPrefab, parent);
            lightObj.name = "RoomLight";
            EnsureLightsEnabled(lightObj);
            lightObj.transform.localPosition = new Vector3(0, wallHeight + roomLightHeightOffset, 0);
            Light lt = lightObj.GetComponent<Light>();
            if (lt != null)
            {
                lt.shadows = LightShadows.Soft;
                lt.shadowStrength = 1f;
                lt.intensity = 50f;
                lt.range = roomSize * 0.9f;
                lt.cullingMask = lightMask;
            }
        }

        // 2) 两排点光（前、后各一排，像吸顶灯阵列）：远离四壁、高架，
        //    多点光从房间内部打亮每面墙，避免背光墙变纯黑；邻居在 darkLayer 被排除 → 依旧黑暗。
        float half = roomSize * 0.5f;
        float h = wallHeight * 0.75f;          // 高架，像吊灯/吸顶灯
        int rowsZ = 2;                          // 前、后各一排
        int colsX = 3;                          // 每排 3 盏（沿 x 排开）
        for (int r = 0; r < rowsZ; r++)
        {
            float z = (r == 0 ? -1f : 1f) * half * 0.5f; // 前后两排，离墙更远
            for (int c = 0; c < colsX; c++)
            {
                float x = (colsX == 1) ? 0f : (c / (float)(colsX - 1) * 2f - 1f) * half * 0.5f;
                GameObject pl = new GameObject("RoomLamp_" + r + "_" + c);
                pl.transform.SetParent(parent, false);
                pl.transform.localPosition = new Vector3(x, h, z);
                Light pll = pl.AddComponent<Light>();
                pll.type = LightType.Point;
                pll.cullingMask = lightMask;
                pll.intensity = 50f;
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
        foreach (var lt in go.GetComponentsInChildren<Light>(true))
            lt.enabled = true;
    }

    void BuildNavMesh()
    {
        if (roomRoot == null) return;
        // 每次都销毁旧 NavMeshSurface 并新建，避免跨房间残留 / 引用失效导致后续房间 NavMesh 不烘焙
        if (navMeshSurface != null)
        {
            navMeshSurface.RemoveData();
            Destroy(navMeshSurface.gameObject);
            navMeshSurface = null;
        }
        int groundLay = LayerMask.NameToLayer("Ground");
        navMeshSurface = new GameObject("NavMeshSurface (Dungeon)").AddComponent<NavMeshSurface>();
        navMeshSurface.layerMask = (groundLay != -1) ? (1 << groundLay) : ~0;
        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.defaultArea = 0;
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
            damage = Mathf.Min(1f + (room - 1) * 0.0278f, 1.5f);
        }
    }

    // 敌人生成间隔随房间数递减：前 4 间 3s，5~9 间 2.5s，10 间起 2s
    float GetSpawnInterval()
    {
        if (roomIndex >= 6) return 1.75f;  // 6 起（含 10+）= 1.75s
        return 2f;                          // 1-5 = 2s
    }

    void SpawnEnemiesForRoom()
    {
        StartCoroutine(SpawnRoomEnemies());
    }

    IEnumerator SpawnRoomEnemies()
    {
        spawningDone = false;
        spawnedCount = 0;

        // 无限(地牢)模式敌人房间上限：1-10 房最多 20 只，11 房起最多 30 只（商店房不走这里）
        int cap = (roomIndex <= 10) ? 20 : 30;

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
            yield return new WaitForSeconds(GetSpawnInterval());
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
                // 地牢：整间房都是追击范围（按当前房间尺寸覆盖，确保覆盖全房）
                ai.ForceDirectChase(true);
                ai.infiniteDetectionRange = roomSize * 1.5f;
                ai.infiniteLoseTargetRange = roomSize * 2f;
            }

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
        GameObject shop;
        if (shopPrefab != null)
        {
            // 使用你提供的商店预制体，并在房间中央生成
            shop = Instantiate(shopPrefab, pos, Quaternion.identity);
            shop.name = "Shop";
            ShopItem item = shop.GetComponent<ShopItem>();
            if (item != null) item.Setup(buffs, this, 100);
            else Debug.LogWarning("[Dungeon] shopPrefab 上没有 ShopItem 组件，请在其上挂 ShopItem.cs");
        }
        else
        {
            // 兜底：运行时自动创建简单的 Shop 物体
            shop = new GameObject("Shop");
            shop.transform.position = pos;
            shop.AddComponent<ShopItem>().Setup(buffs, this, 100);
        }
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

        // 地牢（无尽）模式：累计已通关房间数（用于结算显示）
        if (GameManager.Instance != null) GameManager.Instance.AddRoomCleared();

        // 开启除入口方向（战斗房入口本就开着、可回头）、死墙方向以外的所有出口；死墙永久封死
        for (int i = 0; i < 4; i++)
        {
            if (i == entryDir) continue;
            if (deadWallDirs[i]) continue; // 死墙清场也不开
            doorOpen[i] = true;
            SetDoorBlocker(i, false);
        }

        if (uiManager != null) uiManager.ShowBuffToast("已通关！走向任意出口前往下一间");
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
            if (along <= half - 1.2f) continue; // 还未走到门口
            Vector3 perp = local - DirVec[d] * along;
            if (perp.magnitude > Mathf.Max(doorWidth * 0.9f, 1.5f)) continue; // 没对准门洞（放宽，避免偏移导致不触发）
            Advance(d);
            return;
        }
    }

    void Advance(int dir)
    {
        if (advancing) return;
        if (showDebugLogs) Debug.Log("[Dungeon] 玩家从 " + dir + " 方向前往新房间");
        // 新房间按“网格坐标”相邻拼接（中心相距正好 roomSize），踏出门即落在下一间地板，不会掉进 void。
        Vector2Int newCoord = currentCoord + DirVec2D(dir);
        int entryDir = (dir + 2) % 4; // 从对面墙进入新房间
        StartCoroutine(TransitionToCoord(newCoord, entryDir));
    }

    public int GetRoomIndex() => roomIndex;

    void OnDestroy()
    {
        // 还原环境光与方向光
        RenderSettings.ambientMode = _origAmbientMode;
        RenderSettings.ambientLight = _origAmbient;
        foreach (var kv in _dirLightMasks) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
        _dirLightMasks.Clear();

        foreach (var kv in roomRoots) if (kv.Value != null) Destroy(kv.Value);
        roomRoots.Clear();
        if (roomRoot != null) Destroy(roomRoot);
    }
}

