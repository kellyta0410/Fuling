using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI 面板")]
    public GameObject mainMenuPanel;
    public GameObject settingsPanel;
    public GameObject CreditsPanel;

    //public TextMeshProUGUI versionText;

    [Header("设置")]
    public UnityEngine.UI.Slider musicSlider;
    public UnityEngine.UI.Slider sfxSlider;

    [Header("场景名称")]
    public string difficultySelectionScene = "DifficultySelection";

    private void Start()
    {
        mainMenuPanel.SetActive(true);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (CreditsPanel != null)
            CreditsPanel.SetActive(false);

        // 设置版本号
        //if (versionText != null)
        //{
        //versionText.text = $"v{Application.version}";
        //}

        // 加载保存的音量设置
        LoadSettings();

        // 绑定事件
        BindUIEvents();
    }

    private void BindUIEvents()
    {
        // 音乐音量
        if (musicSlider != null)
        {
            musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        // 音效音量
        if (sfxSlider != null)
        {
            sfxSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        }
    }

    // ========== 按钮方法 ==========

    /// <summary>
    /// 开始游戏按钮 → 跳转到难度选择
    /// </summary>
    public void PlayGame()
    {
        // 播放音效（如果有AudioManager）
        // AudioManager.Instance?.PlaySFX("Click");

        Debug.Log("开始游戏 → 跳转到难度选择");
        SceneManager.LoadScene(difficultySelectionScene);
    }

    /// <summary>
    /// 设置按钮 → 显示设置面板
    /// </summary>
    public void OpenSettings()
    {
        // AudioManager.Instance?.PlaySFX("Click");
        if (settingsPanel != null)
            settingsPanel.SetActive(true);

    }

    public void OpenCredits()
    {
        // AudioManager.Instance?.PlaySFX("Click");
        if (CreditsPanel != null)
            CreditsPanel.SetActive(true);
    }

    /// <summary>
    /// 退出按钮
    /// </summary>
    public void QuitGame()
    {
        // AudioManager.Instance?.PlaySFX("Click");

        Debug.Log("退出游戏");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    /// <summary>
    /// 设置面板返回按钮
    /// </summary>
    public void SettingsBackButton()
    {
        // AudioManager.Instance?.PlaySFX("Click");

        // 保存设置
        SaveSettings();
    }

    /// <summary>
    /// 重置数据按钮
    /// </summary>
    //public void OnResetDataButtonClick()
    //{
        // AudioManager.Instance?.PlaySFX("Click");

        // 弹出确认对话框
        // 简单实现：直接删除所有PlayerPrefs
        //PlayerPrefs.DeleteAll();
        //PlayerPrefs.Save();

        //Debug.Log("所有数据已重置");

        // 可以显示一个提示
        // 例如：显示一个短暂的提示文字
    //}


    // ========== 音量设置 ==========

    private void LoadSettings()
    {
        // 从PlayerPrefs加载音量设置
        float musicVolume = PlayerPrefs.GetFloat("MusicVolume", 0.8f);
        float sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 0.8f);

        if (musicSlider != null)
            musicSlider.value = musicVolume;

        if (sfxSlider != null)
            sfxSlider.value = sfxVolume;

        // 应用到AudioManager（如果有）
        // AudioManager.Instance?.SetMusicVolume(musicVolume);
        // AudioManager.Instance?.SetSFXVolume(sfxVolume);
    }

    private void SaveSettings()
    {
        if (musicSlider != null)
            PlayerPrefs.SetFloat("MusicVolume", musicSlider.value);

        if (sfxSlider != null)
            PlayerPrefs.SetFloat("SFXVolume", sfxSlider.value);

        PlayerPrefs.Save();

        Debug.Log("设置已保存");
    }

    private void OnMusicVolumeChanged(float value)
    {
        // AudioManager.Instance?.SetMusicVolume(value);
        Debug.Log($"音乐音量: {value}");
    }

    private void OnSFXVolumeChanged(float value)
    {
        // AudioManager.Instance?.SetSFXVolume(value);
        Debug.Log($"音效音量: {value}");
    }
}