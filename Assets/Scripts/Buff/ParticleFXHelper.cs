using UnityEngine;
using UnityEngine.Rendering;

// 共享的粒子特效工具：运行时生成柔和光点贴图与粒子材质，避免依赖外部美术资源。
public static class ParticleFXHelper
{
    // buff 类型 → 主题色（与 BuffGroundRing 保持一致）
    public static Color GetBuffColor(BuffType type)
    {
        switch (type)
        {
            case BuffType.SpeedUp: return new Color(0.3f, 0.75f, 1.0f);   // 蓝色
            case BuffType.PowerUp: return new Color(1.0f, 0.55f, 0.15f);  // 橙色
            case BuffType.Heal:    return new Color(0.2f, 1.0f, 0.5f);    // 绿色
            default:               return new Color(0.8f, 0.8f, 0.8f);   // 白色
        }
    }

    // 生成 64x64 径向渐变柔光圆点，边缘透明
    public static Texture2D CreateSoftDotTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float center = (size - 1f) * 0.5f;
        float maxDist = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    // 创建透明叠加粒子材质（适用于 URP / 内置管线，自动回退）
    public static Material MakeSoftMaterial(Color tint, float brightness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            shader = Shader.Find("Legacy Shaders/Particles/Additive");

        var mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        var softDot = CreateSoftDotTexture();
        // URP 用 _BaseMap，legacy/Sprites/Default 用 _MainTex：两边都绑，保证任何 fallback 都贴上圆点，
        // 否则 APK 上 Shader.Find 落空回退到 Sprites/Default 时贴图是空的 → 粒子渲染成白色方块。
        mat.SetTexture("_BaseMap", softDot);
        mat.SetTexture("_MainTex", softDot);
        mat.SetColor("_BaseColor", tint * brightness);
        mat.SetColor("_Color", tint * brightness);
        mat.SetFloat("_Surface", 1f); // transparent
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        return mat;
    }

    // 新建一个空粒子系统子物体，返回已配好基础设置的 ParticleSystem
    public static ParticleSystem CreateParticleSystem(Transform parent, string childName, Color tint, float brightness, int sortingOrder)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;

        var ps = child.AddComponent<ParticleSystem>();

        // AddComponent 的瞬间（OnEnable）系统已按默认 playOnAwake=true 开始播放，
        // 之后才关 playOnAwake 已经太迟。必须先立即 Stop，再关自动播放，
        // 否则后续改 duration / 速度曲线仍会抛 "setting the duration while system is still playing"。
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;

        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = sortingOrder;
        renderer.material = MakeSoftMaterial(tint, brightness);

        // 调用方配置完所有模块后再显式 Play()。此处已停并禁用自播放。
        return ps;
    }
}