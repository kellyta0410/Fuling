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
    private static object preloadedAd;
    private static Action<bool> pendingCallback;
    private static Action<string> pendingUnavailable;

    // 看广告流程看门狗：确保 Load/Show 回调即使永远不来也能超时兜底，避免复活面板一直暂停卡死
    private static bool adPending = false;
    private static bool rewardEarned = false; // 用户是否完整看完（拿到激励），用于"关闭"事件兜底，避免复活被误取消
    private static float watchdogDeadline = -1f;
    private const float WatchdogSeconds = 12f;
    // 广告成功展示后改用较长兜底计时：既不误杀 15~30s 的长广告，又能在"关闭事件丢失"时兜底，
    // 避免复活面板永久卡在暂停态（比原来的 12s 全程计时更合理）。
    private const float WatchdogAfterShowSeconds = 60f;
    private static string adMobUnavailableReason; // 广告不可用原因（如 AdMob 类被裁剪/未打包）

    // ⭐ AdMob 必须在展示前完成初始化（主线程）。
    // 在游戏启动(GameManager.Start)就预先调用，保证首次复活看广告时 SDK 已就绪；
    // MobileAds.Initialize 本身幂等，重复调用安全，故无需额外的"已初始化"标记位。
    public static void EnsureAdMobInitialized()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (ResolveType("GoogleMobileAds.Api.RewardedAd") == null) return; // 没装插件，跳过
            MethodInfo init = FindStaticMethod("GoogleMobileAds.Api.MobileAds", "Initialize", 1);
            if (init != null)
            {
                Delegate initCb = BuildDelegate(init.GetParameters()[0].ParameterType, (Action<object>)(_ => { AdLog("[Ad] MobileAds init done"); }));
                init.Invoke(null, new object[] { initCb });
            }
            AdLog("[Ad] MobileAds.Initialize called");
        }
        catch (Exception e)
        {
            AdLog($"[Ad] pre-init exception (ignored): {e.Message}");
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
    public static void ShowRewardedAd(Action<bool> onResult, Action<string> onUnavailable = null)
    {
        // ⭐ 防重入：广告加载/展示期间按钮仍可点（全屏广告尚未弹出时面板可见），
        // 重复点击会并发多次 Load / Show，导致弹多次广告、SafeInvoke 重入。已在处理中则直接忽略。
        if (adPending)
        {
            AdLog("[Ad] ignore duplicate ShowRewardedAd (ad already pending)");
            return;
        }
        pendingCallback = onResult;
        pendingUnavailable = onUnavailable;
        adPending = true;
        rewardEarned = false;
        watchdogDeadline = -1f;

        // ⭐ 微信小游戏环境（WebGL + SDK 存在）先走：播放真实激励视频
        if (IsWeChatRewardAdAvailable)
        {
            try
            {
                ShowWeChatRewardedAd();
            }
            catch (Exception e)
            {
                AdLog($"[Ad] WeChat rewarded ad call failed: {e.Message}");
                SafeInvoke(false);
            }
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // ⭐ Android 走 AdMob（反射调用）；接管成功则返回，稍后回调。
        // 返回 false = 未安装 AdMob SDK → 视为"无广告可用"，走离线/无广告提示而非直接结算。
        adMobUnavailableReason = null;
        if (TryShowAdMobRewardedAd()) return;
        if (pendingUnavailable != null)
        {
            InvokeUnavailable(string.IsNullOrEmpty(adMobUnavailableReason) ? "广告暂时不可用" : adMobUnavailableReason);
            return;
        }
#endif

        // ⭐ 兜底：没有可用真实广告 provider（离线 / 无广告 SDK）。
        // 优先走"不可用"回调让上层弹离线提示；未提供回调时保持旧行为（编辑器免费复活 / 其它直接结算）。
        if (pendingUnavailable != null)
        {
            InvokeUnavailable("当前平台不支持广告");
            return;
        }
        else
        {
#if UNITY_EDITOR
            // 编辑器内无真实广告，保留免费复活方便测试
            AdLog("[Ad] editor: no ad provider, grant reward (test)");
            SafeInvoke(true);
#else
            AdLog("[Ad] no ad provider, go to settle (no free revive)");
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
                AdLog("[Ad] AdMob (GoogleMobileAds.Api) not found, skip");
                adMobUnavailableReason = "AdMob插件类缺失(可能被裁剪/未打包)";
                return false;
            }

            // Next-Gen SDK 要求先在主线程初始化（静默，成败无所谓）
            EnsureAdMobInitialized();
            AdLog("[Ad] step1: MobileAds.Initialize done");

            // ⭐ 优先用提前预加载好的广告：点完立即展示，无等待，也不会被"结束游戏"抢断
            if (preloadedAd != null)
            {
                object ad = preloadedAd;
                preloadedAd = null;
                AdLog("[Ad] step2: using preloaded ad, show now (no wait)");
                ArmWatchdog(WatchdogSeconds);
                HandleAdMobLoad(ad, null);
                return true;
            }

            object request = CreateInstance("GoogleMobileAds.Api.AdRequest");
            if (request == null)
            {
                AdLog("[Ad] AdMob AdRequest create failed");
                FailOrUnavailable("广告请求(AdRequest)创建失败");
                return true;
            }
            AdLog("[Ad] step2: AdRequest created");

            MethodInfo load = FindStaticMethod("GoogleMobileAds.Api.RewardedAd", "Load", 3);
            if (load == null)
            {
                AdLog("[Ad] AdMob RewardedAd.Load missing");
                FailOrUnavailable("广告Load接口缺失");
                return true;
            }
            AdLog("[Ad] step3: RewardedAd.Load found");

            Delegate loadCb = BuildDelegate2(load.GetParameters()[2].ParameterType, (Action<object, object>)HandleAdMobLoad);
            if (loadCb == null)
            {
                AdLog("[Ad] AdMob Load callback build failed");
                FailOrUnavailable("广告回调构造失败");
                return true;
            }
            AdLog("[Ad] step4: Load callback built");

            load.Invoke(null, new object[] { AdMobUnitId, request, loadCb });
            AdLog($"[Ad] step5: RewardedAd.Load called, ok (unitId={AdMobUnitId})");
            ArmWatchdog(WatchdogSeconds);
            return true;
        }
        catch (Exception e)
        {
            AdLog($"[Ad] AdMob call exception: {e.Message}");
            FailOrUnavailable("AdMob调用异常：" + e.Message);
            return true;
        }
    }

    // 提前预加载激励广告，进游戏/复活面板出现时调用，玩家点"观看广告"即可秒出，无等待
    public static void PreloadRewardedAd()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (ResolveType("GoogleMobileAds.Api.RewardedAd") == null) return;
            EnsureAdMobInitialized();
            if (preloadedAd != null) return; // 已有待命广告

            object request = CreateInstance("GoogleMobileAds.Api.AdRequest");
            if (request == null) return;

            MethodInfo load = FindStaticMethod("GoogleMobileAds.Api.RewardedAd", "Load", 3);
            if (load == null) return;

            Delegate loadCb = BuildDelegate2(load.GetParameters()[2].ParameterType, (Action<object, object>)OnPreloadLoad);
            if (loadCb == null) return;

            load.Invoke(null, new object[] { AdMobUnitId, request, loadCb });
            AdLog("[Ad] preload: RewardedAd.Load called");
        }
        catch (Exception e) { AdLog($"[Ad] preload exception: {e.Message}"); }
#endif
    }

    static void OnPreloadLoad(object adObj, object errorObj)
    {
        if (adObj != null && errorObj == null)
        {
            preloadedAd = adObj;
            AdLog("[Ad] preload: ad ready (standby)");
        }
        else
        {
            AdLog($"[Ad] preload failed: {DescribeAdError(errorObj)}");
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
            AdLog($"[Ad] AdMob load failed: {msg}");
            FailOrUnavailable("网络不可用/无填充(离线)");
            return;
        }

        // ⭐ 订阅关闭/失败事件：v11 中 Show(Action<Reward>) 只在"完整看完"才回调，
        // 中途关闭/跳过/展示失败不会触发，必须靠事件兜底，否则复活面板会一直卡在暂停态。
        // 注意：看完后 AdMob 会先发奖励回调、再发关闭事件；用 rewardEarned 防止"关闭"把已到手的复活误取消。
        SubscribeEvent(adObj, "OnAdFullScreenContentClosed", () =>
        {
            AdLog("[Ad] ad fullscreen closed (incl. skip)");
            if (rewardEarned) { AdLog("[Ad] reward already granted, ignore close"); return; }
            SafeInvoke(false); // 真·中途关闭/跳过：不复活
        });
        SubscribeEvent(adObj, "OnAdFullScreenContentFailed", () => { AdLog("[Ad] ad show failed"); SafeInvoke(false); });

        Type adType = adObj.GetType();
        MethodInfo show = adType.GetMethod("Show");
        if (show == null)
        {
            AdLog("[Ad] AdMob Show missing");
            FailOrUnavailable("广告Show接口缺失");
            return;
        }

        Delegate rewardCb = null;
        try
        {
            rewardCb = BuildDelegate(show.GetParameters()[0].ParameterType, (Action<object>)HandleAdMobReward);
        }
        catch (Exception e)
        {
            AdLog($"[Ad] reward callback build exception: {e.Message}");
        }
        if (rewardCb == null) { AdLog("[Ad] reward callback build failed"); FailOrUnavailable("激励回调构造失败"); return; }

        try
        {
            show.Invoke(adObj, new object[] { rewardCb });
            AdLog("[Ad] step6: RewardedAd.Show called, waiting for user");
            ArmWatchdog(WatchdogAfterShowSeconds); // 展示成功后挂较长兜底：不误杀长广告，关闭事件丢失时也不永久卡死
            PreloadRewardedAd(); // 预拉下一条，供下次游戏复活秒出
        }
        catch (Exception e)
        {
            AdLog($"[Ad] Show call exception: {e.Message}");
            FailOrUnavailable("广告Show调用异常：" + e.Message);
        }
    }

    // Show 回调触发 = 用户看完整段再说（v11 在 Android 主线程回调）
    static void HandleAdMobReward(object rewardObj)
    {
        AdLog("[Ad] AdMob rewarded ad fully watched, grant reward");
        rewardEarned = true;
        SafeInvoke(true); // 看完立刻复活，不等用户点关闭
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
        catch (Exception e) { AdLog($"[Ad] Load failed: {e.Message}"); }

        MethodInfo show = adType.GetMethod("Show");
        if (show == null) show = adType.GetMethod("ShowAsync");
        if (show == null) { SafeInvoke(false); return; }

        try { show.Invoke(ad, null); }
        catch (Exception e)
        {
            AdLog($"[Ad] Show failed: {e.Message}");
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

        AdLog($"[Ad] rewarded ad closed, isEnded={watched}");
        SafeInvoke(watched);
    }

    static void HandleOnError(object err)
    {
        if (pendingCallback == null) return;
        AdLog($"[Ad] rewarded ad error: {err}");
        SafeInvoke(false);
    }

    static void SafeInvoke(bool result)
    {
        adPending = false;
        watchdogDeadline = -1f;
        var cb = pendingCallback;
        pendingCallback = null;
        cb?.Invoke(result);
    }

    // 广告确实无法播放（无 SDK / 离线 / 平台不支持）：若上层提供了 unavailable 回调则弹提示，否则按旧逻辑结算/免费复活
    static void InvokeUnavailable(string reason)
    {
        adPending = false;
        watchdogDeadline = -1f;
        var cb = pendingUnavailable;
        pendingCallback = null;
        pendingUnavailable = null;
        cb?.Invoke(reason);
    }

    // 在 AdMob 内部失败时使用：有 unavailable 回调则弹提示，否则退化为旧行为（SafeInvoke(false) → 结算）
    static void FailOrUnavailable(string reason)
    {
        if (pendingUnavailable != null) InvokeUnavailable(reason);
        else SafeInvoke(false);
    }

    // ==================== 看门狗（防卡死） ====================
    static void ArmWatchdog(float seconds)
    {
        if (!adPending) return;
        watchdogDeadline = Time.realtimeSinceStartup + seconds;
            AdLog($"[Ad] watchdog armed, {seconds}s timeout");
    }

    // 由 GameManager.Update 每帧调用；超时仍未结算则兜底弹提示，避免复活面板永久暂停
    public static void TickWatchdog()
    {
        if (!adPending || watchdogDeadline < 0f) return;
        if (Time.realtimeSinceStartup >= watchdogDeadline)
        {
            watchdogDeadline = -1f;
            AdLog("[Ad] watchdog triggered: ad load/show timeout");
            FailOrUnavailable("广告加载/展示超时");
        }
    }

    // 用反射订阅 AdMob 事件（兼容 0 参 Action 与 1 参 Action<T>）
    static void SubscribeEvent(object target, string eventName, Action onInvoke)
    {
        try
        {
            EventInfo ev = target.GetType().GetEvent(eventName);
            if (ev == null) { AdLog($"[Ad] event {eventName} missing, skip subscribe"); return; }
            Type handlerType = ev.EventHandlerType;
            ParameterInfo[] ps = handlerType.GetMethod("Invoke").GetParameters();
            Delegate d;
            if (ps.Length == 0)
            {
                d = Delegate.CreateDelegate(handlerType, onInvoke.Target, onInvoke.Method);
            }
            else
            {
                // 事件带参数（如 Action<AdError>），但我们的处理是无参 Action，忽略事件参数直接调用
                ParameterExpression p = Expression.Parameter(ps[0].ParameterType, "a");
                Expression call = (onInvoke.Target == null)
                    ? Expression.Call(onInvoke.Method)
                    : Expression.Call(Expression.Constant(onInvoke.Target), onInvoke.Method);
                d = Expression.Lambda(handlerType, call, p).Compile();
            }
            ev.AddEventHandler(target, d);
        }
        catch (Exception e)
        {
            AdLog($"[Ad] subscribe {eventName} failed: {e.Message}");
        }
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
            AdLog($"[Ad] resolve WX type failed: {e.Message}");
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
        catch (Exception e) { AdLog($"[Ad] create {typeName} failed: {e.Message}"); return null; }
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
        Expression body = (handler.Target == null)
            ? Expression.Call(handler.Method, Expression.Convert(p, typeof(object)))
            : Expression.Call(Expression.Constant(handler.Target), handler.Method, Expression.Convert(p, typeof(object)));
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
        Expression body = (handler.Target == null)
            ? Expression.Call(handler.Method, Expression.Convert(p0, typeof(object)), Expression.Convert(p1, typeof(object)))
            : Expression.Call(Expression.Constant(handler.Target), handler.Method, Expression.Convert(p0, typeof(object)), Expression.Convert(p1, typeof(object)));
        return Expression.Lambda(delegateType, body, p0, p1).Compile();
    }
}
