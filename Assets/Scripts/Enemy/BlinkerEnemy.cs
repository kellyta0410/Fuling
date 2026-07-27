using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BlinkerEnemy : EnemyAI
{
    private enum BlinkState { Chasing, Preparing, Blinking, PostBlink }
    private BlinkState blinkState = BlinkState.Chasing;
    private float stateTimer = 0f;
    private bool hasPrepared = false;

    [Header("瞬移参数")]
    [Tooltip("瞬移后与玩家的距离（数值越小越贴脸）")]
    public float blinkDistance = 3f;          // 瞬移后距离玩家多远
    [Tooltip("瞬移前蓄力停顿时间")]
    public float prepareDuration = 1.0f;
    [Tooltip("瞬移后停顿再攻击的时间")]
    public float postBlinkDelay = 0.5f;

    protected override void OnStart()
    {
        blinkState = BlinkState.Chasing;
    }

    protected override void HandleMovement()
    {
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);

        switch (blinkState)
        {
            case BlinkState.Chasing:
                // 正常追击（距离大于瞬移触发距离时）
                if (distance > blinkDistance + 1f) // 加1m余量，防止频繁切换
                {
                    if (isAgentValid)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(player.transform.position);
                    }
                }

                // 当距离 <= blinkDistance + 1 且未攻击，进入准备
                if (distance <= blinkDistance + 1f && !isAttacking && blinkState == BlinkState.Chasing)
                {
                    blinkState = BlinkState.Preparing;
                    stateTimer = 0f;
                    hasPrepared = false;
                    StopAgent();
                }
                break;

            case BlinkState.Preparing:
                StopAgent();
                RotateTowardPlayer();
                stateTimer += Time.deltaTime;
                if (stateTimer >= prepareDuration && !hasPrepared)
                {
                    hasPrepared = true;
                    PerformBlink();
                    blinkState = BlinkState.Blinking;
                }
                break;

            case BlinkState.Blinking:
                // 瞬移完成，直接进入后摇
                blinkState = BlinkState.PostBlink;
                stateTimer = 0f;
                break;

            case BlinkState.PostBlink:
                StopAgent();
                RotateTowardPlayer();
                stateTimer += Time.deltaTime;
                if (stateTimer >= postBlinkDelay)
                {
                    // 尝试攻击（如果距离足够且面向玩家）
                    float distAfterBlink = Vector3.Distance(transform.position, player.transform.position);
                    if (distAfterBlink <= enemyData.attackRange && IsFacingPlayer() && canAttack)
                        PerformAttack();
                    // 回到追逐状态
                    blinkState = BlinkState.Chasing;
                }
                break;
        }
    }

    /// <summary>
    /// 执行瞬移：瞬移到玩家前方 blinkDistance 米处
    /// </summary>
    private void PerformBlink()
    {
        Vector3 dirToPlayer = (player.transform.position - transform.position).normalized;
        // 目标点 = 玩家位置 - 方向 * 瞬移距离（这样敌人会出现在玩家前方）
        Vector3 targetPos = player.transform.position - dirToPlayer * blinkDistance;
        targetPos.y = transform.position.y; // 保持相同高度

        if (isAgentValid && agent.isOnNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPos, out hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                agent.Warp(targetPos);
        }
        else
        {
            transform.position = targetPos;
        }
        RotateTowardPlayer();
    }

    private void RotateTowardPlayer()
    {
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        if (enemyData == null || !enemyData.showGizmos) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, blinkDistance);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, $"瞬移距离: {blinkDistance:F1}");
#endif
    }
}