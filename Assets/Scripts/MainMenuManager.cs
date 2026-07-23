using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuManager : MonoBehaviour
{
    [Header("UI 面板")]
    public GameObject mainMenuPanel;
    public GameObject settingsPanel;
    public GameObject creditsPanel;

    [Header("设置 - 音量控制")]
    public Slider musicSlider;
    public Slider sfxSlider;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;

    [Header("场景名称")]
    public string difficultySelectionScene = "DifficultySelection";

    private void Start()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (creditsPanel != null)
            creditsPanel.SetActive(false);

        // 加载音量设置
        LoadSettings();

        // 绑定滑块事件
        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);
    }

    // ==================== 加载/保存设置 ====================

    void LoadSettings()
    {
        if (SettingsManager.Instance == null) return;

        float music = SettingsManager.Instance.GetMusicVolume();
        float sfx = SettingsManager.Instance.GetSFXVolume();

        if (musicSlider != null) musicSlider.value = music;
        if (sfxSlider != null) sfxSlider.value = sfx;

        UpdateVolumeTexts();
    }

    void UpdateVolumeTexts()
    {
        if (musicValueText != null && musicSlider != null)
            musicValueText.text = $"{Mathf.RoundToInt(musicSlider.value * 100)}%";

        if (sfxValueText != null && sfxSlider != null)
            sfxValueText.text = $"{Mathf.RoundToInt(sfxSlider.value * 100)}%";
    }

    void OnMusicSliderChanged(float value)
    {
        SettingsManager.Instance?.SetMusicVolume(value);
        UpdateVolumeTexts();
    }

    void OnSFXSliderChanged(float value)
    {
        SettingsManager.Instance?.SetSFXVolume(value);
        UpdateVolumeTexts();
    }

    // ==================== 按钮方法 ====================

    public void PlayGame()
    {
        Debug.Log("开始游戏 → 跳转到难度选择");
        SceneManager.LoadScene(difficultySelectionScene);
    }

    public void OpenSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadSettings();  // 刷新当前值
        }
    }

    public void CloseSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    public void OpenCredits()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(true);
    }

    public void CloseCredits()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(false);
    }

    public void QuitGame()
    {
        Debug.Log("退出游戏");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}