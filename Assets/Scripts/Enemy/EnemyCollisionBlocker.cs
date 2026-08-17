using UnityEngine;
using UnityEngine.AI;

// 敌人碰撞解决器：检测敌人↔玩家、敌人↔敌人的重叠，并只移动"敌人"来分离。
//
// 为什么需要它：
//   玩家是 CharacterController，一旦敌人的实体碰撞体(BoxCollider)挤入玩家体积，
//   物理引擎会在下一次 Move 时把玩家顶开 —— 这就是普遍被抱怨的"推挤"。
//   本组件在每个 LateUpdate 用 Physics.ComputePenetration 求出最小分离位移，
//   并把位移施加在敌人身上（玩家永远不动），因此：
//     ① 不穿模（敌人不再插入玩家/其他敌人内部）
//     ② 不推挤（玩家绝对不被物理解算推动）
//   敌人之间的静态碰撞体本来不会互相阻挡（无刚体无物理解算），也由这里强制分离。
// 由 EnemyAI.Start 自动挂载到所有敌人上。
public class EnemyCollisionBlocker : MonoBehaviour
{
    [Header("玩家碰撞")]
    [Tooltip("与玩家重叠时，把敌人推出去（玩家不动），避免物理把玩家顶开")]
    public bool resolvePlayerOverlap = true;

    [Header("敌人之间碰撞")]
    [Tooltip("敌人互相重叠时，各自往反方向推一半距离，避免互相穿模/顶堆")]
    public bool resolveEnemyOverlap = true;

    [Header("穿墙兜底")]
    [Tooltip("敌人被击退/冲锋/瞬移/分离挪到与墙重叠时，把它水平推出墙外（实墙与 X-Ray 半透明墙都保证不穿）")]
    public bool resolveWallOverlap = true;

    [Header("事件（上沿触发，可订阅做碰撞检测）")]
    [Tooltip("每次从\"不重叠\"变\"与玩家重叠\"时触发一次")]
    public System.Action onPlayerContactEnter;
    [Tooltip("每次从\"与玩家重叠\"变\"不重叠\"时触发一次")]
    public System.Action onPlayerContactExit;
    [Tooltip("每次从\"不重叠\"变\"与某敌人重叠\"时触发一次（参数：对方敌人）")]
    public System.Action<EnemyAI> onEnemyContactEnter;
    [Tooltip("每次从\"与某敌人重叠\"变\"不重叠\"时触发一次（参数：对方敌人）")]
    public System.Action<EnemyAI> onEnemyContactExit;

    [Header("站桩挂起（蓄力/咏唱等需要锁定站位时由子类临时开启）")]
    [Tooltip("开启后挂起『玩家/敌人分离推挤』：本体停住期间不被其他单位挤动（位置锁定，供蓄力落点等）")]
    public bool suspendSeparation = false;

    [Header("调试")]
    public bool showPenetrationGizmos = false;
    [Header("🔍 穿墙兜底诊断")]
    [Tooltip("勾上后每次触发『穿墙兜底 Warp』打印：是哪面墙、穿透多深、Warp 前后 agent 完整状态（position/destination/steeringTarget/hasPath/pathStatus/velocity）。运行时自动挂载的组件请用 GlobalLogWallResolve")]
    public bool logWallResolveDetails = false;

    // 🔍 全局穿墙兜底日志开关：对场景内所有敌人（含运行时自动挂载的）一次性开启。
    // 运行时自动挂载的 EnemyCollisionBlocker 实例上没法在 Inspector 勾选，
    // 用这个静态开关让 Snake/Jiangshi 等所有敌人同时输出诊断日志。
    public static bool GlobalLogWallResolve = false;

    // ---------- 运行时 ----------
    private EnemyAI enemyAI;
    private Collider myCollider;
    private CharacterController playerController;
    private int enemyLayerMask;
    private NavMeshAgent agent;
    private EnemyAI currentContactEnemy;
    private bool wasTouchingPlayer = false;
    private bool initialised = false;

    // 单帧实测分离使用的方向/距离（供 Gizmos 显示）
    private Vector3 lastPlayerDir = Vector3.zero;
    private float lastPlayerDist = 0f;
    private bool lastPlayerOverlap = false;
    private Vector3 lastEnemyDir = Vector3.zero;
    private float lastEnemyDist = 0f;

    // 🔍 连续穿墙 Warp 检测：记录上一次 Warp 时刻与次数，用于确认"Warp→清路径→重寻路→再 Warp"循环
    private float lastWallWarpTime = -100f;
    private int consecutiveWallWarps = 0;

    public bool IsOverlappingPlayer { get { return wasTouchingPlayer; } }
    public EnemyAI CurrentContactEnemy { get { return currentContactEnemy; } }

    void Start()
    {
        TryInit();
    }

    private void TryInit()
    {
        if (initialised) return;
        initialised = true;

        enemyAI = GetComponent<EnemyAI>();
        myCollider = GetComponent<Collider>();
        if (myCollider == null) myCollider = GetComponentInChildren<Collider>();
        agent = GetComponent<NavMeshAgent>();
        enemyLayerMask = LayerMask.GetMask("Enemy");

        PlayerController pc = Object.FindObjectOfType<PlayerController>();
        if (pc != null) playerController = pc.GetComponent<CharacterController>();
        else playerController = null;
    }

    void LateUpdate()
    {
        TryInit();

        // 死亡后物体只保留尸体碰撞，不再做分离（尸体会被 Update 里 Destroy 延迟回收）
        if (enemyAI != null && enemyAI.isDead) return;
        if (myCollider == null) return;

        // 站桩挂起：蓄力/咏唱等需要锁定站位的状态（如僵尸蓄力落点锁定），
        // 期间不被其他敌人/玩家分离推挤，防止落点/指示错位。穿墙兜底仍保留。
        if (!suspendSeparation)
        {
            if (resolvePlayerOverlap)
            {
                ResolvePlayerBack();
            }

            if (resolveEnemyOverlap)
            {
                ResolveEnemiesBack();
            }
        }

        // 穿墙兜底：无论敌人怎么被移动(追击/击退/冲锋/瞬移/分离)，
        // 只要碰撞体与任何竖直墙(实墙或 X-Ray 半透明墙)重叠，就水平推出墙外。
        if (resolveWallOverlap)
        {
            Vector3 preResolvePos = transform.position;
            bool preHasPath = agent != null && agent.hasPath;
            bool prePending = agent != null && agent.pathPending;
            NavMeshPathStatus preStatus = agent != null ? agent.pathStatus : NavMeshPathStatus.PathInvalid;
            Vector3 preDest = agent != null ? agent.destination : Vector3.zero;
            Vector3 preSteer = agent != null ? agent.steeringTarget : Vector3.zero;
            Vector3 preVel = agent != null ? agent.velocity : Vector3.zero;
            bool preStopped = agent != null && agent.isStopped;

            WallPenetrationResolve.ResolveResult res;
            bool wallResolved = WallPenetrationResolve.Resolve(myCollider, transform, out res);

            if (wallResolved && agent != null && agent.isOnNavMesh)
            {
                NotePathMutationExternal("穿墙兜底 → agent.Warp(推回墙外)");
                agent.Warp(transform.position);

                if (logWallResolveDetails || GlobalLogWallResolve)
                {
                    // 🔍 连续 Warp 计数：距离上次 Warp ≤1s 视为连续触发（循环证据）
                    float now = Time.time;
                    if (now - lastWallWarpTime <= 1f) consecutiveWallWarps++;
                    else consecutiveWallWarps = 1;
                    lastWallWarpTime = now;

                    Vector3 postPos = transform.position;
                    bool warpClearedPath = preHasPath && !agent.hasPath;   // 🔍 Warp 是否把 hasPath 从 Y 打回 N
                    string enemyType = enemyAI != null ? enemyAI.GetType().Name : "?";
                    Debug.Log(
                        $"<color=orange>[穿墙兜底]</color> <b>{enemyType}</b> name:{name} 连续Warp:{consecutiveWallWarps} " +
                        $"| 墙:{res.wallName} pos:{res.wallPosition:F2} 穿透深度:{res.penDist:F3}m 推出方向:{res.pushDir:F2} " +
                        $"| 碰撞体:{res.moverColliderType} 包围盒:{res.moverBounds.size:F2} " +
                        $"| 状态: chase:{enemyAI!=null&&enemyAI.IsChasingNow} atk:{enemyAI!=null&&enemyAI.IsAttackingNow} stagger:{enemyAI!=null&&enemyAI.IsStaggeringNow} dead:{enemyAI!=null&&enemyAI.isDead}\n" +
                        $"  <color=cyan>[Warp前]</color> pos:{preResolvePos:F2} dest:{preDest:F2} steer:{preSteer:F2} " +
                        $"hasPath:{preHasPath} pending:{prePending} status:{preStatus} vel:{preVel:F2} stopped:{preStopped}\n" +
                        $"  <color=yellow>[Resolve后]</color> pos:{transform.position:F2} " +
                        $"<color=cyan>[Warp后]</color> pos:{postPos:F2} dest:{agent.destination:F2} steer:{agent.steeringTarget:F2} " +
                        $"hasPath:{agent.hasPath} pending:{agent.pathPending} status:{agent.pathStatus} vel:{agent.velocity:F2} stopped:{agent.isStopped} " +
                        $"<color=red>warpClearedPath:{warpClearedPath}</color>"
                    );
                }
            }
        }
    }

    // 玩家重叠：把敌人从玩家体积里推出来（方向 = 远离玩家），玩家永远不动。
    // 玩家是 CharacterController（胶囊），用水平面近似（玩家半径 + 敌人水平半径）做几何分离，
    // 比 ComputePenetration 对 CC 更稳定，且天然只推敌人、不推玩家。
    private void ResolvePlayerBack()
    {
        PlayerController pc = enemyAI != null ? enemyAI.Player : null;
        playerController = pc != null ? pc.GetComponent<CharacterController>() : null;
        if (playerController == null || !playerController.enabled) return;

        // 玩家胶囊半径换算到世界单位（CharacterController.radius 是局部单位）
        Vector3 playerScale = playerController.transform.lossyScale;
        float playerWorldRadius = playerController.radius * Mathf.Max(Mathf.Abs(playerScale.x), Mathf.Abs(playerScale.z));

        // 敌人水平半宽（用包围盒水平 extents 近似，覆盖椭圆体/长盒）
        Bounds b = myCollider.bounds;
        float enemyHalf = Mathf.Max(b.extents.x, b.extents.z);

        Vector3 myPos = transform.position;
        Vector3 pcPos = playerController.transform.position;
        myPos.y = 0f;
        pcPos.y = 0f;

        Vector3 toPlayer = pcPos - myPos;
        float distToPlayer = toPlayer.magnitude;
        float needDist = enemyHalf + playerWorldRadius;

        if (distToPlayer < needDist && distToPlayer > 0.0001f)
        {
            Vector3 dir = toPlayer / distToPlayer;
            float push = needDist - distToPlayer;

            lastPlayerOverlap = true;
            lastPlayerDir = -dir;                 // 敌人被推的方向 = 远离玩家
            lastPlayerDist = push;

            // 只移动敌人；玩家保持原位（解决"推挤玩家"的根因）
            MoveEnemy(-dir * push);
            RaisePlayerContact(true);
        }
        else
        {
            lastPlayerOverlap = false;
            lastPlayerDir = Vector3.zero;
            lastPlayerDist = 0f;
            RaisePlayerContact(false);
        }
    }

    // 敌人之间重叠：把两边的敌人各推出重叠量的一半，撮合收敛且互不顶推。
    private void ResolveEnemiesBack()
    {
        if (myCollider == null) return;

        float searchRadius = Mathf.Max(myCollider.bounds.extents.magnitude, 1f) + 2f;
        Collider[] candidates = Physics.OverlapSphere(myCollider.bounds.center, searchRadius, enemyLayerMask);

        bool nowContact = false;
        EnemyAI contact = null;

        foreach (Collider c in candidates)
        {
            if (c == null) continue;
            if (c == myCollider) continue;
            if (c.isTrigger) continue;

            EnemyAI other = c.GetComponentInParent<EnemyAI>();
            if (other == null || other == enemyAI) continue;
            if (other.isDead) continue;

            Vector3 dir;
            float dist;
            bool overlapping = Physics.ComputePenetration(
                myCollider, transform.position, transform.rotation,
                c, c.transform.position, c.transform.rotation,
                out dir, out dist);

            if (overlapping && dist > 0.001f)
            {
                // 各承担一半；对方敌人每帧也会执行本逻辑，双方各推 0.5 恰好分离
                MoveEnemy(dir * dist * 0.5f);
                nowContact = true;
                contact = other;
                lastEnemyDir = dir;
                lastEnemyDist = dist;
            }
        }

        RaiseEnemyContact(nowContact, contact);
    }

    // ---------- 事件（上沿） ----------
    private void RaisePlayerContact(bool touching)
    {
        if (touching && !wasTouchingPlayer && onPlayerContactEnter != null)
        {
            onPlayerContactEnter();
        }
        else if (!touching && wasTouchingPlayer && onPlayerContactExit != null)
        {
            onPlayerContactExit();
        }
        wasTouchingPlayer = touching;
    }

    private void RaiseEnemyContact(bool touching, EnemyAI other)
    {
        if (touching && currentContactEnemy == null && onEnemyContactEnter != null)
        {
            onEnemyContactEnter(other);
        }
        else if (!touching && currentContactEnemy != null && onEnemyContactExit != null)
        {
            onEnemyContactExit(currentContactEnemy);
        }
        currentContactEnemy = touching ? other : null;
    }

    // 用 NavMeshAgent.Move 移动敌人（尊重导航/避障）；若被障碍物挡下、位移不足，
    // 剩余部分用 Warp 补齐，保证每次都精确分离、不残留穿透。
    private void NotePathMutationExternal(string source)
    {
        if (enemyAI != null)
            enemyAI.NotePathMutationExternal(source);
    }

    private void MoveEnemy(Vector3 delta)
    {
        delta.y = 0f;
        if (delta.sqrMagnitude < 1e-6f) return;

        if (agent != null && agent.isOnNavMesh)
        {
            Vector3 before = transform.position;
            NotePathMutationExternal("分离移动 → agent.Move(delta)");
            agent.Move(delta);
            Vector3 after = transform.position;
            Vector3 residual = delta - (after - before);
            if (residual.sqrMagnitude > 1e-6f)
            {
                transform.position += residual;
                NotePathMutationExternal("分离被挡 → agent.Warp(补齐)");
                agent.Warp(transform.position);
            }
        }
        else
        {
            transform.position += delta;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!showPenetrationGizmos) return;

        Gizmos.color = lastPlayerOverlap ? Color.red : Color.green;
        if (lastPlayerOverlap && lastPlayerDist > 0.001f)
        {
            Gizmos.DrawRay(transform.position, lastPlayerDir * lastPlayerDist);
            Gizmos.DrawWireSphere(transform.position + lastPlayerDir * lastPlayerDist, 0.15f);
        }
        if (lastEnemyDist > 0.001f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, lastEnemyDir * lastEnemyDist);
        }
    }
}