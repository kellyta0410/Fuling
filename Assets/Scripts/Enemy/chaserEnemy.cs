using UnityEngine;
using UnityEngine.AI;

public class ChaserEnemy : EnemyAI
{
    protected override void HandleMovement()
    {
        // 不在追击状态 → 待机
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            return;
        }

        // 距离玩家
        float distance = Vector3.Distance(transform.position, player.transform.position);

        // 如果在攻击范围内，停止移动，转向玩家，尝试攻击
        if (distance <= enemyData.attackRange)
        {
            StopAgent();
            RotateTowardPlayer();
            if (IsFacingPlayer() && canAttack && !isAttacking)
            {
                PerformAttack();
            }
            return;
        }

        // 否则持续追
        if (isAgentValid)
        {
            agent.isStopped = false;
            agent.SetDestination(player.transform.position);
        }
    }

    private void RotateTowardPlayer()
    {
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }
}