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

    // 计时器模式
    private bool isTimerMode = false;  // false=倒计时, true=正计时
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

        // 检测是否为无限模式
        if (gameManager != null && gameManager.IsInfiniteMode())
        {
            isTimerMode = true;
            elapsedTime = 0f;
            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
                timerText.text = "00:00";
                timerText.color = Color.white;  // ⭐ 固定白色
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

    // ⭐ 公共方法：设置计时模式
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
                timerText.color = Color.white;  // ⭐ 固定白色
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
            LoadSettings();
            Time.timeScale = 0f;

            if (player != null)
            {
                player.SetJoystickEnabled(false);
            }
        }
    }

    public void CloseSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            Time.timeScale = 1f;

            if (player != null)
            {
                player.SetJoystickEnabled(true);
            }
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
            coinText.text = $"Coins: {player.GetCoins()}";
    }

    public void UpdateKillUI()
    {
        if (enemyCountText != null && player != null)
            enemyCountText.text = $"Kills: {player.GetKills()}";
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

        if (remaining <= 0) { timerText.color = new Color(1f, 0.2f, 0.2f, 1f); timerText.text = "00:00"; }
    }

    // 正计时显示（无限模式用）
    void UpdateTimerDisplay(float time)
    {
        if (timerText == null) return;

        int min = Mathf.FloorToInt(time / 60);
        int sec = Mathf.FloorToInt(time % 60);
        timerText.text = $"{min:00}:{sec:00}";

        // ⭐ 无限模式：保持白色，不变化颜色
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

        // 如果是无限模式，显示总时间
        string timeText = "";
        if (isTimerMode)
        {
            int min = Mathf.FloorToInt(elapsedTime / 60);
            int sec = Mathf.FloorToInt(elapsedTime % 60);
            timeText = $"\nTime: {min:00}:{sec:00}";
        }

        if (finalCoinText != null) finalCoinText.text = $"Coins: {coins}";
        if (finalKillText != null) finalKillText.text = $"Kills: {kills}{timeText}";

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
    public void OnPlayerDied() => ShowGameOver();
}