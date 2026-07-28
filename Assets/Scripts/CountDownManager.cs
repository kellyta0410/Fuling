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

        // ⭐ 2. 查找并明确禁用敌人生成器
        EnemySpawner spawner = FindObjectOfType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.StopSpawning(); // 确保倒计时期间绝对关闭生成
            Debug.Log("⏸️ 倒计时期间禁用敌人生成");
        }

        // ⭐ 3. 冻结场景中原有的敌人（防止地图自带预设敌人移动）
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

        // ⭐ 5. 倒计时循环（使用真实时间计时）
        for (int i = (int)countdownDuration; i > 0; i--)
        {
            if (countdownText != null)
                countdownText.text = i.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownText != null)
            countdownText.text = "GO!";

        yield return new WaitForSecondsRealtime(0.5f);

        // ⭐ 6. 恢复游戏常规运行
        Time.timeScale = 1f;
        isCountingDown = false;
        gameStarted = true;

        if (countdownPanel != null)
            countdownPanel.SetActive(false);

        if (player != null) player.SetCanMove(true);

        // ⭐ 7. 启动游戏核心逻辑
        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();

        // ⭐ 8. 【关键】重新获取 Spawner 并启动敌人生成！
        // 重新 Find 确保即使 Spawner 是动态生成的也不会报空
        if (spawner == null) spawner = FindObjectOfType<EnemySpawner>();

        if (spawner != null)
        {
            spawner.StartSpawning(); // 内部会设置 canSpawn = true 并触发 PerformInitialSpawn()
            Debug.Log("✅ 倒计时结束，敌人生成已启用！");
        }
        else
        {
            Debug.LogWarning("⚠️ 未能在场景中找到 EnemySpawner！");
        }

        // ⭐ 9. 恢复原有敌人的 AI 组件
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

        EnemySpawner spawner = FindObjectOfType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.StartSpawning();
        }
    }

    public bool IsGameStarted() => gameStarted;
    public bool IsCountingDown() => isCountingDown;
}