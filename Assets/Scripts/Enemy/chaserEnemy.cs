using UnityEngine;
using UnityEngine.AI;

public class ChaserEnemy : EnemyAI
{
    [Header("蓄力提示条")]
    [Tooltip("是否启用\"红条蓄力填充后才命中\"（仅 Snake 开启，Basic 保持直接攻击）")]
    public bool enableTelegraph = false;
    [Tooltip("蓄力填充时长（秒），填满才命中")]
    public float telegraphDuration = 2.5f;

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
                // Basic（直接攻击）：停在攻击范围边缘（attackRange），不贴脸
                agent.stoppingDistance = Mathf.Min(2.5f, (enemyData != null ? enemyData.attackRange : 2f));
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

            agent.isStopped = false;
            agent.SetDestination(target);
        }
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