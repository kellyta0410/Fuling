using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

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
    private float gameStartTime = 0f;  // ✅ 新增：记录游戏开始时间
    private bool isGameRunning = false;
    private bool isGameOver = false;

    // 事件
    public System.Action<float, float> OnTimerUpdated;
    public System.Action<bool> OnTimerVisibilityChanged;
    public System.Action OnGameOver;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (enemySpawner == null)
            enemySpawner = FindObjectOfType<EnemySpawner>();

        // ✅ 方案2：根据场景名自动设置难度
        LoadDifficultyByScene();

        if (currentDifficulty != null && enemySpawner != null)
        {
            ApplyCurrentDifficultyToSpawner();
            StartGame();
        }
        else
        {
            Debug.LogError("无法应用难度：currentDifficulty 或 enemySpawner 为空");
        }
    }

    // ✅ 新增：根据场景名加载难度
    void LoadDifficultyByScene()
    {
        string sceneName = SceneManager.GetActiveScene().name.ToLower();
        Debug.Log("当前场景: " + sceneName);

        if (sceneName.Contains("Easy"))
        {
            currentDifficulty = easyConfig;
            Debug.Log("✅ 自动加载: Easy 难度");
        }
        else if (sceneName.Contains("Medium"))
        {
            currentDifficulty = normalConfig;
            Debug.Log("✅ 自动加载: Normal 难度");
        }
        else if (sceneName.Contains("Hard"))
        {
            currentDifficulty = hardConfig;
            Debug.Log("✅ 自动加载: Hard 难度");
        }
        else if (sceneName.Contains("Infinite"))
        {
            currentDifficulty = infiniteConfig;
            Debug.Log("✅ 自动加载: Infinite 难度");
        }
        else
        {
            // 兜底：从 PlayerPrefs 读取
            string selectedDifficultyName = PlayerPrefs.GetString("SelectedDifficulty", "普通");
            Debug.Log("场景名不匹配，从 PlayerPrefs 读取: " + selectedDifficultyName);
            currentDifficulty = GetDifficultyByName(selectedDifficultyName);
        }

        // 如果还是 null，用 normalConfig 兜底
        if (currentDifficulty == null)
        {
            Debug.LogWarning("未找到匹配的难度配置，使用 Normal 作为默认");
            currentDifficulty = normalConfig;
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
        gameStartTime = Time.time;  // ✅ 记录开始时间

        if (currentDifficulty != null)
        {
            timeLimit = currentDifficulty.timeLimit;
            remainingTime = timeLimit;

            bool isInfinite = currentDifficulty.IsInfiniteMode();

            if (isInfinite)
            {
                currentSpawnInterval = currentDifficulty.spawnInterval;
                currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
                currentSpeedMultiplier = 1f;
                currentHealthMultiplier = 1f;
                currentDamageMultiplier = 1f;

                Debug.Log("无限模式启动，基础生成间隔: " + currentSpawnInterval);
            }
            else
            {
                currentSpawnInterval = currentDifficulty.spawnInterval;
                currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
                currentSpeedMultiplier = 1f;
                currentHealthMultiplier = 1f;
                currentDamageMultiplier = 1f;
            }

            ApplyCurrentDifficultyToSpawner();
            NotifyTimerVisibility();

            string timeDisplay = isInfinite ? "无限" : timeLimit + "秒";
            Debug.Log("游戏开始！难度: " + currentDifficulty.difficultyName + ", 时间: " + timeDisplay);
        }
    }

    void Update()
    {
        if (!isGameRunning || isGameOver) return;

        if (currentDifficulty != null && currentDifficulty.IsInfiniteMode())
        {
            if (OnTimerVisibilityChanged != null)
            {
                OnTimerVisibilityChanged(false);
            }

            if (currentDifficulty.enableScaling)
            {
                HandleScaling();
            }
            return;
        }

        if (OnTimerVisibilityChanged != null)
        {
            OnTimerVisibilityChanged(true);
        }

        remainingTime -= Time.deltaTime;

        if (OnTimerUpdated != null)
        {
            OnTimerUpdated(remainingTime, timeLimit);
        }

        if (remainingTime <= 0)
        {
            remainingTime = 0;
            if (OnTimerUpdated != null)
            {
                OnTimerUpdated(remainingTime, timeLimit);
            }
            GameOver(false);
        }
    }

    void HandleScaling()
    {
        scalingTimer += Time.deltaTime;

        if (scalingTimer >= currentDifficulty.scalingInterval)
        {
            scalingTimer = 0f;

            // ✅ 添加最大等级限制（需要在 DifficultySettings 中添加 maxScalingLevel 字段）
            int maxLevel = currentDifficulty.maxScalingLevel > 0 ? currentDifficulty.maxScalingLevel : 999;
            if (scalingLevel >= maxLevel)
            {
                return;  // 达到上限，不再增长
            }

            scalingLevel++;

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

        bool isInfinite = currentDifficulty.IsInfiniteMode();

        if (isInfinite)
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

    void NotifyTimerVisibility()
    {
        if (OnTimerVisibilityChanged == null) return;

        bool isInfinite = currentDifficulty != null && currentDifficulty.IsInfiniteMode();
        OnTimerVisibilityChanged(!isInfinite);
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
        }

        // ✅ 修复：无限模式用真实时间
        float timeToSave = 0f;
        if (currentDifficulty != null && currentDifficulty.IsInfiniteMode())
        {
            timeToSave = Time.time - gameStartTime;  // 真实经过时间
        }
        else
        {
            timeToSave = timeLimit - remainingTime;
            if (timeToSave < 0) timeToSave = 0;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            dataManager.UpdateRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
            dataManager.AddCoins(coins);
        }
        else
        {
            SaveBestRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
        }

        if (OnGameOver != null)
        {
            OnGameOver();
        }

        Debug.Log($"游戏结束！{currentDifficulty.difficultyName} 金币: {coins}, 击杀: {kills}, 时间: {timeToSave:F1}s");
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
        return currentDifficulty != null && currentDifficulty.IsInfiniteMode();
    }

    // ✅ 修复：无限模式返回真实时间
    public float GetElapsedTime()
    {
        if (IsInfiniteMode())
        {
            return Time.time - gameStartTime;
        }
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

    public bool IsGameRunning()
    {
        return isGameRunning && !isGameOver;
    }
}