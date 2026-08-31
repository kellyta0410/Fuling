using UnityEngine;
using TMPro;
using System.Collections;

public class CountdownManager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI countdownText;
    public GameObject countdownPanel;

    [Header("教程面板（无尽模式专用）")]
    [Tooltip("进入无尽模式时先显示教程面板，关闭后才开始倒计时")]
    public GameObject tutorialPanel;

    [Header("设置")]
    public float countdownDuration = 3f;
    public bool enableCountdown = true;

    private bool isCountingDown = false;
    private bool gameStarted = false;
    private bool tutorialDone = false;

    void Start()
    {
        // 无尽模式 + 有教程面板：先显示教程，冻结游戏
        if (tutorialPanel != null && IsInfiniteMode())
        {
            tutorialPanel.SetActive(true);
            if (countdownPanel != null) countdownPanel.SetActive(false);
            FreezeGame();
            return;
        }

        // 非无尽模式或无教程面板：直接走倒计时或直接开始
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

    /// <summary>
    /// 教程面板关闭按钮调用此方法（UnityEvent / Button.OnClick 绑定）
    /// </summary>
    public void CloseTutorial()
    {
        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);

        tutorialDone = true;

        if (enableCountdown)
        {
            if (countdownPanel != null)
                countdownPanel.SetActive(true);
            StartCoroutine(StartCountdown());
        }
        else
        {
            StartGameImmediately();
        }
    }

    void FreezeGame()
    {
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.SetCanMove(false);
        Time.timeScale = 0f;
    }

    IEnumerator StartCountdown()
    {
        isCountingDown = true;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.SetCanMove(false);

        DisableAllSpawners();

        EnemyAI[] allEnemies = FindObjectsOfType<EnemyAI>();
        foreach (EnemyAI enemy in allEnemies)
        {
            if (enemy != null) enemy.enabled = false;
        }

        Time.timeScale = 0f;

        for (int i = (int)countdownDuration; i > 0; i--)
        {
            if (countdownText != null)
                countdownText.text = i.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownText != null)
            countdownText.text = "GO!";

        yield return new WaitForSecondsRealtime(0.5f);

        Time.timeScale = 1f;
        isCountingDown = false;
        gameStarted = true;

        if (countdownPanel != null)
            countdownPanel.SetActive(false);

        if (player != null) player.SetCanMove(true);

        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();

        EnableAllSpawners();

        foreach (EnemyAI enemy in allEnemies)
        {
            if (enemy != null) enemy.enabled = true;
        }

        Debug.Log("倒计时结束，游戏开始！");
    }

    void StartGameImmediately()
    {
        gameStarted = true;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.SetCanMove(true);

        Time.timeScale = 1f;

        if (GameManager.Instance != null)
            GameManager.Instance.StartGame();

        EnableAllSpawners();
    }

    private void DisableAllSpawners()
    {
        EnemySpawner normal = FindObjectOfType<EnemySpawner>();
        if (normal != null) normal.StopSpawning();
    }

    private void EnableAllSpawners()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsDungeonMode())
            return;

        EnemySpawner normal = FindObjectOfType<EnemySpawner>();
        if (normal != null) normal.StartSpawning();
    }

    private bool IsInfiniteMode()
    {
        if (GameManager.Instance != null)
            return GameManager.Instance.IsInfiniteMode();
        return false;
    }

    public bool IsGameStarted() => gameStarted;
    public bool IsCountingDown() => isCountingDown;
}
