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

    [Header("瞬移参数")]
    [Tooltip("进入此距离时开始蓄力停顿")]
    public float prepareDistance = 6f;
    [Tooltip("蓄力停顿时间（秒）")]
    public float prepareDuration = 1.0f;
    [Tooltip("瞬移后距离玩家多远（数值越小越贴脸）")]
    public float distanceToPlayerAfterBlink = 1.5f;
    [Tooltip("小于此距离时直接攻击（不瞬移）")]
    public float directAttackDistance = 2f;
    [Tooltip("指示线满后到瞬移前的停顿时间（秒）")]
    public float postBlinkDelay = 0.2f;
    [Tooltip("瞬移冷却时间（秒）")]
    public float blinkCooldown = 2.5f;

    [Header("地面指示特效")]
    [Tooltip("是否显示地面指示")]
    public bool showGroundIndicator = true;
    [Tooltip("指示线开始颜色（蓄力开始时）")]
    public Color startColor = Color.green;
    [Tooltip("指示线结束颜色（蓄力完成时）")]
    public Color endColor = Color.red;
    [Tooltip("指示线宽度")]
    public float indicatorWidth = 0.2f;
    [Tooltip("指示线高度（贴地）")]
    public float indicatorHeight = 0.05f;

    private LineRenderer lineRenderer;
    private GameObject endMarker;
    private MeshRenderer endMarkerRenderer;

    protected override void OnStart()
    {
        base.OnStart();
        blinkState = BlinkState.Chasing;

        if (showGroundIndicator)
        {
            CreateGroundIndicator();
        }
    }

    private void CreateGroundIndicator()
    {
        GameObject lineObj = new GameObject("BlinkIndicator_Line");
        lineObj.transform.SetParent(transform);
        lineObj.transform.localPosition = Vector3.zero;

        lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = indicatorWidth;
        lineRenderer.endWidth = indicatorWidth;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = startColor;
        lineRenderer.endColor = startColor;
        lineRenderer.sortingOrder = 10;
        lineRenderer.enabled = false;

        endMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        endMarker.name = "BlinkIndicator_End";
        endMarker.transform.SetParent(transform);
        endMarker.transform.localScale = new Vector3(0.3f, 0.05f, 0.3f);
        Destroy(endMarker.GetComponent<Collider>());

        endMarkerRenderer = endMarker.GetComponent<MeshRenderer>();
        endMarkerRenderer.material = new Material(Shader.Find("Sprites/Default"));
        endMarkerRenderer.material.color = startColor;
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
                // ⭐ 如果距离很近（贴脸），直接攻击，不瞬移
                if (distance <= directAttackDistance && canAttack && !isAttacking)
                {
                    StopAgent();
                    RotateTowardPlayer();
                    if (IsFacingPlayer())
                    {
                        PerformAttack();
                    }
                    return;
                }

                // ⭐ 正常追击
                if (distance > prepareDistance + 0.5f)
                {
                    if (isAgentValid)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(player.transform.position);
                    }
                }

                // ⭐ 进入蓄力（距离在 prepareDistance 和 directAttackDistance 之间）
                if (distance <= prepareDistance && distance > directAttackDistance &&
                    !isAttacking && blinkState == BlinkState.Chasing)
                {
                    blinkState = BlinkState.Preparing;
                    stateTimer = 0f;
                    hasPrepared = false;
                    hasAttacked = false;
                    StopAgent();

                    if (showGroundIndicator)
                    {
                        ShowGroundIndicator();
                    }
                }
                break;

            case BlinkState.Preparing:
                StopAgent();
                RotateTowardPlayer();

                stateTimer += Time.deltaTime;

                if (showGroundIndicator && lineRenderer != null)
                {
                    UpdateGroundIndicator();
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
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, GetGroundPosition(transform.position));
            lineRenderer.SetPosition(1, GetGroundPosition(transform.position));
            lineRenderer.startColor = startColor;
            lineRenderer.endColor = startColor;
        }

        if (endMarker != null)
        {
            endMarker.SetActive(true);
            endMarker.transform.position = GetGroundPosition(transform.position);
            endMarkerRenderer.material.color = startColor;
        }
    }

    private void UpdateGroundIndicator()
    {
        float progress = Mathf.Clamp01(stateTimer / prepareDuration);

        Vector3 startPos = GetGroundPosition(transform.position);

        // ⭐ 计算瞬移目标方向（朝向玩家）
        Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;
        float currentDistance = Vector3.Distance(transform.position, player.transform.position);
        float blinkDistance = currentDistance - distanceToPlayerAfterBlink;
        blinkDistance = Mathf.Max(blinkDistance, 0);

        Vector3 endPos = GetGroundPosition(transform.position + directionToPlayer * blinkDistance * progress);

        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);

        Color currentColor = Color.Lerp(startColor, endColor, progress);
        lineRenderer.startColor = currentColor;
        lineRenderer.endColor = currentColor;

        float currentWidth = indicatorWidth * (0.5f + 0.5f * progress);
        lineRenderer.startWidth = currentWidth;
        lineRenderer.endWidth = currentWidth;

        if (endMarker != null)
        {
            endMarker.transform.position = endPos;
            endMarkerRenderer.material.color = currentColor;
            float scale = 0.2f + 0.4f * progress;
            endMarker.transform.localScale = new Vector3(scale, 0.05f, scale);
        }
    }

    private void HideGroundIndicator()
    {
        if (lineRenderer != null)
            lineRenderer.enabled = false;
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