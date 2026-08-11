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

    [Header("===== 转向跟随（贪吃蛇式：头先转、身体后段延迟跟随）=====")]
    [Tooltip("整个身体追认头部转向所需的总时长（秒）。越大身体越拖、转弯越蛇形")]
    public float turnLag = 1.0f;
    [Tooltip("紧跟头部（不滞后）的身体段长度比例（0~1，从头部算起）。越小=只有头跟着转，越大=更多身体跟着转")]
    public float turnNeck = 0.25f;

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
    [Tooltip("不移动时盘成圆形的圈数（0=不盘）")]
    public float coilTurns = 1f;
    [Tooltip("盘卷的平滑时长（秒）")]
    public float coilTime = 0.8f;

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
    private float coilK = 0f;                       // 盘卷程度 0=直线 1=盘成圆

    private int normalFrame = 0;

    // 贪吃蛇式转向：记录头部朝向历史，按弧长取滞后朝向做跟随旋转
    private const int FacingHistN = 256;
    private readonly float[] facingTimes = new float[FacingHistN];
    private readonly float[] facingYaws = new float[FacingHistN];
    private int facingHead = 0;
    private int facingCount = 0;
    private float lastFacingTime = -1f;

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

        bool chasing = enemyAI != null
            ? enemyAI.IsChasingNow
            : (animator != null && animator.GetBool("IsMoving"));
        bool moving = enemyAI != null
            ? enemyAI.IsMovingNow
            : (animator != null && animator.GetBool("IsMoving"));
        // 追击/攻击状态下蠕动前进；完全盘卷时静止（盘卷为闭圈的过渡期保留微蠕动）
        bool chaseActive = chasing || attacking;
        float movingAmt = chaseActive
            ? (moving ? 1f : 0f)
            : Mathf.Lerp(idleSlither, 0f, Mathf.Clamp01(coilK));

        float time = Time.time * slitherSpeed;

        // —— 状态驱动：非追击盘卷；追击/攻击保持展开，且整个追击期间前半段直立（眼镜蛇姿态）——
        bool keepUncoiled = chasing || attacking;
        coilK = Mathf.MoveTowards(coilK, keepUncoiled ? 0f : (coilTurns > 0f ? 1f : 0f),
            Time.deltaTime / Mathf.Max(coilTime, 0.01f));
        rearLift = Mathf.MoveTowards(rearLift, chaseActive ? 1f : 0f,
            Time.deltaTime / Mathf.Max(poiseLiftTime, 0.01f));

        // 记录头部当前朝向（世界 yaw），供贪吃蛇式滞后跟随采样
        float curYaw = GetWorldYaw(transform.forward);
        PushFacing(curYaw);

        // —— 轴系（网格局部空间，全部顶点运算不带世界坐标往返）——
        int upAxis = 3 - longAxis - sideAxis;                       // 剩下的第三个轴=竖直
        Vector3 lVec = AxisVec(longAxis);                           // 长轴单位方向
        Vector3 sVec = AxisVec(sideAxis);                           // 水平横向
        Vector3 uVec = AxisVec(upAxis);                             // 竖直

        Vector3 center0 = meshBounds.center;                        // 直线中心线基准

        // 盘卷：把中心线弯成 (longAxis, sideAxis) 平面里的圆（躺在世界地面上）, 半径=身长/周长
        float turns = Mathf.Max(coilTurns, 0.01f);
        float coilR = bodyLen / (turns * Mathf.PI * 2f);            // 局部单位
        Vector3 coilCenter = center0;

        // 攻击窗口
        float tA = 1f - attackTimer;
        float lunge = (attackTimer > 0f && tA >= 0.45f && tA <= 0.85f)
            ? Mathf.Sin(Mathf.Clamp01((tA - 0.45f) / 0.4f) * Mathf.PI)
            : 0f;
        float rearBaseU = rearAtBigU ? (1f - rearBodyEnd) : rearBodyEnd;  // 仰起部分的底部

        for (int i = 0; i < baseVertices.Length; i++)
        {
            Vector3 v = baseVertices[i];
            float u = Mathf.Clamp01((v[longAxis] - minU) / bodyLen);

            // 1) 直线中心线点 + 蠕动(横向)
            Vector3 pStraight = center0;
            pStraight[longAxis] = Mathf.Lerp(minU, maxU, u);
            float stiffU = stiffAtBigU ? (1f - stiffFront) : stiffFront;
            float ramp = stiffAtBigU
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffFeather, 0.001f)))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - stiffU) / Mathf.Max(stiffFeather, 0.001f)));
            float pAmt = stiffAtBigU ? Mathf.Clamp01((u - stiffU) / Mathf.Max(1f - stiffU, 0.001f))
                                    : Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffU, 0.001f));
            float gain = Mathf.Lerp(0.3f, 1f, Mathf.Pow(pAmt, Mathf.Max(tailGain, 0.2f)));
            float wave = Mathf.Sin(u * slitherWaves * Mathf.PI * 2f + time)
                       * (bodyLen * slitherAmplitude) * movingAmt * ramp * gain;

            // 2) 中心线点：直线 ↔ 盘卷 混合
            float theta = (u - 0.5f) * Mathf.PI * 2f * turns;
            Vector3 pCoil = coilCenter + (Mathf.Cos(theta) * lVec + Mathf.Sin(theta) * sVec) * coilR;
            Vector3 pRef = Vector3.Lerp(pStraight, pCoil, coilK);
            // 盘卷时的切线方向（与顶点自身的中心线正交方向一致）
            Vector3 tCoil = (-Mathf.Sin(theta) * lVec + Mathf.Cos(theta) * sVec).normalized;

            // 3) 顶点相对中心线的偏移（含蠕动），再随切线旋转弯曲
            Vector3 o3 = v - pStraight;                             // 相对直线中心线
            o3[sideAxis] += wave;                                   // 蠕动沿横向
            Vector3 tDir = coilK > 0.001f ? tCoil : lVec;           // 该处切线方向
            Vector3 point = pRef + Quaternion.FromToRotation(lVec, tDir) * o3;

            // 3.5) 贪吃蛇式转向：追击移动中，头部跟随当前朝向，身体后段按
            //      "弧长→时间滞后"采样头部当时的朝向，绕颈部铰链水平旋转，形成拖尾弧线
            //      幅度随 rearLift 平滑淡入淡出（追击立起后尾随曲线渐强，退出追击渐退，避免突然拉直）
            if (chaseActive && turnLag > 0.001f && coilK < 0.999f && facingCount >= 2)
            {
                float arcFromHead = Mathf.Clamp01(rearAtBigU ? (1f - u) : u);
                float histYaw = SampleFacingYaw(curYaw, arcFromHead * turnLag);
                float arcK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(turnNeck, 1f, arcFromHead));
                float ang = -Mathf.DeltaAngle(histYaw, curYaw) * arcK * rearLift;
                if (Mathf.Abs(ang) > 0.02f)
                {
                    // 颈部铰链点（朝头的第 turnNeck 段处），绕它竖直旋转拖尾
                    float nU = rearAtBigU ? (1f - turnNeck) : turnNeck;
                    Vector3 neckStraight = center0;
                    neckStraight[longAxis] = Mathf.Lerp(minU, maxU, nU);
                    float nTheta = (nU - 0.5f) * Mathf.PI * 2f * turns;
                    Vector3 neckRef = Vector3.Lerp(neckStraight,
                        coilCenter + (Mathf.Cos(nTheta) * lVec + Mathf.Sin(nTheta) * sVec) * coilR, coilK);
                    point = neckRef + Quaternion.AngleAxis(ang, uVec) * (point - neckRef);
                }
            }

            // 4) 前半段仰起（绕“仰起部底部”的水平横轴旋转，头抬向上）
            bool inRear = rearAtBigU ? (u >= rearBaseU) : (u <= rearBaseU);
            if (inRear && rearLift > 0f)
            {
                // 从仰起段底部算起的长度比例：0=贴地处, 1=头部
                float part = rearAtBigU
                    ? Mathf.Clamp01((u - rearBaseU) / Mathf.Max(rearBodyEnd, 0.001f))
                    : Mathf.Clamp01((rearBaseU - u) / Mathf.Max(rearBodyEnd, 0.001f));
                float bendK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(part));

                Vector3 pivotStraight = center0;
                pivotStraight[longAxis] = rearAtBigU ? (maxU - rearBodyEnd * bodyLen) : (minU + rearBodyEnd * bodyLen);
                float pTheta = (rearBaseU - 0.5f) * Mathf.PI * 2f * turns;
                Vector3 pivotRef = Vector3.Lerp(pivotStraight,
                    coilCenter + (Mathf.Cos(pTheta) * lVec + Mathf.Sin(pTheta) * sVec) * coilR, coilK);
                Vector3 tPivot = coilK > 0.001f
                    ? (-Mathf.Sin(pTheta) * lVec + Mathf.Cos(pTheta) * sVec).normalized
                    : lVec;
                Vector3 liftAxis = Vector3.Cross(tPivot, uVec).normalized;   // 水平横轴，向上抬
                if (liftAxis.sqrMagnitude < 0.5f) liftAxis = sVec;
                // 沿仰起段渐进弯曲：基部贴地、越靠头部弯得越多 → 蛇颈自然 S 形弯弧
                float ang = rearLift * rearRaiseAngle * Mathf.Deg2Rad * rearFlipSign * bendK;
                point = pivotRef + Quaternion.AngleAxis(ang * Mathf.Rad2Deg, liftAxis) * (point - pivotRef);
            }

            // 5) 攻击：头部再沿切线前扑
            if (lunge > 0f && inRear)
            {
                float part = rearAtBigU ? (u - rearBaseU) / Mathf.Max(rearBodyEnd, 0.001f)
                                        : (rearBaseU - u) / Mathf.Max(rearBodyEnd, 0.001f);
                float falloff = Mathf.Pow(Mathf.Clamp01(1f - part), 2f);
                Vector3 headDir = rearAtBigU ? tDir : -tDir;
                point += headDir * (bodyLen * headLungeRatio) * lunge * falloff;
            }

            tempVerts[i] = point;
        }

        instanceMesh.vertices = tempVerts;
        if (++normalFrame % 3 == 0)
        {
            try { instanceMesh.RecalculateNormals(); }
            catch { /* 法线不可重算时跳过，不影响顶点蠕动 */ }
        }
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

    // 世界朝向 → yaw 角（度，-180~180）
    static float GetWorldYaw(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return 0f;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    // 每帧记录头部朝向（同帧只记一次，环形缓冲）
    void PushFacing(float yaw)
    {
        if (facingCount > 0 && Mathf.Abs(Time.time - lastFacingTime) < 0.0001f)
            return;
        facingTimes[facingHead] = Time.time;
        facingYaws[facingHead] = yaw;
        facingHead = (facingHead + 1) % FacingHistN;
        if (facingCount < FacingHistN) facingCount++;
        lastFacingTime = Time.time;
    }

    // 采样 lagT 秒前头部的朝向（线性插值，超出历史返回最旧值）
    float SampleFacingYaw(float curYaw, float lagT)
    {
        if (facingCount < 2) return curYaw;
        float target = Time.time - lagT;
        int latest = (facingHead - 1 + FacingHistN) % FacingHistN;
        if (target >= facingTimes[latest]) return curYaw;

        int idx = latest;
        for (int k = 0; k < facingCount - 1; k++)
        {
            int prev = (idx - 1 + FacingHistN) % FacingHistN;
            float tPrev = facingTimes[prev];
            float tCur = facingTimes[idx];
            if (target >= tPrev && target <= tCur)
            {
                float f = (tCur - tPrev) > 0.0001f
                    ? Mathf.Clamp01((target - tPrev) / (tCur - tPrev)) : 0f;
                return Mathf.LerpAngle(facingYaws[prev], facingYaws[idx], f);
            }
            idx = prev;
        }
        return facingYaws[(facingHead - facingCount + FacingHistN) % FacingHistN];
    }
}
