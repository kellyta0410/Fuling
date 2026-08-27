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
    public GameManager gameManager;

    [Header("Buff 提示")]
    [Tooltip("获得 Buff 时的提示文字（可不拖，运行时自动创建）")]
    public TextMeshProUGUI buffToastText;

    [Header("商店面板")]
    public GameObject shopPanel;
    public GameObject shopButtonPrefab;
    public Transform shopButtonContainer;
    [Header("Buff 图标")]
    public Transform buffIconContainer;
    public GameObject buffIconPrefab;
    private Queue<string> buffToastQueue = new Queue<string>();
    private bool buffToastShowing = false;

    [Header("GameOver 复活")]
    [Tooltip("看广告复活面板（在场景里摆好，含两个按钮）")]
    public GameObject revivePanel;
    [Tooltip("复活面板下手动摆放的提示子物体（背景 Image + TextMeshProUGUI），做在 hierarchy 里而非代码自动生成；不拖则回退到自动居中 Toast")]
    public GameObject reviveHint;
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

        // 检测模式：地牢模式显示房间数；无限模式显示计时
        if (gameManager != null && gameManager.IsDungeonMode())
        {
            isTimerMode = false;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
                timerText.text = "房间 1";
                timerText.color = Color.white;
            }
        }
        else if (gameManager != null && gameManager.IsInfiniteMode())
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

    // 地牢模式：把计时器文本改为“第 N 关”（阿拉伯数字）；商店房显示为“商店”（不计入关序号）
    public void SetRoomDisplay(int level, bool isShop = false)
    {
        if (timerText == null) return;
        timerText.gameObject.SetActive(true);
        timerText.text = isShop ? "商店" : ("第" + level + "关");
        timerText.color = Color.white;
    }

    // 整数转中文（1-99 正常中文，≥100 用阿拉伯数字兜底，避免无限地牢数字过长）
    private string ToChineseNumber(int n)
    {
        if (n <= 0) return n.ToString();
        string[] d = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
        if (n < 10) return d[n];
        if (n == 10) return "十";
        if (n < 20) return "十" + d[n - 10];
        if (n < 100)
        {
            int tens = n / 10;
            int ones = n % 10;
            return ones == 0 ? d[tens] + "十" : d[tens] + "十" + d[ones];
        }
        return n.ToString();
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

        // 地牢模式：计时器位置被用来显示房间数，始终可见
        if (GameManager.Instance != null && GameManager.Instance.IsDungeonMode())
        {
            timerText.gameObject.SetActive(true);
            return;
        }

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

        // 地牢（无尽）模式：结算界面的"金币"位置改为显示"通关房间数量"
        if (gameManager != null && gameManager.IsDungeonMode())
        {
            int rooms = gameManager.GetRoomsCleared();
            if (finalCoinText != null) finalCoinText.text = $"{rooms}";
            if (finalKillText != null) finalKillText.text = $"{kills}";
        }
        else
        {
            if (finalCoinText != null) finalCoinText.text = $"{coins}";
            if (finalKillText != null) finalKillText.text = $"{kills}{timeText}";
        }

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
        // PC 版（导出的 Windows 构建）不提供复活界面，死亡直接进结算；
        // 编辑器内仍保留复活流程，方便测试。
        if (!Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer)
        {
            ShowGameOver();
            if (gameManager != null) gameManager.GameOver(false);
            return;
        }

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
                // 离线 / 无广告可用：弹提示（带具体原因）并留在复活面板，不直接结算。
                // 优先用 RevivePanel 下挂的提示子物体 ReviveHint（hierarchy 配置，复制自选择场景 hint），否则回退自动居中 Toast。
                if (reviveHint == null && revivePanel != null)
                    reviveHint = revivePanel.transform.Find("ReviveHint")?.gameObject;
                if (reviveHint != null)
                {
                    var hintText = reviveHint.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (hintText != null)
                    {
                        HintToast.Show("无法播放复活广告：" + reason, textOverride: hintText, panelOverride: reviveHint);
                        return;
                    }
                }
                HintToast.Show("无法播放复活广告：" + reason);
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

    // ==================== 商店 ====================
    // ⭐ 规则：列出商店池内所有 Buff；每种最多叠 maxStack 层（默认5）；
    // 价格随下一层递进：30 / 65 / 105 / 150 / 200；即时恢复(Heal)按各自 shopCost。
    private List<BuffDataSO> shopPool = new List<BuffDataSO>();
    private int shopCost = 0;

    public UnityEngine.UI.Button shopRefreshButton;   // 新系统已停用（可留空）
    public UnityEngine.UI.Button shopCloseButton;     // 关闭按钮（Inspector 拖入商店面板）

    int CostForLayer(int layer)
    {
        switch (layer)
        {
            case 1: return 30;
            case 2: return 65;
            case 3: return 105;
            case 4: return 150;
            case 5: return 200;
            default: return 200;
        }
    }

    public void OpenShop(List<BuffDataSO> pool, int cost)
    {
        if (shopPanel == null || shopButtonPrefab == null || shopButtonContainer == null)
        {
            Debug.LogWarning("UIManager: 商店面板未配置（shopPanel / shopButtonPrefab / shopButtonContainer）");
            return;
        }
        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning("UIManager: 商店池为空，无法开商店");
            return;
        }
        shopPool = pool;

        if (shopRefreshButton != null) shopRefreshButton.gameObject.SetActive(false);
        if (shopCloseButton != null)
        {
            shopCloseButton.onClick.RemoveAllListeners();
            shopCloseButton.onClick.AddListener(CloseShop);
        }

        BuildShopButtons();
        shopPanel.SetActive(true);
    }

    private void BuildShopButtons()
    {
        foreach (Transform c in shopButtonContainer) Destroy(c.gameObject);
        for (int i = 0; i < shopPool.Count; i++)
        {
            GameObject btn = Instantiate(shopButtonPrefab, shopButtonContainer);
            var icon = btn.transform.Find("Icon");
            if (icon != null && shopPool[i].icon != null)
                icon.GetComponent<UnityEngine.UI.Image>().sprite = shopPool[i].icon;
            int index = i;
            var button = btn.GetComponent<UnityEngine.UI.Button>();
            if (button != null) button.onClick.AddListener(() => BuyBuff(index));
            UpdateShopRow(btn, shopPool[i]);
        }
    }

    // 刷新单行的名称/层数/价格/可购买状态
    private void UpdateShopRow(GameObject btn, BuffDataSO buff)
    {
        var nameT = btn.transform.Find("Name");
        var costT = btn.transform.Find("Cost");
        var button = btn.GetComponent<UnityEngine.UI.Button>();
        var img = btn.GetComponent<UnityEngine.UI.Image>();

        bool isHeal = buff.isInstantEffect;
        BuffHandler bh = FindObjectOfType<BuffHandler>();
        int stack = isHeal ? 0 : (bh != null ? bh.GetStack(buff.buffType) : 0);
        int price = isHeal ? buff.shopCost : CostForLayer(stack + 1);

        if (nameT != null)
        {
            var t = nameT.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = buff.buffName + (isHeal ? "" : $"  ({stack}/{buff.maxStack})");
        }
        if (costT != null)
        {
            var t = costT.GetComponent<TextMeshProUGUI>();
            if (t != null) t.text = (isHeal || stack < buff.maxStack) ? price.ToString() : "已满级";
        }

        int coins = player != null ? player.GetCoins() : 0;
        bool canBuy = isHeal ? (coins >= price) : (stack < buff.maxStack && coins >= price);
        if (button != null) button.interactable = canBuy;
        if (img != null) img.color = canBuy ? Color.white : new Color(1f, 1f, 1f, 0.4f);
    }

    public void BuyBuff(int index)
    {
        if (index < 0 || index >= shopPool.Count) return;
        BuffDataSO buff = shopPool[index];
        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;
        BuffHandler bh = pc.GetComponent<BuffHandler>();
        if (bh == null) return;

        bool isHeal = buff.isInstantEffect;
        int stack = isHeal ? 0 : bh.GetStack(buff.buffType);
        int price = isHeal ? buff.shopCost : CostForLayer(stack + 1);

        if (!isHeal && stack >= buff.maxStack)
        {
            ShowBuffToast("已满级");
            return;
        }
        if (pc.GetCoins() < price)
        {
            ShowBuffToast("金币不足，需要 " + price + " 金币");
            return;
        }

        pc.AddCoin(-price);
        if (isHeal) bh.ApplyBuff(buff);        // 即时恢复
        else bh.ApplyBuff(buff, true);         // 永久叠加一层

        // 刷新所有行的显示与金币
        for (int i = 0; i < shopPool.Count; i++)
        {
            var child = shopButtonContainer.GetChild(i);
            if (child != null) UpdateShopRow(child.gameObject, shopPool[i]);
        }
        UpdateCoinUI();
    }

    public void CloseShop()
    {
        if (shopPanel != null) shopPanel.SetActive(false);
    }

    public void RefreshBuffIcons()
    {
        if (buffIconContainer == null || buffIconPrefab == null) return;
        BuffHandler bh = FindObjectOfType<BuffHandler>();
        if (bh == null) return;
        foreach (Transform c in buffIconContainer) Destroy(c.gameObject);
        var owned = bh.GetOwnedBuffs();
        foreach (var o in owned)
        {
            GameObject icon = Instantiate(buffIconPrefab, buffIconContainer);
            var img = icon.transform.Find("Icon");
            if (img != null && o.data.icon != null) img.GetComponent<UnityEngine.UI.Image>().sprite = o.data.icon;
            var cnt = icon.transform.Find("Count");
            if (cnt != null) cnt.GetComponent<TextMeshProUGUI>().text = "x" + o.stack;
        }
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