using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BumperEnemy : EnemyAI
{
    private enum ChargeState { Chasing, WindUp, Charging, Recovery, Cooldown }
    private ChargeState chargeState = ChargeState.Chasing;
    private float stateTimer = 0f;
    private Vector3 chargeTarget;
    private Vector3 chargeStartPos;

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
                    // ✅ 进入蓄力时不锁定目标，只记录起始位置（后续会更新）
                    chargeStartPos = transform.position;  // 保留，但冲锋开始时会被覆盖
                    if (animator != null)
                        animator.SetTrigger("ChargeWindUp");
                }
                break;

            case ChargeState.WindUp:
                // ✅ 蓄力过程中：持续面向玩家
                StopAgent();
                Vector3 dirToTarget = (player.transform.position - transform.position).normalized;
                dirToTarget.y = 0;
                if (dirToTarget != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dirToTarget), 5f * Time.deltaTime);

                stateTimer += Time.deltaTime;
                if (stateTimer >= windUpDuration)
                {
                    chargeState = ChargeState.Charging;
                    stateTimer = 0f;
                    if (isAgentValid) agent.isStopped = true;
                    if (animator != null)
                        animator.SetTrigger("ChargeStart");

                    // ✅ 蓄力结束时，记录冲锋起点和终点（基于当前玩家位置，也就是当前面朝方向）
                    chargeStartPos = transform.position;
                    chargeTarget = player.transform.position;   // 固定目标点，冲锋时不更新

                    IgnorePlayerCollision(true);
                }
                break;

            case ChargeState.Charging:
                // ✅ 冲锋：使用蓄力结束时锁定的方向，直线冲锋，不跟踪玩家
                Vector3 dir = (chargeTarget - chargeStartPos).normalized;
                dir.y = 0;
                float distanceTraveled = Vector3.Distance(transform.position, chargeStartPos);

                if (distanceTraveled >= chargeMinDistance)
                {
                    if (distanceTraveled >= chargeMaxDistance || IsBlocked())
                    {
                        Debug.Log($"[冲锋] 冲锋结束！已冲距离: {distanceTraveled:F1}m");

                        chargeState = ChargeState.Recovery;
                        stateTimer = 0f;
                        if (isAgentValid) agent.isStopped = false;
                        if (animator != null)
                            animator.SetTrigger("ChargeEnd");

                        IgnorePlayerCollision(false);
                        break;
                    }
                }

                transform.position += dir * chargeSpeed * Time.deltaTime;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(dir);

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
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out hit, 1.5f))
        {
            if (!hit.collider.CompareTag("Player") && !hit.collider.CompareTag("Enemy"))
            {
                Debug.Log($"[冲锋] 撞到障碍物: {hit.collider.name}");
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
            $"蓄力: {windUpDistance:F1}m\n冲锋: {chargeMinDistance:F1} ~ {chargeMaxDistance:F1}m\n击退: {pushDistance:F1}m");
#endif
    }
}