using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class JiangshiEnemy : EnemyAI
{
    private enum BlinkState { Chasing, Preparing, PostBlink, Cooldown }
    private BlinkState blinkState = BlinkState.Chasing;
    private float stateTimer = 0f;
    private bool hasPrepared = false;
    private bool hasAttacked = false;
    private Vector3 lockedFacingDir = Vector3.zero;  // 蓄力开始时锁定朝向（身体 + 指示线共用）

    [Header("瞬移参数")]
    [Tooltip("大于此距离才会瞬移（远距离用瞬移接近；近距离直接走过去打）")]
    public float prepareDistance = 6f;
    [Tooltip("蓄力停顿时间（秒）")]
    public float prepareDuration = 3.0f;
    [Tooltip("蓄力时长随机偏移(±秒)：拉开多个僵尸的瞬移时刻，避免全部同一时间瞬移到玩家面前")]
    public float prepareDurationJitter = 0.8f;
    [Tooltip("瞬移后距离玩家多远（数值越小越贴脸）")]
    public float distanceToPlayerAfterBlink = 1.7f;
    [Tooltip("小于等于此距离时直接走过去攻击（不再瞬移）")]
    public float directAttackDistance = 2f;
    [Tooltip("瞬移冷却时间（秒）")]
    public float blinkCooldown = 10f;
    [Tooltip("瞬移会被哪些层挡住（默认 Wall 层）。有墙挡着就不会瞬移，改为绕墙走过去")]
    public LayerMask blinkObstacleMask = 1 << 6;

    [Header("地面指示特效")]
    [Tooltip("是否显示地面指示")]
    public bool showGroundIndicator = true;
    [Tooltip("指示条填充颜色（红色慢慢填充）")]
    public Color endColor = Color.red;
    [Tooltip("指示条的宽度(米)——长条方向从僵尸指向锁定玩家位置")]
    public float indicatorSize = 1.0f;
    [Tooltip("指示条高度（贴地）")]
    public float indicatorHeight = 0.05f;
    [Tooltip("外框线宽（米）")]
    public float frameLineWidth = 0.06f;

    private GameObject endMarker;
    private MeshRenderer endMarkerRenderer;
    private LineRenderer endFrame;
    private Vector3 lockedIndicatorTarget = Vector3.zero; // 蓄力开始时锁定的玩家位置（地面）

    private EnemyCollisionBlocker blocker; // 蓄力站桩时挂起分离推挤，避免被其他单位挤动导致落点/指示错位
    private float effectivePrepareDuration; // 每次蓄力时随机化的实际时长（错开多僵尸的瞬移时刻）

    // 动画 root motion 的 Y（跳跃离地）会作用在子模型上；根节点 XZ 由 NavMeshAgent 驱动。
    private Transform visualModel;

    [Header("落地音效")]
    [Tooltip("视觉模型离地超过此高度视为'在空中'(米)")]
    public float airborneThreshold = 0.08f;
    [Tooltip("视觉模型降到此高度以下视为'落地'(米)")]
    public float landThreshold = 0.03f;
    private bool wasAirborne = false;

    protected override void OnStart()
    {
        base.OnStart();
        blinkState = BlinkState.Chasing;
        if (transform.childCount > 0)
            visualModel = transform.GetChild(0);

        blocker = GetComponent<EnemyCollisionBlocker>();

        // 僵尸碰撞体 1.6×1.4 + 玩家 CC 半径 0.8 → 至少需 1.6m 才不重叠。
        // 通用 stoppingDistance(攻击范围×0.5) 只有 1.1m，会挤进玩家体积顶动玩家(CC depenetrate)，
        // 这里覆盖为 ≥ 碰撞不重叠距离，保证追击全程不把碰撞体扫进玩家。
        if (isAgentValid && agent != null)
        {
            agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, 1.7f);
            agent.autoBraking = true;
            // ⭐ 缩小寻路半径：预制体默认 radius=1 比碰撞体(1.6×1.4 半宽约0.8)还宽，
            // 转角/窄通道里 NavMeshAgent 判定绕不过去导致卡墙角。降到 0.5 与碰撞体匹配，
            // 配合 WallPenetrationResolve/EnemyCollisionBlocker 兜底仍不穿墙。
            agent.radius = 0.5f;
        }

        if (showGroundIndicator)
        {
            CreateGroundIndicator();
        }
    }

    protected override void Update()
    {
        base.Update();
        UpdateLandSFX();
    }

    // ⭐ 被击退中断蓄力：恢复分离推挤（击退用 agent.Move 推，无需再挂起；蓄力残留落点锁定已失效）
    protected override void OnKnockback()
    {
        base.OnKnockback();
        if (blinkState == BlinkState.Preparing || blinkState == BlinkState.PostBlink)
        {
            SetSeparationSuspended(false);
            blinkState = BlinkState.Cooldown;
            stateTimer = 0f;
        }
    }

    // 僵尸跳着走：检测视觉模型从"离地"落到"贴地"的下降沿，落地瞬间播放一次 moveSFX。
    // 动画 root motion 的 Y 作用在子模型 visualModel 上，根节点高度不变，所以看子模型的 localPosition.y。
    private void UpdateLandSFX()
    {
        if (moveSFX == null || visualModel == null) return;

        float modelY = visualModel.localPosition.y;
        bool airborne = modelY > airborneThreshold;
        bool landed = !airborne && wasAirborne && modelY <= landThreshold;

        wasAirborne = airborne;

        if (landed && !isDead && isChasing && !isAttacking && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(moveSFX, transform.position);
        }
    }

    // 蓄力站桩期间挂起/恢复分离推挤（防止其他单位挤动导致落点/指示错位）
    private void SetSeparationSuspended(bool suspended)
    {
        if (blocker != null) blocker.suspendSeparation = suspended;
    }

    private void CreateGroundIndicator()
    {
        // 填充方块：从僵尸脚下开始，沿锁定方向朝玩家推进填充
        endMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        endMarker.name = "BlinkIndicator_Fill";
        endMarker.transform.SetParent(transform);
        // 每帧用 LookRotation 对齐方向，不再用固定 90° 旋转（避免长度轴向错位）
        endMarker.transform.localScale = new Vector3(indicatorSize, indicatorHeight, indicatorSize);
        Destroy(endMarker.GetComponent<Collider>());

        endMarkerRenderer = endMarker.GetComponent<MeshRenderer>();
        endMarkerRenderer.material = new Material(Shader.Find("Sprites/Default"));
        endMarkerRenderer.material.color = endColor; // 全红
        endMarker.SetActive(false);

        // 方形外框：LineRenderer 画长方形边框，包住从僵尸到锁定玩家的整条路径
        GameObject frameGO = new GameObject("BlinkIndicator_Frame");
        frameGO.transform.SetParent(transform);
        endFrame = frameGO.AddComponent<LineRenderer>();
        endFrame.useWorldSpace = true;
        endFrame.positionCount = 4; // 长方形四角
        endFrame.loop = true;
        endFrame.startWidth = frameLineWidth;
        endFrame.endWidth = frameLineWidth;
        endFrame.material = new Material(Shader.Find("Sprites/Default"));
        endFrame.startColor = endColor;
        endFrame.endColor = endColor;
        frameGO.SetActive(false);
    }

    protected override void HandleMovement()
    {
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            HideGroundIndicator();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);

        switch (blinkState)
        {
            case BlinkState.Chasing:
                // ⭐ 近距离：直接走过去打，不瞬移
                if (distance <= prepareDistance)
                {
                    if (distance > enemyData.attackRange)
                    {
                        if (isAgentValid)
                        {
                            agent.isStopped = false;
                            agent.SetDestination(player.transform.position);
                        }
                    }
                    else
                    {
                        StopAgent();
                        RotateTowardPlayer();
                        if (canAttack && !isAttacking && IsFacingPlayer())
                        {
                            PerformAttack();
                        }
                    }
                    return;
                }

                // ⭐ 远距离：只在无墙遮挡时瞬移接近（蓄力 → 指示 → 瞬移到玩家面前 → 攻击）。
                // 有墙挡着就继续绕墙追击，等玩家出墙再瞬移。
                if (distance > prepareDistance && !isAttacking && blinkState == BlinkState.Chasing)
                {
                    if (!HasClearLineToPlayer())
                    {
                        if (isAgentValid)
                        {
                            agent.isStopped = false;
                            agent.SetDestination(player.transform.position);
                        }
                        return;
                    }

                    blinkState = BlinkState.Preparing;
                    stateTimer = 0f;
                    // ⭐ 本次蓄力随机化时长：不同僵尸瞬移时刻错开，避免同时瞬移到玩家面前
                    effectivePrepareDuration = Mathf.Max(prepareDuration + Random.Range(-prepareDurationJitter, prepareDurationJitter), 0.5f);
                    hasPrepared = false;
                    hasAttacked = false;
                    lockedFacingDir = GetLockedFacingDir();
                    // ⭐ 锁定瞬移落点基准：蓄力开始时玩家的位置（地面），之后不跟随玩家
                    lockedIndicatorTarget = player != null
                        ? GetGroundPosition(player.transform.position)
                        : GetGroundPosition(transform.position);
                    StopAgent();

                    if (showGroundIndicator)
                    {
                        ShowGroundIndicator();
                    }
                    return;
                }
                break;

            case BlinkState.Preparing:
                StopAgent();
                // ⭐ 蓄力站桩锁定站位：挂起分离推挤，避免被其他敌人/玩家挤动导致落点/指示错位
                SetSeparationSuspended(true);
                // ⭐ 蓄力期间朝锁定方向转向（身体不跟随玩家，落点/朝向都锁定在蓄力开始瞬间）
                RotateToLockedFacing();

                stateTimer += Time.deltaTime;

                if (showGroundIndicator && endMarker != null)
                {
                    UpdateGroundIndicator();
                }

                // ⭐ 蓄力过程中玩家躲到墙后：取消蓄力，回到追击
                if (!HasClearLineToPlayer())
                {
                    HideGroundIndicator();
                    SetSeparationSuspended(false);
                    blinkState = BlinkState.Chasing;
                    stateTimer = 0f;
                    return;
                }

                // ⭐ 蓄力过程中如果玩家靠近到直接攻击距离，取消蓄力直接攻击
                float currentDistance = Vector3.Distance(transform.position, player.transform.position);
                if (currentDistance <= directAttackDistance && canAttack && !isAttacking)
                {
                    HideGroundIndicator();
                    SetSeparationSuspended(false);
                    StopAgent();
                    RotateTowardPlayer();
                    if (IsFacingPlayer())
                    {
                        PerformAttack();
                        hasAttacked = true;
                        blinkState = BlinkState.Cooldown;
                        stateTimer = 0f;
                    }
                    return;
                }

                // ⭐ 蓄力填满的当下立刻瞬移
                if (stateTimer >= effectivePrepareDuration && !hasPrepared)
                {
                    hasPrepared = true;
                    HideGroundIndicator();
                    SetSeparationSuspended(false);

                    // 瞬移前最后校验：玩家已躲到墙后或瞬移路径被墙挡，就放弃瞬移
                    if (!HasClearLineToPlayer())
                    {
                        blinkState = BlinkState.Chasing;
                        stateTimer = 0f;
                        return;
                    }
                    if (PerformBlinkToPlayer())
                    {
                        blinkState = BlinkState.PostBlink;
                        stateTimer = 0f;
                    }
                    else
                    {
                        // ⭐ 落点路径被墙挡：放弃瞬移，回到追击
                        blinkState = BlinkState.Chasing;
                        stateTimer = 0f;
                    }
                }
                break;

            case BlinkState.PostBlink:
                // ⭐ 瞬移后立即攻击（不等待）
                StopAgent();
                float distAfterBlink = Vector3.Distance(transform.position, player.transform.position);
                if (!hasAttacked && distAfterBlink <= enemyData.attackRange && canAttack)
                {
                    PerformAttack();
                    hasAttacked = true;
                }

                // 立即进入冷却
                blinkState = BlinkState.Cooldown;
                stateTimer = 0f;
                break;

            case BlinkState.Cooldown:
                // ⭐ 冷却期间：只追击，不攻击
                if (distance > enemyData.attackRange)
                {
                    if (isAgentValid)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(player.transform.position);
                    }
                }
                else
                {
                    StopAgent();
                    RotateTowardPlayer();
                }

                stateTimer += Time.deltaTime;
                if (stateTimer >= blinkCooldown)
                {
                    blinkState = BlinkState.Chasing;
                    Debug.Log("[Jiangshi] 冷却结束");
                }
                break;
        }
    }

    private void ShowGroundIndicator()
    {
        if (endMarker != null)
        {
            endMarker.SetActive(true);
            endMarkerRenderer.material.color = endColor; // 全红
            // 填充从僵尸脚下开始，长度几乎为 0
            endMarker.transform.position = GetGroundPosition(transform.position);
            endMarker.transform.localScale = new Vector3(0.001f, indicatorHeight, 0.001f);
        }
        if (endFrame != null) endFrame.gameObject.SetActive(true);
    }

    private void UpdateGroundIndicator()
    {
        float progress = Mathf.Clamp01(stateTimer / effectivePrepareDuration);

        Vector3 startPos = GetGroundPosition(transform.position);
        Vector3 dirToTarget = lockedIndicatorTarget - startPos;
        float totalDist = dirToTarget.magnitude;
        Vector3 dir = totalDist > 0.01f ? dirToTarget / totalDist : transform.forward;

        // ⭐ 红色填充：从僵尸脚下沿锁定方向填充到 进度×全程
        float fillLength = Mathf.Lerp(0.0f, totalDist, progress);
        Vector3 fillCenter = startPos + dir * (fillLength * 0.5f);
        fillCenter.y = startPos.y;

        if (endMarker != null)
        {
            endMarker.transform.position = fillCenter;
            // 让立方体长度轴(z) 沿锁定方向：宽度=X，长度=Z，高度=贴地厚度
            endMarker.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            endMarker.transform.localScale = new Vector3(indicatorSize, indicatorHeight, Mathf.Max(fillLength, 0.001f));
        }

        // ⭐ 外框：始终完整大小，包住从僵尸到锁定玩家的整条路径
        if (endFrame != null)
        {
            float half = indicatorSize * 0.5f;
            float y = startPos.y;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            endFrame.SetPosition(0, startPos + (-right * half));
            endFrame.SetPosition(1, startPos + (right * half));
            endFrame.SetPosition(2, lockedIndicatorTarget + (right * half));
            endFrame.SetPosition(3, lockedIndicatorTarget + (-right * half));
        }
    }

    private void HideGroundIndicator()
    {
        if (endMarker != null)
            endMarker.SetActive(false);
        if (endFrame != null)
            endFrame.gameObject.SetActive(false);
    }

    private Vector3 GetGroundPosition(Vector3 position)
    {
        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out hit, 5f))
        {
            return new Vector3(position.x, hit.point.y + indicatorHeight, position.z);
        }
        return new Vector3(position.x, indicatorHeight, position.z);
    }

    /// <summary>
    /// 视线检测：敌人到玩家之间是否有墙（blinkObstacleMask）阻挡。
    /// 从敌人胸口高度向玩家胸口发射射线，命中障碍即返回 false（不能瞬移/取消瞬移）。
    /// </summary>
    private bool HasClearLineToPlayer()
    {
        if (player == null) return false;

        Vector3 from = transform.position + Vector3.up * 1.4f;
        Vector3 to = player.transform.position + Vector3.up * 1.4f;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 0.01f) return true;

        return !Physics.Raycast(from, dir / dist, dist, blinkObstacleMask);
    }

    /// <summary>
    /// ⭐ 瞬移到"蓄力开始时锁定的玩家位置"前固定距离处（与指示条终点一致，不实时跟随玩家）。
    /// 成功瞬移返回 true；落点路径被墙挡则返回 false（不瞬移，由调用方回到追击）。
    /// </summary>
    private bool PerformBlinkToPlayer()
    {
        if (player == null) return false;

        // ⭐ 落点基准：蓄力开始时锁定的玩家位置（地面），之后不跟随玩家
        Vector3 lockedPlayerPos = lockedIndicatorTarget;
        if (lockedPlayerPos.sqrMagnitude < 0.001f)
            lockedPlayerPos = GetGroundPosition(player.transform.position);

        // 从僵尸指向锁定玩家位置的水平方向
        Vector3 toTarget = lockedPlayerPos - transform.position;
        toTarget.y = 0;
        Vector3 directionToTarget = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : -transform.forward;

        // ⭐ 目标点 = 锁定玩家位置 - 方向 × 固定距离（落在玩家来向的后方，始终保持固定间隔）
        Vector3 targetPos = lockedPlayerPos - directionToTarget * distanceToPlayerAfterBlink;
        targetPos.y = transform.position.y;

        // ⭐ 瞬移落点校验：从僵尸到落点的路径被墙挡（会穿墙瞬移、玩家看不到瞬移）就放弃瞬移
        if (IsBlinkTargetBlocked(targetPos))
            return false;

        // 确保目标点在地面上
        if (isAgentValid && agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPos, out hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                transform.position = hit.position;
            }
            else
            {
                agent.Warp(targetPos);
                transform.position = targetPos;
            }
        }
        else
        {
            transform.position = targetPos;
        }

        // 瞬移后面向玩家
        Vector3 faceDir = (player.transform.position - transform.position).normalized;
        faceDir.y = 0;
        if (faceDir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(faceDir);;
        return true;
    }

    /// <summary>
    /// 从僵尸胸口高度到落点胸口高度发射射线，命中障碍即返回 true（落点路径穿墙）。
    /// </summary>
    private bool IsBlinkTargetBlocked(Vector3 targetPos)
    {
        Vector3 from = transform.position + Vector3.up * 1.4f;
        Vector3 to = targetPos + Vector3.up * 1.4f;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 0.01f) return false;

        return Physics.Raycast(from, dir / dist, dist, blinkObstacleMask);
    }

    private void RotateTowardPlayer()
    {
        if (player == null) return;
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    // 蓄力开始时锁定朝向：面对蓄力瞬间玩家的方向，后续蓄力全程不跟随玩家转动
    private Vector3 GetLockedFacingDir()
    {
        if (player == null) return transform.forward;
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        return dir != Vector3.zero ? dir : transform.forward;
    }

    // 蓄力期间朝锁定方向转向（一次到位，不随玩家变）
    private void RotateToLockedFacing()
    {
        if (lockedFacingDir == Vector3.zero) lockedFacingDir = GetLockedFacingDir();
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lockedFacingDir), 10f * Time.deltaTime);
    }

    // 开启 ApplyRootMotion 后由动画回调：把动画 root 的 Y（跳跃离地）体现到子模型上，
    // 根节点位置/朝向仍由 NavMeshAgent 控制，避免跳跃同时被 agent 拉回地面、也不干扰寻路。
    // 用"覆盖式"而非累积（+=）：跳跃动画循环接缝的 root Y 不严格归位，累积几圈会持续下沉。
    private int lastAnimStateHash = 0;
    private float jumpBaseRootY = 0f;
    private bool jumpBaseValid = false;

    private void OnAnimatorMove()
    {
        if (visualModel == null || animator == null) return;

        // 进入新动画状态时重置基线（动画 root 位置从新 clip 起点重新累计）
        AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(0);
        if (si.fullPathHash != lastAnimStateHash)
        {
            lastAnimStateHash = si.fullPathHash;
            jumpBaseValid = false;
        }
        if (!jumpBaseValid)
        {
            jumpBaseRootY = animator.rootPosition.y;
            jumpBaseValid = true;
        }

        Vector3 lp = visualModel.localPosition;
        lp.y = animator.rootPosition.y - jumpBaseRootY;
        visualModel.localPosition = lp;
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        if (enemyData == null || !enemyData.showGizmos) return;

        // 蓄力触发距离（紫色）
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, prepareDistance);

        // 直接攻击距离（黄色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, directAttackDistance);

        // 攻击范围（红色）
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, enemyData.attackRange);

        // ⭐ 瞬移目标距离（青色虚线圆）- 显示瞬移后会出现在玩家多近
        if (player != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
            Gizmos.DrawWireSphere(player.transform.position, distanceToPlayerAfterBlink);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.5f,
            $"蓄力: {prepareDistance:F1}m\n直接攻击: {directAttackDistance:F1}m\n瞬移后距离: {distanceToPlayerAfterBlink:F1}m");
#endif
    }
}