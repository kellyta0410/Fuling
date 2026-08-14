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

    public void SetMusicVolume(float value)
    {
        cachedMusicVolume = value;
        PlayerPrefs.SetFloat("MusicVolume", value);
        PlayerPrefs.Save();
        Debug.Log($"🎵 音乐音量: {Mathf.RoundToInt(value * 100)}%");
    }

    public void SetSFXVolume(float value)
    {
        cachedSFXVolume = value;
        PlayerPrefs.SetFloat("SFXVolume", value);
        PlayerPrefs.Save();
        Debug.Log($"🔊 音效音量: {Mathf.RoundToInt(value * 100)}%");
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