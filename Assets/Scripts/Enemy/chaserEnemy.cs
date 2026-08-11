using System.Collections;
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
    [Tooltip("关闭 NavMeshAgent 的自动旋转（否则整个身体瞬转抽搐），由蛇自己平滑转动头部引导方向。仅挂有 SnakeBodyAnimation 的蛇类生效")]
    public bool serpentineMove = true;
    [Tooltip("追击时头部最大转向速度（度/秒）。越小形态越呆、拖尾越明显")]
    public float headTurnSpeed = 160f;

    [Header("蛇头攻击判定（以蛇头为攻击点）")]
    [Tooltip("蛇头攻击判定球半径（世界单位）。攻击动画期间跟随蛇头变形位置，只有蛇头碰到玩家才造成伤害")]
    public float headHitRadius = 0.5f;
    [Tooltip("蛇头判定球只在攻击动画期间启用（true），结束后关闭避免身体误伤")]
    public bool enableHeadHitbox = true;

    private bool telegraphing = false;
    private float fillTimer = 0f;

    // 蛇形移动只在有蛇身分段动画时生效（避免影响同样挂 ChaserEnemy 的 Basic / Bumper 等普通敌人。
    // 蛇身动画挂在子物体上，用 GetComponentInChildren 查找）
    private bool IsSerpentine => serpentineMove && GetComponentInChildren<SnakeBodyAnimation>(true) != null;

    private SnakeBodyAnimation snakeBody;
    private GameObject headHitbox;
    private SnakeHeadHitbox headHitboxScript;
    private bool wasAttacking;   // 攻击上升沿检测（启用判定球/重置命中标志）

    protected override void OnStart()
    {
        base.OnStart();
        if (isAgentValid && agent != null)
        {
            if (IsSerpentine)
            {
                // 蛇（眼镜蛇式）：身体停远些，攻击靠蛇头突刺命中。
                // 蛇头在根前方约半个身长，突刺+判定球还能再够 ~1.9m，
                // 根停约 0.9×攻击范围时蛇头(前伸+突刺)恰好够到玩家。
                agent.stoppingDistance = (enemyData != null ? enemyData.attackRange * 0.9f : 3.6f);
                // 旋转交给蛇自己平滑控制（头先转），否则 agent 会把整个身体瞬转抽搐
                agent.updateRotation = false;
            }
            else if (enableTelegraph)
                // Snake（蓄力命中）：贴近到约 1.5m 内再蓄力
                agent.stoppingDistance = Mathf.Min(1.5f, (enemyData != null ? enemyData.attackRange * 0.7f : 1.5f));
            else
                // Basic（直接攻击）：停到 1.5m 再出拳，更贴近玩家
                agent.stoppingDistance = 1.5f;
        }
        CreateHeadHitbox();
    }

    // 被击退后立即打断蓄力（红条归零、重新索敌），避免击退结束时仍处于蓄力态而卡住
    protected override void OnKnockback()
    {
        base.OnKnockback();
        telegraphing = false;
        fillTimer = 0f;
    }

    protected override void Update()
    {
        base.Update();
        UpdateHeadHitbox();
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

            if (progress >= 1f)
            {
                telegraphing = false;
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

            // 蛇式移动：头先平滑转向移动方向，身体后段由 SnakeBodyAnimation 延迟跟随。
            // 用"步进方向"而非玩家方位，这样转弯时是头先拐、身体被拖着走。
            // 非蛇类敌人不做蛇形转向，交给 agent 按原逻辑（updateRotation=true）自行转向
            if (IsSerpentine)
            {
                Vector3 moveDir = agent.desiredVelocity;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude > 0.0001f)
                    RotateHeadTowards(moveDir.normalized, Time.deltaTime);
                else
                    RotateHeadTowards(target - transform.position, Time.deltaTime);
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

    // 蓄力瞄准：与追击一致地限速平滑转向玩家（头先转，身体后段由 SnakeBodyAnimation 拖尾），
    // 避免蓄力瞬间整个蛇身 "咔" 一下转向玩家
    private void RotateTowardPlayer()
    {
        if (player == null) return;
        Vector3 dir = player.transform.position - transform.position;
        dir.y = 0f;
        RotateHeadTowards(dir, Time.deltaTime);
    }

    // 生成蛇头攻击判定球（只对蛇生效）。攻击动画期间启用并跟随蛇头世界位置。
    private void CreateHeadHitbox()
    {
        if (!IsSerpentine || !enableHeadHitbox) return;

        snakeBody = GetComponentInChildren<SnakeBodyAnimation>(true);
        headHitbox = new GameObject("SnakeHeadHitbox");
        headHitbox.transform.SetParent(transform, false);

        SphereCollider sc = headHitbox.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = Mathf.Max(0.1f, headHitRadius);

        // 触发球需要刚体才能与玩家触发；kinematic 避免被物理推动
        Rigidbody rb = headHitbox.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        headHitboxScript = headHitbox.AddComponent<SnakeHeadHitbox>();
        headHitboxScript.SetOwner(this);
        headHitbox.SetActive(false);
    }

    // 攻击动画期间：把判定球贴到蛇头变形后的世界位置并启用；攻击结束关闭（身体/尾巴无判定）
    private void UpdateHeadHitbox()
    {
        if (headHitbox == null) return;

        bool attacking = isAttacking && !isDead;
        if (attacking)
        {
            if (!wasAttacking && headHitboxScript != null)
                headHitboxScript.BeginAttack();     // 攻击上升沿：重置本轮命中
            headHitbox.SetActive(true);
            if (snakeBody != null)
                headHitbox.transform.position = snakeBody.HeadWorldPosition;
        }
        else
        {
            headHitbox.SetActive(false);
        }
        wasAttacking = attacking;
    }

    // 由 SnakeHeadHitbox 回调：蛇头碰到玩家时才造成伤害；返回是否已造成伤害
    public bool TryDealHeadDamage(PlayerController target)
    {
        if (isDead || target == null || target.IsDead()) return false;
        float finalDamage = baseAttackDamage * currentDamageMultiplier;
        target.TakeDamage(Mathf.RoundToInt(finalDamage));
        return true;
    }

    // 蛇的伤害判定交给蛇头判定球，不再使用“身体中心到玩家距离”的通用判定，避免身体误伤
    protected override IEnumerator DelayedDamage()
    {
        if (IsSerpentine) yield break;
        yield return base.DelayedDamage();
    }

    // 攻击触发距离以蛇头为基准：蛇头在身体前端（约身长一半的前方）。
    // 基类用根(agent)到玩家的距离判断，对长身体的蛇来说根停 4m 时蛇头已在 2m 外、够不着；
    // 改成把触发距离减去蛇头前伸量，让根停得足够近、蛇头(前伸+突刺)恰好够到玩家，
    // 同时不低于 stoppingDistance，避免永远停不到攻击位置。
    protected override float GetAttackActivationRange()
    {
        if (!IsSerpentine || snakeBody == null) return base.GetAttackActivationRange();

        float baseRange = base.GetAttackActivationRange();
        float headOffset = snakeBody.HeadForwardOffset;     // 蛇头在根前方多远（世界单位）
        // 蛇头已经把"根到玩家"的有效距离前伸了半个身长，触发距离相应收紧半个身长，
        // 但至少不小于 agent 停距（否则蛇永远进不了攻击态）。
        return Mathf.Max(agent != null ? agent.stoppingDistance : baseRange,
                         baseRange - headOffset);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (headHitbox != null) Destroy(headHitbox);
    }
}