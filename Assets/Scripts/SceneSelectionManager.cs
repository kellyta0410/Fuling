using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

    [Header("===== 角色按钮 =====")]
    public Button[] characterButtons;
    public TextMeshProUGUI[] characterButtonTexts;  

    [Header("===== 解锁确认面板 =====")]
    public GameObject unlockConfirmPanel;
    public TextMeshProUGUI unlockConfirmText;
    public Button unlockConfirmButton;
    public Button unlockCancelButton;

    // 当前准备解锁的角色
    private CharacterData pendingUnlockCharacter;

    void Start()
    {
        if (gameDataManager == null)
        {
            gameDataManager = GameDataManager.Instance;
        }

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！");
            return;
        }

        gameDataManager.OnDataChanged += RefreshAllUI;

        // 默认隐藏确认面板
        if (unlockConfirmPanel != null)
            unlockConfirmPanel.SetActive(false);

        // 绑定确认/取消按钮
        if (unlockConfirmButton != null)
            unlockConfirmButton.onClick.AddListener(ConfirmUnlock);

        if (unlockCancelButton != null)
            unlockCancelButton.onClick.AddListener(CancelUnlock);

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
        UpdateCharacterButtons();
    }

    public void RefreshCoinDisplay()
    {
        if (coinText != null && gameDataManager != null)
        {
            coinText.text = $"{gameDataManager.TotalCoins}";
        }
    }

    public void RefreshPlayerName()
    {
        if (playerNameText != null && gameDataManager != null)
        {
            if (gameDataManager.CurrentCharacter != null)
            {
                playerNameText.text = $"{gameDataManager.CurrentCharacter.characterName}";
            }
            else
            {
                playerNameText.text = "havent choose";
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
        SetPanelActive(recordsPanel, true);
        SetPanelActive(characterPanel, false);
        SetPanelActive(upgradePanel, false);
        RefreshRecordsDisplay();
    }

    public void ShowCharacterPanel()
    {
        SetPanelActive(recordsPanel, false);
        SetPanelActive(characterPanel, true);
        SetPanelActive(upgradePanel, false);
    }

    public void ShowUpgradePanel()
    {
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

    public void CloseRecordsPanel()
    {
        SetPanelActive(recordsPanel, false);
    }

    public void CloseCharacterPanel()
    {
        SetPanelActive(characterPanel, false);
    }

    public void CloseUpgradePanel()
    {
        SetPanelActive(upgradePanel, false);
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

        Debug.Log($"开始游戏！难度: {difficulty.difficultyName}，场景: {difficulty.sceneName}，角色: {gameDataManager.CurrentCharacter.characterName}");

        if (!string.IsNullOrEmpty(difficulty.sceneName))
        {
            SceneManager.LoadScene(difficulty.sceneName);
        }
        else
        {
            Debug.LogError($"难度 {difficulty.difficultyName} 的场景名未设置！");
        }
    }

    // ==================== 角色选择 ====================

    public void SelectCharacter(CharacterData character)
    {
        if (gameDataManager == null) return;

        if (character == null)
        {
            Debug.LogWarning("角色数据为空");
            return;
        }

        // ⭐ 如果已经解锁 → 直接选择
        if (gameDataManager.IsCharacterUnlocked(character))
        {
            gameDataManager.SelectCharacter(character);
            RefreshAllUI();
            return;
        }

        // ⭐ 未解锁 → 检查金币是否足够
        if (gameDataManager.TotalCoins < character.unlockCost)
        {
            Debug.Log($"金币不足！需要 {character.unlockCost}，当前 {gameDataManager.TotalCoins}");
            // 可以显示一个提示（可选）
            return;
        }

        // ⭐ 金币足够 → 显示确认面板
        pendingUnlockCharacter = character;
        ShowUnlockConfirmPanel(character);
    }

    // ==================== 解锁确认面板 ====================

    void ShowUnlockConfirmPanel(CharacterData character)
    {
        if (unlockConfirmPanel == null)
        {
            // 如果没有确认面板，直接解锁（降级方案）
            UnlockCharacterDirect(character);
            return;
        }

        if (unlockConfirmText != null)
        {
            unlockConfirmText.text = $"是否花费 {character.unlockCost} 金币解锁\n<color=#FFD700>{character.characterName}</color>？";
        }

        unlockConfirmPanel.SetActive(true);
    }

    void ConfirmUnlock()
    {
        if (pendingUnlockCharacter == null) return;

        UnlockCharacterDirect(pendingUnlockCharacter);
        pendingUnlockCharacter = null;

        if (unlockConfirmPanel != null)
            unlockConfirmPanel.SetActive(false);
    }

    void CancelUnlock()
    {
        pendingUnlockCharacter = null;

        if (unlockConfirmPanel != null)
            unlockConfirmPanel.SetActive(false);
    }

    void UnlockCharacterDirect(CharacterData character)
    {
        if (gameDataManager.UnlockCharacter(character))
        {
            // 解锁成功后自动选中该角色
            gameDataManager.SelectCharacter(character);
            RefreshAllUI();
            Debug.Log($"解锁并选择角色: {character.characterName}");
        }
        else
        {
            Debug.Log($"解锁失败！");
        }
    }

    // ==================== 更新角色按钮状态 ====================

    public void UpdateCharacterButtons()
    {
        if (gameDataManager == null) return;

        CharacterData[] allCharacters = gameDataManager.GetAllCharacters();
        CharacterData currentCharacter = gameDataManager.CurrentCharacter;
        int totalCoins = gameDataManager.TotalCoins;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] == null) continue;

            if (i >= allCharacters.Length)
            {
                characterButtons[i].gameObject.SetActive(false);
                continue;
            }

            characterButtons[i].gameObject.SetActive(true);

            CharacterData character = allCharacters[i];
            bool isUnlocked = gameDataManager.IsCharacterUnlocked(character);
            bool isSelected = currentCharacter != null && character != null &&
                              character.characterName == currentCharacter.characterName;
            bool canAfford = totalCoins >= character.unlockCost;

            // ========== 设置按钮文字 ==========
            if (characterButtonTexts != null && characterButtonTexts.Length > i && characterButtonTexts[i] != null)
            {
                TextMeshProUGUI text = characterButtonTexts[i];

                if (isSelected)
                {
                    text.text = "selected";
                    text.color = Color.green;
                }
                else if (isUnlocked)
                {
                    text.text = "select";
                    text.color = Color.white;
                }
                else if (canAfford)
                {
                    text.text = $"Unclock ({character.unlockCost})";
                    text.color = Color.yellow;
                }
                else
                {
                    text.text = $"{character.unlockCost}";
                    text.color = Color.gray;
                }
            }

            // ========== 设置按钮交互状态 ==========
            if (isSelected)
            {
                characterButtons[i].interactable = false;
            }
            else if (isUnlocked)
            {
                characterButtons[i].interactable = true;
            }
            else if (canAfford)
            {
                characterButtons[i].interactable = true;   // 可点击 → 弹出确认
            }
            else
            {
                characterButtons[i].interactable = false;  // 金币不足
            }
        }
    }

    public GameDataManager GetGameDataManager()
    {
        return gameDataManager;
    }
}