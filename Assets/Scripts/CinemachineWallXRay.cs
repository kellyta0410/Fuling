using UnityEngine;
using System.Collections.Generic;

public class CinemachineWallXRay : MonoBehaviour
{
    [Header("设置")]
    public Transform player;
    public float fadeSpeed = 8f;
    public float targetAlpha = 0.15f;
    public LayerMask wallLayer = -1;

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

    private Transform currentFadingWall = null;  // 当前正在半透明的墙壁（只存一个）
    private WallData currentWallData = null;

    private class WallData
    {
        public Material material;
        public Shader originalShader;
        public Color originalColor;
        public float currentAlpha;
    }

    void LateUpdate()
    {
        if (player == null) return;

        Vector3 camPos = transform.position;
        // 本次帧检测到的墙壁及其命中次数
        Dictionary<Transform, int> hitCounts = new Dictionary<Transform, int>();

        // 对每个偏移点发射射线
        foreach (Vector3 offset in playerOffsets)
        {
            Vector3 targetPoint = player.position + player.TransformDirection(offset);
            Vector3 direction = (targetPoint - camPos).normalized;
            float distance = Vector3.Distance(camPos, targetPoint);

            RaycastHit hit;
            if (Physics.Raycast(camPos, direction, out hit, distance, wallLayer))
            {
                Transform wall = hit.transform;
                if (!hitCounts.ContainsKey(wall))
                    hitCounts[wall] = 0;
                hitCounts[wall]++;
            }
        }

        // 找出命中次数 >= minHitsToFade 的墙壁中，距离最近的那个
        Transform targetWall = null;
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

        // 处理目标墙壁（淡化）
        if (targetWall != null)
        {
            Renderer renderer = targetWall.GetComponent<Renderer>();
            if (renderer != null)
            {
                // 如果当前没有半透明墙壁，或者换了新墙壁
                if (currentFadingWall != targetWall)
                {
                    // 如果有旧的，先恢复
                    if (currentFadingWall != null && currentWallData != null)
                    {
                        RestoreWallData(currentWallData);
                    }

                    // 初始化新墙壁
                    Shader xrayShader = Shader.Find("Custom/XRayWall");
                    if (xrayShader == null)
                    {
                        Debug.LogWarning("[CinemachineWallXRay] 找不到 Shader: Custom/XRayWall，跳过 X-Ray 处理");
                        currentFadingWall = null;
                        currentWallData = null;
                        return;
                    }

                    Material mat = renderer.material;
                    currentWallData = new WallData
                    {
                        material = mat,
                        originalShader = mat.shader,
                        originalColor = mat.color,
                        currentAlpha = 1f
                    };
                    mat.shader = xrayShader;
                    currentFadingWall = targetWall;
                }

                // 淡化
                currentWallData.currentAlpha = Mathf.Lerp(currentWallData.currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
                Color newColor = currentWallData.originalColor;
                newColor.a = currentWallData.currentAlpha;
                currentWallData.material.color = newColor;
                currentWallData.material.SetFloat("_Transparency", currentWallData.currentAlpha);
            }
        }
        else
        {
            // 没有需要半透明的墙壁，恢复当前的
            if (currentFadingWall != null && currentWallData != null)
            {
                currentWallData.currentAlpha = Mathf.Lerp(currentWallData.currentAlpha, 1f, Time.deltaTime * fadeSpeed);
                Color restoreColor = currentWallData.originalColor;
                restoreColor.a = currentWallData.currentAlpha;
                currentWallData.material.color = restoreColor;
                currentWallData.material.SetFloat("_Transparency", currentWallData.currentAlpha);

                if (currentWallData.currentAlpha > 0.99f)
                {
                    RestoreWallData(currentWallData);
                    currentFadingWall = null;
                    currentWallData = null;
                }
            }
        }
    }

    // 完全还原墙壁材质（shader + 颜色）
    void RestoreWallData(WallData data)
    {
        if (data == null || data.material == null) return;
        if (data.originalShader != null)
            data.material.shader = data.originalShader;
        Color finalColor = data.originalColor;
        finalColor.a = 1f;
        data.material.color = finalColor;
        if (data.material.HasProperty("_Transparency"))
            data.material.SetFloat("_Transparency", 1f);
    }

    void OnDestroy()
    {
        if (currentWallData != null)
        {
            RestoreWallData(currentWallData);
        }
        currentFadingWall = null;
        currentWallData = null;
    }
}