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
    [Tooltip("至少需要多少条射线命中同一面墙，才认为该墙遮挡玩家（建议设为2或3）")]
    public int minHitsToFade = 2;

    private Dictionary<Transform, WallData> activeWalls = new Dictionary<Transform, WallData>();

    private class WallData
    {
        public Material material;
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

        // 确定需要半透明的墙壁：命中次数 >= minHitsToFade
        HashSet<Transform> wallsToFade = new HashSet<Transform>();
        foreach (var kvp in hitCounts)
        {
            if (kvp.Value >= minHitsToFade)
                wallsToFade.Add(kvp.Key);
        }

        // 处理这些墙壁：如果还未加入字典则创建，并淡化
        foreach (Transform wall in wallsToFade)
        {
            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer == null) continue;

            if (!activeWalls.ContainsKey(wall))
            {
                Material mat = renderer.material;
                mat.shader = Shader.Find("Custom/XRayWall");
                WallData data = new WallData
                {
                    material = mat,
                    originalColor = mat.color,
                    currentAlpha = 1f
                };
                activeWalls.Add(wall, data);
            }

            WallData wallData = activeWalls[wall];
            wallData.currentAlpha = Mathf.Lerp(wallData.currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);
            Color color = wallData.originalColor;
            color.a = wallData.currentAlpha;
            wallData.material.color = color;
            wallData.material.SetFloat("_Transparency", wallData.currentAlpha);
        }

        // 恢复没有被选中的墙壁（即不在 wallsToFade 中）
        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in activeWalls)
        {
            Transform wall = kvp.Key;
            if (!wallsToFade.Contains(wall))
            {
                WallData wallData = kvp.Value;
                wallData.currentAlpha = Mathf.Lerp(wallData.currentAlpha, 1f, Time.deltaTime * fadeSpeed);
                Color color = wallData.originalColor;
                color.a = wallData.currentAlpha;
                wallData.material.color = color;
                wallData.material.SetFloat("_Transparency", wallData.currentAlpha);

                if (wallData.currentAlpha > 0.99f)
                    toRemove.Add(wall);
            }
        }

        foreach (Transform wall in toRemove)
        {
            activeWalls.Remove(wall);
        }
    }

    void OnDestroy()
    {
        foreach (var kvp in activeWalls)
        {
            WallData data = kvp.Value;
            Color color = data.originalColor;
            color.a = 1f;
            data.material.color = color;
            data.material.SetFloat("_Transparency", 1f);
        }
        activeWalls.Clear();
    }
}