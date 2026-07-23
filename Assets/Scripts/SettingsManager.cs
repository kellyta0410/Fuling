using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance;

    [Header("默认值")]
    public float defaultMusicVolume = 0.8f;
    public float defaultSFXVolume = 0.8f;

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
        }
    }

    public float GetMusicVolume()
    {
        return PlayerPrefs.GetFloat("MusicVolume", defaultMusicVolume);
    }

    public float GetSFXVolume()
    {
        return PlayerPrefs.GetFloat("SFXVolume", defaultSFXVolume);
    }

    public void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat("MusicVolume", value);
        PlayerPrefs.Save();
        Debug.Log($"🎵 音乐音量: {Mathf.RoundToInt(value * 100)}%");
    }

    public void SetSFXVolume(float value)
    {
        PlayerPrefs.SetFloat("SFXVolume", value);
        PlayerPrefs.Save();
        Debug.Log($"🔊 音效音量: {Mathf.RoundToInt(value * 100)}%");
    }

    public void ResetAllData()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("🗑️ 所有数据已重置");
    }
}