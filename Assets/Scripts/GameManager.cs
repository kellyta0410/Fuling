using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("游戏状态")]
    public DifficultySettings currentDifficulty;

    [Header("生成器（两种模式）")]
    public EnemySpawner normalSpawner;
    public InfiniteEnemySpawner infiniteSpawner;

    [Header("无限模式 - 当前成长值")]
    public int scalingLevel = 0;
    public float currentSpawnInterval;
    public int currentSpawnPerInterval;
    public float currentSpeedMultiplier;
    public float currentHealthMultiplier;
    public float currentDamageMultiplier;
    public int currentMaxEnemyCount;


    [Header("地牢模式（改造 Infinite 场景）")]
    public bool isDungeon = false;
    public DungeonManager dungeonManager;
    private int dungeonRoom = 0;

    private float timeLimit;
    private float remainingTime;
    private float scalingTimer = 0f;
    private float gameStartTime = 0f;
    private bool isGameRunning = false;
    private bool isGameOver = false;
    private bool hasProcessedGameOver = false;

    // ⭐ 复活：每局只允许「第一次死亡」看广告复活一次，之后死亡直接结算
    private bool reviveUsedThisRun = false;

    public System.Action<float, float> OnTimerUpdated;
    public System.Action<bool> OnTimerVisibilityChanged;
    public System.Action OnGameOver;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Debug.Log("[Boot] GameManager.Start begin");
        // ⭐ 开局即初始化 AdMob 并预拉取激励广告：把首条广告的 SDK 初始化成本挪到开局，
        // 玩家死亡点「观看广告」时 preloadedAd 已就绪 → 秒出，不再走联网 Load 等待。
        // （原"闪屏即退"崩溃在更早的开屏阶段，AdMob 初始化只在本玩法场景 Start 才跑，与之无关；
        //   该崩溃已确认是增量构建缓存坏档所致，故此处恢复早初始化是安全的。）
        RewardVideoAdService.EnsureAdMobInitialized();
        RewardVideoAdService.PreloadRewardedAd();

        if (normalSpawner == null)
            normalSpawner = FindObjectOfType<EnemySpawner>();

        if (infiniteSpawner == null)
            infiniteSpawner = FindObjectOfType<InfiniteEnemySpawner>();

        if (dungeonManager != null && !isDungeon)
            dungeonManager.gameObject.SetActive(false);

        // 根据模式启用对应的生成器
        if (currentDifficulty != null)
        {
            // 地牢模式：在 Infinite 场景（且为无限难度）时自动启用，把开放世界替换为房间制地牢
            if (!isDungeon && currentDifficulty.IsInfiniteMode()
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Infinite")
            {
                isDungeon = true;
            }

            if (currentDifficulty.IsInfiniteMode())
            {
                if (normalSpawner != null)
                {
                    normalSpawner.gameObject.SetActive(false);
                    normalSpawner.enabled = false;
                }
                if (isDungeon)
                {
                    // 地牢模式：禁用开放世界与无限生成器，改由 DungeonManager 接管
                    if (infiniteSpawner != null)
                    {
                        infiniteSpawner.gameObject.SetActive(false);
                        infiniteSpawner.enabled = false;
                    }
                    if (dungeonManager != null)
                    {
                        dungeonManager.gameObject.SetActive(true);
                        Debug.Log($"🏰 地牢模式激活（改造 Infinite 场景）");
                    }
                    else
                    {
                        Debug.LogError("🏰 地牢模式已启用，但 GameManager.dungeonManager 未赋值（请在 Manager 预制体上拖入 DungeonManager）");
                    }
                }
                else if (infiniteSpawner != null)
                {
                    if (dungeonManager != null) dungeonManager.gameObject.SetActive(false);
                    infiniteSpawner.gameObject.SetActive(true);
                    infiniteSpawner.enabled = true;
                    if (infiniteSpawner.playerTarget == null)
                    {
                        GameObject player = GameObject.FindGameObjectWithTag("Player");
                        if (player != null) infiniteSpawner.playerTarget = player.transform;
                    }
                    Debug.Log($"♾️ 无限模式激活，使用 InfiniteEnemySpawner");
                }
            }
            else
            {
                if (infiniteSpawner != null)
                {
                    infiniteSpawner.gameObject.SetActive(false);
                    infiniteSpawner.enabled = false;
                }
                if (normalSpawner != null)
                {
                    normalSpawner.gameObject.SetActive(true);
                    normalSpawner.enabled = true;
                    if (normalSpawner.playerTarget == null)
                    {
                        GameObject player = GameObject.FindGameObjectWithTag("Player");
                        if (player != null) normalSpawner.playerTarget = player.transform;
                    }
                }
                Debug.Log($"📋 普通模式激活，使用 EnemySpawner");
            }

            ApplyCurrentDifficultyToSpawner();
            StartGame();
        }
        else
        {
            Debug.LogError($"❌ 场景 '{SceneManager.GetActiveScene().name}' 配置错误！请在 Inspector 中拖拽 currentDifficulty");
        }
        Debug.Log("[Boot] GameManager.Start end");
    }

    public void StartGame()
    {
        isGameRunning = true;
        isGameOver = false;
        hasProcessedGameOver = false;
        reviveUsedThisRun = false;
        scalingLevel = 0;
        scalingTimer = 0f;
        gameStartTime = Time.time;

        if (currentDifficulty != null)
        {
            timeLimit = currentDifficulty.timeLimit;
            remainingTime = timeLimit;

            bool isInfinite = currentDifficulty.IsInfiniteMode();

            currentSpawnInterval = currentDifficulty.spawnInterval;
            currentSpawnPerInterval = currentDifficulty.spawnPerInterval;
            currentSpeedMultiplier = currentDifficulty.enemySpeedMultiplier;
            currentHealthMultiplier = currentDifficulty.enemyHealthMultiplier;
            currentDamageMultiplier = currentDifficulty.enemyDamageMultiplier;
            currentMaxEnemyCount = currentDifficulty.maxEnemyCount;

            if (isInfinite)
            {
                Debug.Log($"✅ 无限模式启动，场景: {SceneManager.GetActiveScene().name}");

                if (isDungeon)
                {
                    // 地牢模式：世界/生成器由 DungeonManager 管理，UI 显示房间数而非计时
                    UIManager uiManager = FindObjectOfType<UIManager>();
                    if (uiManager != null)
                    {
                        uiManager.SetTimerMode(false);
                        uiManager.SetRoomDisplay(1);
                    }
                    Debug.Log("🏰 地牢模式：DungeonManager 接管房间生成");
                }
                else
                {
                    Debug.Log($"   基础生成间隔: {currentSpawnInterval}秒");
                    Debug.Log($"   基础敌人上限: {currentMaxEnemyCount}");

                    if (infiniteSpawner != null)
                    {
                        infiniteSpawner.EnableSpawning();
                        Debug.Log("✅ InfiniteEnemySpawner 生成已启用");
                    }

                    UIManager uiManager = FindObjectOfType<UIManager>();
                    if (uiManager != null)
                    {
                        uiManager.SetTimerMode(true);
                    }
                }
            }
                else
                {
                    if (normalSpawner != null)
                {
                    normalSpawner.EnableSpawning();
                    Debug.Log("✅ EnemySpawner 生成已启用（普通模式）");
                }

                UIManager uiManager = FindObjectOfType<UIManager>();
                if (uiManager != null)
                {
                    uiManager.SetTimerMode(false);
                }
            }

            ApplyCurrentDifficultyToSpawner();
            ApplyBuffDifficultySettings();
            NotifyTimerVisibility();

            string timeDisplay = isInfinite ? "无限" : timeLimit + "秒";
            Debug.Log($"✅ 游戏开始！场景: {SceneManager.GetActiveScene().name}, 难度: {currentDifficulty.difficultyName}, 时间: {timeDisplay}");
        }
    }

    void Update()
    {
        RewardVideoAdService.TickWatchdog();
        if (!isGameRunning || isGameOver) return;

        if (currentDifficulty != null && currentDifficulty.IsInfiniteMode())
        {
            if (isDungeon) return; // 地牢模式：难度按房间序号递增，由 DungeonManager 驱动，不按时间
            if (currentDifficulty.enableScaling)
            {
                HandleScaling();
            }
            return;
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
                currentDifficulty.spawnPerInterval + (scalingLevel * currentDifficulty.spawnPerIntervalStep),
                currentDifficulty.spawnPerIntervalMax
            );

            currentSpeedMultiplier = currentDifficulty.enemySpeedMultiplier * Mathf.Min(
                1f + (scalingLevel * currentDifficulty.speedMultiplierStep),
                currentDifficulty.speedMultiplierMax
            );

            currentHealthMultiplier = currentDifficulty.enemyHealthMultiplier * Mathf.Min(
                1f + (scalingLevel * currentDifficulty.healthMultiplierStep),
                currentDifficulty.healthMultiplierMax
            );

            currentDamageMultiplier = currentDifficulty.enemyDamageMultiplier * Mathf.Min(
                1f + (scalingLevel * currentDifficulty.damageMultiplierStep),
                currentDifficulty.damageMultiplierMax
            );

            if (currentDifficulty.enableMaxLimitScaling)
            {
                currentMaxEnemyCount = Mathf.Min(
                    currentDifficulty.maxEnemyCount + (scalingLevel * currentDifficulty.maxEnemyCountStep),
                    currentDifficulty.maxEnemyCountMax
                );
            }

            ApplyCurrentDifficultyToSpawner();

            Debug.Log($"🔄 无限模式成长 - 等级: {scalingLevel}");
            Debug.Log($"   生成间隔: {currentSpawnInterval}秒");
            Debug.Log($"   每次生成: {currentSpawnPerInterval}个");
            Debug.Log($"   敌人上限: {currentMaxEnemyCount}");
            Debug.Log($"   速度倍率: {currentSpeedMultiplier:F2}");
            Debug.Log($"   血量倍率: {currentHealthMultiplier:F2}");
            Debug.Log($"   攻击倍率: {currentDamageMultiplier:F2}");
        }
    }

    void ApplyCurrentDifficultyToSpawner()
    {
        if (currentDifficulty == null) return;

        bool isInfinite = currentDifficulty.IsInfiniteMode();

        if (isInfinite)
        {
            if (infiniteSpawner != null)
            {
                infiniteSpawner.ApplyScalingParameters(
                    currentSpawnInterval,
                    currentSpawnPerInterval,
                    currentSpeedMultiplier,
                    currentHealthMultiplier,
                    currentDamageMultiplier,
                    currentDifficulty.enableMaxLimit,
                    currentMaxEnemyCount,
                    currentDifficulty.enableCooldown,
                    currentDifficulty.cooldownTime,
                    currentDifficulty.allowedEnemyPrefabs
                );
            }
        }
        else
        {
            // 普通模式：生成节奏与属性倍率由 EnemySpawner.difficultyTiers 统一管理，
            // 参数已在 InitializeSpawner→ApplyDifficultySettings 设置，此处无需下发。
        }
    }

    // 按当前难度把 Buff 生产数量与时间下发到场景中的 Buff Manager。
    // 场景里的 Buff Manager 只用兜底值，真正生效的以难度数据为准。
    void ApplyBuffDifficultySettings()
    {
        if (currentDifficulty == null) return;

        RandomBuffSpawner buffSpawner = FindObjectOfType<RandomBuffSpawner>();
        if (buffSpawner == null)
        {
            Debug.LogWarning("⚠️ 场景中未找到 RandomBuffSpawner（Buff Manager），本局不会产出 Buff");
            return;
        }

        buffSpawner.ApplyDifficultySettings(currentDifficulty);
        Debug.Log($"🎁 Buff 参数已按难度 [{currentDifficulty.difficultyName}] 下发: " +
                  $"间隔={currentDifficulty.buffSpawnInterval}s, 上限={currentDifficulty.buffMaxCount}, " +
                  $"存活={currentDifficulty.buffLifeTime}s, 开局={currentDifficulty.buffInitialCount}个");
    }

    void NotifyTimerVisibility()
    {
        if (OnTimerVisibilityChanged == null) return;

        bool isInfinite = currentDifficulty != null && currentDifficulty.IsInfiniteMode();
        OnTimerVisibilityChanged(!isInfinite);
    }

    public void GameOver(bool isWin)
    {
        if (isGameOver || hasProcessedGameOver)
        {
            Debug.Log("⚠️ GameOver 已经处理过，跳过重复调用");
            return;
        }

        Debug.Log($"🎯 ===== GameOver 被调用 ===== | isWin: {isWin}");

        isGameRunning = false;
        isGameOver = true;
        hasProcessedGameOver = true;

        // 禁用所有生成器
        if (normalSpawner != null)
        {
            normalSpawner.DisableSpawning();
        }
        if (infiniteSpawner != null)
        {
            infiniteSpawner.DisableSpawning();
        }

        int coins = 0;
        int kills = 0;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            coins = player.GetCoins();
            kills = player.GetKills();

            // ⭐ 结算场景残留金币：把尚未收集的金币预制体也计入最终金币
            Coin[] leftoverCoins = FindObjectsOfType<Coin>();
            foreach (Coin c in leftoverCoins)
            {
                if (c == null || c.coinValue <= 0) continue;
                coins += c.coinValue;
                player.AddCoin(c.coinValue);
                Destroy(c.gameObject);
            }

            Debug.Log($"🎯 玩家数据 - 金币: {coins}, 击杀: {kills}");
        }
        else
        {
            Debug.LogWarning("⚠️ 找不到 PlayerController！");
        }

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

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            if (currentDifficulty != null)
            {
                dataManager.UpdateRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
            }
            dataManager.AddCoins(coins);
        }
        else
        {
            Debug.LogError("❌ GameDataManager.Instance 为空！使用 PlayerPrefs 降级方案");
            if (currentDifficulty != null)
            {
                SaveBestRecord(currentDifficulty.difficultyName, coins, kills, timeToSave);
            }
        }

        if (OnGameOver != null)
        {
            OnGameOver();
        }

        Debug.Log($"🏁 ===== 游戏结束完成 ===== | {currentDifficulty?.difficultyName ?? "未知"}");
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
        }

        if (kills > bestKills)
        {
            PlayerPrefs.SetInt(killsKey, kills);
            updated = true;
        }

        if (time > bestTime)
        {
            PlayerPrefs.SetFloat(timeKey, time);
            updated = true;
        }

        if (updated)
        {
            PlayerPrefs.Save();
            Debug.Log($"✅ {difficultyName} 最高纪录已更新");
        }
    }

    // ==================== 公共接口 ====================

    public float GetRemainingTime() => remainingTime;
    public float GetTimePercent() => remainingTime / timeLimit;
    public bool IsInfiniteMode() => currentDifficulty != null && currentDifficulty.IsInfiniteMode();
    public bool IsDungeonMode() => isDungeon;
    public int GetDungeonRoom() => dungeonRoom;

    public void SetDungeonRoom(int n)
    {
        dungeonRoom = n;
    }

    public float GetElapsedTime()
    {
        if (IsInfiniteMode())
            return Time.time - gameStartTime;
        return timeLimit - remainingTime;
    }

    public float GetCurrentSpeedMultiplier() => currentSpeedMultiplier;
    public float GetCurrentHealthMultiplier() => currentHealthMultiplier;
    public float GetCurrentDamageMultiplier() => currentDamageMultiplier;
    public int GetCurrentMaxEnemyCount() => currentMaxEnemyCount;

    public bool IsGameRunning() => isGameRunning && !isGameOver;
    public bool IsGameOver() => isGameOver;

    // ==================== 复活 ====================

    /// <summary>本局是否还可以看广告复活（只在第一次死亡时提供）</summary>
    public bool CanRevive()
    {
        return !isGameOver && !hasProcessedGameOver && !reviveUsedThisRun;
    }

    /// <summary>标记本局复活已用掉（后续死亡直接结算）</summary>
    public void MarkReviveUsed()
    {
        reviveUsedThisRun = true;
    }

    public void ResetGameState()
    {
        isGameRunning = false;
        isGameOver = false;
        hasProcessedGameOver = false;
        reviveUsedThisRun = false;
        scalingLevel = 0;
        scalingTimer = 0f;
        gameStartTime = 0f;
        currentMaxEnemyCount = currentDifficulty != null ? currentDifficulty.maxEnemyCount : 30;

        if (normalSpawner != null)
        {
            normalSpawner.DisableSpawning();
            normalSpawner.ResetSpawner();
        }
        if (infiniteSpawner != null)
        {
            infiniteSpawner.DisableSpawning();
            infiniteSpawner.ResetSpawner();
        }

        Debug.Log("🔄 游戏状态已重置");
    }
}