using UnityEngine;
using UnityEngine.VFX;

public class SlashFixedRotation : MonoBehaviour
{
    private Quaternion fixedRotation;
    private VisualEffect visualEffect;

    private void Awake()
    {
        visualEffect = GetComponent<VisualEffect>();
    }

    // 每次被启用（攻击时 timeline 激活 slash）时记录当时的朝向，
    // 之后每帧强制保持该朝向，避免 slash 跟随模型/根节点旋转。
    // 同时重播 VFX：slash 在启动时为 inactive、由动画中途激活，
    // VFX Graph 不会自动重发初始事件，必须手动 Reinit 才会生成粒子。
    private void OnEnable()
    {
        fixedRotation = transform.rotation;
        if (visualEffect != null)
            visualEffect.Reinit();
    }

    private void LateUpdate()
    {
        transform.rotation = fixedRotation;
    }
}