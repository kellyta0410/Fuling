using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("UI 引用")]
    public Slider healthSlider;
    public GameObject gameOverPanel;
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI enemyCountText;
    public TextMeshProUGUI finalCoinText;
    public TextMeshProUGUI finalKillText;
    public Button restartButton;

    [Header("计时器 UI")]
    public TextMeshProUGUI timerText;  // 从 GameManager 移过来的

    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;
    public Image healthFillImage;

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner enemySpawner;
    public GameManager gameManager;

    // 计时器动画相关
    private Vector2 timerOriginalPosition;
    private float timeLimit;

    private void Start()
    {
        // 自动查找组件
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }

        if (enemySpawner == null)
        {
            enemySpawner = FindObjectOfType<EnemySpawner>();
        }

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        // 绑定按钮事件
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartGame);
        }

        // 初始隐藏 GameOver
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        // 获取血条的 Fill 图片
        if (healthSlider != null && healthFillImage == null)
        {
            Transform fillTransform = healthSlider.transform.Find("Fill Area/Fill");
            if (fillTransform != null)
            {
                healthFillImage = fillTransform.GetComponent<Image>();
            }

            if (healthFillImage == null)
            {
                healthFillImage = healthSlider.GetComponentInChildren<Image>();
            }
        }

        // 保存计时器原始位置
        if (timerText != null)
        {
            timerOriginalPosition = timerText.rectTransform.anchoredPosition;
        }

        // 订阅 GameManager 事件
        if (gameManager != null)
        {
            gameManager.OnTimerUpdated += UpdateTimerUI;
            gameManager.OnTimerVisibilityChanged += SetTimerVisibility;
            gameManager.OnGameOver += OnGameOver;
        }

        // 强制设置血条为满血
        if (healthSlider != null)
        {
            healthSlider.value = 1f;
        }

        // 初始化 UI
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();

        // 确保游戏时间正常
        Time.timeScale = 1f;

        // 延迟一帧再更新一次
        StartCoroutine(DelayedUIUpdate());
    }

    private void OnDestroy()
    {
        // 取消订阅事件
        if (gameManager != null)
        {
            gameManager.OnTimerUpdated -= UpdateTimerUI;
            gameManager.OnTimerVisibilityChanged -= SetTimerVisibility;
            gameManager.OnGameOver -= OnGameOver;
        }
    }

    private void Update()
    {
        // 实时更新击杀数
        UpdateKillUI();

        // 按 R 键重启（方便测试）
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartGame();
        }
    }

    IEnumerator DelayedUIUpdate()
    {
        yield return null;
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();
    }

    // ==================== 计时器 UI 方法 ====================

    public void SetTimerVisibility(bool isVisible)
    {
        if (timerText != null)
        {
            timerText.gameObject.SetActive(isVisible);
        }
    }

    public void UpdateTimerUI(float remainingTime, float timeLimit)
    {
        if (timerText == null) return;

        this.timeLimit = timeLimit;

        // 格式化时间显示
        int minutes = Mathf.FloorToInt(remainingTime / 60);
        int seconds = Mathf.FloorToInt(remainingTime % 60);
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // 根据剩余时间百分比更新颜色和动画
        float percent = timeLimit > 0 ? remainingTime / timeLimit : 0;

        if (percent < 0.1f && percent > 0)
        {
            timerText.color = new Color(1f, 0.2f, 0.2f, 1f);

            // 抖动效果
            float shakeAmount = 3f;
            float shakeX = Random.Range(-shakeAmount, shakeAmount);
            float shakeY = Random.Range(-shakeAmount, shakeAmount);
            timerText.rectTransform.anchoredPosition = timerOriginalPosition + new Vector2(shakeX, shakeY);
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

        // 时间到
        if (remainingTime <= 0)
        {
            timerText.color = new Color(1f, 0.2f, 0.2f, 1f);
            timerText.text = "00:00";
        }
    }

    // ==================== UI 更新方法 ====================

    public void UpdateHealthUI()
    {
        if (player != null && healthSlider != null)
        {
            float healthPercent = player.GetHealthPercent();
            healthSlider.value = healthPercent;
            UpdateHealthBarColor(healthPercent);
        }
    }

    void UpdateHealthBarColor(float healthPercent)
    {
        if (healthFillImage == null) return;

        Color targetColor;

        if (healthPercent >= 0.6f)
        {
            targetColor = fullHealthColor;
        }
        else if (healthPercent >= 0.3f)
        {
            targetColor = midHealthColor;
        }
        else
        {
            targetColor = lowHealthColor;
        }

        healthFillImage.color = targetColor;
    }

    public void UpdateCoinUI()
    {
        if (player != null && coinText != null)
        {
            coinText.text = $"Coins: {player.GetCoins()}";
        }
    }

    public void UpdateKillUI()
    {
        if (enemyCountText != null && player != null)
        {
            enemyCountText.text = $"Kills: {player.GetKills()}";
        }
    }

    // ==================== GameOver ====================

    public void OnGameOver()
    {
        ShowGameOver();
    }

    public void ShowGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);

            if (finalCoinText != null && player != null)
            {
                finalCoinText.text = $"Coins: {player.GetCoins()}";
            }

            if (finalKillText != null && player != null)
            {
                finalKillText.text = $"Kills: {player.GetKills()}";
            }

            Time.timeScale = 0f;
            Debug.Log("⏸️ Game Paused");
        }
    }

    public void HideGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        Time.timeScale = 1f;
        Debug.Log("▶️ Game Resumed");
    }

    // ==================== 重新开始 ====================

    public void RestartGame()
    {
        Debug.Log("🔄 Restarting Game...");

        Time.timeScale = 1f;

        if (player != null)
        {
            player.RestartGame();
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // ==================== 公共方法 ====================

    public void OnPlayerDamaged()
    {
        UpdateHealthUI();
    }

    public void OnPlayerCoinChanged()
    {
        UpdateCoinUI();
    }

    public void OnPlayerKillChanged()
    {
        UpdateKillUI();
    }

    public void OnPlayerDied()
    {
        ShowGameOver();
    }
}