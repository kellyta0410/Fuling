using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

public class BuffHandler : MonoBehaviour
{
    [Header("buff 特效")]
    [Tooltip("拾取任意 buff 时，在玩家脚下生成的一次性光柱特效预制体（如 HealEffect，颜色按 buff 类型在运行时设置）")]
    public GameObject healEffectPrefab;
    public GameObject attackEffectPrefab;
    public GameObject speedEffectPrefab;
    [Tooltip("特效存在时间（秒），结束后自动销毁")]
    public float healEffectLifetime = 2f;
    public float attackEffectLifetime = 2f;
    public float speedEffectLifetime = 2f;

    private Dictionary<BuffType, Buff> activeBuffs = new Dictionary<BuffType, Buff>();
    private PlayerController player;
    private UIManager cachedUI;

    void Start()
    {
        player = GetComponent<PlayerController>();
        if (player == null)
            Debug.LogError("BuffHandler 需要 PlayerController 组件！");
    }

    // ---------- 外部接口：应用Buff或即时效果 ----------
    public void ApplyBuff(BuffDataSO data)
    {
        if (data == null || player == null) return;

        // ===== 即时效果（Heal） =====
        if (data.isInstantEffect)
        {
            ApplyInstantEffect(data);
            ShowBuffToast(GetBuffMessage(data));
            return;
        }

        // ===== 持续性 Buff（SpeedUp / PowerUp） =====
        if (activeBuffs.ContainsKey(data.buffType))
        {
            // 若已存在，刷新持续时间（可扩展叠加逻辑）；已生效的持续buff重拾也要重新播放光柱特效
            activeBuffs[data.buffType].Refresh(data);
            SpawnBuffEffect(data.buffType);
        }
        else
        {
            Buff newBuff = new Buff(data, this);
            activeBuffs.Add(data.buffType, newBuff);
            newBuff.OnApply();
        }

        ShowBuffToast(GetBuffMessage(data));
    }

    // ---------- Buff 获取提示 ----------
    private string GetBuffMessage(BuffDataSO data)
    {
        if (data == null) return "获得 Buff！";

        switch (data.buffType)
        {
            case BuffType.Heal:
                return data.isFullRestore ? "生命回满！" : $"恢复 {Mathf.RoundToInt(data.effectValue)} 点生命";
            case BuffType.SpeedUp:
                return $"获得 {data.buffName}！";
            case BuffType.PowerUp:
                return $"获得 {data.buffName}，攻击 +{Mathf.RoundToInt(data.effectValue)}！";
            default:
                return $"获得 {data.buffName}！";
        }
    }

    private void ShowBuffToast(string message)
    {
        if (cachedUI == null) cachedUI = FindObjectOfType<UIManager>();
        if (cachedUI != null) cachedUI.ShowBuffToast(message);
    }

    // ---------- 即时效果执行 ----------
    private void ApplyInstantEffect(BuffDataSO data)
    {
        switch (data.buffType)
        {
            case BuffType.Heal:
                if (data.isFullRestore)
                {
                    player.RestoreFullHealth();
                    Debug.Log("生命值已回满！");
                }
                else
                {
                    player.Heal(data.effectValue);
                    Debug.Log($"恢复了 {data.effectValue} 点生命值");
                }
                SpawnBuffEffect(data.buffType);
                break;

            default:
                Debug.LogWarning($"未处理的即时效果类型：{data.buffType}");
                break;
        }
    }

    // ---------- 任意 buff 生效时在玩家脚下生成一次光柱（颜色按 buff 类型） ----------
    // 原版用 VFX Graph(.vfx)。VFX 在 Android OpenGL ES 下不渲染，这里做运行时兜底：
    //   - GLES 设备直接放纯粒子版（ParticleFXHelper/HealEffectBuilder），保证有特效；
    //   - 其它 API（Vulkan/Metal/DX11）放原版 VFX，若仍 0 粒子（个别驱动问题）也自动切回粒子版。
    public void SpawnBuffEffect(BuffType type)
    {
        if (player == null) return;

        GameObject prefab = GetEffectPrefab(type);
        float lifetime = GetEffectLifetime(type);

        bool vfxUnsupported = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3
                           || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2;

        if (prefab == null || vfxUnsupported)
        {
            SpawnParticleFallback(type, lifetime);
            return;
        }

        GameObject effect = Instantiate(prefab, player.transform.position, Quaternion.identity, player.transform);
        if (lifetime > 0f) Destroy(effect, lifetime);

        StartCoroutine(FallbackIfVfxInvisible(effect, type, lifetime));
    }

    GameObject GetEffectPrefab(BuffType type)
    {
        switch (type)
        {
            case BuffType.Heal: return healEffectPrefab;
            case BuffType.PowerUp: return attackEffectPrefab;
            case BuffType.SpeedUp: return speedEffectPrefab;
            default: return null;
        }
    }

    float GetEffectLifetime(BuffType type)
    {
        switch (type)
        {
            case BuffType.Heal: return healEffectLifetime;
            case BuffType.PowerUp: return attackEffectLifetime;
            case BuffType.SpeedUp: return speedEffectLifetime;
            default: return 0f;
        }
    }

    // 纯 ParticleSystem 版光柱（HealEffectBuilder），颜色按 buff 类型
    void SpawnParticleFallback(BuffType type, float lifetime)
    {
        GameObject effect = new GameObject($"BuffEffectFallback_{type}");
        effect.transform.SetParent(player.transform, false);
        effect.transform.localPosition = Vector3.zero;

        HealEffectBuilder builder = effect.AddComponent<HealEffectBuilder>();
        builder.Init(ParticleFXHelper.GetBuffColor(type));

        if (lifetime > 0f) Destroy(effect, lifetime);
    }

    // VFX 放了但一直 0 粒子 = 没渲染出来（驱动/API 兼容问题），删掉换成粒子版
    IEnumerator FallbackIfVfxInvisible(GameObject vfxObj, BuffType type, float lifetime)
    {
        VisualEffect vfx = vfxObj != null ? vfxObj.GetComponent<VisualEffect>() : null;
        if (vfx == null) yield break;

        float wait = lifetime > 0.1f ? Mathf.Clamp(lifetime * 0.7f, 0.4f, 1.6f) : 1.2f;
        yield return new WaitForSeconds(wait);

        if (vfxObj == null) yield break;
        if (vfx.aliveParticleCount > 0) yield break;

        if (vfxObj != null) Destroy(vfxObj);
        SpawnParticleFallback(type, lifetime);
    }

    // ---------- 移除Buff ----------
    public void RemoveBuff(BuffType type)
    {
        if (activeBuffs.ContainsKey(type))
        {
            activeBuffs[type].OnRemove();
            activeBuffs.Remove(type);
        }
    }

    // ---------- 每帧更新（持续性Buff计时） ----------
    private void Update()
    {
        List<BuffType> toRemove = new List<BuffType>();
        foreach (var kvp in activeBuffs)
        {
            kvp.Value.Tick(Time.deltaTime);
            if (kvp.Value.IsExpired)
                toRemove.Add(kvp.Key);
        }
        foreach (var type in toRemove)
        {
            RemoveBuff(type);
        }
    }

    // ---------- 供 Buff 类调用的修改方法 ----------
    public void ModifySpeed(float multiplier)
    {
        player.ApplySpeedMultiplier(multiplier);
    }

    public void ModifyAttack(int additive)
    {
        player.ApplyAttackAdditive(additive);
    }
}

// ============ Buff 运行时类（仅用于持续性Buff） ============
public class Buff
{
    public BuffDataSO Data { get; private set; }
    private BuffHandler handler;
    private float currentDuration;
    public bool IsExpired { get; private set; }

    public Buff(BuffDataSO data, BuffHandler handler)
    {
        this.Data = data;
        this.handler = handler;
        currentDuration = data.duration;
        IsExpired = false;
    }

    public void Refresh(BuffDataSO newData)
    {
        if (newData != null && newData.duration > 0)
            currentDuration = newData.duration;
        // 若需要叠加层数可在此扩展
    }

    public void OnApply()
    {
        switch (Data.buffType)
        {
            case BuffType.SpeedUp:
                float speedMult = 1f + Data.effectValue; // 例如 effectValue=0.2 → 1.2 倍
                handler.ModifySpeed(speedMult);
                Debug.Log($"SpeedUp 生效，速度倍率：{speedMult}");
                break;

            case BuffType.PowerUp:
                int atkAdd = Mathf.RoundToInt(Data.effectValue); // 取整
                handler.ModifyAttack(atkAdd);
                Debug.Log($"PowerUp 生效，攻击力 +{atkAdd}");
                break;

            default:
                Debug.LogWarning($"未知持续Buff类型：{Data.buffType}");
                break;
        }
        handler.SpawnBuffEffect(Data.buffType); // 持续buff也播对应颜色的光柱
    }

    public void OnRemove()
    {
        switch (Data.buffType)
        {
            case BuffType.SpeedUp:
                handler.ModifySpeed(1f); // 恢复原始速度
                break;

            case BuffType.PowerUp:
                handler.ModifyAttack(0); // 恢复为基础攻击力
                break;
        }
        Debug.Log($"Buff {Data.buffName} 已移除");
    }

    public void Tick(float deltaTime)
    {
        if (IsExpired || Data.duration <= 0) return;
        currentDuration -= deltaTime;
        if (currentDuration <= 0) IsExpired = true;
    }
}