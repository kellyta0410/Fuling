using UnityEngine;

// 拾取物漂浮时的地面光圈：平铺在地面上的一圈柔和光环，颜色随 buff 类型不同。
// 组件常驻于 BuffPickupItem 物体，自动生成光环并跟踪其地面投影位置。
public class BuffGroundRing : MonoBehaviour
{
    public float ringRadius = 1.1f;
    public float ringThickness = 0.18f;
    public float glowBrightness = 1.2f;
    public bool followGround = true;
    public float yOffset = 0.06f;

    // 与 BuffHandler 的光柱同理：该运行时叠加粒子在 GLES + HDR/Bloom 的部分移动 GPU
    // 上会整屏抖闪，故默认关闭。需要恢复地面光圈时把下面开关改为 true 即可。
    const bool EnableBuffGroundRing = false;

    private ParticleSystem ring;
    private BuffPickupItem pickup;
    private float groundY = 0f;   // 记录贴地高度，不与 buff 漂浮高度混淆
    private bool groundValid = false;

    void Awake()
    {
        pickup = GetComponent<BuffPickupItem>();
    }

    void Start()
    {
        BuildRing();
        Update();
    }

    void BuildRing()
    {
        if (!EnableBuffGroundRing) return;

        Color color = GetRingColor();
        ring = ParticleFXHelper.CreateParticleSystem(transform, "GroundRing", color, glowBrightness, 0);

        // 关键：光环脱离 buff 层级。buff 会在 Update 里自转(绕Y)并上下漂浮，
        // 若 ring 作为子物体就会继承这些运动，导致"贴地位置 + 父体旋转"互相叠加而抽搐。
        // 独立后每帧只显式设置位置/朝向，完全不受父级影响。
        ring.transform.SetParent(null, true);

        var main = ring.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startColor = new Color(color.r, color.g, color.b, 0.9f);

        var emission = ring.emission;
        emission.enabled = true;
        emission.rateOverTimeMultiplier = 12f;

        var shape = ring.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = ringRadius;
        shape.radiusThickness = ringThickness;
        shape.arc = 360f;

        // 平铺地面：绕 X 轴转 90°，让环形躺在水平面上
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var sizeOverLifetime = ring.sizeOverLifetime;
        sizeOverLifetime.enabled = true;

        var colorOverLifetime = ring.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.35f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // 配置完成后再播放（CreateParticleSystem 已关 playOnAwake，避免播放中改参数报错）
        ring.Play();
    }

    void Update()
    {
        // 拾取物在漂浮（上下浮动），光环应固定贴地，只跟随水平地面投影。
        if (ring == null) return;

        Vector3 pos = transform.position;
        if (followGround)
        {
            // Raycast 向下找真实地面，忽略所有 trigger（含 buff 自身的 trigger 碰撞器与地面光圈）
            // 只有真正命中地面才算有效；否则沿用上次的地面高度，绝不落到 buff 的漂浮高度上。
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                groundValid = true;
            }
        }
        if (!groundValid) groundY = pos.y - 0.6f; // 兜底：没有地时大致放到脚下

        ring.transform.position = new Vector3(pos.x, groundY + yOffset, pos.z);
        // 独立后需自行保持平铺地面（父级不再提供旋转基准）
        ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void OnDestroy()
    {
        // ring 已脱离父级，buff 销毁时需手动清理
        if (ring != null && ring.gameObject != null)
        {
            Destroy(ring.gameObject);
        }
    }

    Color GetRingColor()
    {
        if (pickup != null && pickup.buffData != null)
        {
            return ParticleFXHelper.GetBuffColor(pickup.buffData.buffType);
        }
        return new Color(0.8f, 0.8f, 0.8f); // 默认白色
    }
}