using UnityEngine;

// 挂在“带门墙”预制体里的门子物体上，供 DungeonManager 在运行时可靠地找到门
// （替代按名字 Transform.Find，避免拼写/重命名导致的找不到）。
// 门物体需自带 BoxCollider，并（在其自身或父级）挂有 Animator，
// 且 Animator 含名为 doorOpenAnimParam（默认 "DoorOpen"）的参数用于开关门。
public class DoorMarker : MonoBehaviour { }
