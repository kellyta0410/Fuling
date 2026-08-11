using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 激励视频广告服务（微信小游戏）。
/// 通过反射调用官方转换工具（minigame-unity-webgl-transform）提供的 WeChatWASM.WX，
/// 因此即使当前工程还没导入微信 SDK，本代码也可以正常编译运行：
///   - 微信小游戏环境（WebGL + SDK 存在）→ 播放真实激励视频，isEnded 才给奖励
///   - 其他平台 / SDK 未导入 → 直接判定看完（方便 PC/编辑器测试）
/// </summary>
public static class RewardVideoAdService
{
    // ⚠️ TODO: 替换成你自己的广告位 id（测试 id 获取方法见类底部注释）
    public const string ReviveAdUnitId = "adunit-xxxxxxxxxxxxxxxx";

    /// <summary>是否处于微信小游戏环境（WebGL 且 SDK 可解析）</summary>
    public static bool IsWeChatRewardAdAvailable
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ResolveWXType() != null;
#else
            return false;
#endif
        }
    }

    private static object cachedAd;
    private static Action<bool> pendingCallback;

    /// <summary>
    /// 展示激励视频。onResult(true) = 完整看完，可发奖励；onResult(false) = 中途关闭/加载失败。
    /// 非微信环境直接回调 true（免费复活，方便测试）。
    /// </summary>
    public static void ShowRewardedAd(Action<bool> onResult)
    {
        pendingCallback = onResult;

        if (!IsWeChatRewardAdAvailable)
        {
            Debug.Log("[广告] 非微信小游戏环境，直接发放奖励（免费复活）");
            SafeInvoke(true);
            return;
        }

        try
        {
            ShowWeChatRewardedAd();
        }
        catch (Exception e)
        {
            Debug.LogError($"[广告] 激励视频调用失败：{e.Message}");
            SafeInvoke(false);
        }
    }

    // ==================== 微信 SDK 调用（反射） ====================

    static void ShowWeChatRewardedAd()
    {
        Type wxType = ResolveWXType();
        if (wxType == null) { SafeInvoke(false); return; }

        Type paramType = wxType.Assembly.GetType("WeChatWASM.WXCreateRewardedVideoAdParam");
        if (paramType == null) { SafeInvoke(false); return; }

        // 1. 创建参数对象 { adUnitId = ..., multiton = true }
        object param = Activator.CreateInstance(paramType);
        SetField(paramType, param, "adUnitId", ReviveAdUnitId);
        SetField(paramType, param, "multiton", true);

        // 2. WX.CreateRewardedVideoAd(param)
        MethodInfo create = wxType.GetMethod("CreateRewardedVideoAd");
        object ad = create != null ? create.Invoke(null, new[] { param }) : null;
        if (ad == null) { SafeInvoke(false); return; }
        cachedAd = ad;
        Type adType = ad.GetType();

        // 3. 注册 onClose / onError
        MethodInfo onClose = adType.GetMethod("OnClose");
        MethodInfo onError = adType.GetMethod("OnError");
        if (onClose != null)
        {
            Delegate closeDelegate = BuildDelegate(onClose.GetParameters()[0].ParameterType,
                (Action<object>)HandleOnClose);
            if (closeDelegate != null) onClose.Invoke(ad, new object[] { closeDelegate });
        }
        if (onError != null)
        {
            Delegate errorDelegate = BuildDelegate(onError.GetParameters()[0].ParameterType,
                (Action<object>)HandleOnError);
            if (errorDelegate != null) onError.Invoke(ad, new object[] { errorDelegate });
        }

        // 4. 先 Load 再 Show（官方推荐：避免 show() 在未拉取到时 reject）
        MethodInfo load = adType.GetMethod("Load");
        try { if (load != null) load.Invoke(ad, null); }
        catch (Exception e) { Debug.LogWarning($"[广告] Load 失败：{e.Message}"); }

        MethodInfo show = adType.GetMethod("Show");
        if (show == null) show = adType.GetMethod("ShowAsync");
        if (show == null) { SafeInvoke(false); return; }

        try { show.Invoke(ad, null); }
        catch (Exception e)
        {
            Debug.LogWarning($"[广告] Show 失败：{e.Message}");
            SafeInvoke(false);
        }
    }

    // 回调：res.isEnded 才是完整看完
    static void HandleOnClose(object res)
    {
        if (pendingCallback == null) return;

        bool watched = false;
        if (res == null)
        {
            // 老版本基础库 (<2.1.0)：onClose 触发时必然已看完
            watched = true;
        }
        else
        {
            object v = GetFieldOrProperty(res, "isEnded");
            watched = v is bool b && b;
        }

        Debug.Log($"[广告] 激励视频关闭，isEnded={watched}");
        SafeInvoke(watched);
    }

    static void HandleOnError(object err)
    {
        if (pendingCallback == null) return;
        Debug.LogWarning($"[广告] 激励视频错误：{err}");
        SafeInvoke(false);
    }

    static void SafeInvoke(bool result)
    {
        var cb = pendingCallback;
        pendingCallback = null;
        cb?.Invoke(result);
    }

    // ==================== 反射工具 ====================

    static Type ResolveWXType()
    {
        try
        {
            Type t = Type.GetType("WeChatWASM.WX, Assembly-CSharp");
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("WeChatWASM.WX");
                if (t != null) return t;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[广告] 解析 WX 类型失败：{e.Message}");
        }
        return null;
    }

    static void SetField(Type type, object obj, string name, object value)
    {
        FieldInfo f = type.GetField(name);
        if (f == null)
        {
            PropertyInfo p = type.GetProperty(name);
            p?.SetValue(obj, value, null);
        }
        else f.SetValue(obj, value);
    }

    static object GetFieldOrProperty(object obj, string name)
    {
        Type t = obj.GetType();
        FieldInfo f = t.GetField(name);
        if (f != null) return f.GetValue(obj);
        PropertyInfo p = t.GetProperty(name);
        return p?.GetValue(obj, null);
    }

    /// <summary>为任意 Action&lt;T&gt; 委托类型构造一个把 T 转 object 转发的委托</summary>
    static Delegate BuildDelegate(Type delegateType, Action<object> handler)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke");
        ParameterInfo[] ps = invoke.GetParameters();
        if (ps.Length != 1) return null;

        ParameterExpression p = Expression.Parameter(ps[0].ParameterType, "arg");
        Expression body = Expression.Call(
            Expression.Constant(handler.Target),
            handler.Method,
            Expression.Convert(p, typeof(object)));
        return Expression.Lambda(delegateType, body, p).Compile();
    }
}
