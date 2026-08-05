using UnityEngine;
using System.Collections.Generic;

public class CinemachineWallXRay : MonoBehaviour
{
    [Header("设置")]
    public Transform player;
    public float fadeSpeed = 8f;
    [Tooltip("半透明目标透明度（越小越透明）")]
    public float targetAlpha = 0.15f;
    public int hysteresisFrames = 6;

    [Header("多射线检测设置")]
    [Tooltip("从摄像机向玩家身上的多个偏移点发射射线，用于判断是否真正遮挡")]
    public Vector3[] playerOffsets = new Vector3[]
    {
        Vector3.zero,              // 玩家中心
        new Vector3(0, 0.5f, 0),   // 胸部
        new Vector3(0, -0.5f, 0),  // 脚部
        new Vector3(0.3f, 0, 0),   // 右侧
        new Vector3(-0.3f, 0, 0)   // 左侧
    };
    [Tooltip("至少需要多少条射线命中同一面墙，才认为该墙遮挡玩家")]
    public int minHitsToFade = 2;
    [Tooltip("射线检测的层级（墙所在的层）")]
    public LayerMask wallLayer = -1;

    private Transform currentFadingWall = null;  // 当前正在半透明的墙（只处理一个）
    private WallData currentWallData = null;
    private int missingFrames = 0;               // 当前半透明墙连续未命中帧数（防抖动）

    private class WallData
    {
        public Renderer renderer;
        public Material material;
        public Color originalColor;
        public float currentAlpha;
    }

    void LateUpdate()
    {
        if (player == null) return;

        Vector3 camPos = transform.position;
        Dictionary<Transform, int> hitCounts = new Dictionary<Transform, int>();

        foreach (Vector3 offset in playerOffsets)
        {
            Vector3 targetPoint = player.position + player.TransformDirection(offset);
            Vector3 direction = (targetPoint - camPos).normalized;
            float distance = Vector3.Distance(camPos, targetPoint);

            RaycastHit hit;
            if (Physics.Raycast(camPos, direction, out hit, distance, wallLayer))
            {
                // 只处理标记为 Wall 的物体，避免把角色/敌人/Buff/金币等当墙淡化
                if (!hit.collider.CompareTag("Wall")) continue;
                // 跳过敌人（含 BlinkerEnemy/BumperEnemy）
                if (hit.collider.GetComponentInParent<EnemyAI>() != null) continue;

                Transform wall = hit.transform;
                if (!hitCounts.ContainsKey(wall))
                    hitCounts[wall] = 0;
                hitCounts[wall]++;
            }
        }

        // 优先保留当前半透明墙（若仍被命中），避免在候选墙之间频繁切换导致闪烁
        Transform targetWall = null;
        if (currentFadingWall != null &&
            hitCounts.TryGetValue(currentFadingWall, out int currentHits) &&
            currentHits >= minHitsToFade)
        {
            targetWall = currentFadingWall;
        }
        else
        {
            float minDistance = float.MaxValue;
            foreach (var kvp in hitCounts)
            {
                if (kvp.Value >= minHitsToFade)
                {
                    float dist = Vector3.Distance(transform.position, kvp.Key.position);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        targetWall = kvp.Key;
                    }
                }
            }
        }

        if (targetWall != null)
        {
            missingFrames = 0;

            // 换了墙：先平滑恢复旧墙，再初始化新墙
            if (currentFadingWall != targetWall)
            {
                RestoreWallSmooth();
                if (!TryInitWall(targetWall))
                    return;
            }

            // 淡化到目标透明度
            currentWallData.currentAlpha = Mathf.Lerp(currentWallData.currentAlpha, Mathf.Max(targetAlpha, 0.01f), Time.deltaTime * fadeSpeed);
            ApplyAlpha(currentWallData);
        }
        else if (currentFadingWall != null && currentWallData != null)
        {
            // 当前墙未命中：滞后若干帧后平滑恢复，避免阈值边缘抖动
            missingFrames++;
            if (missingFrames >= hysteresisFrames)
            {
                RestoreWallSmooth();
            }
            else
            {
                currentWallData.currentAlpha = Mathf.Lerp(currentWallData.currentAlpha, Mathf.Max(targetAlpha, 0.01f), Time.deltaTime * fadeSpeed);
                ApplyAlpha(currentWallData);
            }
        }
    }

    // 初始化墙：永久切换为 X-Ray Shader（渲染队列恒定，避免切换导致闪烁），
    // 之后只通过 _Transparency 控制透明度
    bool TryInitWall(Transform wall)
    {
        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer == null) return false;

        Shader xrayShader = Shader.Find("Custom/XRayWall");
        if (xrayShader == null)
        {
            Debug.LogWarning("[CinemachineWallXRay] 找不到 Shader: Custom/XRayWall，跳过");
            currentFadingWall = null;
            currentWallData = null;
            return false;
        }

        Material mat = renderer.material;
        currentWallData = new WallData
        {
            renderer = renderer,
            material = mat,
            originalColor = mat.color,
            currentAlpha = 1f
        };
        mat.shader = xrayShader;
        // 初始完全不透明：启用不透明 Pass（ZWrite On），正确写深度
        mat.SetFloat("_Transparency", 1f);
        mat.EnableKeyword("_XRAY_OPAQUE");
        currentFadingWall = wall;
        missingFrames = 0;
        return true;
    }

    void ApplyAlpha(WallData data)
    {
        if (data == null || data.material == null) return;
        // 颜色保持原样，仅通过 _Transparency 控制可见性
        data.material.SetFloat("_Transparency", data.currentAlpha);

        // 半透明态（<0.98）：ZWrite Off Pass，不写深度，墙后角色/敌人不被剔除；
        // 接近不透明态（>=0.98）：ZWrite On Pass，正确写深度、正确遮挡。
        // 通过材质 keyword 切换 Pass（非切 Shader，渲染队列恒定，不会闪烁）
        if (data.currentAlpha >= 0.98f)
            data.material.EnableKeyword("_XRAY_OPAQUE");
        else
            data.material.DisableKeyword("_XRAY_OPAQUE");
    }

    // 平滑恢复：透明度慢慢回到 1（保持 X-Ray Shader 不变）
    void RestoreWallSmooth()
    {
        if (currentWallData == null || currentWallData.material == null) return;

        currentWallData.currentAlpha = Mathf.Lerp(currentWallData.currentAlpha, 1f, Time.deltaTime * fadeSpeed);
        ApplyAlpha(currentWallData);

        if (currentWallData.currentAlpha > 0.99f)
        {
            RestoreWallNow();
        }
    }

    // 结束：仅确保透明度回到 1（不透明 Pass, ZWrite On），不切回原 Shader
    void RestoreWallNow()
    {
        if (currentWallData != null)
        {
            currentWallData.currentAlpha = 1f;
            if (currentWallData.material != null)
            {
                currentWallData.material.SetFloat("_Transparency", 1f);
                currentWallData.material.EnableKeyword("_XRAY_OPAQUE");
            }
        }
        currentFadingWall = null;
        currentWallData = null;
        missingFrames = 0;
    }

    void OnDestroy()
    {
        RestoreWallNow();
    }
}