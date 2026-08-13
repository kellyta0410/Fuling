using UnityEngine;
using System.Collections.Generic;

public class CinemachineWallXRay : MonoBehaviour
{
    [Header("设置")]
    public Transform player;
    public float fadeSpeed = 8f;
    public float targetAlpha = 0.15f;
    public int hysteresisFrames = 6;

    public Vector3[] playerOffsets = new Vector3[]
    {
        Vector3.zero,
        new Vector3(0, 0.5f, 0),
        new Vector3(0, -0.5f, 0),
        new Vector3(0.3f, 0, 0),
        new Vector3(-0.3f, 0, 0)
    };
    public int minHitsToFade = 2;
    public LayerMask wallLayer = -1;

    // 当前所有已切换到 X-Ray 的墙（支持多面墙同时半透明，且都能恢复）
    private Dictionary<Transform, WallData> activeWalls = new Dictionary<Transform, WallData>();

    // 方案B注册表：被大 collider 盖住、但自身没有 collider 的纯网格墙，
    // 物理 Overlap 找不到它们，由 WallXRayTarget 主动注册供范围收集。
    private static readonly List<WallXRayTarget> staticWallTargets = new List<WallXRayTarget>();

    public static void RegisterWall(WallXRayTarget t)
    {
        if (t == null || staticWallTargets.Contains(t)) return;
        staticWallTargets.Add(t);
    }

    public static void UnregisterWall(WallXRayTarget t)
    {
        if (t != null) staticWallTargets.Remove(t);
    }

    private class WallData
    {
        public Renderer renderer;
        public Material material;
        public Shader originalShader;
        public Color originalColor;
        public float currentAlpha;
    }

    void LateUpdate()
    {
        if (player == null) return;

        Vector3 camPos = transform.position;
        Dictionary<Transform, int> hitCounts = new Dictionary<Transform, int>();

        // 用 RaycastAll：射线可穿过相机自己所在的墙，命中后面所有墙
        foreach (Vector3 offset in playerOffsets)
        {
            Vector3 targetPoint = player.position + player.TransformDirection(offset);
            Vector3 dir = targetPoint - camPos;
            float dist = dir.magnitude;
            if (dist < 0.0001f) continue;

            RaycastHit[] hits = Physics.RaycastAll(camPos, dir / dist, dist, wallLayer);
            foreach (RaycastHit hit in hits)
            {
                if (!hit.collider.CompareTag("Wall")) continue;
                if (hit.collider.GetComponentInParent<EnemyAI>() != null) continue;

                // 排除相机自身所在的墙（相机在该墙碰撞体内）。
                // 相机在墙内时会一直命中它（射线从内部穿出表面），导致其永不恢复，
                // 且相机在墙内时透明化该墙本身也没有意义。
                if (IsPointInsideCollider(hit.collider, camPos)) continue;

                Transform wall = hit.transform;
                if (!hitCounts.ContainsKey(wall))
                    hitCounts[wall] = 0;
                hitCounts[wall]++;

                // 方案B：这个 collider 可能是一个"大碰撞体"盖住了若干独立的小墙 mesh。
                // 把被它覆盖的其它 Wall 也一起计入命中（一起淡入/恢复）。
                CollectCoveredWalls(hit.collider, hitCounts);
            }
        }

        // 1) 不再被命中的墙：平滑恢复（透明度回 1），完成后切回原 shader 并移除
        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in activeWalls)
        {
            if (!hitCounts.ContainsKey(kvp.Key))
            {
                RestoreWallSmooth(kvp.Value);
                if (kvp.Value.currentAlpha > 0.99f)
                {
                    RestoreWallNow(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
        }
        foreach (Transform t in toRemove)
            activeWalls.Remove(t);

        // 2) 被命中的墙：不存在则初始化，已存在则保持/淡入到 targetAlpha
        foreach (var kvp in hitCounts)
        {
            if (kvp.Value < minHitsToFade) continue;

            if (!activeWalls.TryGetValue(kvp.Key, out WallData data))
            {
                data = TryInitWall(kvp.Key);
                if (data == null) continue;
                activeWalls[kvp.Key] = data;
            }

            data.currentAlpha = Mathf.Lerp(
                data.currentAlpha,
                Mathf.Max(targetAlpha, 0.01f),
                Time.deltaTime * fadeSpeed
            );
            ApplyAlpha(data);
        }
    }

    /// <summary>点是否在碰撞体内部（含恰好贴表）。</summary>
    bool IsPointInsideCollider(Collider collider, Vector3 point)
    {
        Vector3 closest = collider.ClosestPoint(point);
        return (closest - point).sqrMagnitude < 0.0001f;
    }

    // 方案B：收集被"大碰撞体"覆盖的所有墙体。两个渠道：
    //  1) 物理 OverlapSphere：能扫到带 collider 的墙（即便不是子物体）。
    //  2) 注册表（WallXRayTarget）：纯网格、无 collider 的墙，物理找不到，靠注册表按包围盒收集。
    void CollectCoveredWalls(Collider container, Dictionary<Transform, int> hitCounts)
    {
        if (container == null || hitCounts == null) return;

        // 渠道1：物理范围查询。以容器包围盒中心/半尺寸做球扫，收集 Wall tag 的物体。
        Bounds b = container.bounds;
        float radius = Mathf.Max(b.extents.magnitude, 0.01f);
        Collider[] overlaps = Physics.OverlapSphere(b.center, radius, wallLayer, QueryTriggerInteraction.Collide);
        foreach (Collider c in overlaps)
        {
            if (c == container) continue;
            if (ReferenceEquals(c.transform, container.transform)) continue;
            if (!c.CompareTag("Wall")) continue;
            if (c.GetComponentInParent<EnemyAI>() != null) continue;
            if (IsPointInsideCollider(container, c.transform.position)) continue; // 相机的墙不算
            Transform t = c.transform;
            if (!hitCounts.ContainsKey(t))
                hitCounts[t] = 0;
            hitCounts[t]++;
        }

        // 渠道2：注册表（无 collider 的纯网格墙），按包围盒相交收集。
        for (int i = staticWallTargets.Count - 1; i >= 0; i--)
        {
            WallXRayTarget target = staticWallTargets[i];
            if (target == null) { staticWallTargets.RemoveAt(i); continue; }
            Transform t = target.transform;
            if (ReferenceEquals(t, container.transform)) continue;
            if (!t.gameObject.CompareTag("Wall")) continue;
            Renderer r = target.Renderer;
            if (r == null) continue;
            if (IsPointInsideCollider(container, t.position)) continue;
            if (!b.Intersects(r.bounds)) continue;
            if (!hitCounts.ContainsKey(t))
                hitCounts[t] = 0;
            hitCounts[t]++;
        }
    }

    WallData TryInitWall(Transform wall)
    {
        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer == null) return null;

        Shader xrayShader = Shader.Find("Custom/XRayWall");
        if (xrayShader == null)
        {
            Debug.LogWarning("[CinemachineWallXRay] 找不到 Shader: Custom/XRayWall，跳过");
            return null;
        }

        Material mat = renderer.material;
        WallData data = new WallData
        {
            renderer = renderer,
            material = mat,
            originalShader = mat.shader,
            originalColor = mat.color,
            currentAlpha = 1f
        };
        mat.shader = xrayShader;
        mat.SetFloat("_Transparency", 1f);
        return data;
    }

    void ApplyAlpha(WallData data)
    {
        if (data == null || data.material == null) return;
        data.material.SetFloat("_Transparency", data.currentAlpha);
    }

    void RestoreWallSmooth(WallData data)
    {
        if (data == null || data.material == null) return;
        data.currentAlpha = Mathf.Lerp(data.currentAlpha, 1f, Time.deltaTime * fadeSpeed);
        ApplyAlpha(data);
    }

    void RestoreWallNow(WallData data)
    {
        if (data == null || data.material == null) return;

        if (data.originalShader != null)
        {
            data.material.shader = data.originalShader;
            data.material.color = data.originalColor;
        }
    }

    void OnDestroy()
    {
        foreach (var kvp in activeWalls)
            RestoreWallNow(kvp.Value);
        activeWalls.Clear();
    }
}