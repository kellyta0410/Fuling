using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement; // ⭐ 新增：用于场景管理

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("时间")]
    public TextMeshProUGUI timerText;

    [Header("游戏状态")]
    public DifficultySettings currentDifficulty;
    public EnemySpawner enemySpawner;

    [Header("难度配置文件")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    [Header("场景加载")]
    public bool loadSceneOnDifficultyChange = true; // 是否切换场景
    public GameObject loadingScreen; // 加载界面（可选）

    private float timeLimit;
    private float remainingTime;
    private bool isGameRunning = false;
    private bool isGameOver = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (enemySpawner == null)
            enemySpawner = FindObjectOfType<EnemySpawner>();

        if (currentDifficulty != null && enemySpawner != null)
        {
            enemySpawner.ApplyDifficulty(currentDifficulty);
            StartGame();
        }
    }

    public void StartGame()
    {
        isGameRunning = true;
        isGameOver = false;

        if (currentDifficulty != null)
        {
            timeLimit = currentDifficulty.timeLimit;
            remainingTime = timeLimit;

            UpdateTimerVisibility();
        }
    }

    void Update()
    {
        if (!isGameRunning || isGameOver) return;

        if (currentDifficulty != null && currentDifficulty.isInfiniteMode)
        {
            HideTimer();
            return;
        }

        ShowTimer();
        remainingTime -= Time.deltaTime;
        UpdateTimerUI();

        if (remainingTime <= 0)
        {
            remainingTime = 0;
            GameOver(false);
        }
    }

    void HideTimer()
    {
        if(timerText != null && !timerText.gameObject.activeSelf)
        {
            timerText.gameObject.SetActive(false);
        }
    }

    void ShowTimer()
    {
        if (timerText != null && !timerText.gameObject.activeSelf)
        {
            timerText.gameObject.SetActive(true);
        }
    }

    void UpdateTimerVisibility()
    {
        if (currentDifficulty == null) return;

        if (currentDifficulty.isInfiniteMode)
        {
            HideTimer();
        }
        else
        {
            ShowTimer();
            UpdateTimerUI();
        }
    }

    void UpdateTimerUI()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(remainingTime / 60);
            int seconds = Mathf.FloorToInt(remainingTime % 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

            float percent = remainingTime / timeLimit;

            // ⭐ 颜色逻辑
            if (percent < 0.1f)
            {
                // 红色闪烁
                float flash = Mathf.PingPong(Time.time * 8, 0.5f) + 0.5f;
                timerText.color = new Color(1f, 0.2f, 0.2f, 1f);

                // ⭐ 震动效果
                float shakeAmount = 3f;
                float shakeX = Random.Range(-shakeAmount, shakeAmount);
                float shakeY = Random.Range(-shakeAmount, shakeAmount);
                timerText.rectTransform.anchoredPosition = new Vector2(shakeX, shakeY);
            }
            else if (percent < 0.3f)
            {
                timerText.color = Color.yellow;
                // 归位
                timerText.rectTransform.anchoredPosition = Vector2.zero;
            }
            else
            {
                timerText.color = Color.white;
                timerText.rectTransform.anchoredPosition = Vector2.zero;
            }
        }
    }

    public void OnDifficultyChanged(DifficultySettings newDifficulty)
    {
        if (newDifficulty == null) return;

        currentDifficulty = newDifficulty;
        timeLimit = newDifficulty.timeLimit;
        remainingTime = timeLimit;
        isGameRunning = true;
        isGameOver = false;

        UpdateTimerVisibility();

        string timeDisplay = newDifficulty.isInfiniteMode ? "∞ (隐藏)" : timeLimit + "秒";
        Debug.Log($"⏱️ 切换到: {newDifficulty.difficultyName} | 时间: {timeDisplay}");
    }

    public void GameOver(bool isWin)
    {
        isGameRunning = false;
        isGameOver = true;

        if (isWin)
        {
            Debug.Log("🎉 胜利！");
        }
        else
        {
            Debug.Log("💀 时间到！失败");
        }
    }

    // ⭐⭐⭐ 核心方法：切换难度并加载场景 ⭐⭐⭐
    public void SelectEasy()
    {
        if (easyConfig != null) ChangeDifficulty(easyConfig);
        else Debug.LogWarning("⚠️ Easy Config 未设置！");
    }

    public void SelectNormal()
    {
        if (normalConfig != null) ChangeDifficulty(normalConfig);
        else Debug.LogWarning("⚠️ Normal Config 未设置！");
    }

    public void SelectHard()
    {
        if (hardConfig != null) ChangeDifficulty(hardConfig);
        else Debug.LogWarning("⚠️ Hard Config 未设置！");
    }

    public void SelectInfinite()
    {
        if (infiniteConfig != null) ChangeDifficulty(infiniteConfig);
        else Debug.LogWarning("⚠️ Infinite Config 未设置！");
    }

    void ChangeDifficulty(DifficultySettings newDifficulty)
    {
        if (newDifficulty == null) return;

        Debug.Log($"🔄 切换到难度: {newDifficulty.difficultyName}");

        // ⭐ 检查是否需要加载场景
        if (loadSceneOnDifficultyChange && !string.IsNullOrEmpty(newDifficulty.sceneName))
        {
            // 保存当前选中的难度（在场景加载后使用）
            currentDifficulty = newDifficulty;

            // 显示加载界面（如果有）
            if (loadingScreen != null) loadingScreen.SetActive(true);

            // 异步加载场景
            StartCoroutine(LoadSceneAsync(newDifficulty.sceneName));
        }
        else
        {
            // 不切换场景，直接应用难度
            if (enemySpawner != null)
            {
                enemySpawner.ApplyDifficulty(newDifficulty);
            }
            else
            {
                currentDifficulty = newDifficulty;
                StartGame();
            }
        }
    }

    // ⭐ 协程：异步加载场景
    IEnumerator LoadSceneAsync(string sceneName)
    {
        Debug.Log($"📂 加载场景: {sceneName}");

        // 开始异步加载
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // 等待加载完成（进度到 90%）
        while (asyncLoad.progress < 0.9f)
        {
            // 可以在这里更新加载进度条
            // loadingProgressBar.fillAmount = asyncLoad.progress;
            yield return null;
        }

        // 隐藏加载界面
        if (loadingScreen != null) loadingScreen.SetActive(false);

        // 激活场景
        asyncLoad.allowSceneActivation = true;

        // 等待场景完全加载
        yield return asyncLoad;

        // 场景加载完成后，重新查找组件并应用难度
        yield return new WaitForEndOfFrame();

        // 查找新场景中的 EnemySpawner
        enemySpawner = FindObjectOfType<EnemySpawner>();
        if (enemySpawner != null && currentDifficulty != null)
        {
            enemySpawner.ApplyDifficulty(currentDifficulty);
        }

        // 查找新场景中的 Player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && enemySpawner != null)
        {
            enemySpawner.playerTarget = player.transform;
        }

        StartGame();
        Debug.Log($"✅ 场景 {sceneName} 加载完成，难度: {currentDifficulty.difficultyName}");
    }

    public float GetRemainingTime() { return remainingTime; }
    public float GetTimePercent() { return remainingTime / timeLimit; }
    public bool IsInfiniteMode() { return currentDifficulty != null && currentDifficulty.isInfiniteMode; }
}