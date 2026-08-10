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

    [Header("===== 调试预览 =====")]
    [Tooltip("Scene 中始终显示分段/波形预览")]
    public bool showGizmos = true;

    [Header("===== 攻击动作 =====")]
    [Tooltip("后扑：头+身体抬起后头向前扑出；冲撞：整条前冲")]
    public AttackMotion attackMotion = AttackMotion.RearStrike;
    [Tooltip("攻击动作时长（秒）")]
    public float attackDuration = 0.35f;
    [Tooltip("冲撞：整条蛇沿朝向向前一冲再回原位（米）")]
    public float bodyRushDistance = 1.5f;

    [Header("===== 攻击：抬身扑击 =====")]
    [Tooltip("抬起部分的身长比例（头+身体，如 0.7 = 前七成抬起）")]
    public float rearBodyEnd = 0.7f;
    [Tooltip("抬起最大角度（度）")]
    public float rearRaiseAngle = 60f;
    [Tooltip("头向前扑出的距离（相对身长的比例）")]
    public float headLungeRatio = 0.25f;
    [Tooltip("头在哪一端：关=小端(常见), 开=大端。抬起的若是尾巴就切换它")]
    public bool rearAtBigU = false;
    [Tooltip("抬/扑方向符号（头没上抬反而下压就设 -1）")]
    public float rearFlipSign = 1f;

    [Header("===== 死亡倒下 =====")]
    public float deathFallDuration = 0.7f;
    [Tooltip("倒下支点抬高的高度（避免穿模进地底）")]
    public float deathFallLift = 0.5f;
    [Tooltip("倒下方向：1=朝玩家仆倒, -1=反向（仍是背对就改成 -1 的相反）")]
    public float fallDirection = -1f;
    [Tooltip("无玩家时的回退倒下轴（如 (1,0,0) 左右倒）")]
    public Vector3 fallAxis = new Vector3(1f, 0f, 0f);

    public enum AttackMotion { TailSwing, BodyRush, RearStrike }

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
    private Vector3 rushStartPos;
    private Collider bodyCollider;

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
        bodyCollider = GetComponentInParent<Collider>();

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

        Bounds b = instanceMesh.bounds;
        longAxis = (b.size.x >= b.size.y && b.size.x >= b.size.z) ? 0
                 : (b.size.y >= b.size.z ? 1 : 2);
        sideAxis = longAxis == 0 ? 2 : 0;
        bodyLen = Mathf.Max(b.size[longAxis], 0.0001f);
        minU = b.min[longAxis];
        maxU = b.max[longAxis];
        Debug.Log("[SnakeBodyAnimation] 蛇身初始化: 长轴=" + longAxis + " 侧轴=" + sideAxis
            + " 身长=" + bodyLen.ToString("F2") + " 顶点数=" + baseVertices.Length
            + " 包围盒size=" + b.size + " attackMotion=" + attackMotion, this);

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
            rushStartPos = transform.position;
            Debug.Log("[SnakeBodyAnimation] 攻击触发 attackMotion=" + attackMotion, this);
            // 冲撞期间把身体碰撞临时设为 Trigger，穿过玩家而不推开
            if (attackMotion == AttackMotion.BodyRush && bodyCollider != null)
                bodyCollider.isTrigger = true;
        }
        wasAttacking = attacking;
        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime / Mathf.Max(attackDuration, 0.01f);
            if (attackTimer < 0f) attackTimer = 0f;
            if (attackTimer <= 0f && bodyCollider != null && bodyCollider.isTrigger)
                bodyCollider.isTrigger = false;
        }

        bool moving = enemyAI != null
            ? enemyAI.IsMovingNow
            : (animator != null && animator.GetBool("IsMoving"));
        float movingAmt = Mathf.Max(moving ? 1f : 0f, idleSlither);

        float time = Time.time * slitherSpeed;
        Vector3 sideVec = sideAxis == 0 ? Vector3.right
                        : sideAxis == 1 ? Vector3.up
                        : Vector3.forward;
        Vector3 headFwdLocal = transform.InverseTransformDirection(transform.forward);
        Vector3 headDownLocal = transform.InverseTransformDirection(Vector3.down);

        // 冲撞：整条蛇沿朝向真正冲出一段（transform 整体前移），顶住再收回
        if (attackTimer > 0f && attackMotion == AttackMotion.BodyRush)
        {
            float t = 1f - attackTimer;                       // 0 → 1
            float fwd;
            if (t < 0.35f) fwd = (t / 0.35f) * bodyRushDistance;                 // 快速冲出
            else if (t < 0.7f) fwd = bodyRushDistance;                            // 顶住保持
            else fwd = bodyRushDistance * Mathf.Clamp01(1f - (t - 0.7f) / 0.3f);  // 收回

            transform.position = rushStartPos + transform.forward * fwd;
        }

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

            // 攻击动作
            if (attackTimer > 0f)
            {
                if (attackMotion == AttackMotion.RearStrike)
                {
                    // 时间包络：快速立起 → 保持 → 前扑
                    float t = 1f - attackTimer;                                           // 0→1
                    float up = t < 0.45f ? t / 0.45f
                             : t < 0.85f ? 1f
                             : 1f - (t - 0.85f) / 0.15f;                                 // 抬身
                    float lunge = (t >= 0.45f && t <= 0.85f)
                        ? Mathf.Sin(Mathf.Clamp01((t - 0.45f) / 0.4f) * Mathf.PI)        // 0→1→0
                        : 0f;                                                            // 头前扑

                    // “头”所在端（rearAtBigU 切换头尾）
                    bool inHead = rearAtBigU ? (u >= 1f - rearBodyEnd) : (u <= rearBodyEnd);

                    if (inHead)
                    {
                        // 头+身体以 rearBodyEnd 处为轴抬起
                        float angle = up * rearRaiseAngle * rearFlipSign * Mathf.Deg2Rad;
                        Vector3 pivot = v;
                        pivot[longAxis] = rearAtBigU
                            ? (maxU - rearBodyEnd * bodyLen)
                            : (minU + rearBodyEnd * bodyLen);
                        Vector3 dir = v - pivot;
                        o += (Quaternion.AngleAxis(angle * Mathf.Rad2Deg, sideVec) * dir) - dir;
                    }

                    // 抢出的那一段（头端）再向前扑出一点
                    if (lunge > 0f && inHead)
                    {
                        float part = rearAtBigU ? (u - (1f - rearBodyEnd)) / Mathf.Max(rearBodyEnd, 0.001f)
                                                : u / Mathf.Max(rearBodyEnd, 0.001f);
                        float falloff = Mathf.Pow(Mathf.Clamp01(1f - part), 2f);
                        Vector3 longVec = Vector3.zero; longVec[longAxis] = 1f;
                        Vector3 headDir = rearAtBigU ? longVec : -longVec;
                        o += headDir * (bodyLen * headLungeRatio) * lunge * falloff;
                    }
                }
            }

            tempVerts[i] = v + o;
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
        Mesh m = mf != null ? mf.sharedMesh : null;
        if (m == null) return;
        Bounds b = m.bounds;

        // 网格包围盒
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(b.center, b.size);

        int la = (b.size.x >= b.size.y && b.size.x >= b.size.z) ? 0
               : (b.size.y >= b.size.z ? 1 : 2);
        int sa = la == 0 ? 2 : 0;
        int a2 = (la + 1) % 3;
        int a3 = (la + 2) % 3;

        int n = Mathf.Max(1, segmentCount);
        float amp = b.size[la] * slitherAmplitude;
        Vector3 prev = Vector3.zero;
        bool havePrev = false;

        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n;                      // 0(根)..1(尾)
            // 分段点：长轴上均分
            Vector3 seg = b.center;
            seg[la] = Mathf.Lerp(b.min[la], b.max[la], u);

            // 每段画一个十字（垂直长轴的切面示意）
            Vector3 h = Vector3.zero; h[a2] = b.size[a2] * 0.5f;
            Vector3 h2 = Vector3.zero; h2[a3] = b.size[a3] * 0.5f;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);   // 橙色：段切面
            Gizmos.DrawLine(seg - h - h2, seg + h + h2);

            // 波形点（蠕动形态预览，带直身包络）
            float stiffU = stiffAtBigU ? (1f - stiffFront) : stiffFront;
            float ramp = stiffAtBigU
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stiffU - u) / Mathf.Max(stiffFeather, 0.001f)))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - stiffU) / Mathf.Max(stiffFeather, 0.001f)));
            float wave = Mathf.Sin(u * slitherWaves * Mathf.PI * 2f) * amp * ramp;
            Vector3 wp = seg;
            wp[sa] += wave;
            Gizmos.color = Color.cyan;                     // 青色：波形点
            Gizmos.DrawSphere(wp, b.size[la] / n * 0.35f);

            if (havePrev && i > 0)
            {
                Gizmos.color = new Color(0f, 1f, 0.2f, 0.9f);  // 绿线：蛇形连线
                Gizmos.DrawLine(prev, wp);
            }
            prev = wp;
            havePrev = true;
        }
    }
}
