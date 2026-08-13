using UnityEngine;

// 程序化生成的一次性 buff 光柱：从地板处亮起一根竖线往上冲一下。
// 不使用 VFX Graph，纯 ParticleSystem 运行时构建，避免依赖出问题的 .vfx 资产。
public class HealEffectBuilder : MonoBehaviour
{
    public float effectDuration = 1.1f;
    public float columnHeight = 3.4f;      // 光柱往上冲到的高度
    public Color glowColor = new Color(0.2f, 0.9f, 0.45f, 1f); // 默认绿（Heal）

    // 由外部按 buff 类型设置颜色后调用；Awake 不自动构建，避免用默认色
    public void Init(Color color)
    {
        glowColor = color;
        BuildFloorFlash();   // 地板一圈亮起
        BuildRisingColumn(); // 竖线光柱往上冲
    }

    // 地板一圈瞬间亮起 + 淡出：给"从地板升起"的起点感
    void BuildFloorFlash()
    {
        var ps = ParticleFXHelper.CreateParticleSystem(transform, "FloorFlash", glowColor, 1.4f, 2000);

        var main = ps.main;
        main.duration = effectDuration;
        main.loop = false;
        main.startLifetime = 0.55f;
        main.startSpeed = 0f;
        main.startSize = 0.08f;
        main.startColor = glowColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTimeMultiplier = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 70)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 0.55f;
        shape.radiusThickness = 0.7f;
        shape.arc = 360f;

        // 平铺在地面
        ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve grow = new AnimationCurve();
        grow.AddKey(0f, 0.5f);
        grow.AddKey(0.3f, 1.9f);   // 快速外扩
        grow.AddKey(1f, 5f);       // 扩大后淡出
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, grow);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = BuildFadeGradient(0.05f, 0.85f);

        ps.Play();
    }

    // 竖线光柱：粒子从地板原位沿 +Y 飞快上升，形成"往上冲一下"的线，
    // 粒子尺寸收窄变淡，像光柱从地板冒起。
    void BuildRisingColumn()
    {
        var ps = ParticleFXHelper.CreateParticleSystem(transform, "RisingColumn", glowColor, 1.8f, 1500);

        var main = ps.main;
        main.duration = effectDuration;
        main.loop = false;
        main.startLifetime = 0.7f;
        main.startSpeed = 0f;   // 速度交给 velocityOverLifetime 严格控制方向
        main.startSize = 0.42f;
        main.startColor = glowColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTimeMultiplier = 0f;
        emission.SetBursts(new ParticleSystem.Burst[]
        {
            new ParticleSystem.Burst(0f, 180)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.14f;   // 集中在原地的小半径，成一条线

        // 严格竖直向上：x/z 为随机双常量(同模式)，y 高速上升
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        float riseSpeed = columnHeight / 0.7f; // 保证 0.7s 内冲到目标高度
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(riseSpeed, riseSpeed);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve thin = new AnimationCurve();
        thin.AddKey(0f, 1f);
        thin.AddKey(0.5f, 0.7f);
        thin.AddKey(1f, 0.2f);   // 顶部略收窄（保留冲上天焰感）
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, thin);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = BuildFadeGradient(0.05f, 0.95f);

        ps.Play();
    }

    // 共用渐隐渐变：快速冲起 → 顶部淡出消散
    ParticleSystem.MinMaxGradient BuildFadeGradient(float fadeIn, float fadeOut)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(glowColor, 0f), new GradientColorKey(glowColor, 1f) },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(fadeOut, 0.12f),
                new GradientAlphaKey(fadeOut, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        return new ParticleSystem.MinMaxGradient(gradient);
    }
}