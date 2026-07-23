using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneSelectionManager : MonoBehaviour
{
    [Header("===== 数据管理 =====")]
    public GameDataManager gameDataManager;

    [Header("===== 难度配置 =====")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    [Header("===== UI 面板 =====")]
    public GameObject mainPanel;
    public GameObject recordsPanel;
    public GameObject characterPanel;
    public GameObject upgradePanel;

    [Header("===== 玩家信息 =====")]
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI playerNameText;

    [Header("===== 记录显示 =====")]
    public TextMeshProUGUI easyCoinsText;
    public TextMeshProUGUI easyKillsText;
    public TextMeshProUGUI easyTimeText;
    public TextMeshProUGUI normalCoinsText;
    public TextMeshProUGUI normalKillsText;
    public TextMeshProUGUI normalTimeText;
    public TextMeshProUGUI hardCoinsText;
    public TextMeshProUGUI hardKillsText;
    public TextMeshProUGUI hardTimeText;
    public TextMeshProUGUI infiniteCoinsText;
    public TextMeshProUGUI infiniteKillsText;
    public TextMeshProUGUI infiniteTimeText;

    [Header("===== 场景 =====")]
    public string gameSceneName = "GameScene";

    void Start()
    {
        // 获取 GameDataManager
        if (gameDataManager == null)
        {
            gameDataManager = GameDataManager.Instance;
        }

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！请确保场景中有 GameDataManager");
            return;
        }

        // 监听数据变化
        gameDataManager.OnDataChanged += RefreshAllUI;

        // 初始化UI
        ShowMainPanel();
        RefreshAllUI();
    }

    void OnDestroy()
    {
        if (gameDataManager != null)
        {
            gameDataManager.OnDataChanged -= RefreshAllUI;
        }
    }

    void OnEnable()
    {
        if (gameDataManager != null)
        {
            RefreshAllUI();
        }
    }

    // ==================== 刷新UI ====================

    public void RefreshAllUI()
    {
        RefreshCoinDisplay();
        RefreshPlayerName();
        RefreshRecordsDisplay();
    }

    public void RefreshCoinDisplay()
    {
        if (coinText != null && gameDataManager != null)
        {
            coinText.text = $"🪙 {gameDataManager.TotalCoins}";
        }
    }

    public void RefreshPlayerName()
    {
        if (playerNameText != null && gameDataManager != null)
        {
            if (gameDataManager.CurrentCharacter != null)
            {
                playerNameText.text = $"👤 {gameDataManager.CurrentCharacter.characterName}";
            }
            else
            {
                playerNameText.text = "👤 未选择";
            }
        }
    }

    public void RefreshRecordsDisplay()
    {
        if (gameDataManager == null) return;

        UpdateRecordUI("简单", easyCoinsText, easyKillsText, easyTimeText);
        UpdateRecordUI("普通", normalCoinsText, normalKillsText, normalTimeText);
        UpdateRecordUI("困难", hardCoinsText, hardKillsText, hardTimeText);
        UpdateRecordUI("无限", infiniteCoinsText, infiniteKillsText, infiniteTimeText);
    }

    void UpdateRecordUI(string difficultyName, TextMeshProUGUI coinsText, TextMeshProUGUI killsText, TextMeshProUGUI timeText)
    {
        GameRecord record = gameDataManager.GetRecord(difficultyName);

        if (coinsText != null)
            coinsText.text = record.HasRecord ? record.bestCoins.ToString() : "--";

        if (killsText != null)
            killsText.text = record.HasRecord ? record.bestKills.ToString() : "--";

        if (timeText != null)
        {
            if (record.HasRecord && record.bestTime > 0)
            {
                int minutes = Mathf.FloorToInt(record.bestTime / 60);
                int seconds = Mathf.FloorToInt(record.bestTime % 60);
                timeText.text = $"{minutes:00}:{seconds:00}";
            }
            else
            {
                timeText.text = "--:--";
            }
        }
    }

    // ==================== 面板切换 ====================

    public void ShowMainPanel()
    {
        SetPanelActive(mainPanel, true);
        SetPanelActive(recordsPanel, false);
        SetPanelActive(characterPanel, false);
        SetPanelActive(upgradePanel, false);
    }

    public void ShowRecordsPanel()
    {
        SetPanelActive(mainPanel, false);
        SetPanelActive(recordsPanel, true);
        SetPanelActive(characterPanel, false);
        SetPanelActive(upgradePanel, false);
        RefreshRecordsDisplay();
    }

    public void ShowCharacterPanel()
    {
        SetPanelActive(mainPanel, false);
        SetPanelActive(recordsPanel, false);
        SetPanelActive(characterPanel, true);
        SetPanelActive(upgradePanel, false);
    }

    public void ShowUpgradePanel()
    {
        SetPanelActive(mainPanel, false);
        SetPanelActive(recordsPanel, false);
        SetPanelActive(characterPanel, false);
        SetPanelActive(upgradePanel, true);
        RefreshCoinDisplay();
    }

    void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    public void CloseRecordsPanel() => ShowMainPanel();
    public void CloseCharacterPanel() => ShowMainPanel();
    public void CloseUpgradePanel() => ShowMainPanel();

    public void ToggleRecordsPanel()
    {
        if (recordsPanel != null && recordsPanel.activeSelf)
            ShowMainPanel();
        else
            ShowRecordsPanel();
    }

    public void ToggleCharacterPanel()
    {
        if (characterPanel != null && characterPanel.activeSelf)
            ShowMainPanel();
        else
            ShowCharacterPanel();
    }

    public void ToggleUpgradePanel()
    {
        if (upgradePanel != null && upgradePanel.activeSelf)
            ShowMainPanel();
        else
            ShowUpgradePanel();
    }

    // ==================== 返回主菜单 ====================

    public void GoToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    // ==================== 难度选择 ====================

    public void SelectEasy()
    {
        SelectDifficulty(easyConfig);
    }

    public void SelectNormal()
    {
        SelectDifficulty(normalConfig);
    }

    public void SelectHard()
    {
        SelectDifficulty(hardConfig);
    }

    public void SelectInfinite()
    {
        SelectDifficulty(infiniteConfig);
    }

    void SelectDifficulty(DifficultySettings difficulty)
    {
        if (difficulty == null)
        {
            Debug.LogError("难度配置为空，请检查 Inspector");
            return;
        }

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！");
            return;
        }

        if (gameDataManager.CurrentCharacter == null)
        {
            Debug.LogWarning("请先选择一个角色！");
            ShowCharacterPanel();
            return;
        }

        PlayerPrefs.SetString("SelectedDifficulty", difficulty.difficultyName);
        PlayerPrefs.SetString("SelectedCharacter", gameDataManager.CurrentCharacter.characterName);
        PlayerPrefs.Save();

        Debug.Log($"开始游戏！难度: {difficulty.difficultyName}，角色: {gameDataManager.CurrentCharacter.characterName}");

        if (!string.IsNullOrEmpty(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
        }
        else
        {
            Debug.LogError("游戏场景名未设置！");
        }
    }

    // ==================== 角色选择 ====================

    public void SelectCharacterByName(string characterName)
    {
        if (gameDataManager == null) return;

        CharacterData character = gameDataManager.GetCharacterData(characterName);
        if (character == null)
        {
            Debug.LogWarning($"未找到角色: {characterName}");
            return;
        }

        if (!gameDataManager.IsCharacterUnlocked(character))
        {
            if (gameDataManager.UnlockCharacter(character))
            {
                Debug.Log($"解锁角色: {characterName}");
            }
            else
            {
                Debug.Log($"金币不足，无法解锁 {characterName}");
                return;
            }
        }

        gameDataManager.SelectCharacter(character);
        RefreshAllUI();
    }

    public GameDataManager GetGameDataManager()
    {
        return gameDataManager;
    }
}