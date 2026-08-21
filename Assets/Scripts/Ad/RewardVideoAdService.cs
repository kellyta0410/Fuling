using System;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 激励视频广告服务。
    /// 平台路由：微信小游戏 / Android AdMob / 其它平台兜底直接结算（离线/无广告不复活）。
/// 全部通过反射调用 SDK（微信 WX、GoogleMobileAds.Api），
/// 因此即使当前工程还没导入对应 SDK，本代码也可以正常编译运行。
/// </summary>
public static class RewardVideoAdService
{
    // ⚠️ TODO: 微信小游戏：替换成你自己的广告位 id（测试 id 获取方法见类底部注释）
    public const string ReviveAdUnitId = "adunit-xxxxxxxxxxxxxxxx";

    // AdMob 广告位 id：
    //  测试阶段用官方测试位（不挑账号，稳定出测试广告）；
    //  上线前把下面 AdMobUnitId 换成真实广告位 ca-app-pub-6804806239678291/2443740599。
    public const string AdMobTestRewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
    public static string AdMobUnitId = AdMobTestRewardedUnitId;

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

    // ⭐ AdMob 必须在展示前完成初始化（主线程）。
    // 这里缓存一次初始化结果，并在游戏启动(GameManager.Start)就预先调用，保证首次复活看广告时 SDK 已就绪。
    private static bool adMobInitialized = false;
    public static void EnsureAdMobInitialized()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (adMobInitialized) return;
        try
        {
            if (ResolveType("GoogleMobileAds.Api.RewardedAd") == null) return; // 没装插件，跳过
            MethodInfo init = FindStaticMethod("GoogleMobileAds.Api.MobileAds", "Initialize", 1);
            if (init != null)
            {
                Delegate initCb = BuildDelegate(init.GetParameters()[0].ParameterType, (Action<object>)(_ => { adMobInitialized = true; }));
                init.Invoke(null, new object[] { initCb });
            }
            adMobInitialized = true;
            AdLog("[广告] MobileAds.Initialize 已预先调用");
        }
        catch (Exception e)
        {
            AdLog($"[广告] 预初始化异常（不影响流程）：{e.Message}");
        }
#endif
    }

    // 广告日志：同时打到 Unity 日志和文件（真机无 Device Logs 时也能看）
    static void AdLog(string msg)
    {
        UnityEngine.Debug.Log(msg);
        try
        {
            string path = Path.Combine(Application.persistentDataPath, "fuling_ad_log.txt");
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff ") + msg + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// 展示激励视频。onResult(true) = 完整看完，可发奖励；onResult(false) = 中途关闭/加载失败。
    /// 平台路由：
    ///   微信小游戏 → 真激励视频（isEnded 才给奖励）；
    ///   Android → AdMob（默认官方测试位；插件没装/加载失败/离线 → 回调 false 进入结算）；
    ///   其它平台（真机，不含编辑器）→ 回调 false 进入结算（无免费复活）。
    /// </summary>
    public static void ShowRewardedAd(Action<bool> onResult, Action onUnavailable = null)
    {
        pendingCallback = onResult;

        // ⭐ 微信小游戏环境（WebGL + SDK 存在）先走：播放真实激励视频
        if (IsWeChatRewardAdAvailable)
        {
            try
            {
                ShowWeChatRewardedAd();
            }
            catch (Exception e)
            {
                AdLog($"[广告] 微信激励视频调用失败：{e.Message}");
                SafeInvoke(false);
            }
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // ⭐ Android 走 AdMob（反射调用）；接管成功则返回，稍后回调。
        // 返回 false = 未安装 AdMob SDK → 视为"无广告可用"，走离线/无广告提示而非直接结算。
        if (TryShowAdMobRewardedAd()) return;
        if (onUnavailable != null)
        {
            pendingCallback = null;
            onUnavailable();
            return;
        }
#endif

        // ⭐ 兜底：没有可用真实广告 provider（离线 / 无广告 SDK）。
        // 优先走"不可用"回调让上层弹离线提示；未提供回调时保持旧行为（编辑器免费复活 / 其它直接结算）。
        if (onUnavailable != null)
        {
            pendingCallback = null;
            onUnavailable();
        }
        else
        {
#if UNITY_EDITOR
            // 编辑器内无真实广告，保留免费复活方便测试
            AdLog("[广告] 编辑器内无真实广告 provider，直接发放奖励（测试）");
            SafeInvoke(true);
#else
            AdLog("[广告] 无真实广告 provider，直接进入结算（不免费复活）");
            SafeInvoke(false);
#endif
        }
    }

    // ==================== AdMob（Google Mobile Ads Unity v11 Next-Gen API，反射调用） ====================

    // 静态方法签名（v11.3 实测）：
    //   MobileAds.Initialize(Action<InitializationStatus>)
    //   RewardedAd.Load(string, AdRequest, Action<RewardedAd, LoadAdError>)   → 静态工厂
    //   ad.Show(Action<Reward>)                                               → 展示，回调里才发奖
    /// <summary>返回 true 表示已接管本次展示（成功或已免费发放），false 表示没装 AdMob SDK</summary>
    static bool TryShowAdMobRewardedAd()
    {
        try
        {
            if (ResolveType("GoogleMobileAds.Api.RewardedAd") == null)
            {
                AdLog("[广告] 未检测到 AdMob（GoogleMobileAds.Api），跳过");
                return false;
            }

            // Next-Gen SDK 要求先在主线程初始化（静默，成败无所谓）
            EnsureAdMobInitialized();
            AdLog("[广告] step1: MobileAds.Initialize 完成");

            object request = CreateInstance("GoogleMobileAds.Api.AdRequest");
            if (request == null)
            {
                AdLog("[广告] AdMob AdRequest 创建失败 → 进入结算");
                SafeInvoke(false);
                return true;
            }
            AdLog("[广告] step2: AdRequest 创建完成");

            MethodInfo load = FindStaticMethod("GoogleMobileAds.Api.RewardedAd", "Load", 3);
            if (load == null)
            {
                AdLog("[广告] AdMob RewardedAd.Load 不存在 → 进入结算");
                SafeInvoke(false);
                return true;
            }
            AdLog("[广告] step3: RewardedAd.Load 找到");

            Delegate loadCb = BuildDelegate2(load.GetParameters()[2].ParameterType, (Action<object, object>)HandleAdMobLoad);
            if (loadCb == null)
            {
                AdLog("[广告] AdMob Load 回调构造失败 → 进入结算");
                SafeInvoke(false);
                return true;
            }
            AdLog("[广告] step4: Load 回调构造完成");

            load.Invoke(null, new object[] { AdMobUnitId, request, loadCb });
            AdLog($"[广告] step5: RewardedAd.Load 已调用，无异常（unitId={AdMobUnitId}）");
            return true;
        }
        catch (Exception e)
        {
            AdLog($"[广告] AdMob 调用异常 → 进入结算：{e}");
            SafeInvoke(false);
            return true;
        }
    }

    // 提取 AdMob 错误信息（LoadAdError），供日志显示
    static string DescribeAdError(object errorObj)
    {
        if (errorObj == null) return "ad is null";
        try
        {
            string msg = GetFieldOrProperty(errorObj, "Message")?.ToString();
            if (string.IsNullOrEmpty(msg))
            {
                MethodInfo getMessage = errorObj.GetType().GetMethod("GetMessage");
                if (getMessage != null) msg = getMessage.Invoke(errorObj, null)?.ToString();
            }
            if (string.IsNullOrEmpty(msg)) msg = errorObj.ToString();
            return msg;
        }
        catch { return errorObj.ToString(); }
    }

    static void HandleAdMobLoad(object adObj, object errorObj)
    {
        if (adObj == null || errorObj != null)
        {
            string msg = DescribeAdError(errorObj);
            AdLog($"[广告] AdMob 加载失败：{msg} → 进入结算（离线/无填充）");
            SafeInvoke(false);
            return;
        }

        Type adType = adObj.GetType();
        MethodInfo show = adType.GetMethod("Show");
        if (show == null)
        {
            AdLog("[广告] AdMob Show 不存在 → 进入结算");
            SafeInvoke(false);
            return;
        }

        Delegate rewardCb = BuildDelegate(show.GetParameters()[0].ParameterType, (Action<object>)HandleAdMobReward);
        if (rewardCb == null) { AdLog("[广告] 激励回调构造失败 → 进入结算"); SafeInvoke(false); return; }

        show.Invoke(adObj, new object[] { rewardCb });
    }

    // Show 回调触发 = 用户看完整段再说（v11 在 Android 主线程回调）
    static void HandleAdMobReward(object rewardObj)
    {
        AdLog("[广告] AdMob 激励视频完整看完，发放奖励");
        SafeInvoke(true);
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
        catch (Exception e) { AdLog($"[广告] Load 失败：{e.Message}"); }

        MethodInfo show = adType.GetMethod("Show");
        if (show == null) show = adType.GetMethod("ShowAsync");
        if (show == null) { SafeInvoke(false); return; }

        try { show.Invoke(ad, null); }
        catch (Exception e)
        {
            AdLog($"[广告] Show 失败：{e.Message}");
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

        AdLog($"[广告] 激励视频关闭，isEnded={watched}");
        SafeInvoke(watched);
    }

    static void HandleOnError(object err)
    {
        if (pendingCallback == null) return;
        AdLog($"[广告] 激励视频错误：{err}");
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
            AdLog($"[广告] 解析 WX 类型失败：{e.Message}");
        }
        return null;
    }

    static Type ResolveType(string fullName)
    {
        try
        {
            Type t = Type.GetType(fullName);
            if (t != null) return t;
        }
        catch { }
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type t = asm.GetType(fullName);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    static MethodInfo FindStaticMethod(string typeName, string methodName, int paramCount)
    {
        Type t = ResolveType(typeName);
        if (t == null) return null;
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name == methodName && m.GetParameters().Length == paramCount) return m;
        }
        return null;
    }

    static object CreateInstance(string typeName)
    {
        Type t = ResolveType(typeName);
        if (t == null) return null;
        try { return Activator.CreateInstance(t); }
        catch (Exception e) { AdLog($"[广告] 创建 {typeName} 失败：{e.Message}"); return null; }
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

    /// <summary>构造 Action&lt;T1,T2&gt; 委托（把两个参数转 object 转发）</summary>
    static Delegate BuildDelegate2(Type delegateType, Action<object, object> handler)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke");
        ParameterInfo[] ps = invoke.GetParameters();
        if (ps.Length != 2) return null;

        ParameterExpression p0 = Expression.Parameter(ps[0].ParameterType, "a");
        ParameterExpression p1 = Expression.Parameter(ps[1].ParameterType, "b");
        Expression body = Expression.Call(
            Expression.Constant(handler.Target),
            handler.Method,
            Expression.Convert(p0, typeof(object)),
            Expression.Convert(p1, typeof(object)));
        return Expression.Lambda(delegateType, body, p0, p1).Compile();
    }
}
