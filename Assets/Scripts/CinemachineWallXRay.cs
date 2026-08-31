using UnityEngine;
using System.Collections.Generic;

public class CinemachineWallXRay : MonoBehaviour
{
    [Header("设置")]
    public Transform player;
    public float fadeSpeed = 8f;
    public float targetAlpha = 0.15f;
    public int hysteresisFrames = 6;

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
        Vector3 playerPos = player.position;

        // 射线检测：从相机到玩家，检查是否被 Wall 标签的物体挡住
        HashSet<Transform> fadeSet = new HashSet<Transform>();
        CheckWallsByRaycast(camPos, playerPos, fadeSet);

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
            if (hitStreak[w] < hysteresisFrames) continue;
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

        // 达到迟滞帧数未命中才平滑恢复
        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in activeWalls)
        {
            if (fadeSet.Contains(kvp.Key)) continue;
            if (missStreak[kvp.Key] < hysteresisFrames) continue;
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

    /// <summary>
    /// 射线检测：从相机高度(y=2)到玩家高度(y=2)发射射线，
    /// 水平方向检测墙是否挡在相机和玩家之间。
    /// 用 y=2 是因为墙壁碰撞体仅 wallHeight≈4 高，从相机实际位置(y≈12)发射射线会越过墙顶。
    /// </summary>
    void CheckWallsByRaycast(Vector3 camPos, Vector3 playerPos, HashSet<Transform> fadeSet)
    {
        Vector3 rayStart = new Vector3(camPos.x, 2f, camPos.z);
        Vector3 rayEnd = new Vector3(playerPos.x, 2f, playerPos.z);
        Vector3 dir = rayEnd - rayStart;
        float dist = dir.magnitude;
        if (dist < 0.001f) return;

        RaycastHit[] hits = Physics.RaycastAll(rayStart, dir.normalized, dist);
        foreach (var hit in hits)
        {
            if (hit.collider != null && hit.collider.CompareTag("Wall"))
            {
                fadeSet.Add(hit.collider.transform);
            }
        }
    }

    WallData TryInitWall(Transform wall)
    {
        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = wall.GetComponentInParent<Renderer>();
            if (renderer == null) return null;
        }

        Shader xrayShader = Shader.Find("Custom/XRayWall");
        if (xrayShader == null) return null;

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
