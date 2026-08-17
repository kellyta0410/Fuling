using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("UI 引用")]
    public Slider healthSlider;
    public GameObject gameOverPanel;
    public GameObject settingsPanel;
    public GameObject pausemenu;
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI enemyCountText;
    public TextMeshProUGUI finalCoinText;
    public TextMeshProUGUI finalKillText;
    public Button restartButton;

    [Header("计时器 UI")]
    public TextMeshProUGUI timerText;

    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;
    public Image healthFillImage;

    [Header("InGame 设置 - 音量控制")]
    public Slider musicSlider;
    public Slider sfxSlider;
    [Tooltip("拖动 SFX 音量滑块松手后播放一次确认音（不填会自动生成一个短促提示音）")]
    public AudioClip sfxSliderConfirmClip;

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner enemySpawner;
    public InfiniteEnemySpawner infiniteSpawner;
    public GameManager gameManager;

    [Header("Buff 提示")]
    [Tooltip("获得 Buff 时的提示文字（可不拖，运行时自动创建）")]
    public TextMeshProUGUI buffToastText;
    private Coroutine buffToastCoroutine;

    [Header("GameOver 复活")]
    [Tooltip("看广告复活面板（在场景里摆好，含两个按钮）")]
    public GameObject revivePanel;
    [Tooltip("「观看广告」按钮")]
    public Button reviveButton;
    [Tooltip("「结束游戏」按钮（进入 GameOver 结算）")]
    public Button reviveEndButton;

    private Vector2 timerOriginalPosition;
    private float timeLimit;
    private Coroutine sfxSliderConfirmCoroutine;
    private AudioClip generatedConfirmClip;
    private bool suppressSFXConfirmSound;

    // 计时器模式
    private bool isTimerMode = false;
    private float elapsedTime = 0f;

    void Start()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (enemySpawner == null) enemySpawner = FindObjectOfType<EnemySpawner>();
        if (infiniteSpawner == null) infiniteSpawner = FindObjectOfType<InfiniteEnemySpawner>();
        if (gameManager == null) gameManager = GameManager.Instance;

        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (healthSlider != null && healthFillImage == null)
        {
            Transform fill = healthSlider.transform.Find("Fill Area/Fill");
            healthFillImage = fill != null ? fill.GetComponent<Image>() : healthSlider.GetComponentInChildren<Image>();
        }

        if (timerText != null) timerOriginalPosition = timerText.rectTransform.anchoredPosition;

        if (gameManager != null)
        {
            gameManager.OnTimerUpdated += UpdateTimerUI;
            gameManager.OnTimerVisibilityChanged += SetTimerVisibility;
            gameManager.OnGameOver += OnGameOver;
        }

        LoadSettings();

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);

        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();

        // 检测是否为无限模式
        if (gameManager != null && gameManager.IsInfiniteMode())
        {
            isTimerMode = true;
            elapsedTime = 0f;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
                timerText.text = "00:00";
                timerText.color = Color.white;
            }
        }

        if (player == null || player.GetHealthPercent() <= 0)
        {
            StartCoroutine(DelayedUIUpdate());
        }
    }

    void Update()
    {
        UpdateKillUI();

        // 无限模式：手动更新计时器
        if (isTimerMode && timerText != null && timerText.gameObject.activeSelf)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerDisplay(elapsedTime);
        }

        if (Input.GetKeyDown(KeyCode.R)) RestartGame();
    }

    IEnumerator DelayedUIUpdate()
    {
        yield return null;
        yield return null;

        if (player == null) player = FindObjectOfType<PlayerController>();

        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();

        Debug.Log("✅ UIManager: 延迟更新完成");
    }

    void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnTimerUpdated -= UpdateTimerUI;
            gameManager.OnTimerVisibilityChanged -= SetTimerVisibility;
            gameManager.OnGameOver -= OnGameOver;
        }
    }

    public void SetTimerMode(bool isInfinite)
    {
        isTimerMode = isInfinite;

        if (isInfinite)
        {
            elapsedTime = 0f;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
                timerText.text = "00:00";
                timerText.color = Color.white;
            }
        }
        else
        {
            if (timerText != null)
            {
                timerText.color = Color.white;
            }
        }
    }

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
            return;
        }

        // 场景中没有 SettingsManager（如直接播放游戏场景调试）时，直接读 PlayerPrefs
        float musicV = SettingsManager.GetMusicVolumeStatic();
        float sfxV = SettingsManager.GetSFXVolumeStatic();

        suppressSFXConfirmSound = true;
        if (musicSlider != null) musicSlider.value = musicV;
        if (sfxSlider != null) sfxSlider.value = sfxV;
        suppressSFXConfirmSound = false;

        //UpdateVolumeTexts();
    }

    //void UpdateVolumeTexts()
    //{
        //if (musicValueText != null && musicSlider != null)
           // musicValueText.text = $"{Mathf.RoundToInt(musicSlider.value * 100)}%";
        
        //if (sfxValueText != null && sfxSlider != null)
            //sfxValueText.text = $"{Mathf.RoundToInt(sfxSlider.value * 100)}%";
    //}

    void OnMusicSliderChanged(float value)
    {
        SettingsManager.SetMusicVolumeStatic(value);
        //UpdateVolumeTexts();
    }

    void OnSFXSliderChanged(float value)
    {
        SettingsManager.SetSFXVolumeStatic(value);

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

    public void OpenPauseMenu()
    {
        if (pausemenu != null)
        {
            pausemenu.SetActive(true);
            Time.timeScale = 0f;

            if (player != null)
            {
                player.SetJoystickEnabled(false);
            }
        }
    }

    public void ClosePauseMenu()
    {
        if (pausemenu != null)
        {
            pausemenu.SetActive(false);
            Time.timeScale = 1f;

            if (player != null)
            {
                player.SetJoystickEnabled(true);
            }
        }
    }

    public void OpenSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadSettings();
        }
    }

    public void CloseSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            Time.timeScale = 1f;
        }

    }

    public void UpdateHealthUI()
    {
        if (player != null && healthSlider != null)
        {
            float percent = player.GetHealthPercent();
            healthSlider.value = percent;
            UpdateHealthBarColor(percent);
        }
    }

    void UpdateHealthBarColor(float percent)
    {
        if (healthFillImage == null) return;
        healthFillImage.color = percent >= 0.6f ? fullHealthColor : (percent >= 0.3f ? midHealthColor : lowHealthColor);
    }

    public void UpdateCoinUI()
    {
        if (player != null && coinText != null)
            coinText.text = $"{player.GetCoins()}";
    }

    public void UpdateKillUI()
    {
        if (enemyCountText != null && player != null)
        {
            // 显示击杀数
            enemyCountText.text = $"{player.GetKills()}";
        }
    }

    public void SetTimerVisibility(bool visible)
    {
        if (timerText == null) return;

        // 无限模式：始终显示计时器
        if (isTimerMode)
        {
            timerText.gameObject.SetActive(true);
            return;
        }

        timerText.gameObject.SetActive(visible);
    }

    // 倒计时更新（普通模式用）
    public void UpdateTimerUI(float remaining, float limit)
    {
        if (timerText == null) return;

        // 如果是无限模式，不处理倒计时
        if (isTimerMode) return;

        timeLimit = limit;
        int min = Mathf.FloorToInt(remaining / 60);
        int sec = Mathf.FloorToInt(remaining % 60);
        timerText.text = $"{min:00}:{sec:00}";

        float percent = limit > 0 ? remaining / limit : 0;

        if (percent < 0.1f && percent > 0)
        {
            timerText.color = new Color(1f, 0.2f, 0.2f, 1f);
            float shake = 3f;
            timerText.rectTransform.anchoredPosition = timerOriginalPosition + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
        }
        else if (percent < 0.3f && percent > 0)
        {
            timerText.color = Color.yellow;
            timerText.rectTransform.anchoredPosition = timerOriginalPosition;
        }
        else
        {
            timerText.color = Color.white;
            timerText.rectTransform.anchoredPosition = timerOriginalPosition;
        }

        if (remaining <= 0)
        {
            timerText.color = new Color(1f, 0.2f, 0.2f, 1f);
            timerText.text = "00:00";
        }
    }

    // 正计时显示（无限模式用）
    void UpdateTimerDisplay(float time)
    {
        if (timerText == null) return;

        int min = Mathf.FloorToInt(time / 60);
        int sec = Mathf.FloorToInt(time % 60);
        timerText.text = $"{min:00}:{sec:00}";

        timerText.color = Color.white;
    }

    // ==================== GameOver ====================

    public void OnGameOver() => ShowGameOver();

    public void ShowGameOver()
    {
        if (gameOverPanel == null) return;

        if (settingsPanel != null) settingsPanel.SetActive(false);
        gameOverPanel.SetActive(true);

        int coins = player != null ? player.GetCoins() : 0;
        int kills = player != null ? player.GetKills() : 0;

        string timeText = "";
        if (isTimerMode)
        {
            int min = Mathf.FloorToInt(elapsedTime / 60);
            int sec = Mathf.FloorToInt(elapsedTime % 60);
            timeText = $"\nTime: {min:00}:{sec:00}";
        }

        if (finalCoinText != null) finalCoinText.text = $"{coins}";
        if (finalKillText != null) finalKillText.text = $"{kills}{timeText}";

        Time.timeScale = 0f;

        if (player != null)
        {
            player.SetJoystickEnabled(false);
        }
    }

    public void HideGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        Time.timeScale = 1f;

        if (player != null)
        {
            player.SetJoystickEnabled(true);
        }
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitToGameOver()
    {
        if (pausemenu != null) pausemenu.SetActive(false);
        if (gameManager != null) gameManager.GameOver(false);
        else ShowGameOver();
    }

    public void GoToSelection()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Selection");
    }

    public void OnPlayerDamaged() => UpdateHealthUI();
    public void OnPlayerCoinChanged() => UpdateCoinUI();
    public void OnPlayerKillChanged() => UpdateKillUI();

    // ==================== 死亡 / 复活 ====================

    /// <summary>
    /// 玩家死亡后的入口：第一次死亡可看广告复活，否则直接进 GameOver 结算。
    /// </summary>
    public void HandlePlayerDied(PlayerController diedPlayer)
    {
        if (gameManager != null && gameManager.CanRevive())
        {
            ShowRevivePanel();
        }
        else
        {
            ShowGameOver();
            if (gameManager != null)
            {
                gameManager.GameOver(false);
            }
        }
    }

    // ==================== 复活面板 ====================

    public void ShowRevivePanel()
    {
        if (revivePanel == null) return;

        revivePanel.SetActive(true);
        Time.timeScale = 0f;

        if (player != null)
        {
            player.SetJoystickEnabled(false);
        }
    }

    public void HideRevivePanel()
    {
        if (revivePanel != null) revivePanel.SetActive(false);
        Time.timeScale = 1f;

        if (player != null)
        {
            player.SetJoystickEnabled(true);
        }
    }

    /// <summary>「观看广告」按钮</summary>
    public void OnReviveWatchAd()
    {
        RewardVideoAdService.ShowRewardedAd((watchedFull) =>
        {
            if (watchedFull)
            {
                DoRevive();
            }
            else
            {
                // 广告未看完/失败：不复活，直接结算
                HandleReviveDecline();
            }
        });
    }

    /// <summary>「结束游戏」按钮（进入 GameOver 结算）</summary>
    public void OnReviveEndGame()
    {
        HandleReviveDecline();
    }

    void DoRevive()
    {
        HideRevivePanel();

        if (gameManager != null)
        {
            gameManager.MarkReviveUsed();
        }

        if (player != null)
        {
            player.ReviveInPlace();
        }
    }

    void HandleReviveDecline()
    {
        HideRevivePanel();

        ShowGameOver();
        if (gameManager != null)
        {
            gameManager.GameOver(false);
        }
    }

    /// <summary>复活完成后由 PlayerController 调用</summary>
    public void OnPlayerRevived()
    {
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();
    }

    // ==================== Buff 提示 ====================

    public void ShowBuffToast(string message)
    {
        TextMeshProUGUI toast = GetBuffToast();
        if (toast == null) return;

        toast.gameObject.SetActive(true);
        toast.text = message;
        toast.color = Color.white;

        if (buffToastCoroutine != null) StopCoroutine(buffToastCoroutine);
        buffToastCoroutine = StartCoroutine(HideBuffToast(2f));
    }

    IEnumerator HideBuffToast(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (buffToastText != null)
        {
            buffToastText.gameObject.SetActive(false);
        }
        buffToastCoroutine = null;
    }

    TextMeshProUGUI GetBuffToast()
    {
        if (buffToastText != null) return buffToastText;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return null;

        // 复用场景中已有文本的字体（华文行楷），保证中文正常显示
        TMP_FontAsset font = null;
        if (coinText != null) font = coinText.font;
        else if (timerText != null) font = timerText.font;
        else if (finalCoinText != null) font = finalCoinText.font;

        GameObject obj = new GameObject("BuffToast");
        obj.transform.SetParent(canvas.transform, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.85f);
        rt.anchorMax = new Vector2(0.5f, 0.85f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(800, 80);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 40;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.yellow;
        if (font != null) tmp.font = font;
        else tmp.font = TMP_Settings.defaultFontAsset;

        buffToastText = tmp;
        return tmp;
    }
}