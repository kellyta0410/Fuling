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

    [Header("血条颜色")]
    public Color fullHealthColor = Color.green;
    public Color midHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;
    public Image healthFillImage;  // ⭐ 血条的 Fill 图片

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner enemySpawner;

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

        // ⭐ 获取血条的 Fill 图片
        if (healthSlider != null && healthFillImage == null)
        {
            // 尝试获取 Slider 的 Fill 子物体
            Transform fillTransform = healthSlider.transform.Find("Fill Area/Fill");
            if (fillTransform != null)
            {
                healthFillImage = fillTransform.GetComponent<Image>();
            }

            // 如果没找到，尝试直接获取
            if (healthFillImage == null)
            {
                healthFillImage = healthSlider.GetComponentInChildren<Image>();
            }
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

    // 延迟更新
    IEnumerator DelayedUIUpdate()
    {
        yield return null;
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();
    }

    // ==================== UI 更新方法 ====================

    public void UpdateHealthUI()
    {
        if (player != null && healthSlider != null)
        {
            float healthPercent = player.GetHealthPercent();
            healthSlider.value = healthPercent;

            // ⭐ 更新血条颜色
            UpdateHealthBarColor(healthPercent);
        }
    }

    // ==================== ⭐ 血条颜色更新 ====================
    void UpdateHealthBarColor(float healthPercent)
    {
        if (healthFillImage == null) return;

        Color targetColor;

        if (healthPercent >= 0.6f)
        {
            // 60% - 100%: 绿色
            targetColor = fullHealthColor;
        }
        else if (healthPercent >= 0.3f)
        {
            // 30% - 60%: 黄色
            targetColor = midHealthColor;
        }
        else
        {
            // 0% - 30%: 红色
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