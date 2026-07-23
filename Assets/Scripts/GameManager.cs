using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("UI 组件")]
    public TextMeshProUGUI timerText;

    [Header("游戏状态")]
    public DifficultySettings currentDifficulty;
    public EnemySpawner enemySpawner;

    [Header("所有难度配置")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    [Header("无限模式 - 当前成长值")]
    public int scalingLevel = 0;
    public float currentSpawnInterval;
    public int currentSpawnPerInterval;
    public float currentSpeedMultiplier;
    public float currentHealthMultiplier;
    public float currentDamageMultiplier;

    private float timeLimit;
    private float remainingTime;
    private float scalingTimer = 0f;
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

        string selectedDifficultyName = PlayerPrefs.GetString("SelectedDifficulty", "普通");
        Debug.Log("读取到选中的难度: " + selectedDifficultyName);

        DifficultySettings loadedDifficulty = GetDifficultyByName(selectedDifficultyName);

        if (loadedDifficulty != null)
        {
            currentDifficulty = loadedDifficulty;
            Debug.Log("加载难度配置: " + currentDifficulty.difficultyName);
        }
        else
        {
            Debug.LogWarning("未找到难度: " + selectedDifficultyName + "，使用默认（普通）");
            currentDifficulty = normalConfig;
        }

        if (currentDifficulty != null && enemySpawner != null)
        {
            // 初始化生成参数
            ApplyCurrentDifficultyToSpawner();
            StartGame();
        }
        else
        {
            Debug.LogError("无法应用难度：currentDifficulty 或 enemySpawner 为空");
        }
    }

    DifficultySettings GetDifficultyByName(string name)
    {
        DifficultySettings[] allConfigs = { easyConfig, normalConfig, hardConfig, infiniteConfig };

        foreach (DifficultySettings config in allConfigs)
        {
            if (config != null && config.difficultyName == name)
            {
                return config;
            }
        }
        return null;
    }

    public void StartGame()
    {
        isGameRunning = true;
        isGameOver = false;
        scalingLevel = 0;
        scalingTimer = 0f;

        if (currentDifficulty != null)
        {
            timeLimit = currentDifficulty.timeLimit;
            remainingTime = timeLimit;

            if (currentDifficulty.isInfiniteMode)
            {
                // 无限模式：初始值 = 基础值
                currentSpawnInterval = currentDifficulty.spawnInterval;
                currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
                currentSpeedMultiplier = 1f;
                currentHealthMultiplier = 1f;
                currentDamageMultiplier = 1f;

                Debug.Log("无限模式启动，基础生成间隔: " + currentSpawnInterval);
            }
            else
            {
                // 普通模式：使用固定值
                currentSpawnInterval = currentDifficulty.spawnInterval;
                currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
                currentSpeedMultiplier = 1f;
                currentHealthMultiplier = 1f;
                currentDamageMultiplier = 1f;
            }

            ApplyCurrentDifficultyToSpawner();
            UpdateTimerVisibility();

            string timeDisplay = currentDifficulty.isInfiniteMode ? "无限" : timeLimit + "秒";
            Debug.Log("游戏开始！难度: " + currentDifficulty.difficultyName + ", 时间: " + timeDisplay);
        }
    }

    void Update()
    {
        if (!isGameRunning || isGameOver) return;

        // 无限模式
        if (currentDifficulty != null && currentDifficulty.isInfiniteMode)
        {
            HideTimer();

            if (currentDifficulty.enableScaling)
            {
                HandleScaling();
            }
            return;
        }

        // 普通模式
        ShowTimer();
        remainingTime -= Time.deltaTime;
        UpdateTimerUI();

        if (remainingTime <= 0)
        {
            remainingTime = 0;
            GameOver(false);
        }
    }

    void HandleScaling()
    {
        scalingTimer += Time.deltaTime;

        if (scalingTimer >= currentDifficulty.scalingInterval)
        {
            scalingTimer = 0f;
            scalingLevel++;

            // 计算当前值（固定值方式）
            currentSpawnInterval = Mathf.Max(
                currentDifficulty.spawnInterval - (scalingLevel * currentDifficulty.spawnIntervalStep),
                currentDifficulty.spawnIntervalMin
            );

            currentSpawnPerInterval = Mathf.Min(
                currentDifficulty.spawnPerInterval + Mathf.RoundToInt(scalingLevel * currentDifficulty.spawnPerIntervalStep),
                currentDifficulty.spawnPerIntervalMax
            );

            currentSpeedMultiplier = Mathf.Min(
                1f + (scalingLevel * currentDifficulty.speedMultiplierStep),
                currentDifficulty.speedMultiplierMax
            );

            currentHealthMultiplier = Mathf.Min(
                1f + (scalingLevel * currentDifficulty.healthMultiplierStep),
                currentDifficulty.healthMultiplierMax
            );

            currentDamageMultiplier = Mathf.Min(
                1f + (scalingLevel * currentDifficulty.damageMultiplierStep),
                currentDifficulty.damageMultiplierMax
            );

            // 应用到生成器
            ApplyCurrentDifficultyToSpawner();

            Debug.Log("无限模式成长 - 等级: " + scalingLevel);
            Debug.Log("  生成间隔: " + currentSpawnInterval + "秒");
            Debug.Log("  每次生成: " + currentSpawnPerInterval + "个");
            Debug.Log("  速度倍率: " + currentSpeedMultiplier);
            Debug.Log("  血量倍率: " + currentHealthMultiplier);
            Debug.Log("  攻击倍率: " + currentDamageMultiplier);
        }
    }

    void ApplyCurrentDifficultyToSpawner()
    {
        if (enemySpawner == null) return;

        // 根据是否无限模式，传递不同的值给生成器
        if (currentDifficulty != null && currentDifficulty.isInfiniteMode)
        {
            enemySpawner.ApplyScalingParameters(
                currentSpawnInterval,
                currentSpawnPerInterval,
                currentSpeedMultiplier,
                currentHealthMultiplier,
                currentDamageMultiplier,
                currentDifficulty.enableMaxLimit,
                currentDifficulty.maxEnemyCount,
                currentDifficulty.enableCooldown,
                currentDifficulty.cooldownTime,
                currentDifficulty.allowedEnemyPrefabs
            );
        }
        else
        {
            // 普通模式：使用基础值
            enemySpawner.ApplyScalingParameters(
                currentDifficulty.spawnInterval,
                currentDifficulty.spawnPerInterval,
                1f,
                1f,
                1f,
                currentDifficulty.enableMaxLimit,
                currentDifficulty.maxEnemyCount,
                currentDifficulty.enableCooldown,
                currentDifficulty.cooldownTime,
                currentDifficulty.allowedEnemyPrefabs
            );
        }
    }

    void HideTimer()
    {
        if (timerText != null && timerText.gameObject.activeSelf)
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

            if (percent < 0.1f)
            {
                timerText.color = new Color(1f, 0.2f, 0.2f, 1f);

                float shakeAmount = 3f;
                float shakeX = Random.Range(-shakeAmount, shakeAmount);
                float shakeY = Random.Range(-shakeAmount, shakeAmount);
                timerText.rectTransform.anchoredPosition = new Vector2(shakeX, shakeY);
            }
            else if (percent < 0.3f)
            {
                timerText.color = Color.yellow;
                timerText.rectTransform.anchoredPosition = Vector2.zero;
            }
            else
            {
                timerText.color = Color.white;
                timerText.rectTransform.anchoredPosition = Vector2.zero;
            }
        }
    }

    public void GameOver(bool isWin)
    {
        if (isGameOver) return;

        isGameRunning = false;
        isGameOver = true;

        int coins = 0;
        int kills = 0;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            coins = player.GetCoins();
            kills = player.GetKills();
            Debug.Log("从 Player 获取数据: 金币=" + coins + ", 击杀=" + kills);
        }
        else
        {
            Debug.LogWarning("未找到 PlayerController");
        }

        float timeToSave = 0f;

        if (currentDifficulty != null && currentDifficulty.isInfiniteMode)
        {
            timeToSave = GetElapsedTime();
            Debug.Log("无限模式游玩时长: " + timeToSave + "秒");
        }
        else
        {
            timeToSave = timeLimit - remainingTime;
            if (timeToSave < 0) timeToSave = 0;
            Debug.Log("生存时间: " + timeToSave + "秒");
        }

        SaveBestRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);

        Debug.Log("游戏结束！" + currentDifficulty.difficultyName + " 记录已保存");
    }

    void SaveBestRecord(string difficultyName, int coins, int kills, float time)
    {
        string coinsKey = difficultyName + "_BestCoins";
        string killsKey = difficultyName + "_BestKills";
        string timeKey = difficultyName + "_BestTime";

        int bestCoins = PlayerPrefs.GetInt(coinsKey, 0);
        int bestKills = PlayerPrefs.GetInt(killsKey, 0);
        float bestTime = PlayerPrefs.GetFloat(timeKey, 0f);

        bool updated = false;

        if (coins > bestCoins)
        {
            PlayerPrefs.SetInt(coinsKey, coins);
            updated = true;
            Debug.Log("新纪录！" + difficultyName + " 金币: " + coins + " (之前: " + bestCoins + ")");
        }

        if (kills > bestKills)
        {
            PlayerPrefs.SetInt(killsKey, kills);
            updated = true;
            Debug.Log("新纪录！" + difficultyName + " 击杀: " + kills + " (之前: " + bestKills + ")");
        }

        if (time > bestTime)
        {
            PlayerPrefs.SetFloat(timeKey, time);
            updated = true;
            Debug.Log("新纪录！" + difficultyName + " 时间: " + time + "秒 (之前: " + bestTime + "秒)");
        }

        if (updated)
        {
            PlayerPrefs.Save();
            Debug.Log(difficultyName + " 最高纪录已更新");
        }
        else
        {
            Debug.Log(difficultyName + " 本次未打破纪录");
        }
    }

    public float GetRemainingTime()
    {
        return remainingTime;
    }

    public float GetTimePercent()
    {
        return remainingTime / timeLimit;
    }

    public bool IsInfiniteMode()
    {
        return currentDifficulty != null && currentDifficulty.isInfiniteMode;
    }

    public float GetElapsedTime()
    {
        return timeLimit - remainingTime;
    }

    public float GetCurrentSpeedMultiplier()
    {
        return currentSpeedMultiplier;
    }

    public float GetCurrentHealthMultiplier()
    {
        return currentHealthMultiplier;
    }

    public float GetCurrentDamageMultiplier()
    {
        return currentDamageMultiplier;
    }
}