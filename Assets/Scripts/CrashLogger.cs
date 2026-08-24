#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// 崩溃日志器：把启动期所有日志 + 异常堆栈写入
//   /storage/emulated/0/Android/data/com.fuling.game/files/crash_log.txt
// 不连电脑也能定位"闪屏即退"问题。仅 Android 真机生效。
public class CrashLogger
{
    static string path;
    static bool ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad()
    {
        try
        {
            path = Path.Combine(Application.persistentDataPath, "crash_log.txt");
            ready = true;
            // 每次启动先清空，只保留最近一次的运行记录
            File.WriteAllText(path, "========== BOOT BeforeSceneLoad " + DateTime.Now + " ==========\n");
        }
        catch { ready = false; }

        Application.logMessageReceivedThreaded += OnLog;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        Log("========== BOOT AfterSceneLoad（当前场景: " +
            (SceneManager.GetActiveScene() != null ? SceneManager.GetActiveScene().name : "?") +
            "） " + DateTime.Now + " ==========");
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Log("[sceneLoaded] name=" + s.name + " mode=" + m);
    }

    static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        Log("UNHANDLED EXCEPTION: " + (e.ExceptionObject as Exception)?.ToString());
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        Log("[" + type + "] " + condition + "\n" + stackTrace);
    }

    static void Log(string s)
    {
        if (!ready) return;
        try { File.AppendAllText(path, s + "\n"); } catch { }
    }
}
#endif
