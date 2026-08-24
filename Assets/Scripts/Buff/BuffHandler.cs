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

    // ---------- 任意 buff 生效时在玩家脚下生成特效（颜色按 buff 类型） ----------
    // 原版用 VFX Graph(.vfx)。VFX 在 Android OpenGL ES 下不渲染；原先的运行时叠加粒子兜底版
    // 在 GLES + HDR 的部分移动 GPU 上会整屏抖闪，已整段移除，GLES 设备直接无脚下特效。
    public void SpawnBuffEffect(BuffType type)
    {
        if (player == null) return;

        // ⚠️ 诊断用开关：临时关闭所有 buff 特效生成，用于定位屏幕闪烁是否由 buff VFX 引起。
        //    真机拾取 buff 后若闪消失 → 确认是它；验证完把此处改回 true 即可恢复。
        const bool buffVfxEnabled = true;
        if (!buffVfxEnabled) return;

        GameObject prefab = GetEffectPrefab(type);
        if (prefab == null) return;

        bool vfxUnsupported = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3
                           || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2;
        if (vfxUnsupported) return; // GLES 下 VFX 不渲染，且不再用叠加粒子兜底

        float lifetime = GetEffectLifetime(type);
        GameObject effect = Instantiate(prefab, player.transform.position, Quaternion.identity, player.transform);
        if (lifetime > 0f) Destroy(effect, lifetime);
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