using UnityEngine;

public class SnakeBodyAnimation : MonoBehaviour
{
    [Header("===== 蛇身蠕动（伪分段顶点波）=====")]
    [Tooltip("视觉分段数（仅影响波浪密度）")]
    public int segmentCount = 10;
    [Tooltip("蠕动幅度（相对身长的比例）")]
    public float slitherAmplitude = 0.05f;
    [Tooltip("身体有几个 S 弯（越小越缓）")]
    public float slitherWaves = 1.5f;
    [Tooltip("波的传播速度（抽搐就调小）")]
    public float slitherSpeed = 2.2f;
    [Tooltip("摆动幅度沿身长递增的程度：尾端摆幅/前段摆幅")]
    public float tailGain = 1.4f;
    [Tooltip("静止时保留的微蠕动(0~1)")]
    public float idleSlither = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("前段(0~此值)保持直身不蠕动；蠕动集中在后半段。0=整条都动, 0.6=前面六成不动只甩尾")]
    public float stiffFront = 0.6f;
    [Tooltip("stiffFront 到蠕动区的平滑过渡宽度值")]
    public float stiffFeather = 0.12f;
    [Tooltip("直身段在身长的哪一端：关=0端(小端)直身, 开=1端(大端)直身。前后反了就切换它")]
    public bool stiffAtBigU = true;

    [Header("===== 转向跟随（路径跟随，无需参数）=====")]

    [Header("===== 调试预览 =====")]
    [Tooltip("Scene 中始终显示分段/波形预览")]
    public bool showGizmos = true;

    [Header("===== 攻击：前段抬起 + 头扑出 =====")]
    [Tooltip("攻击动作时长（秒）")]
    public float attackDuration = 0.35f;
    [Tooltip("抬起部分的身长比例（头+身体，如 0.5 = 10 段中前 5 段抬起）")]
    public float rearBodyEnd = 0.5f;
    [Tooltip("抬起最大角度（度）")]
    public float rearRaiseAngle = 60f;
    [Tooltip("头向前扑出的距离（相对身长的比例）")]
    public float headLungeRatio = 0.25f;
    [Tooltip("头在哪一端：关=小端(常见), 开=大端。抬起的若是尾巴就切换它")]
    public bool rearAtBigU = false;
    [Tooltip("抬/扑方向符号（头没上抬反而下压就设 -1）")]
    public float rearFlipSign = 1f;
    [Tooltip("离玩家这个距离内（米）保持立起蓄势，移动时才趴回")]
    public float poiseRange = 3f;
    [Tooltip("立起/趴下切换的平滑时长（秒）")]
    public float poiseLiftTime = 0.3f;

    [Header("===== 死亡倒下 =====")]
    public float deathFallDuration = 0.7f;
    [Tooltip("倒下支点抬高的高度（避免穿模进地底）")]
    public float deathFallLift = 0.5f;
    [Tooltip("倒下方向：1=朝玩家仆倒, -1=反向（仍是背对就改成 -1 的相反）")]
    public float fallDirection = -1f;
    [Tooltip("无玩家时的回退倒下轴（如 (1,0,0) 左右倒）")]
    public Vector3 fallAxis = new Vector3(1f, 0f, 0f);

    private MeshFilter meshFilter;
    private Mesh instanceMesh;
    private Vector3[] baseVertices;
    private Vector3[] tempVerts;
    private float bodyLen;
    private int longAxis;
    private int sideAxis;
    private float minU;
    private float maxU;

    private Animator animator;
    private EnemyAI enemyAI;

    private bool wasAttacking;
    private float attackTimer = 0f;
    private Bounds meshBounds;
    private float rearLift = 0f;

    // 蛇头走过的世界轨迹（转弯时身体沿此路径摆，自然 S 形且不穿墙）
    private const int TrailN = 1024;
    private readonly Vector3[] trailPos = new Vector3[TrailN];
    private int trailHead = 0;
    private int trailCount = 0;
    private Vector3 localSpineForwardStored = Vector3.forward;

    private int normalFrame = 0;

    private bool isFalling;
    private float fallProgress = 0f;
    private Vector3 fallStartPos;
    private Quaternion fallStartRot;
    private Vector3 fallPivotWorld;
    private Vector3 fallAxisWorld;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>();
        animator = GetComponentInParent<Animator>();
        enemyAI = GetComponentInParent<EnemyAI>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[SnakeBodyAnimation] 未找到有效的 MeshFilter（自身或子物体都没有），蛇身蠕动/攻击动画不会运行。挂点路径: " + TransformPath(transform), this);
            return;
        }

        // 克隆一份 mesh，避免污染原始资源
        instanceMesh = Instantiate(meshFilter.sharedMesh);
        instanceMesh.name = meshFilter.sharedMesh.name + "_SnakeAnim";
        meshFilter.mesh = instanceMesh;
        baseVertices = instanceMesh.vertices;
        tempVerts = new Vector3[baseVertices.Length];
        meshBounds = instanceMesh.bounds;

        Bounds b = instanceMesh.bounds;
        longAxis = (b.size.x >= b.size.y && b.size.x >= b.size.z) ? 0
                 : (b.size.y >= b.size.z ? 1 : 2);
        sideAxis = longAxis == 0 ? 2 : 0;
        bodyLen = Mathf.Max(b.size[longAxis], 0.0001f);
        minU = b.min[longAxis];
        maxU = b.max[longAxis];
        Debug.Log("[SnakeBodyAnimation] 蛇身初始化: 长轴=" + longAxis + " 侧轴=" + sideAxis
            + " 身长=" + bodyLen.ToString("F2") + " 顶点数=" + baseVertices.Length
            + " 包围盒size=" + b.size, this);

        // 蛇身很长，把分离半径按世界尺寸调到身长一半，避免互相穿插推挤
        if (enemyAI != null)
        {
            Vector3 worldSize = transform.TransformVector(b.size);
            float worldLen = Mathf.Max(worldSize[longAxis], worldSize[(longAxis + 1) % 3], worldSize[(longAxis + 2) % 3]);
            float want = worldLen * 0.5f;
            if (enemyAI.separationRadius < want) enemyAI.separationRadius = want;
        }
    }

    static string TransformPath(Transform t)
    {
        string s = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            s = t.name + "/" + s;
        }
        return s;
    }

    void Update()
    {
        if (instanceMesh == null || baseVertices == null) return;

        if (enemyAI != null && enemyAI.isDead)
        {
            UpdateDeathFall();
            return;
        }

        UpdateBody();
    }

    void UpdateBody()
    {
        // 攻击动作：检测攻击上升沿（优先 EnemyAI 状态，无骨骼模型 Animator 参数不可靠）
        bool attacking = enemyAI != null
            ? enemyAI.IsAttackingNow
            : (animator != null && animator.GetBool("IsAttacking"));
        if (attacking && !wasAttacking)
        {
            attackTimer = 1f;                       // 攻击动画 1 -> 0
        }
        wasAttacking = attacking;
        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime / Mathf.Max(attackDuration, 0.01f);
            if (attackTimer < 0f) attackTimer = 0f;
        }

        bool moving = enemyAI != null
            ? enemyAI.IsMovingNow
            : (animator != null && animator.GetBool("IsMoving"));
        float movingAmt = Mathf.Max(moving ? 1f : 0f, idleSlither);

        float time = Time.time * slitherSpeed;
        Vector3 sideVec = sideAxis == 0 ? Vector3.right
                        : sideAxis == 1 ? Vector3.up
                        : Vector3.forward;

        // —— 离玩家近就保持“立起蓄势”，移动才趴下 ——
        float distToPlayer = float.MaxValue;
        PlayerController pc = enemyAI != null ? enemyAI.Player : null;
        if (pc != null) distToPlayer = Vector3.Distance(transform.position, pc.transform.position);
        bool poising = !moving && pc != null && enemyAI != null && !enemyAI.isDead
            && distToPlayer <= Mathf.Max(poiseRange, 0.01f);
        rearLift = Mathf.MoveTowards(rearLift, (poising || attacking) ? 1f : 0f,
            Time.deltaTime / Mathf.Max(poiseLiftTime, 0.01f));

        // —— 路径跟随：记录蛇头世界轨迹，身体按离头的弧长摆在走过的路上（自然 S 形，不穿模）——
        Vector3 longUnit = Vector3.zero; longUnit[longAxis] = 1f;
        int headEndU = rearAtBigU ? 1 : 0;
        Vector3 headPivot = meshBounds.center;
        headPivot[longAxis] = rearAtBigU ? meshBounds.max[longAxis] : meshBounds.min[longAxis];
        Vector3 headWorld = meshFilter.transform.TransformPoint(headPivot);
        PushTrail(headWorld);
        float worldBody = meshFilter.transform.TransformVector(longUnit * bodyLen).magnitude;
        Vector3 localSpineForward = meshFilter.transform.TransformDirection((headEndU == 1 ? 1f : -1f) * longUnit).normalized;
        if (localSpineForward.sqrMagnitude < 0.5f) localSpineForward = Vector3.forward;
        localSpineForwardStored = localSpineForward;

        for (int i = 0; i < baseVertices.Length; i++)
        {
            Vector3 v = baseVertices[i];
            float u = Mathf.Clamp01((v[longAxis] - minU) / bodyLen);
            Vector3 o = Vector3.zero;

            // 蠕动：直身段不动，摆动幅度沿身长递增(尾大、头小)
            float stiffU = stiffAtBigU ? (1f - stiffFront) : stiffFront;
            float ramp = stiffAtBigU
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffFeather, 0.001f)))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - stiffU) / Mathf.Max(stiffFeather, 0.001f)));
            float p = stiffAtBigU ? Mathf.Clamp01((u - stiffU) / Mathf.Max(1f - stiffU, 0.001f))
                                  : Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffU, 0.001f));
            float gain = Mathf.Lerp(0.3f, 1f, Mathf.Pow(p, Mathf.Max(tailGain, 0.2f)));
            float wave = Mathf.Sin(u * slitherWaves * Mathf.PI * 2f + time);
            o[sideAxis] += wave * (bodyLen * slitherAmplitude) * movingAmt * ramp * gain;

            Vector3 point = v + o;

            // 攻击动作：立身扑击（已移除冲撞，固定执行）
            // 攻击窗口内头部额外前扑（立起高度由 rearLift 平滑保持）
            float t = 1f - attackTimer;                                         // 攻击中 0→1
            float lunge = (attackTimer > 0f && t >= 0.45f && t <= 0.85f)
                ? Mathf.Sin(Mathf.Clamp01((t - 0.45f) / 0.4f) * Mathf.PI)       // 0→1→0
                : 0f;

            // “头”所在端（rearAtBigU 切换头尾）
            bool inHead = rearAtBigU ? (u >= 1f - rearBodyEnd) : (u <= rearBodyEnd);

            if (inHead)
            {
                // 头+身体以 rearBodyEnd 处为轴抬起（立身姿态/攻击共用）
                float angle = rearLift * rearRaiseAngle * rearFlipSign * Mathf.Deg2Rad;
                Vector3 pivot = v;
                pivot[longAxis] = rearAtBigU
                    ? (maxU - rearBodyEnd * bodyLen)
                    : (minU + rearBodyEnd * bodyLen);
                Vector3 dir = v - pivot;
                point += (Quaternion.AngleAxis(angle * Mathf.Rad2Deg, sideVec) * dir) - dir;
            }

            // 抢出的那一段（头端）再向前扑出一点
            if (lunge > 0f && inHead)
            {
                float part = rearAtBigU ? (u - (1f - rearBodyEnd)) / Mathf.Max(rearBodyEnd, 0.001f)
                                        : u / Mathf.Max(rearBodyEnd, 0.001f);
                float falloff = Mathf.Pow(Mathf.Clamp01(1f - part), 2f);
                Vector3 longVec = Vector3.zero; longVec[longAxis] = 1f;
                Vector3 headDir = rearAtBigU ? longVec : -longVec;
                point += headDir * (bodyLen * headLungeRatio) * lunge * falloff;
            }

            // 路径跟随：按"离头的弧长"在轨迹上取基准点，朝向沿该处切线，身体自然弯成走过的路
            float s = Mathf.Abs(u - headEndU) * worldBody;      // 网格局部 u→世界弧长
            Vector3 centerW, tangW;
            SampleTrail(s, out centerW, out tangW);
            if (tangW.sqrMagnitude < 0.001f) tangW = localSpineForward;

            // 局部"沿长轴拉伸后垂直方向"：把该顶点在切面里的偏移（蠕动/抬身）转到轨迹切线坐标系；
            // 纵向（长轴方向）偏移会丢掉 y 的部分但保留"沿切线前推"（头扑击）
            Vector3 cLocal = meshBounds.center;
            cLocal[longAxis] = Mathf.Lerp(minU, maxU, u);
            Vector3 offLocal = point - cLocal;                  // 该顶点相对中心线的局部偏移（含蠕动+抬身+扑击）
            float longComp = offLocal[longAxis];                // 往前扑的纵向分量
            offLocal[longAxis] = 0f;                            // 长轴位置交给轨迹弧长，纵向上只保留前推
            Quaternion q = Quaternion.FromToRotation(localSpineForward, tangW.normalized);
            Vector3 offW = q * transform.TransformVector(offLocal);

            // 纵向前推按世界比例换算，沿轨迹切线叠加
            float worldPerLocal = worldBody / Mathf.Max(bodyLen, 0.0001f);
            tempVerts[i] = transform.InverseTransformPoint(centerW + offW + tangW.normalized * (longComp * worldPerLocal));
        }

        instanceMesh.vertices = tempVerts;
        if (++normalFrame % 3 == 0)
        {
            try { instanceMesh.RecalculateNormals(); }
            catch { /* 法线不可重算时跳过，不影响顶点蠕动 */ }
        }
    }

    // 记录蛇头走过的轨迹点（世界坐标），供身体路径跟随
    void PushTrail(Vector3 p)
    {
        if (trailCount > 0 &&
            Vector3.Distance(trailPos[(trailHead - 1 + TrailN) % TrailN], p) < 0.02f)
            return;                                     // 位移太小不重复记录，省内存
        trailPos[trailHead] = p;
        trailHead = (trailHead + 1) % TrailN;
        if (trailCount < TrailN) trailCount++;
    }

    // 在轨迹上取"离最新点往回 arcLen 米"的位置与朝向
    void SampleTrail(float arcLen, out Vector3 pos, out Vector3 dir)
    {
        pos = trailPos[(trailHead - 1 + TrailN) % TrailN];
        dir = localSpineForwardStored;
        if (trailCount < 2) return;
        Vector3 prev = pos;
        float acc = 0f;
        for (int k = 0; k < trailCount - 1; k++)
        {
            int idx = (trailHead - 1 - k + TrailN) % TrailN;
            int nxt = (idx + 1) % TrailN;
            Vector3 p0 = trailPos[idx];
            Vector3 p1 = trailPos[nxt];
            float seg = Vector3.Distance(p0, p1);
            if (acc + seg >= arcLen && seg > 0.0001f)
            {
                float t = (arcLen - acc) / seg;
                pos = Vector3.Lerp(p0, p1, t);
                dir = (p1 - p0).normalized;
                return;
            }
            acc += seg;
        }
        // 轨迹不够长，取最旧的端点并沿用最后一段方向
        int oldIdx = (trailHead - trailCount + TrailN) % TrailN;
        int oldNxt = (oldIdx + 1) % TrailN;
        pos = trailPos[oldIdx];
        dir = (trailPos[oldNxt] - trailPos[oldIdx]).normalized;
        if (dir.sqrMagnitude < 0.001f) dir = localSpineForwardStored;
    }

    void UpdateDeathFall()
    {
        if (!isFalling)
        {
            isFalling = true;
            fallProgress = 0f;
            fallStartPos = transform.position;
            fallStartRot = transform.rotation;
            // 以身体底部中心（抬升一点防穿模）为支点倒下
            Vector3 tip = transform.TransformPoint(GetBottomLocalPoint()) + Vector3.up * deathFallLift;
            fallPivotWorld = tip;

            // 绕身体朝向(forward)轴仆倒：尸体头永远保持原朝向，不会翻成背对
            fallAxisWorld = transform.forward * fallDirection;
            if (fallAxisWorld.sqrMagnitude < 0.0001f)
            {
                fallAxisWorld = transform.TransformDirection(fallAxis).normalized;
                if (fallAxisWorld == Vector3.zero) fallAxisWorld = Vector3.right;
            }
        }

        fallProgress += Time.deltaTime / deathFallDuration;
        float t = Mathf.Clamp01(fallProgress);
        float angle = Mathf.SmoothStep(0f, 90f, t);
        Quaternion rot = Quaternion.AngleAxis(angle, fallAxisWorld);
        Quaternion q = rot * fallStartRot;                 // 叠加在原始朝向上，保留头部方向
        Vector3 p = fallPivotWorld + rot * (fallStartPos - fallPivotWorld);
        transform.SetPositionAndRotation(p, q);

        if (t >= 1f) enabled = false;
    }

    // 身体底部中心（局部），倒地时当支点
    Vector3 GetBottomLocalPoint()
    {
        Mesh mm = instanceMesh != null ? instanceMesh : (meshFilter != null ? meshFilter.sharedMesh : null);
        if (mm == null) return Vector3.zero;
        Bounds b = mm.bounds;
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        MeshFilter mf = meshFilter;
        if (mf == null) mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        Transform mt = mf.transform;
        Bounds b = mf.sharedMesh.bounds;

        // 网格包围盒（换算到世界坐标：网格是局部空间，还带 0.3 缩放）
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(mt.TransformPoint(b.center), mt.TransformVector(b.size));

        int la = (b.size.x >= b.size.y && b.size.x >= b.size.z) ? 0
               : (b.size.y >= b.size.z ? 1 : 2);
        int sa = la == 0 ? 2 : 0;
        int a2 = (la + 1) % 3;
        int a3 = (la + 2) % 3;

        int n = Mathf.Max(1, segmentCount);
        Vector3 prev = Vector3.zero;
        bool havePrev = false;

        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n;                      // 0(根)..1(尾)
            // 分段点：长轴上均分（网格局部）
            Vector3 segLocal = b.center;
            segLocal[la] = Mathf.Lerp(b.min[la], b.max[la], u);
            Vector3 segW = mt.TransformPoint(segLocal);

            // 每段画一个十字（垂直长轴的切面示意，方向也要旋转到世界）
            Vector3 hw2 = mt.TransformVector(AxisVec(a2)) * b.size[a2] * 0.5f;
            Vector3 hw3 = mt.TransformVector(AxisVec(a3)) * b.size[a3] * 0.5f;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);   // 橙色：段切面
            Gizmos.DrawLine(segW - hw2 - hw3, segW + hw2 + hw3);

            // 波形点（蠕动形态预览，带直身包络；在局部算振幅再整体变换）
            float stiffU = stiffAtBigU ? (1f - stiffFront) : stiffFront;
            float ramp = stiffAtBigU
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffFeather, 0.001f)))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - stiffU) / Mathf.Max(stiffFeather, 0.001f)));
            float wave = Mathf.Sin(u * slitherWaves * Mathf.PI * 2f) * (b.size[la] * slitherAmplitude) * ramp;
            Vector3 wpLocal = segLocal;
            wpLocal[sa] += wave;
            Vector3 wpW = mt.TransformPoint(wpLocal);
            Gizmos.color = Color.cyan;                     // 青色：波形点
            Vector3 rrLocal = Vector3.zero; rrLocal[la] = b.size[la] / n * 0.35f;
            float rr = mt.TransformVector(rrLocal).magnitude;
            Gizmos.DrawSphere(wpW, rr);

            if (havePrev && i > 0)
            {
                Gizmos.color = new Color(0f, 1f, 0.2f, 0.9f);  // 绿线：蛇形连线
                Gizmos.DrawLine(prev, wpW);
            }
            prev = wpW;
            havePrev = true;
        }
    }

    static Vector3 AxisVec(int axis)
    {
        return axis == 0 ? Vector3.right
             : axis == 1 ? Vector3.up
             : Vector3.forward;
    }
}
