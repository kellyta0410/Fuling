using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("跟随目标")]
    public Transform target;        // 拖入你的角色
 
    [Header("相机偏移量")]
    public float distance = 10f;    // 水平距离（相机离角色多远）
    public float height = 8f;       // 垂直高度（相机多高）

    [Header("平滑跟随")]
    public float smoothSpeed = 5f;  // 跟随平滑度（数值越大越跟得紧）

    void LateUpdate()
    {
        if (target == null)
        {
            Debug.LogWarning("FollowCamera: 没有设置目标！");
            return;
        }

        // 1. 计算目标位置：角色位置 + 斜上方偏移
        Vector3 targetPosition = target.position + new Vector3(0, height, -distance);

        // 2. 平滑移动到目标位置（可选，如果不想平滑就把 smoothSpeed 设成 100）
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);

        // 3. 相机始终看着角色（看向角色上半身，避免看脚底）
        Vector3 lookTarget = target.position + Vector3.up * 1.5f;
        transform.LookAt(lookTarget);
    }
}