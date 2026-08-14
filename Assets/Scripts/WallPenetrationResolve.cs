using UnityEngine;

// 通用"不穿墙"兜底：无论实墙，还是被相机 X-Ray 半透明化的墙（碰撞体始终存在），
// 只要移动体(Collider)与任一环境实体阻挡物重叠，就沿最小分离方向把它水平推出墙外。
//
// 为什么只根据"物理重叠 + 水平推出"而不挑 tag/layer：
//   迷宫/无限世界里的墙大多是预制体 BoxCollider（未打 Wall tag、默认 Default layer），
//   而地面、地面 tile 是水平大平面——用"推出方向是否基本水平"来区分，天然不碰地板。
//
// 玩家(CharacterController)与敌人共用：
//   玩家在自己 LateUpdate 调用；敌人由 EnemyCollisionBlocker.LateUpdate 调用。
//   这是最后一道物理防线：追击/击退/冲锋/瞬移/分离无论怎么位移，随时能穿墙的都会被推回来。
public static class WallPenetrationResolve
{
    // 静态复用缓冲，避免每帧为每个单位分配
    private static Collider[] buffer = new Collider[64];

    // 高出多少算"竖直推出"（地板/墙顶/天花板）：|dir.y| 超过它就不做水平推出
    private const float VerticalRatioLimit = 0.55f;
    // 推出后额外留的间隙，避免下一帧又因为贴合而误测重叠
    private const float Margin = 0.03f;

    /// <summary>
    /// 若 mover 与任一"竖直墙面"重叠，把 mover 沿水平方向推出墙外。
    /// 返回是否有位移（敌人调用方可用它决定是否让 NavMeshAgent.Warp 同步）。
    /// </summary>
    public static bool Resolve(Collider mover, Transform selfRoot)
    {
        if (mover == null) return false;

        Bounds b = mover.bounds;
        if (b.size.sqrMagnitude < 1e-6f) return false;

        int count = Physics.OverlapSphereNonAlloc(b.center, b.extents.magnitude + 0.6f, buffer);
        bool moved = false;
        CharacterController cc = mover as CharacterController;

        for (int i = 0; i < count; i++)
        {
            Collider wall = buffer[i];
            if (!IsWall(mover, wall, selfRoot)) continue;

            Vector3 dir;
            float dist;
            if (Physics.ComputePenetration(mover, mover.transform.position, mover.transform.rotation,
                    wall, wall.transform.position, wall.transform.rotation, out dir, out dist))
            {
                if (dist <= 0.001f) continue;

                // 只做"水平推出"：竖直分成大的(地板/墙顶)跳过，避免把角色反复往上/下顶
                if (Mathf.Abs(dir.y) > VerticalRatioLimit) continue;

                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                else dir.Normalize();

                Vector3 delta = dir * (dist + Margin);

                if (cc != null)
                {
                    // CharacterController 直接改 transform 前先禁用，否则被物理解算缓存回弹
                    bool wasEnabled = cc.enabled;
                    cc.enabled = false;
                    mover.transform.position += delta;
                    cc.enabled = wasEnabled;
                }
                else
                {
                    mover.transform.position += delta;
                }
                moved = true;
            }
        }
        return moved;
    }

    /// <summary>
    /// 视线检测：from 到 to 之间是否被竖直环境墙挡住（命中判定用，敌我通用）。
    /// 与 IsWall 同口径：排除触发器/玩家/敌人，只认 Box/Sphere/Capsule/凸网格。
    /// ⭐ 近身豁免：两点距离 ≤ CloseCombatDistance 时直接放行（不判隔墙）。
    /// 墙角/贴墙肉搏时双方中心连线会擦到墙角墙体，直线检测会误判"隔墙打不到"，
    /// 导致玩家挥空、敌人不攻击。近身距离内必然互可接触，跳过墙挡判定。
    /// </summary>
    private const float CloseCombatDistance = 1.2f;

    public static bool IsBlockedBetween(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 0.01f) return false;
        if (dist <= CloseCombatDistance) return false; // ⭐ 近身豁免：贴脸/墙角互搏必然命中
        dir /= dist;

        RaycastHit[] hits = Physics.RaycastAll(from, dir, dist);
        foreach (RaycastHit hit in hits)
        {
            Collider c = hit.collider;
            if (c == null || c.isTrigger) continue;
            if (c.GetComponentInParent<PlayerController>() != null) continue;
            if (c.GetComponentInParent<EnemyAI>() != null) continue;

            bool convexMesh = (c is MeshCollider) && (c as MeshCollider).convex;
            if (!(c is BoxCollider) && !(c is SphereCollider) && !(c is CapsuleCollider) && !convexMesh)
                continue;
            if (hit.distance >= dist - 0.05f) continue;
            return true;
        }
        return false;
    }

    // 判定是不是"环境阻挡墙"：触发体/自身子物体/玩家/敌人/不可解析的墙体都排除
    private static bool IsWall(Collider mover, Collider c, Transform selfRoot)
    {
        if (c == null) return false;
        if (c.isTrigger) return false;
        if (c == mover) return false;
        if (selfRoot != null && (c.transform == selfRoot || c.transform.IsChildOf(selfRoot))) return false;

        // 玩家、敌人自己不算墙（敌人↔敌人、↔玩家由 EnemyCollisionBlocker 单独处理）
        if (c.GetComponentInParent<PlayerController>() != null) return false;
        if (c.GetComponentInParent<EnemyAI>() != null) return false;

        // 只对能用 ComputePenetration 解析的碰撞体做：盒子/球/胶囊/凸网格
        bool convexMesh = (c is MeshCollider) && (c as MeshCollider).convex;
        if (!(c is BoxCollider) && !(c is SphereCollider) && !(c is CapsuleCollider) && !convexMesh)
            return false;

        return true;
    }
}