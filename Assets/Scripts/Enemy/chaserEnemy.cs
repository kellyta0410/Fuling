using UnityEngine;
using UnityEngine.AI;

public class ChaserEnemy : EnemyAI
{
    [Header("蓄力提示条")]
    [Tooltip("是否启用\"红条蓄力填充后才命中\"（仅 Snake 开启，Basic 保持直接攻击）")]
    public bool enableTelegraph = false;
    [Tooltip("蓄力填充时长（秒），填满才命中")]
    public float telegraphDuration = 2.5f;

    [Header("蛇形移动（头先转，身体后段跟随）")]
    [Tooltip("关闭 NavMeshAgent 的自动旋转（否则整个身体瞬转抽搐），由蛇自己平滑转动头部引导方向")]
    public bool serpentineMove = true;
    [Tooltip("追击时头部最大转向速度（度/秒）。越小形态越呆、拖尾越明显")]
    public float headTurnSpeed = 160f;

    private GameObject telegraph;
    private bool telegraphing = false;
    private float fillTimer = 0f;

    protected override void OnStart()
    {
        base.OnStart();
        if (isAgentValid && agent != null)
        {
            if (enableTelegraph)
                // Snake（蓄力命中）：贴近到 2m 内再蓄力
                agent.stoppingDistance = Mathf.Min(1.8f, (enemyData != null ? enemyData.attackRange * 0.8f : 1.8f));
            else
                // Basic（直接攻击）：停在 2.5m（略多于攻击范围，不贴脸）
                agent.stoppingDistance = 2.5f;

            // 蛇式移动：旋转交给蛇自己平滑控制（头先转），否则 agent 会把整个身体瞬转抽搐
            if (serpentineMove)
                agent.updateRotation = false;
        }
        CreateTelegraph();
    }

    protected override void Update()
    {
        base.Update();
        if (!telegraphing) HideTelegraph();
    }

    // 进入攻击范围时由 EnemyAI.Update 调用（每帧）。接管蛇的蓄力-命中逻辑。
    protected override bool TryPerformInRangeAttack()
    {
        // 未启用蓄力红条（Basic 等情况）→ 走原本的直接攻击
        if (!enableTelegraph)
            return base.TryPerformInRangeAttack();

        RotateTowardPlayer();
        StopAgent();

        // 正在蓄力：红条从蛇向玩家逐渐填满，方向实时跟随玩家
        if (telegraphing)
        {
            fillTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(fillTimer / telegraphDuration);
            ShowTelegraph(transform.position, player.transform.position, progress);

            if (progress >= 1f)
            {
                telegraphing = false;
                HideTelegraph();
                if (canAttack && !isAttacking)
                    PerformAttack();
            }
            return true;
        }

        // 满足攻击条件 → 开始蓄力停顿（不立刻咬/撞）
        if (IsFacingPlayer() && canAttack && !isAttacking)
        {
            telegraphing = true;
            fillTimer = 0f;
        }
        return true;
    }

    protected override void HandleMovement()
    {
        // 不在追击状态 → 待机
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            telegraphing = false;
            return;
        }

        // 持续追击：玩家在攻击范围外时，蓄力中断，红条隐藏
        telegraphing = false;
        if (isAgentValid)
        {
            Vector3 target = player.transform.position;

            // 环形占位：有免费空位就过去绕玩家，全满则排到前面敌人身后
            if (enableFormation)
            {
                Vector3? slot = GetFormationTarget();
                if (slot.HasValue)
                {
                    target = slot.Value;
                }
                else
                {
                    Vector3? queue = GetQueueTarget();
                    if (queue.HasValue) target = queue.Value;
                }
            }

            bool wasStopped = agent.isStopped;
            agent.isStopped = false;
            agent.SetDestination(target);

            // 蛇式移动：头先平滑转向移动方向，身体后段由 SnakeBodyAnimation 延迟跟随。
            // 用“步进方向”而非玩家方位，这样转弯时是头先拐、身体被拖着走。
            if (serpentineMove)
            {
                Vector3 moveDir = agent.desiredVelocity;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude > 0.0001f)
                    RotateHeadTowards(moveDir.normalized, Time.deltaTime);
                else
                    RotateHeadTowards(target - transform.position, Time.deltaTime);
            }
            else if (wasStopped)
            {
                agent.isStopped = true;
            }
        }
    }

    // 限速转动头部（蛇身体不整体瞬转；超过最大转向速度时按速度截断）
    private void RotateHeadTowards(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir.normalized);
        float step = headTurnSpeed * Mathf.Max(dt, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, step);
    }

    private void RotateTowardPlayer()
    {
        if (player == null) return;
        Vector3 dir = (player.transform.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    private void CreateTelegraph()
    {
        telegraph = GameObject.CreatePrimitive(PrimitiveType.Cube);
        telegraph.name = "SnakeAttackTelegraph";
        Material mat = new Material(Shader.Find("Custom/XRayWall"));
        if (mat != null)
        {
            mat.SetColor("_Color", new Color(1f, 0.15f, 0.1f, 1f));
            mat.SetFloat("_Transparency", 0.5f);
            telegraph.GetComponent<Renderer>().sharedMaterial = mat;
        }
        telegraph.SetActive(false);
    }

    // progress: 0→1, 红条长度 = 蛇到玩家距离 × progress（从蛇往玩家填满）
    private void ShowTelegraph(Vector3 from, Vector3 to, float progress)
    {
        if (telegraph == null) return;

        Vector3 a = from; a.y = 0f;
        Vector3 b = to; b.y = 0f;
        Vector3 delta = b - a;
        float fullLength = delta.magnitude;
        if (fullLength < 0.1f) { HideTelegraph(); return; }

        Vector3 dir = delta / fullLength;
        float length = Mathf.Max(0.1f, fullLength * Mathf.Clamp01(progress));
        telegraph.SetActive(true);
        telegraph.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        telegraph.transform.localScale = new Vector3(0.8f, 0.02f, length);
        telegraph.transform.position = a + dir * (length * 0.5f);
        Vector3 p = telegraph.transform.position;
        p.y = 0.02f;
        telegraph.transform.position = p;
    }

    private void HideTelegraph()
    {
        if (telegraph != null) telegraph.SetActive(false);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (telegraph != null) Destroy(telegraph);
    }
}