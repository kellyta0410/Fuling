using UnityEngine;
using TMPro;
using System.Collections;

public class CountdownManager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI countdownText;
    public GameObject countdownPanel;

    [Header("设置")]
    public float countdownDuration = 3f;
    public bool enableCountdown = true;

    private bool isCountingDown = false;
    private bool gameStarted = false;

    void Start()
    {
        // ⭐ 检查是否为无限模式
        bool isInfinite = false;
        if (GameManager.Instance != null)
        {
            isInfinite = GameManager.Instance.IsInfiniteMode();
        }

        // ⭐ 如果启用倒计时并且是无限模式（或者所有模式都启用）
        if (enableCountdown)
        {
            if (countdownPanel != null)
                countdownPanel.SetActive(true);

            StartCoroutine(StartCountdown());
        }
        else
        {
            if (countdownPanel != null)
                countdownPanel.SetActive(false);

            StartGameImmediately();
        }
    }

    IEnumerator StartCountdown()
    {
        isCountingDown = true;

        // 禁止玩家移动
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            player.SetCanMove(false);
        }

        // 暂停游戏（Time.timeScale = 0 会影响所有非实时操作）
        Time.timeScale = 0f;

        // 显示倒计时
        for (int i = (int)countdownDuration; i > 0; i--)
        {
            if (countdownText != null)
                countdownText.text = i.ToString();

            // 使用 WaitForSecondsRealtime 不受 Time.timeScale 影响
            yield return new WaitForSecondsRealtime(1f);
        }

        // 显示 "GO!"
        if (countdownText != null)
            countdownText.text = "GO!";

        yield return new WaitForSecondsRealtime(0.5f);

        // 隐藏倒计时
        if (countdownPanel != null)
            countdownPanel.SetActive(false);

        // 恢复游戏
        Time.timeScale = 1f;

        isCountingDown = false;
        gameStarted = true;

        // 允许玩家移动
        if (player != null)
        {
            player.SetCanMove(true);
        }

        // 通知 GameManager 游戏开始
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartGame();
        }

        // ⭐ 通知 EnemySpawner 开始生成
        EnemySpawner spawner = FindObjectOfType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.StartSpawning();
        }

        Debug.Log("✅ 倒计时结束，游戏开始！");
    }

    void StartGameImmediately()
    {
        gameStarted = true;

        // 允许玩家移动
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            player.SetCanMove(true);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartGame();
        }

        EnemySpawner spawner = FindObjectOfType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.StartSpawning();
        }

        Debug.Log("✅ 游戏直接开始（无倒计时）");
    }

    public bool IsGameStarted()
    {
        return gameStarted;
    }

    public bool IsCountingDown()
    {
        return isCountingDown;
    }
}