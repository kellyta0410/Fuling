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
    public void ApplyBuff(BuffDataSO data, bool permanent = false)
    {
        if (data == null || player == null) return;

        // ===== 即时效果（Heal） =====
        if (data.isInstantEffect)
        {
            ApplyInstantEffect(data);
            ShowBuffToast(GetBuffMessage(data));
            return;
        }

        // ===== 永久叠加 Buff（商店购买；SpeedUp / PowerUp） =====
        if (permanent)
        {
            ApplyPermanent(data);
            return;
        }

        // ===== 限时 Buff（地图上拾取等） =====
        if (activeBuffs.ContainsKey(data.buffType))
        {
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

    // ---------- 永久叠加 Buff（每种最多 maxStack 层，除 Heal 外） ----------
    private Dictionary<BuffType, int> permanentStacks = new Dictionary<BuffType, int>();
    private Dictionary<BuffType, BuffDataSO> permanentData = new Dictionary<BuffType, BuffDataSO>();
    // 商店购买进度：记录每个 buff 下次可购买的层数（1-based），0 表示还没买过第一层
    private Dictionary<BuffType, int> shopProgress = new Dictionary<BuffType, int>();

    void ApplyPermanent(BuffDataSO data)
    {
        if (!permanentStacks.ContainsKey(data.buffType)) permanentStacks[data.buffType] = 0;
        int max = data.maxStack > 0 ? data.maxStack : 1;
        if (permanentStacks[data.buffType] >= max)
        {
            ShowBuffToast(data.buffName + " 已达上限 (" + max + "层)");
            return;
        }
        permanentStacks[data.buffType]++;
        permanentData[data.buffType] = data;
        ReapplyPermanent(data.buffType);
        ShowBuffToast(GetBuffMessage(data) + " [" + permanentStacks[data.buffType] + "/" + max + "层]");
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null) ui.RefreshBuffIcons();
    }

    // 按类型把"当前层数 × 每层幅度"从 base 重算并应用到玩家
    void ReapplyPermanent(BuffType type)
    {
        if (!permanentData.ContainsKey(type) || player == null) return;
        int n = permanentStacks[type];
        BuffDataSO data = permanentData[type];
        switch (type)
        {
            case BuffType.SpeedUp:
                player.ApplySpeedMultiplier(1f + n * data.effectValue); break;
            case BuffType.PowerUp:
                player.ApplyAttackAdditive(Mathf.RoundToInt(n * data.effectValue)); break;
            case BuffType.AttackRangeUp:
                player.ApplyAttackRangeAdditive(n * data.effectValue); break;
            case BuffType.SkillPowerUp:
                player.ApplySkillDamageAdditive(Mathf.RoundToInt(n * data.effectValue)); break;
            case BuffType.SkillRangeUp:
                player.ApplySkillRangeAdditive(n * data.effectValue); break;
            case BuffType.SkillCooldownUp:
                player.ApplySkillCooldownAdditive(n * data.effectValue); break;
            case BuffType.CoinMultUp:
                player.ApplyCoinMultiplier(1f + n * data.effectValue); break;
            case BuffType.MaxHealthUp:
                player.ApplyMaxHealthAdditive(n * data.effectValue); break;
            case BuffType.Heal:
                break; // 不会走这里
        }
    }

    public int GetStack(BuffType type)
    {
        return permanentStacks.ContainsKey(type) ? permanentStacks[type] : 0;
    }

    // 商店购买进度：返回下次可购买的层数（1-based），0=未买过
    public int GetShopProgress(BuffType type)
    {
        return shopProgress.ContainsKey(type) ? shopProgress[type] : 0;
    }

    // 购买后推进商店进度（调用时机：BuyBuff 扣币成功后）
    public void AdvanceShopProgress(BuffType type)
    {
        if (!shopProgress.ContainsKey(type)) shopProgress[type] = 0;
        shopProgress[type]++;
    }

    // 供 UI 显示已购买 Buff 图标
    public class BuffOwned
    {
        public BuffType type;
        public BuffDataSO data;
        public int stack;
        public BuffOwned(BuffType t, BuffDataSO d, int s) { type = t; data = d; stack = s; }
    }

    public List<BuffOwned> GetOwnedBuffs()
    {
        var list = new List<BuffOwned>();
        foreach (var kvp in permanentStacks)
        {
            if (kvp.Value > 0 && permanentData.ContainsKey(kvp.Key))
                list.Add(new BuffOwned(kvp.Key, permanentData[kvp.Key], kvp.Value));
        }
        return list;
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
                else if (data.isHalfRestore)
                {
                    player.HealToHalf();
                    Debug.Log("生命值恢复到一半！");
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

        // VFX Graph 在 Vulkan 下正常渲染（Android 已设 Vulkan 优先、GLES3 兜底）。
        // GLES3 设备该特效不可见但无害，故不再在此早退。

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