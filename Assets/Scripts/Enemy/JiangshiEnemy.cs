using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class JiangshiEnemy : EnemyAI
{
    private enum BlinkState { Chasing, Preparing, Charging, Cooldown }
    private BlinkState blinkState = BlinkState.Chasing;
    private float stateTimer = 0f;
    private bool hasPrepared = false;
    private bool hasAttacked = false;
    private Vector3 lockedFacingDir = Vector3.zero;  // 蓄力开始时锁定朝向（身体 + 指示线共用）

    // 冲撞运行时状态
    private float chargeTraveled = 0f;
    private bool hasHitPlayer = false;
    private float chargeDistanceCache = 0f;

    // 全局：同时处于冲撞(蓄力+冲刺)的僵尸最多 MaxConcurrentCharges 只，避免一窝蜂同时冲
    private const int MaxConcurrentCharges = 2;
    private static readonly HashSet<JiangshiEnemy> s_charging = new HashSet<JiangshiEnemy>();
    private bool meChargeActive = false;

    private void LeaveChargeState()
    {
        if (meChargeActive)
        {
            meChargeActive = false;
            s_charging.Remove(this);
        }
    }

    protected override void OnDestroy()
    {
        LeaveChargeState();
        base.OnDestroy();
    }

    [Header("冲撞/蓄力参数")]
    [Tooltip("大于此距离才会冲撞（远距离冲撞接近；近距离直接走过去打）")]
    public float prepareDistance = 6f;
    [Tooltip("蓄力停顿时间（秒）")]
    public float prepareDuration = 2.0f;
    [Tooltip("蓄力时长随机偏移(±秒)：拉开多个僵尸的瞬移时刻，避免全部同一时间瞬移")]
    public float prepareDurationJitter = 0.8f;
    [Tooltip("小于等于此距离时直接走过去攻击（不再瞬移）")]
    public float directAttackDistance = 2f;
    [Tooltip("冲撞冷却时间（秒）")]
    public float blinkCooldown = 15f;
    [Tooltip("瞬移会被哪些层挡住（默认 Wall 层）。有墙挡着就不会冲撞，改为绕墙走过去")]
    public LayerMask blinkObstacleMask = 1 << 6;

    [Header("冲撞攻击参数")]
    [Tooltip("冲撞冲刺速度(米/秒)")]
    public float chargeSpeed = 34f;
    [Tooltip("冲撞终点超过玩家当前位置的距离(米)：红条会越过玩家，冲过去")]
    public float chargeOvershoot = 3f;
    [Tooltip("冲撞命中玩家的判定半径(米)")]
    public float chargeHitRadius = 2f;
    [Tooltip("冲撞命中后把玩家击退的距离(米)，轻微")]
    public float chargeKnockback = 2.5f;
    [Tooltip("冲撞伤害基础值（不再 ×普通攻击，固定基础值 × 房间缩放）")]
    public float chargeBaseDamage = 8f;

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
        // 冲撞/蓄力每帧由这里驱动：即使 base 在攻击范围内提前 return 跳过 HandleMovement，
        // 也能保证蓄力读条不被普通攻击打断（"不打断瞬移"）。
        if (blinkState == BlinkState.Preparing || blinkState == BlinkState.Charging)
            DriveSpecial();
        UpdateLandSFX();
    }

    // 任意攻击（普通攻击 / 技能攻击）命中并触发击退时，EnemyAI.AddKnockback 都会调用 OnKnockback，
    // 这里统一中断冲撞（蓄力/冲刺）→ 回到冷却。两个攻击入口都走 AddKnockback，因此“两种攻击都能打断”。
    public void InterruptCharge()
    {
        if (blinkState == BlinkState.Preparing || blinkState == BlinkState.Charging)
        {
            HideGroundIndicator();
            SetSeparationSuspended(false);
            LeaveChargeState();
            blinkState = BlinkState.Cooldown;
            stateTimer = 0f;
        }
    }

    // 被击中/击退会中断蓄力与冲撞（恢复分离推挤，回到冷却）
    protected override void OnKnockback()
    {
        base.OnKnockback();
        InterruptCharge();
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
                            NotePathMutation("Jiangshi.Chasing近距 → isStopped=false");
                            agent.isStopped = false;
                            SetChaseDestination(GetStandoffTarget(player.transform.position));
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

                // ⭐ 远距离：只在无墙遮挡时冲撞接近（蓄力 → 指示红条越过玩家 → 冲撞 → 冷却）。
                // 有墙挡着就继续绕墙追击，等玩家出墙再冲撞。
                if (distance > prepareDistance && !isAttacking && blinkState == BlinkState.Chasing)
                {
                    if (!HasClearLineToPlayer())
                    {
                        if (isAgentValid)
                        {
                            NotePathMutation("Jiangshi.blink挡线 → isStopped=false");
                            agent.isStopped = false;
                            SetChaseDestination(GetStandoffTarget(player.transform.position));
                        }
                        return;
                    }

                    // ⭐ 同时冲撞的僵尸最多 2 只：已达上限本帧不进入蓄力，继续追击，下帧再试
                    if (s_charging.Count >= MaxConcurrentCharges)
                        return;

                    s_charging.Add(this);
                    meChargeActive = true;
                    blinkState = BlinkState.Preparing;
                    stateTimer = 0f;
                    // ⭐ 本次蓄力随机化时长：不同僵尸瞬移时刻错开，避免同时瞬移
                    effectivePrepareDuration = Mathf.Max(prepareDuration + Random.Range(-prepareDurationJitter, prepareDurationJitter), 0.5f);
                    hasPrepared = false;
                    hasAttacked = false;
                    lockedFacingDir = GetLockedFacingDir();
                    // ⭐ 锁定冲撞终点：沿锁定方向冲过玩家再超出 chargeOvershoot（红条越过玩家）
                    float distToPlayer0 = player != null ? Vector3.Distance(transform.position, player.transform.position) : 0f;
                    lockedIndicatorTarget = player != null
                        ? GetGroundPosition(transform.position + lockedFacingDir * (distToPlayer0 + chargeOvershoot))
                        : GetGroundPosition(transform.position);
                    StopAgent();

                    if (showGroundIndicator)
                    {
                        ShowGroundIndicator();
                    }
                    return;
                }

                // ⭐ 走到这里说明距离 <= prepareDistance（近距已在上方处理）或正在攻击，
                // 保持追击即可（远距一律进入冲撞蓄力）
                if (isAgentValid)
                {
                    NotePathMutation("Jiangshi.blink未触发 → isStopped=false");
                    agent.isStopped = false;
                    SetChaseDestination(GetStandoffTarget(player.transform.position));
                }
                return;

            case BlinkState.Preparing:
                // 蓄力逻辑已移到 Update()->DriveSpecial()，避免被普通攻击打断（不打断瞬移）
                break;

            case BlinkState.Charging:
                // 冲撞逻辑同样由 Update()->DriveSpecial() 驱动
                break;

            case BlinkState.Cooldown:
                // ⭐ 冷却期间：只追击，不攻击
                if (distance > enemyData.attackRange)
                {
                    if (isAgentValid)
                    {
                        NotePathMutation("Jiangshi.冷却追击 → isStopped=false");
                        agent.isStopped = false;
                        SetChaseDestination(GetStandoffTarget(player.transform.position));
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

    // ⭐ 冲撞/蓄力由 Update 每帧驱动（即使 base 在攻击范围内提前 return 跳过 HandleMovement，
    // 也能保证蓄力读条不被普通攻击/击退打断）。保留墙检测：蓄力期玩家躲墙后→取消；冲撞遇墙→停。
    private void DriveSpecial()
    {
        if (blinkState == BlinkState.Preparing)
        {
            StopAgent();
            // 蓄力站桩锁定站位：挂起分离推挤，避免被挤动导致落点/指示错位
            SetSeparationSuspended(true);
            // 蓄力期间朝锁定方向转向（身体不跟随玩家，落点/朝向都锁定在蓄力开始瞬间）
            RotateToLockedFacing();

            stateTimer += Time.deltaTime;

            if (showGroundIndicator && endMarker != null)
            {
                UpdateGroundIndicator();
            }

            // ⭐ 蓄力过程中玩家躲到墙后：取消蓄力，回到追击（瞬移不能穿墙）
            if (!HasClearLineToPlayer())
            {
                HideGroundIndicator();
                SetSeparationSuspended(false);
                LeaveChargeState();
                blinkState = BlinkState.Chasing;
                stateTimer = 0f;
                return;
            }

            // ⭐ 蓄力填满：进入冲撞（锁定方向不变，红条已越过玩家）
            if (stateTimer >= effectivePrepareDuration && !hasPrepared)
            {
                hasPrepared = true;
                HideGroundIndicator();

                // 最后校验：玩家躲到墙后 / 冲撞路径被墙挡，就放弃冲撞改为追击
                if (!HasClearLineToPlayer())
                {
                    LeaveChargeState();
                    blinkState = BlinkState.Chasing;
                    stateTimer = 0f;
                    return;
                }

                float distToPlayer = player != null ? Vector3.Distance(transform.position, player.transform.position) : 0f;
                chargeDistanceCache = distToPlayer + chargeOvershoot;
                chargeTraveled = 0f;
                hasHitPlayer = false;
                if (player != null)
                    lockedIndicatorTarget = GetGroundPosition(transform.position + lockedFacingDir * chargeDistanceCache);
                blinkState = BlinkState.Charging;   // 蓄力期间已挂起分离推挤，冲撞全程保持
                stateTimer = 0f;
            }
        }
        else if (blinkState == BlinkState.Charging)
        {
            DriveCharge();
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
    /// 额外用 NavMesh.Raycast 检测 carv ed 的 NavMeshObstacle（装饰物挖洞也阻断瞬移路径）。
    /// </summary>
    private bool HasClearLineToPlayer()
    {
        if (player == null) return false;

        Vector3 from = transform.position + Vector3.up * 1.4f;
        Vector3 to = player.transform.position + Vector3.up * 1.4f;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 0.01f) return true;

        if (Physics.Raycast(from, dir / dist, dist, blinkObstacleMask))
            return false;

        if (IsNavMeshPathBlocked())
            return false;

        return true;
    }

    // ============ 冲撞攻击 ============
    // 蓄力(Preparing)填满后进入冲撞：沿锁定方向高速冲刺，红条已越过玩家；
    // 冲刺中命中玩家→轻微击退 + 高于普攻的伤害；冲撞期间抑制普通攻击（避免同时触发）。
    private void DriveCharge()
    {
        StopAgent();
        RotateToLockedFacing();   // 冲撞全程锁定蓄力朝向，不跟随玩家

        if (player != null && !player.IsDead())
        {
            // 正前方有墙就提前结束（不穿墙）
            if (WallAhead(lockedFacingDir, chargeSpeed * Time.deltaTime + 0.6f))
            {
                EndCharge();
                return;
            }

            float step = chargeSpeed * Time.deltaTime;
            if (isAgentValid && agent != null && agent.isOnNavMesh)
                agent.Move(lockedFacingDir * step);
            else
                transform.position += lockedFacingDir * step;
            chargeTraveled += step;

            if (!hasHitPlayer)
            {
                Vector3 a = transform.position; a.y = 0f;
                Vector3 b = player.transform.position; b.y = 0f;
                if (Vector3.Distance(a, b) <= chargeHitRadius)
                {
                    DealChargeHit();
                    hasHitPlayer = true;
                    // 命中即结束冲撞并进入冷却：只撞开玩家一次，绝不连撞/顶着人继续冲
                    EndCharge();
                    return;
                }
            }
        }

        if (chargeTraveled >= chargeDistanceCache)
            EndCharge();
    }

    private void DealChargeHit()
    {
        if (player == null || player.IsDead()) return;
        float dmg = chargeBaseDamage * currentDamageMultiplier;
        player.TakeDamage(Mathf.RoundToInt(dmg));
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        Vector3 fwd = lockedFacingDir; fwd.y = 0f;
        // 击退方向改成“往旁边”：取玩家相对僵尸的位置在垂直于冲撞方向平面上的分量，
        // 把玩家甩离冲撞中线，而不是沿僵尸前进方向一路往前顶（避免连续推着走）。
        Vector3 lateral = toPlayer - fwd * Vector3.Dot(toPlayer, fwd);
        if (lateral.sqrMagnitude < 1e-4f)
            lateral = Vector3.Cross(fwd, Vector3.up).normalized; // 玩家正好在冲撞中线上时随便选一侧
        else
            lateral.Normalize();
        player.AddKnockback(lateral, chargeKnockback);
        if (attackSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(attackSFX, transform.position);
    }

    private bool WallAhead(Vector3 dir, float dist)
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 1.0f, dir, out hit, dist, blinkObstacleMask))
            return true;
        return false;
    }

    private void EndCharge()
    {
        HideGroundIndicator();
        SetSeparationSuspended(false);
        hasHitPlayer = false;
        LeaveChargeState();
        blinkState = BlinkState.Cooldown;
        stateTimer = 0f;
    }

    // 蓄力/冲撞期间抑制普通攻击，避免“同时触发”；位移与读条由 Update()->DriveSpecial() 驱动
    protected override bool TryPerformInRangeAttack()
    {
        if (blinkState == BlinkState.Preparing || blinkState == BlinkState.Charging)
        {
            return true;   // 蓄力/冲撞期间吃掉攻击请求，普通攻击不打断瞬移
        }
        return base.TryPerformInRangeAttack();
    }

    /// <summary>
    /// 装饰物(NavMeshObstacle, carve)会在导航网格上挖洞，普通物理射线打不到；
    /// 用 NavMesh.Raycast 沿导航网格从当前位置朝玩家/落点方向投射，
    /// 若路径被挖洞处阻断（返回 true 且命中的是 carve 边界）则视为瞬移会穿过装饰物，放弃瞬移。
    /// </summary>
    private bool IsNavMeshPathBlocked()
    {
        if (!isAgentValid || agent == null || !agent.isOnNavMesh) return false;
        if (player == null) return false;

        NavMeshHit hit;
        Vector3 from = transform.position;
        Vector3 to = player.transform.position;
        to.y = from.y;
        if (NavMesh.Raycast(from, to, out hit, NavMesh.AllAreas))
            return true;
        return false;
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

        // ⭐ 冲撞步进距离（青色圆）- 显示冲撞越过玩家的超出量
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, chargeOvershoot);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.5f,
            $"蓄力触发: {prepareDistance:F1}m\n直接攻击: {directAttackDistance:F1}m\n冲撞超出: {chargeOvershoot:F1}m\n冲撞速度: {chargeSpeed:F1}m/s");
#endif
    }
}