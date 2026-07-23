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
    public GameObject settingsPanel;          // InGame 专用
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
    public TextMeshProUGUI musicValueText;
    public TextMeshProUGUI sfxValueText;

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner enemySpawner;
    public GameManager gameManager;

    private Vector2 timerOriginalPosition;
    private float timeLimit;

    void Start()
    {
        // 查找组件
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (enemySpawner == null) enemySpawner = FindObjectOfType<EnemySpawner>();
        if (gameManager == null) gameManager = GameManager.Instance;

        // 按钮绑定
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);

        // 初始隐藏面板
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        // 血条 Fill
        if (healthSlider != null && healthFillImage == null)
        {
            Transform fill = healthSlider.transform.Find("Fill Area/Fill");
            healthFillImage = fill != null ? fill.GetComponent<Image>() : healthSlider.GetComponentInChildren<Image>();
        }

        // 计时器位置
        if (timerText != null) timerOriginalPosition = timerText.rectTransform.anchoredPosition;

        // 订阅事件
        if (gameManager != null)
        {
            gameManager.OnTimerUpdated += UpdateTimerUI;
            gameManager.OnTimerVisibilityChanged += SetTimerVisibility;
            gameManager.OnGameOver += OnGameOver;
        }

        // 加载音量设置
        LoadSettings();

        // 绑定滑块事件
        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);

        // 初始化 UI
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();

        Time.timeScale = 1f;
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

    void Update()
    {
        UpdateKillUI();
        if (Input.GetKeyDown(KeyCode.R)) RestartGame();
    }

    // ==================== 设置面板 ====================

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

    public void OpenSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadSettings();  // 刷新当前值
            Time.timeScale = 0f;
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

    // ==================== UI 更新 ====================

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
            coinText.text = $"Coins: {player.GetCoins()}";
    }

    public void UpdateKillUI()
    {
        if (enemyCountText != null && player != null)
            enemyCountText.text = $"Kills: {player.GetKills()}";
    }

    // ==================== 计时器 ====================

    public void SetTimerVisibility(bool visible)
    {
        if (timerText != null) timerText.gameObject.SetActive(visible);
    }

    public void UpdateTimerUI(float remaining, float limit)
    {
        if (timerText == null) return;

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

        if (remaining <= 0) { timerText.color = new Color(1f, 0.2f, 0.2f, 1f); timerText.text = "00:00"; }
    }

    // ==================== GameOver ====================

    public void OnGameOver() => ShowGameOver();

    public void ShowGameOver()
    {
        if (gameOverPanel == null) return;

        if (settingsPanel != null) settingsPanel.SetActive(false);
        gameOverPanel.SetActive(true);

        if (finalCoinText != null && player != null) finalCoinText.text = $"Coins: {player.GetCoins()}";
        if (finalKillText != null && player != null) finalKillText.text = $"Kills: {player.GetKills()}";

        Time.timeScale = 0f;
    }

    public void HideGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    // ==================== 按钮方法 ====================

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitToGameOver()
    {
        if (gameManager != null) gameManager.GameOver(false);
        else ShowGameOver();
    }

    public void GoToSelection()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("DifficultySelection");
    }

    // ==================== 外部调用 ====================

    public void OnPlayerDamaged() => UpdateHealthUI();
    public void OnPlayerCoinChanged() => UpdateCoinUI();
    public void OnPlayerKillChanged() => UpdateKillUI();
    public void OnPlayerDied() => ShowGameOver();
}