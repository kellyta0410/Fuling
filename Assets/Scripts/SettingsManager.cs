using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance;

    [Header("默认值")]
    public float defaultMusicVolume = 0.8f;
    public float defaultSFXVolume = 0.8f;

    // 缓存音量：避免每帧读 PlayerPrefs
    private float cachedMusicVolume;
    private float cachedSFXVolume;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        cachedMusicVolume = PlayerPrefs.GetFloat("MusicVolume", defaultMusicVolume);
        cachedSFXVolume = PlayerPrefs.GetFloat("SFXVolume", defaultSFXVolume);
    }

    public float GetMusicVolume()
    {
        return cachedMusicVolume;
    }

    public float GetSFXVolume()
    {
        return cachedSFXVolume;
    }

    public static float GetMusicVolumeStatic()
    {
        return PlayerPrefs.GetFloat("MusicVolume", 0.8f);
    }

    public static float GetSFXVolumeStatic()
    {
        return PlayerPrefs.GetFloat("SFXVolume", 0.8f);
    }

    public void SetMusicVolume(float value)
    {
        SetMusicVolumeStatic(value);
    }

    public void SetSFXVolume(float value)
    {
        SetSFXVolumeStatic(value);
    }

    // 静态写入：不依赖单例是否在场景中。
    // 主菜单场景之外（直接开游戏场景调试 / 跨场景）SettingsManager 可能不在场，
    // 但 AudioManager 播放 SFX 时直接读 PlayerPrefs，所以这里必须保证始终写入 PlayerPrefs。
    public static void SetMusicVolumeStatic(float value)
    {
        PlayerPrefs.SetFloat("MusicVolume", value);
        PlayerPrefs.Save();
        if (Instance != null) Instance.cachedMusicVolume = value;
    }

    public static void SetSFXVolumeStatic(float value)
    {
        PlayerPrefs.SetFloat("SFXVolume", value);
        PlayerPrefs.Save();
        if (Instance != null) Instance.cachedSFXVolume = value;
    }

    public void ResetAllData()
    {
        cachedMusicVolume = defaultMusicVolume;
        cachedSFXVolume = defaultSFXVolume;
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("🗑️ 所有数据已重置");
    }
}