using UnityEngine;
using System.Collections.Generic;

public class CinemachineWallXRay : MonoBehaviour
{
    [Header("设置")]
    public Transform player;
    public float fadeSpeed = 8f;
    public float targetAlpha = 0.15f;
    public LayerMask wallLayer = -1;

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

        // 从摄像机向玩家发射射线
        Vector3 direction = (player.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, player.position);

        RaycastHit[] hits = Physics.RaycastAll(transform.position, direction, distance, wallLayer);

        HashSet<Transform> hitThisFrame = new HashSet<Transform>();

        foreach (RaycastHit hit in hits)
        {
            Transform wall = hit.transform;
            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer == null) continue;

            hitThisFrame.Add(wall);

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

        // 恢复没有被挡住的墙壁
        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in activeWalls)
        {
            if (!hitThisFrame.Contains(kvp.Key))
            {
                WallData wallData = kvp.Value;
                wallData.currentAlpha = Mathf.Lerp(wallData.currentAlpha, 1f, Time.deltaTime * fadeSpeed);

                Color color = wallData.originalColor;
                color.a = wallData.currentAlpha;
                wallData.material.color = color;
                wallData.material.SetFloat("_Transparency", wallData.currentAlpha);

                if (wallData.currentAlpha > 0.99f)
                {
                    toRemove.Add(kvp.Key);
                }
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