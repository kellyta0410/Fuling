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
    private float gameStartTime = 0f;
    private bool isGameRunning = false;
    private bool isGameOver = false;
    private bool hasProcessedGameOver = false;  // ✅ 防止重复处理

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

        // ✅ 方案1：完全依赖 Inspector 拖拽，不自动加载
        if (currentDifficulty != null && enemySpawner != null)
        {
            ApplyCurrentDifficultyToSpawner();
            StartGame();
        }
        else
        {
            Debug.LogError($"❌ 场景 '{SceneManager.GetActiveScene().name}' 配置错误！请在 Inspector 中拖拽 currentDifficulty 和 enemySpawner");
        }
    }

    public void StartGame()
    {
        isGameRunning = true;
        isGameOver = false;
        hasProcessedGameOver = false;  // ✅ 重置标记
        scalingLevel = 0;
        scalingTimer = 0f;
        gameStartTime = Time.time;

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

                Debug.Log($"✅ 无限模式启动，场景: {SceneManager.GetActiveScene().name}");
                Debug.Log($"   基础生成间隔: {currentSpawnInterval}秒");
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
            Debug.Log($"✅ 游戏开始！场景: {SceneManager.GetActiveScene().name}, 难度: {currentDifficulty.difficultyName}, 时间: {timeDisplay}");
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

            int maxLevel = currentDifficulty.maxScalingLevel > 0 ? currentDifficulty.maxScalingLevel : 999;
            if (scalingLevel >= maxLevel)
            {
                return;
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

            Debug.Log($"🔄 无限模式成长 - 等级: {scalingLevel}");
            Debug.Log($"   生成间隔: {currentSpawnInterval}秒");
            Debug.Log($"   每次生成: {currentSpawnPerInterval}个");
            Debug.Log($"   速度倍率: {currentSpeedMultiplier:F2}");
            Debug.Log($"   血量倍率: {currentHealthMultiplier:F2}");
            Debug.Log($"   攻击倍率: {currentDamageMultiplier:F2}");
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
        // ✅ 防止重复调用
        if (isGameOver)
        {
            Debug.Log("⚠️ GameOver 已经执行过，跳过本次调用");
            return;
        }

        if (hasProcessedGameOver)
        {
            Debug.Log("⚠️ GameOver 已处理过数据，跳过");
            return;
        }

        Debug.Log($"🎯 ===== GameOver 被调用 =====");
        Debug.Log($"🎯 isWin: {isWin}");
        Debug.Log($"🎯 当前场景: {SceneManager.GetActiveScene().name}");
        Debug.Log($"🎯 当前难度: {currentDifficulty?.difficultyName ?? "未知"}");

        isGameRunning = false;
        isGameOver = true;
        hasProcessedGameOver = true;

        // ========== 获取游戏数据 ==========
        int coins = 0;
        int kills = 0;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            coins = player.GetCoins();
            kills = player.GetKills();
            Debug.Log($"🎯 玩家数据 - 金币: {coins}, 击杀: {kills}");
        }
        else
        {
            Debug.LogWarning("⚠️ 找不到 PlayerController！");
        }

        // 计算游戏时间
        float timeToSave = 0f;
        if (currentDifficulty != null && currentDifficulty.IsInfiniteMode())
        {
            timeToSave = Time.time - gameStartTime;
        }
        else
        {
            timeToSave = timeLimit - remainingTime;
            if (timeToSave < 0) timeToSave = 0;
        }
        Debug.Log($"🎯 游戏时间: {timeToSave:F1}秒");

        // ========== 保存数据到 GameDataManager ==========
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            Debug.Log($"🎯 GameDataManager 存在，开始保存数据...");
            Debug.Log($"🎯 累加前总金币: {dataManager.TotalCoins}");

            // 1. 更新最高纪录
            if (currentDifficulty != null)
            {
                dataManager.UpdateRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
                Debug.Log($"🎯 已更新 {currentDifficulty.difficultyName} 模式的最高纪录");
            }

            // 2. 累加总金币
            dataManager.AddCoins(coins);
            Debug.Log($"🎯 累加后总金币: {dataManager.TotalCoins}");
        }
        else
        {
            Debug.LogError("❌ GameDataManager.Instance 为空！使用 PlayerPrefs 降级方案");
            if (currentDifficulty != null)
            {
                SaveBestRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
            }
        }

        // ========== 触发事件 ==========
        if (OnGameOver != null)
        {
            Debug.Log("🎯 触发 OnGameOver 事件");
            OnGameOver();
        }

        Debug.Log($"🏁 ===== 游戏结束完成 =====");
        Debug.Log($"🏁 {currentDifficulty?.difficultyName ?? "未知"} - 金币: {coins}, 击杀: {kills}, 时间: {timeToSave:F1}s");
    }

    // ✅ 降级方案：直接使用 PlayerPrefs
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
            Debug.Log($"🏆 新纪录！{difficultyName} 金币: {coins} (之前: {bestCoins})");
        }

        if (kills > bestKills)
        {
            PlayerPrefs.SetInt(killsKey, kills);
            updated = true;
            Debug.Log($"🏆 新纪录！{difficultyName} 击杀: {kills} (之前: {bestKills})");
        }

        if (time > bestTime)
        {
            PlayerPrefs.SetFloat(timeKey, time);
            updated = true;
            Debug.Log($"🏆 新纪录！{difficultyName} 时间: {time:F1}秒 (之前: {bestTime:F1}秒)");
        }

        if (updated)
        {
            PlayerPrefs.Save();
            Debug.Log($"✅ {difficultyName} 最高纪录已更新");
        }
        else
        {
            Debug.Log($"ℹ️ {difficultyName} 本次未打破纪录");
        }
    }

    // ==================== 公共方法 ====================

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

    // ✅ 新增：检查是否已经 GameOver
    public bool IsGameOver()
    {
        return isGameOver;
    }

    // ✅ 新增：重置游戏状态（用于重新开始）
    public void ResetGameState()
    {
        isGameRunning = false;
        isGameOver = false;
        hasProcessedGameOver = false;
        scalingLevel = 0;
        scalingTimer = 0f;
        gameStartTime = 0f;
        Debug.Log("🔄 游戏状态已重置");
    }
}