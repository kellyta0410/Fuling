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

    // 连续命中/未命中计数：达到 hysteresisFrames 帧才切换，避免墙角等临界几何疯狂闪烁
    private Dictionary<Transform, int> hitStreak = new Dictionary<Transform, int>();
    private Dictionary<Transform, int> missStreak = new Dictionary<Transform, int>();

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

        // 用 RaycastAll：射线可穿过相机自己所在的墙，命中后面所有墙。
        // 先汇总本帧"应淡入"的墙（达到 minHitsToFade 的射线命中）。
        HashSet<Transform> fadeSet = new HashSet<Transform>();
        int totalRayHits = 0;
        int wallHits = 0;
        foreach (Vector3 offset in playerOffsets)
        {
            Vector3 targetPoint = player.position + player.TransformDirection(offset);
            Vector3 dir = targetPoint - camPos;
            float dist = dir.magnitude;
            if (dist < 0.0001f) continue;

            RaycastHit[] hits = Physics.RaycastAll(camPos, dir / dist, dist, wallLayer);
            totalRayHits += hits.Length;
            Dictionary<Transform, int> counts = new Dictionary<Transform, int>();
            foreach (RaycastHit hit in hits)
            {
                if (!hit.collider.CompareTag("Wall")) continue;
                if (hit.collider.GetComponentInParent<EnemyAI>() != null) continue;
                wallHits++;

                // 排除相机自身所在的墙（相机在该墙碰撞体内）。
                // 相机在墙内时会一直命中它（射线从内部穿出表面），导致其永不恢复，
                // 且相机在墙内时透明化该墙本身也没有意义。
                if (IsPointInsideCollider(hit.collider, camPos)) continue;

                Transform wall = hit.transform;
                if (!counts.ContainsKey(wall))
                    counts[wall] = 0;
                counts[wall]++;
            }
            foreach (var kvp in counts)
                if (kvp.Value >= minHitsToFade)
                    fadeSet.Add(kvp.Key);
        }

        // 调试：每 60 帧输出一次射线命中统计
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[XRay] frame={Time.frameCount} totalRayHits={totalRayHits} wallTaggedHits={wallHits} fadeSetCount={fadeSet.Count} activeWalls={activeWalls.Count} player={player.name} cam={Camera.main != null}");

        // 更新连续命中/未命中计数（迟滞防抖）
        foreach (var w in fadeSet)
        {
            missStreak[w] = 0;
            int h = hitStreak.ContainsKey(w) ? hitStreak[w] : 0;
            hitStreak[w] = h + 1;
        }
        foreach (var kvp in activeWalls)
        {
            if (!fadeSet.Contains(kvp.Key))
            {
                hitStreak[kvp.Key] = 0;
                int m = missStreak.ContainsKey(kvp.Key) ? missStreak[kvp.Key] : 0;
                missStreak[kvp.Key] = m + 1;
            }
        }

        // 达到迟滞帧数才进入/保持半透明
        foreach (var w in fadeSet)
        {
            if (hitStreak[w] < hysteresisFrames) continue; // 还没稳定命中，先不切换（防抖）
            if (!activeWalls.TryGetValue(w, out WallData data))
            {
                data = TryInitWall(w);
                if (data == null) continue;
                activeWalls[w] = data;
            }
            data.currentAlpha = Mathf.Lerp(
                data.currentAlpha,
                Mathf.Max(targetAlpha, 0.01f),
                Time.deltaTime * fadeSpeed
            );
            ApplyAlpha(data);
        }

        // 达到迟滞帧数未命中才平滑恢复（透明度回 1 后切回原 shader 并移除）
        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in activeWalls)
        {
            if (fadeSet.Contains(kvp.Key)) continue;       // 仍在命中（可能未达迟滞）→ 保持当前，不恢复
            if (missStreak[kvp.Key] < hysteresisFrames) continue; // 未达迟滞 → 不恢复（防抖）
            RestoreWallSmooth(kvp.Value);
            if (kvp.Value.currentAlpha > 0.99f)
            {
                RestoreWallNow(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (Transform t in toRemove)
        {
            activeWalls.Remove(t);
            hitStreak.Remove(t);
            missStreak.Remove(t);
        }
    }

    /// <summary>点是否在碰撞体内部（含恰好贴表）。</summary>
    bool IsPointInsideCollider(Collider collider, Vector3 point)
    {
        Vector3 closest = collider.ClosestPoint(point);
        return (closest - point).sqrMagnitude < 0.0001f;
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