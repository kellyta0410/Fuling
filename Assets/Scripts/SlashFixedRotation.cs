using UnityEngine;

public class SlashFixedRotation : MonoBehaviour
{
    private Quaternion fixedRotation;

    // 每次被启用（攻击时 timeline 激活 slash）时记录当时的朝向，
    // 之后每帧强制保持该朝向，避免 slash 跟随模型/根节点旋转。
    private void OnEnable()
    {
        fixedRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        transform.rotation = fixedRotation;
    }
}