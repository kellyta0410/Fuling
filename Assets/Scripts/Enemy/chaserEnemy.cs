using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class ChaserEnemy : EnemyAI
{
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

    // 蛇形移动只在有蛇身分段动画时生效（避免影响同样挂 ChaserEnemy 的 Basic / Bumper 等普通敌人。
    // 蛇身动画挂在子物体上，用 GetComponentInChildren 查找）
    private bool IsSerpentine => serpentineMove && GetComponentInChildren<SnakeBodyAnimation>(true) != null;

    private SnakeBodyAnimation snakeBody;
    private GameObject headHitbox;
    private SnakeHeadHitbox headHitboxScript;
    private bool wasAttacking;   // 攻击上升沿检测（启用判定球/重置命中标志）
    private bool attackHitDealt = false;   // 本轮攻击是否已造成伤害（触发球+距离兜底共用）
    private float attackStartTime = -1f;   // 本轮攻击开始时刻（用于延误到突刺时判定命中）

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

                // 蛇攻击节奏提速：缩短攻击冷却与击退硬直，让咬击更快更紧凑
                attackCooldownOverride = Mathf.Min(attackCooldownOverride > 0f ? attackCooldownOverride : 99f, 0.9f);
                staggerDuration = 0.2f;
            }
            else
                // Basic（直接攻击）：停到 1.5m 再出拳，更贴近玩家
                agent.stoppingDistance = 1.5f;
        }
        CreateHeadHitbox();
    }

    protected override void Update()
    {
        base.Update();
        UpdateHeadHitbox();
    }

    protected override void HandleMovement()
    {
        // 不在追击状态 → 待机
        if (!isChasing)
        {
            StopAgent();
            IdleRotation();
            return;
        }

        if (isAgentValid)
        {
            Vector3 target = player.transform.position;

            // 环形占位：有免费空位就过去绕玩家。
            // 蛇类：找不到空位也不排队堵到别的敌人身后（那样会停着不攻），直接压向玩家，
            //       靠蛇头长距离突刺命中；普通敌人保留"全满则排前面敌人身后"的兜底。
            if (enableFormation)
            {
                Vector3? slot = GetFormationTarget();
                if (slot.HasValue)
                {
                    target = slot.Value;
                }
                else if (!IsSerpentine)
                {
                    Vector3? queue = GetQueueTarget();
                    if (queue.HasValue) target = queue.Value;
                }
            }

            agent.isStopped = false;
            agent.SetDestination(target);

            // 蛇式移动：追击时蛇头始终面向玩家（头先转锁玩家，身体后段由 SnakeBodyAnimation 延迟跟随）。
            // 用"朝向玩家"而非"步进方向"，这样无论绕到哪一侧都保持对玩家的锁定姿态。
            if (IsSerpentine)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                RotateHeadTowards(toPlayer, Time.deltaTime);
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
            {
                headHitboxScript.BeginAttack();     // 攻击上升沿：重置本轮命中
                attackHitDealt = false;
                attackStartTime = Time.time;
            }
            headHitbox.SetActive(true);
            if (snakeBody != null)
                headHitbox.transform.position = snakeBody.HeadWorldPosition;

            // 命中兜底：等到突刺时刻按"蛇头够到范围"判定，不再依赖物理碰触。
            // 停在 stoppingDistance 攻击（蛇头未必真的碰到玩家）也能稳定造成伤害。
            if (player != null && !attackHitDealt && Time.time - attackStartTime >= attackDamageDelay)
                TryDealHeadDamage(player);
        }
        else
        {
            headHitbox.SetActive(false);
        }
        wasAttacking = attacking;
    }

    // 由 SnakeHeadHitbox 回调 / 攻击中距离兜底共用：蛇头够得到才造成伤害，每轮攻击最多一次
    public bool TryDealHeadDamage(PlayerController target)
    {
        if (isDead || target == null || target.IsDead() || attackHitDealt) return false;
        if (!CanHeadReach(target)) return false;

        float finalDamage = baseAttackDamage * currentDamageMultiplier;
        target.TakeDamage(Mathf.RoundToInt(finalDamage));
        attackHitDealt = true;
        return true;
    }

    // 蛇头够到判定（从蛇根为中心的水平距离）：
    // 蛇头前伸(约半身长, HeadForwardOffset) + 突刺前扑(身长×headLungeRatio) + 蛇头判定球半径
    // + 玩家半径 + 缓冲。即便蛇头物理上还没碰到玩家（停 stoppingDistance），几何上够得到也算命中。
    private bool CanHeadReach(PlayerController target)
    {
        if (snakeBody == null) return true;

        float bodyWorld = snakeBody.HeadForwardOffset * 2f;
        float playerR = 0.5f;
        Collider pc = cachedPlayerCollider;
        if (pc == null && player != null)
        {
            cachedPlayerCollider = player.GetComponentInChildren<Collider>();
            pc = cachedPlayerCollider;
        }
        if (pc != null && pc.bounds.extents.x > 0f) playerR = pc.bounds.extents.x;

        float reach = snakeBody.HeadForwardOffset
                    + bodyWorld * snakeBody.headLungeRatio
                    + headHitRadius + playerR + 0.15f;

        Vector3 a = transform.position; a.y = 0f;
        Vector3 b = target.transform.position; b.y = 0f;
        return Vector3.Distance(a, b) <= reach;
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