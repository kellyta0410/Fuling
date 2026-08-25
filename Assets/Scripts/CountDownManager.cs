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

        // ⭐ 1. 冻结玩家
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.SetCanMove(false);

        // ⭐ 2. 禁用所有敌人生成器
        DisableAllSpawners();

        // ⭐ 3. 冻结场景中原有的敌人
        EnemyAI[] allEnemies = FindObjectsOfType<EnemyAI>();
        foreach (EnemyAI enemy in allEnemies)
        {
            if (enemy != null)
            {
                enemy.enabled = false;
            }
        }

        // ⭐ 4. 暂停物理和时间
        Time.timeScale = 0f;

        // ⭐ 5. 倒计时循环
        for (int i = (int)countdownDuration; i > 0; i--)
        {
            if (countdownText != null)
                countdownText.text = i.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownText != null)
            countdownText.text = "GO!";

        yield return new WaitForSecondsRealtime(0.5f);

        // ⭐ 6. 恢复游戏
        Time.timeScale = 1f;
        isCountingDown = false;
        gameStarted = true;

        if (countdownPanel != null)
            countdownPanel.SetActive(false);

        if (player != null) player.SetCanMove(true);

        // ⭐ 7. 启动游戏
        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();

        // ⭐ 8. 启用所有生成器
        EnableAllSpawners();

        // ⭐ 9. 恢复敌人AI
        foreach (EnemyAI enemy in allEnemies)
        {
            if (enemy != null)
            {
                enemy.enabled = true;
            }
        }

        Debug.Log("✅ 倒计时结束，游戏开始！");
    }

    void StartGameImmediately()
    {
        gameStarted = true;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.SetCanMove(true);

        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();

        EnableAllSpawners();
    }

    private void DisableAllSpawners()
    {
        EnemySpawner normal = FindObjectOfType<EnemySpawner>();
        if (normal != null)
        {
            normal.StopSpawning();
        }

        InfiniteEnemySpawner infinite = FindObjectOfType<InfiniteEnemySpawner>();
        if (infinite != null)
        {
            infinite.StopSpawning();
        }
    }

    private void EnableAllSpawners()
    {
        // 地牢模式：房间生成与敌人刷新完全由 DungeonManager 接管，不要启用旧生成器
        if (GameManager.Instance != null && GameManager.Instance.IsDungeonMode())
        {
            return;
        }

        // 根据 GameManager 的模式启用对应的生成器
        if (GameManager.Instance != null)
        {
            bool isInfinite = GameManager.Instance.IsInfiniteMode();

            if (isInfinite)
            {
                InfiniteEnemySpawner infinite = FindObjectOfType<InfiniteEnemySpawner>();
                if (infinite != null)
                {
                    infinite.StartSpawning();
                }
            }
            else
            {
                EnemySpawner normal = FindObjectOfType<EnemySpawner>();
                if (normal != null)
                {
                    normal.StartSpawning();
                }
            }
        }
        else
        {
            // 降级方案：启用所有
            EnemySpawner normal = FindObjectOfType<EnemySpawner>();
            if (normal != null) normal.StartSpawning();

            InfiniteEnemySpawner infinite = FindObjectOfType<InfiniteEnemySpawner>();
            if (infinite != null) infinite.StartSpawning();
        }
    }

    public bool IsGameStarted() => gameStarted;
    public bool IsCountingDown() => isCountingDown;
}