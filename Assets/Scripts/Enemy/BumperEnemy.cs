using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BumperEnemy : EnemyAI
{
    private enum ChargeState { Chasing, WindUp, Charging, Recovery, Cooldown }
    private ChargeState chargeState = ChargeState.Chasing;
    private float stateTimer = 0f;
    private Vector3 chargeTarget;      // 冲锋目标点（蓄力结束时锁定）
    private Vector3 chargeStartPos;    // 冲锋起点

    private Collider enemyCollider;
    private Collider playerCollider;
    private CharacterController playerController;
    private bool collisionIgnored = false;

    [Header("冲锋参数")]
    [Tooltip("进入此距离时开始蓄力")]
    public float windUpDistance = 10f;
    [Tooltip("蓄力持续时间（秒）")]
    public float windUpDuration = 1.2f;
    [Tooltip("冲锋速度")]
    public float chargeSpeed = 18f;
    [Tooltip("冲锋最远距离")]
    public float chargeMaxDistance = 20f;
    [Tooltip("冲锋最小距离（低于此距离不会停止）")]
    public float chargeMinDistance = 5f;
    [Tooltip("冲锋后硬直（秒）")]
    public float recoveryDuration = 0.6f;
    [Tooltip("冲锋冷却时间（秒），防止连续冲锋")]
    public float chargeCooldown = 3.5f;
    [Tooltip("蓄力结束后，冲锋前的停顿时间（秒），让玩家有反应时间")]
    public float chargeStartDelay = 0.4f;

    [Header("击退参数")]
    [Tooltip("玩家被击退的持续时间（秒）")]
    public float pushDuration = 0.15f;
    [Tooltip("玩家被击退的距离（米）")]
    public float pushDistance = 1.2f;

    protected override void OnStart()
    {
        base.OnStart();
        chargeState = ChargeState.Chasing;

        enemyCollider = GetComponent<Collider>();
        if (player != null)
        {
            playerCollider = player.GetComponent<Collider>();
            playerController = player.GetComponent<CharacterController>();
        }
    }

    protected override void PerformAttack()
    {
        if (!canAttack || isDead) return;

        canAttack = false;
        attackCooldownTimer = 0f;

        float finalDamage = baseAttackDamage * currentDamageMultiplier;
        if (player != null && !player.IsDead())
        {
            player.TakeDamage(Mathf.RoundToInt(finalDamage));
            StartCoroutine(SmoothPushPlayer(pushDuration, pushDistance));
        }

        StartCoroutine(ResetAttackCooldown());
    }

    private IEnumerator SmoothPushPlayer(float duration, float distance)
    {
        if (player == null || playerController == null) yield break;

        Vector3 pushDirection = (player.transform.position - transform.position).normalized;
        pushDirection.y = 0;
        if (pushDirection == Vector3.zero) yield break;

        Vector3 startPos = player.transform.position;
        Vector3 endPos = startPos + pushDirection * distance;

        if (Physics.Raycast(endPos + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 5f))
        {
            endPos.y = groundHit.point.y;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float smoothT = t * t * (3f - 2f * t);
            Vector3 targetPos = Vector3.Lerp(startPos, endPos, smoothT);
            Vector3 moveDelta = targetPos - player.transform.position;
            playerController.Move(moveDelta);
            yield return null;
        }

        Vector3 finalDelta = endPos - player.transform.position;
        if (finalDelta.magnitude > 0.01f)
        {
            playerController.Move(finalDelta);
        }
    }

    private IEnumerator ResetAttackCooldown()
    {
        yield return new WaitForSeconds(enemyData.attackCooldown);
        canAttack = true;
    }

    protected override void HandleMovement()
    {
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            return;
        }

        if (chargeState != ChargeState.Charging && isAttacking)
        {
            StopAgent();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);

        switch (chargeState)
        {
            case ChargeState.Chasing:
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
                    if (IsFacingPlayer() && canAttack && !isAttacking)
                    {
                        base.PerformAttack();
                    }
                }

                if (distance <= windUpDistance && chargeState == ChargeState.Chasing)
                {
                    chargeState = ChargeState.WindUp;
                    stateTimer = 0f;
                    StopAgent();
                    if (animator != null)
                        animator.SetTrigger("ChargeWindUp");
                }
                break;

            case ChargeState.WindUp:
                // 蓄力时：持续面向玩家（实时更新）
                StopAgent();
                Vector3 dirToTarget = (player.transform.position - transform.position).normalized;
                dirToTarget.y = 0;
                if (dirToTarget != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dirToTarget), 5f * Time.deltaTime);

                stateTimer += Time.deltaTime;
                if (stateTimer >= windUpDuration)
                {
                    chargeState = ChargeState.Charging;
                    stateTimer = 0f;                        // 重置计时器，用于冲锋延迟
                    if (isAgentValid) agent.isStopped = true;
                    if (animator != null)
                        animator.SetTrigger("ChargeStart");

                    // 锁定冲锋方向（基于当前玩家位置）
                    chargeStartPos = transform.position;
                    chargeTarget = player.transform.position;

                    IgnorePlayerCollision(true);
                }
                break;

            case ChargeState.Charging:
                // 更新计时器
                stateTimer += Time.deltaTime;

                // ========== 停顿阶段：锁定朝向，静止不动 ==========
                if (stateTimer < chargeStartDelay)
                {
                    // ✅ 不旋转（保持蓄力结束时的朝向），不移动，不检测障碍物
                    // 完全静止，让玩家看到敌人“瞄准”了某个方向
                    break;
                }

                // ========== 冲锋阶段：直线冲刺 ==========
                // 计算固定方向（蓄力结束时锁定的方向）
                Vector3 dir = (chargeTarget - chargeStartPos).normalized;
                dir.y = 0;

                // 1. 检测前方障碍物（每帧检测，忽略玩家和敌人）
                if (IsBlocked())
                {
                    Debug.Log($"[冲锋] 撞到障碍物，立即停止！");
                    chargeState = ChargeState.Recovery;
                    stateTimer = 0f;
                    if (isAgentValid) agent.isStopped = false;
                    if (animator != null)
                        animator.SetTrigger("ChargeEnd");
                    IgnorePlayerCollision(false);
                    break;
                }

                float distanceTraveled = Vector3.Distance(transform.position, chargeStartPos);

                // 2. 距离判定（最小距离内不结束，防止原地停止）
                if (distanceTraveled >= chargeMinDistance)
                {
                    if (distanceTraveled >= chargeMaxDistance)
                    {
                        Debug.Log($"[冲锋] 冲锋结束！已达最大距离: {distanceTraveled:F1}m");
                        chargeState = ChargeState.Recovery;
                        stateTimer = 0f;
                        if (isAgentValid) agent.isStopped = false;
                        if (animator != null)
                            animator.SetTrigger("ChargeEnd");
                        IgnorePlayerCollision(false);
                        break;
                    }
                }

                // 3. 移动
                transform.position += dir * chargeSpeed * Time.deltaTime;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(dir);

                // 4. 撞击玩家
                float currentDistance = Vector3.Distance(transform.position, player.transform.position);
                if (currentDistance <= enemyData.attackRange && canAttack)
                {
                    PerformAttack();
                    Debug.Log($"[冲锋] 撞到玩家！当前距离: {currentDistance:F1}m");
                }
                break;

            case ChargeState.Recovery:
                StopAgent();
                RotateTowardPlayer();
                stateTimer += Time.deltaTime;
                if (stateTimer >= recoveryDuration)
                {
                    chargeState = ChargeState.Cooldown;
                    stateTimer = 0f;
                    if (isAgentValid) agent.isStopped = false;
                }
                break;

            case ChargeState.Cooldown:
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
                    if (IsFacingPlayer() && canAttack && !isAttacking)
                    {
                        base.PerformAttack();
                    }
                }

                stateTimer += Time.deltaTime;
                if (stateTimer >= chargeCooldown)
                {
                    chargeState = ChargeState.Chasing;
                }
                break;
        }
    }

    private void IgnorePlayerCollision(bool ignore)
    {
        if (enemyCollider == null || playerCollider == null) return;
        if (collisionIgnored == ignore) return;

        Physics.IgnoreCollision(enemyCollider, playerCollider, ignore);
        collisionIgnored = ignore;
    }

    private bool IsBlocked()
    {
        float checkDistance = 1.2f;
        float radius = 0.4f;
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        RaycastHit hit;
        if (Physics.SphereCast(origin, radius, transform.forward, out hit, checkDistance))
        {
            if (!hit.collider.CompareTag("Player") && !hit.collider.CompareTag("Enemy"))
            {
                Debug.Log($"[冲锋] 检测到障碍物: {hit.collider.name}");
                return true;
            }
        }
        return false;
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

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, windUpDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, chargeMaxDistance);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chargeMinDistance);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.5f,
            $"蓄力: {windUpDistance:F1}m\n冲锋: {chargeMinDistance:F1} ~ {chargeMaxDistance:F1}m\n击退: {pushDistance:F1}m\n延迟: {chargeStartDelay:F1}s");
#endif
    }
}