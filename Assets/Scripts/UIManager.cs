using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class UIManager : MonoBehaviour
{
    // ⭐ 按钮点击音播放时长：切场景前等这么久，让 AudioSource.Play 播完
    private const float buttonClickDelay = 0.2f;

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

    [Header("按钮点击音")]
    [Tooltip("按钮点击音（不填时回退用 sfxSliderConfirmClip，再没有就生成短提示音）")]
    public AudioClip clickSFX;

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner enemySpawner;
    public InfiniteEnemySpawner infiniteSpawner;
    public GameManager gameManager;

    [Header("Buff 提示")]
    [Tooltip("获得 Buff 时的提示文字（可不拖，运行时自动创建）")]
    public TextMeshProUGUI buffToastText;
    private Queue<string> buffToastQueue = new Queue<string>();
    private bool buffToastShowing = false;

    [Header("广告流程屏幕调试（免设备日志）")]
    private TextMeshProUGUI adDebugText;
    private string adDebugBuffer = "";

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

        // ⭐ 广告流程屏幕调试：把 [广告] 日志直接显示在手机上，免去看设备日志
        SetupAdDebugOverlay();
        RewardVideoAdService.OnAdLog += AppendAdDebug;
    }

    // ==================== 广告流程屏幕调试 ====================
    void SetupAdDebugOverlay()
    {
        // ⭐ 专用顶层 Canvas：sortingOrder 拉到最高并 overrideSorting，确保压在复活面板/结算面板等所有 UI 之上
        GameObject cgo = new GameObject("AdDebugCanvas");
        Canvas canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 9999;
        cgo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // 容器铺满整个 Canvas，子物体才能正确按屏幕四角定位
        GameObject go = new GameObject("AdDebugOverlay");
        go.transform.SetParent(canvas.transform, false);
        RectTransform goRt = go.AddComponent<RectTransform>();
        goRt.anchorMin = Vector2.zero;
        goRt.anchorMax = Vector2.one;
        goRt.offsetMin = Vector2.zero;
        goRt.offsetMax = Vector2.zero;

        // 半透明黑底，保证任何背景下都看得清
        GameObject bg = new GameObject("AdDebugBg");
        bg.transform.SetParent(go.transform, false);
        Image img = bg.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.78f);
        img.raycastTarget = false;
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0, 1); bgRt.anchorMax = new Vector2(0, 1);
        bgRt.pivot = new Vector2(0, 1); bgRt.anchoredPosition = new Vector2(0, 0);
        bgRt.sizeDelta = new Vector2(460, 300);

        adDebugText = go.AddComponent<TextMeshProUGUI>();
        // 用 TMP 默认字体（含英文/ASCII），调试文字一律用英文，避免中文字形缺失导致空白
        adDebugText.alignment = TextAlignmentOptions.TopLeft;
        adDebugText.fontSize = 24;
        adDebugText.color = Color.white;
        adDebugText.outlineWidth = 0f;   // 不加描边，避免黑边把白字吞成黑色
        adDebugText.raycastTarget = false;
        adDebugText.text = "[AdDebug v6 ONLINE]\n(overlay active)";
        RectTransform rt = adDebugText.rectTransform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(8, -8);
        rt.sizeDelta = new Vector2(444, 284);
    }

    void AppendAdDebug(string line)
    {
        if (adDebugText == null) return;
        adDebugBuffer = (adDebugBuffer + "\n" + line).Trim('\n');
        string[] lines = adDebugBuffer.Split('\n');
        if (lines.Length > 11)
            adDebugBuffer = string.Join("\n", lines, lines.Length - 11, 11);
        adDebugText.text = adDebugBuffer;
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
        RewardVideoAdService.OnAdLog -= AppendAdDebug;
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

    // 按钮点击音：走 AudioManager 播放池（与其它 SFX 同一音量体系）。
    // Easy/Medium 的按钮已在场景里通过 AudioSource.Play 接线点击音，这里只用于
    // 没有场景级 AudioSource 的场景（如 Infinite），避免重复播放。
    public void PlayClickSFX()
    {
        AudioClip clip = clickSFX != null ? clickSFX : GetConfirmClip();
        AudioManager.Instance?.PlaySFX(clip);
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
        StartCoroutine(ReloadSceneAfterButtonClick());
    }

    // ⭐ 等 0.2s 让按钮点击音放完再重开场景
    IEnumerator ReloadSceneAfterButtonClick()
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
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
        StartCoroutine(LoadSceneAfterButtonClick("Selection"));
    }

    // ⭐ 等 0.2s 让按钮点击音放完再切场景
    IEnumerator LoadSceneAfterButtonClick(string sceneName)
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
        SceneManager.LoadScene(sceneName);
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

        // 复活面板出现时确保广告已预拉好（秒出，避免等待期间点"结束游戏"放不出广告）
        RewardVideoAdService.PreloadRewardedAd();

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
        RewardVideoAdService.ShowRewardedAd(
            (watchedFull) =>
            {
                if (watchedFull)
                {
                    DoRevive();
                }
                else
                {
                    // 广告未看完/中途关闭：不复活，直接结算
                    HandleReviveDecline();
                }
            },
            (reason) =>
            {
                // 离线 / 无广告可用：弹提示（带具体原因）并留在复活面板，不直接结算
                ShowBuffToast("无法播放复活广告：" + reason);
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
        if (string.IsNullOrEmpty(message)) return;

        // 多条提示排队，按顺序逐条显示，避免同时捡到多个 buff 时只显示最后一条
        buffToastQueue.Enqueue(message);
        if (!buffToastShowing) StartCoroutine(ProcessBuffToastQueue());
    }

    IEnumerator ProcessBuffToastQueue()
    {
        buffToastShowing = true;
        while (buffToastQueue.Count > 0)
        {
            string message = buffToastQueue.Dequeue();
            TextMeshProUGUI toast = GetBuffToast();
            if (toast != null)
            {
                toast.gameObject.SetActive(true);
                toast.text = message;
                toast.color = Color.white;
            }
            yield return new WaitForSecondsRealtime(1f);
        }
        if (buffToastText != null) buffToastText.gameObject.SetActive(false);
        buffToastShowing = false;
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