using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class JiangshiEnemy : EnemyAI
{
    private enum BlinkState { Chasing, Preparing, Blinking, PostBlink, Cooldown }
    private BlinkState blinkState = BlinkState.Chasing;
    private float stateTimer = 0f;
    private bool hasPrepared = false;
    private bool hasAttacked = false;
    private Vector3 lockedFacingDir = Vector3.zero;  // 蓄力开始时锁定朝向（身体 + 指示线共用）

    [Header("瞬移参数")]
    [Tooltip("大于此距离才会瞬移（远距离用瞬移接近）")]
    public float prepareDistance = 6f;
    [Tooltip("蓄力停顿时间（秒）")]
    public float prepareDuration = 1.0f;
    [Tooltip("瞬移后距离玩家多远（数值越小越贴脸）")]
    public float distanceToPlayerAfterBlink = 1.5f;
    [Tooltip("小于等于此距离时直接走过去攻击（不再瞬移）")]
    public float directAttackDistance = 2f;
    [Tooltip("指示线满后到瞬移前的停顿时间（秒）")]
    public float postBlinkDelay = 0.2f;
    [Tooltip("瞬移冷却时间（秒）")]
    public float blinkCooldown = 2.5f;
    [Tooltip("瞬移会被哪些层挡住（默认 Wall 层）。有墙挡着就不会瞬移，改为绕墙走过去")]
    public LayerMask blinkObstacleMask = 1 << 6;

    [Header("地面指示特效")]
    [Tooltip("是否显示地面指示")]
    public bool showGroundIndicator = true;
    [Tooltip("指示方块颜色（全红）")]
    public Color endColor = Color.red;
    [Tooltip("指示方块的边长(米)")]
    public float indicatorSize = 1.0f;
    [Tooltip("指示方块高度（贴地）")]
    public float indicatorHeight = 0.05f;

    private GameObject endMarker;
    private MeshRenderer endMarkerRenderer;

    // 动画 root motion 的 Y（跳跃离地）会作用在子模型上；根节点 XZ 由 NavMeshAgent 驱动。
    private Transform visualModel;

    protected override void OnStart()
    {
        base.OnStart();
        blinkState = BlinkState.Chasing;
        if (transform.childCount > 0)
            visualModel = transform.GetChild(0);

        // 僵尸碰撞体 1.6×1.4 + 玩家 CC 半径 0.8 → 至少需 1.6m 才不重叠。
        // 通用 stoppingDistance(攻击范围×0.5) 只有 1.1m，会挤进玩家体积顶动玩家(CC depenetrate)，
        // 这里覆盖为 ≥ 碰撞不重叠距离，保证追击全程不把碰撞体扫进玩家。
        if (isAgentValid && agent != null)
        {
            agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, 1.7f);
            agent.autoBraking = true;
        }

        if (showGroundIndicator)
        {
            CreateGroundIndicator();
        }
    }

    private void CreateGroundIndicator()
    {
        // 正方块指示器：平铺在地面上，表示瞬移落点区域，随蓄力进度在锁定方向上推进变大
        endMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        endMarker.name = "BlinkIndicator_Square";
        endMarker.transform.SetParent(transform);
        endMarker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 平铺贴地
        endMarker.transform.localScale = new Vector3(indicatorSize, indicatorHeight, indicatorSize);
        Destroy(endMarker.GetComponent<Collider>());

        endMarkerRenderer = endMarker.GetComponent<MeshRenderer>();
        endMarkerRenderer.material = new Material(Shader.Find("Sprites/Default"));
        endMarkerRenderer.material.color = endColor; // 全红
        endMarker.SetActive(false);
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
                    hasPrepared = false;
                    hasAttacked = false;
                    lockedFacingDir = GetLockedFacingDir();
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
                    blinkState = BlinkState.Chasing;
                    stateTimer = 0f;
                    return;
                }

                // ⭐ 蓄力过程中如果玩家靠近到直接攻击距离，取消蓄力直接攻击
                float currentDistance = Vector3.Distance(transform.position, player.transform.position);
                if (currentDistance <= directAttackDistance && canAttack && !isAttacking)
                {
                    HideGroundIndicator();
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

                // ⭐ 蓄力完成，进入"瞬移前等待"状态
                if (stateTimer >= prepareDuration && !hasPrepared)
                {
                    hasPrepared = true;
                    HideGroundIndicator();

                    // 进入瞬移前等待
                    blinkState = BlinkState.Blinking;
                    stateTimer = 0f;
                }
                break;

            case BlinkState.Blinking:
                // ⭐ 瞬移前等待（指示线满后等 postBlinkDelay 秒再瞬移）
                stateTimer += Time.deltaTime;
                if (stateTimer >= postBlinkDelay)
                {
                    // 瞬移前最后校验：玩家已躲到墙后或瞬移路径被墙挡，就放弃瞬移
                    if (!HasClearLineToPlayer())
                    {
                        HideGroundIndicator();
                        blinkState = BlinkState.Chasing;
                        stateTimer = 0f;
                        return;
                    }
                    PerformBlinkToPlayer();
                    blinkState = BlinkState.PostBlink;
                    stateTimer = 0f;
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
            endMarker.transform.position = GetGroundPosition(transform.position);
            endMarkerRenderer.material.color = endColor; // 全红
            endMarker.transform.localScale = new Vector3(0.001f, indicatorHeight, 0.001f); // 从没有开始
        }
    }

    private void UpdateGroundIndicator()
    {
        float progress = Mathf.Clamp01(stateTimer / prepareDuration);

        // ⭐ 计算瞬移目标方向：使用蓄力开始锁定的方向，蓄力全程不随玩家移动改变
        Vector3 directionToPlayer = lockedFacingDir != Vector3.zero
            ? lockedFacingDir
            : (player.transform.position - transform.position).normalized;
        float currentDistance = Vector3.Distance(transform.position, player.transform.position);
        float blinkDistance = currentDistance - distanceToPlayerAfterBlink;
        blinkDistance = Mathf.Max(blinkDistance, 0);

        // 方块在锁定方向上从脚下推进到瞬移落点
        Vector3 endPos = GetGroundPosition(transform.position + directionToPlayer * blinkDistance * progress);

        if (endMarker != null)
        {
            endMarker.transform.position = endPos;
            // 全红，尺寸随进度从无(几乎0)填满到 indicatorSize
            float scale = Mathf.Lerp(0.001f, indicatorSize, progress);
            endMarker.transform.localScale = new Vector3(scale, indicatorHeight, scale);
        }
    }

    private void HideGroundIndicator()
    {
        if (endMarker != null)
            endMarker.SetActive(false);
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
    /// ⭐ 瞬移到距离玩家固定距离的位置
    /// </summary>
    private void PerformBlinkToPlayer()
    {
        if (player == null) return;

        // 计算从敌人指向玩家的方向
        Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;

        // 目标点 = 玩家位置 - 方向 × 距离（出现在玩家面前）
        Vector3 targetPos = player.transform.position - directionToPlayer * distanceToPlayerAfterBlink;
        targetPos.y = transform.position.y;

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
            $"蓄力: {prepareDistance:F1}m\n直接攻击: {directAttackDistance:F1}m\n瞬移后距离: {distanceToPlayerAfterBlink:F1}m\n瞬移前停顿: {postBlinkDelay:F1}s");
#endif
    }
}