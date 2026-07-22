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

    [Header("游戏引用")]
    public PlayerController player;
    public EnemySpawner_NavMesh enemySpawner;

    private void Start()
    {
        // 自动查找组件
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }

        if (enemySpawner == null)
        {
            enemySpawner = FindObjectOfType<EnemySpawner_NavMesh>();
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

        // ⭐ 强制设置血条为满血
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

        // ⭐ 延迟一帧再更新一次
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

    // ⭐ 延迟更新
    IEnumerator DelayedUIUpdate()
    {
        yield return null; // 等待一帧
        UpdateHealthUI();
        UpdateCoinUI();
        UpdateKillUI();
    }

    // ==================== UI 更新方法 ====================

    public void UpdateHealthUI()
    {
        if (player != null && healthSlider != null)
        {
            healthSlider.value = player.GetHealthPercent();
        }
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