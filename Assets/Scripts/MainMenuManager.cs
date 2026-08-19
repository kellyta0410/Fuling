using System.Collections;
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
    [Tooltip("主菜单的'剧情'按钮：新玩家看完剧情后才显示/解锁（没填则不处理）")]
    public GameObject storyButton;

    [Header("设置 - 音量控制")]
    public Slider musicSlider;
    public Slider sfxSlider;
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;
    [Tooltip("拖动 SFX 音量滑块松手后播放一次确认音（不填会自动生成一个短促提示音）")]
    public AudioClip sfxSliderConfirmClip;

    private Coroutine sfxSliderConfirmCoroutine;
    private AudioClip generatedConfirmClip;
    private bool suppressSFXConfirmSound;

    [Header("场景名称")]
    public string difficultySelectionScene = "DifficultySelection";
    public string storySceneName = "Story";

    // ⭐ 按钮点击音大概多长：切场景 / 退出前先等这么久，让 AudioSource.Play 播完再动
    private const float buttonClickDelay = 0.2f;

    private void Start()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (creditsPanel != null)
            creditsPanel.SetActive(false);

        // ⭐ 剧情按钮：看过剧情（StoryFinished==1）才显示；新玩家隐藏，看完剧情解锁
        if (storyButton != null)
        {
            bool storyDone = PlayerPrefs.GetInt(StorySceneManager.StoryFinishedKey, 0) == 1;
            storyButton.SetActive(storyDone);
        }

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
        if (SettingsManager.Instance != null)
        {
            float music = SettingsManager.Instance.GetMusicVolume();
            float sfx = SettingsManager.Instance.GetSFXVolume();

            suppressSFXConfirmSound = true;
            if (musicSlider != null) musicSlider.value = music;
            if (sfxSlider != null) sfxSlider.value = sfx;
            suppressSFXConfirmSound = false;

            UpdateVolumeTexts();
            return;
        }

        // 场景中没有 SettingsManager 时，直接读 PlayerPrefs
        float musicV = SettingsManager.GetMusicVolumeStatic();
        float sfxV = SettingsManager.GetSFXVolumeStatic();

        suppressSFXConfirmSound = true;
        if (musicSlider != null) musicSlider.value = musicV;
        if (sfxSlider != null) sfxSlider.value = sfxV;
        suppressSFXConfirmSound = false;

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
        SettingsManager.SetMusicVolumeStatic(value);
        UpdateVolumeTexts();
    }

    void OnSFXSliderChanged(float value)
    {
        SettingsManager.SetSFXVolumeStatic(value);
        UpdateVolumeTexts();

        // 程序加载/刷新滑块值时不要播确认音，只响应用户拖动
        if (suppressSFXConfirmSound) return;

        // 松手后播放一次确认音，让玩家听到当前 SFX 音量
        if (sfxSliderConfirmCoroutine != null) StopCoroutine(sfxSliderConfirmCoroutine);
        sfxSliderConfirmCoroutine = StartCoroutine(PlaySFXConfirmAfterDelay(0.25f));
    }

    IEnumerator PlaySFXConfirmAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        sfxSliderConfirmCoroutine = null;
        AudioManager.Instance?.PlaySFX(GetConfirmClip());
    }

    AudioClip GetConfirmClip()
    {
        if (sfxSliderConfirmClip != null) return sfxSliderConfirmClip;
        if (generatedConfirmClip == null) generatedConfirmClip = CreateClickClip();
        return generatedConfirmClip;
    }

    AudioClip CreateClickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.08f;
        int length = (int)(sampleRate * duration);
        AudioClip clip = AudioClip.Create("SliderClick", length, 1, sampleRate, false);
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float decay = 1f - t / duration;
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * decay * 0.4f;
        }
        clip.SetData(samples, 0);
        return clip;
    }

    // ==================== 按钮方法 ====================

    public void PlayGame()
    {
        // ⭐ 老玩家（看过剧情）直接去选关；新玩家先看剧情，剧情里点"继续"再去选关
        bool storyDone = PlayerPrefs.GetInt(StorySceneManager.StoryFinishedKey, 0) == 1;
        if (storyDone)
        {
            Debug.Log("开始游戏 → 跳转到选关");
            StartCoroutine(LoadSceneAfterButtonClick(difficultySelectionScene));
        }
        else
        {
            Debug.Log("开始游戏 → 新玩家先看剧情");
            StartCoroutine(LoadSceneAfterButtonClick(storySceneName));
        }
    }

    // ⭐ 主菜单"剧情"按钮：随时可以重看剧情（仅看完剧情后可见）
    public void StoryButton()
    {
        StartCoroutine(LoadSceneAfterButtonClick(storySceneName));
    }

    // ⭐ 等 0.2s 让按钮点击音放完再切场景，否则 AudioSource.Play 一启动就被场景卸载掐断
    IEnumerator LoadSceneAfterButtonClick(string sceneName)
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
        SceneManager.LoadScene(sceneName);
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
        StartCoroutine(QuitAfterButtonClick());
    }

    // ⭐ 等 0.2s 让按钮点击音放完再退出，否则点完立刻退听不到声音
    IEnumerator QuitAfterButtonClick()
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}