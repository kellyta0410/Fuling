using UnityEngine;

// 挂在“带门墙”预制体里的门子物体上，供 DungeonManager 在运行时可靠地找到门
// （替代按名字 Transform.Find，避免拼写/重命名导致的找不到）。
// 门物体需自带 BoxCollider（开门时由代码禁用碰撞体、绕本地轴旋转门叶，不依赖 Animator）。
public class DoorMarker : MonoBehaviour
{
    [Tooltip("要旋转开门的物体（门网格）。留空则默认用挂本组件的物体。把门网格拖到这里，DoorMarker 挂哪都行。")]
    public Transform swingTarget;

    [Tooltip("勾选后用下面的 swingAngle/swingAxis 覆盖 DungeonManager 的全局默认值。双开门左右叶设不同角度（如 +90 / -90）即各自独立旋转。")]
    public bool overrideSwing = false;
    public float swingAngle = 90f;             // 该门叶旋转角度（正/负决定开门方向）
    public Vector3 swingAxis = Vector3.forward; // 该门叶旋转轴（门的本地空间）
}
