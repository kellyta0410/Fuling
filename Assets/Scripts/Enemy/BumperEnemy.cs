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
    private CharacterController playerController;  // ⭐ 玩家 CharacterController
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

        // 获取碰撞体
        enemyCollider = GetComponent<Collider>();
        if (player != null)
        {
            playerCollider = player.GetComponent<Collider>();
            playerController = player.GetComponent<CharacterController>();  // ⭐ 获取玩家 CharacterController
        }
    }

    // ⭐ 重写 PerformAttack：不播放攻击动画，不设置 isAttacking
    protected override void PerformAttack()
    {
        if (!canAttack || isDead) return;

        canAttack = false;
        attackCooldownTimer = 0f;

        float finalDamage = baseAttackDamage * currentDamageMultiplier;
        if (player != null && !player.IsDead())
        {
            player.TakeDamage(Mathf.RoundToInt(finalDamage));

            // ⭐ 平滑击退玩家（使用 CharacterController）
            StartCoroutine(SmoothPushPlayer(pushDuration, pushDistance));

        }

        StartCoroutine(ResetAttackCooldown());
    }

    /// <summary>
    /// ⭐ 使用 CharacterController 平滑击退玩家
    /// </summary>
    private IEnumerator SmoothPushPlayer(float duration, float distance)
    {
        if (player == null || playerController == null)
        {
            yield break;
        }

        // 计算后退方向（从敌人指向玩家）
        Vector3 pushDirection = (player.transform.position - transform.position).normalized;
        pushDirection.y = 0;  // 保持水平

        // 如果方向为零向量，不执行
        if (pushDirection == Vector3.zero) yield break;

        // 起点和终点
        Vector3 startPos = player.transform.position;
        Vector3 endPos = startPos + pushDirection * distance;

        // ⭐ 确保终点不会掉到地下或穿墙（可选）
        if (Physics.Raycast(endPos + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 5f))
        {
            endPos.y = groundHit.point.y;  // 让玩家贴地
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // 使用 SmoothStep 让移动更自然（先快后慢）
            float smoothT = t * t * (3f - 2f * t);

            Vector3 targetPos = Vector3.Lerp(startPos, endPos, smoothT);
            Vector3 moveDelta = targetPos - player.transform.position;

            // ⭐ 使用 CharacterController.Move() 移动（自动处理碰撞）
            playerController.Move(moveDelta);

            yield return null;
        }

        // 确保最终位置精确（使用 Move 处理剩余距离）
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

        // 冲锋过程中不受攻击锁影响（保持惯性）
        if (chargeState != ChargeState.Charging && isAttacking)
        {
            StopAgent();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);

        switch (chargeState)
        {
            case ChargeState.Chasing:
                // 正常追击
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

                // 当距离 <= windUpDistance 时进入蓄力
                if (distance <= windUpDistance && chargeState == ChargeState.Chasing)
                {
                    chargeState = ChargeState.WindUp;
                    stateTimer = 0f;
                    StopAgent();
                    chargeTarget = player.transform.position;
                    chargeStartPos = transform.position;
                    if (animator != null)
                        animator.SetTrigger("ChargeWindUp");
                }
                break;

            case ChargeState.WindUp:
                // 蓄力中：面向目标，停止移动
                StopAgent();
                Vector3 dirToTarget = (chargeTarget - transform.position).normalized;
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

                    // 开始冲锋：忽略与玩家的物理碰撞
                    IgnorePlayerCollision(true);
                }
                break;

            case ChargeState.Charging:
                // ===== 冲锋核心逻辑：直线前进，穿过玩家 =====
                Vector3 dir = (chargeTarget - chargeStartPos).normalized;
                dir.y = 0;
                float distanceTraveled = Vector3.Distance(transform.position, chargeStartPos);

                // ⭐ 只有冲够了最小距离，才检查是否结束
                if (distanceTraveled >= chargeMinDistance)
                {
                    // 结束条件：超出最大距离 或 撞墙
                    if (distanceTraveled >= chargeMaxDistance || IsBlocked())
                    {
                        Debug.Log($"[冲锋] 冲锋结束！已冲距离: {distanceTraveled:F1}m");

                        chargeState = ChargeState.Recovery;
                        stateTimer = 0f;
                        if (isAgentValid) agent.isStopped = false;
                        if (animator != null)
                            animator.SetTrigger("ChargeEnd");

                        // 冲锋结束：恢复碰撞
                        IgnorePlayerCollision(false);
                        break;
                    }
                }

                // 移动（每帧执行）
                transform.position += dir * chargeSpeed * Time.deltaTime;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(dir);

                // ⭐ 撞击检测：调用重写后的 PerformAttack（不播放动画）
                float currentDistance = Vector3.Distance(transform.position, player.transform.position);
                if (currentDistance <= enemyData.attackRange && canAttack)
                {
                    PerformAttack();
                    Debug.Log($"[冲锋] 撞到玩家！当前距离: {currentDistance:F1}m");
                }
                break;

            case ChargeState.Recovery:
                // 硬直
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
                // 冷却期间：可以追击，但不能蓄力
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

    // ⭐ 控制是否忽略与玩家的物理碰撞
    private void IgnorePlayerCollision(bool ignore)
    {
        if (enemyCollider == null || playerCollider == null)
        {
            return;
        }

        // 避免重复设置
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

        // 蓄力触发距离（橙色）
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, windUpDistance);

        // 冲锋最远距离（青色）
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, chargeMaxDistance);

        // 冲锋最小距离（黄色虚线）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chargeMinDistance);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.5f,
            $"蓄力: {windUpDistance:F1}m\n冲锋: {chargeMinDistance:F1} ~ {chargeMaxDistance:F1}m\n击退: {pushDistance:F1}m");
#endif
    }
}