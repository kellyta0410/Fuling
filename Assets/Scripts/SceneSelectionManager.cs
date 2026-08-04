using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SceneSelectionManager : MonoBehaviour
{
    [Header("===== 数据管理 =====")]
    public GameDataManager gameDataManager;

    [Header("===== 难度配置（未开发的可以留空，不会报错）=====")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    [Header("===== UI 面板 =====")]
    public GameObject mainPanel;
    public GameObject recordsPanel;
    public GameObject characterPanel;

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

    [Header("===== 角色按钮（左侧一排，只有头像图片）=====")]
    public Button[] characterButtons;
    public Image[] characterButtonAvatars;  // 头像图片

    [Header("===== 角色详情面板（右侧）=====")]
    public CharacterDetailManager characterDetailManager;

    [Header("===== 解锁确认面板 =====")]
    public GameObject unlockConfirmPanel;
    public TextMeshProUGUI unlockConfirmText;
    public Button unlockConfirmButton;
    public Button unlockCancelButton;

    private CharacterData pendingUnlockCharacter;
    private int currentSelectedCharacterIndex = -1;

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

        if (unlockConfirmPanel != null)
            unlockConfirmPanel.SetActive(false);

        if (unlockConfirmButton != null)
            unlockConfirmButton.onClick.AddListener(ConfirmUnlock);

        if (unlockCancelButton != null)
            unlockCancelButton.onClick.AddListener(CancelUnlock);

        BindCharacterButtons();

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

    // ==================== 绑定角色按钮 ====================

    void BindCharacterButtons()
    {
        if (characterButtons == null) return;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] != null)
            {
                int index = i;
                characterButtons[i].onClick.AddListener(() => OnCharacterButtonClicked(index));
            }
        }
    }

    // ==================== 角色按钮点击 → 切换显示 ====================

    void OnCharacterButtonClicked(int index)
    {
        CharacterData[] allCharacters = gameDataManager.GetAllCharacters();
        if (index < 0 || index >= allCharacters.Length)
        {
            Debug.LogWarning($"角色索引 {index} 超出范围");
            return;
        }

        CharacterData character = allCharacters[index];
        if (character == null) return;

        currentSelectedCharacterIndex = index;

        if (characterDetailManager != null)
        {
            characterDetailManager.ShowCharacterDetail(character);
        }

        HighlightCharacterButton(index);
    }

    // ==================== 高亮角色按钮 ====================

    void HighlightCharacterButton(int selectedIndex)
    {
        if (characterButtons == null) return;

        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] == null) continue;

            ColorBlock colors = characterButtons[i].colors;
            if (i == selectedIndex)
            {
                colors.normalColor = new Color(0.8f, 0.8f, 0.2f, 1f);
            }
            else
            {
                colors.normalColor = Color.white;
            }
            characterButtons[i].colors = colors;
        }
    }

    // ==================== 刷新UI ====================

    public void RefreshAllUI()
    {
        RefreshCoinDisplay();
        RefreshPlayerName();
        RefreshRecordsDisplay();
        UpdateCharacterButtons();

        if (characterDetailManager != null)
        {
            if (currentSelectedCharacterIndex >= 0)
            {
                CharacterData[] allCharacters = gameDataManager.GetAllCharacters();
                if (currentSelectedCharacterIndex < allCharacters.Length)
                {
                    characterDetailManager.ShowCharacterDetail(allCharacters[currentSelectedCharacterIndex]);
                }
            }
            else
            {
                characterDetailManager.RefreshPanel();
            }
        }
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
                playerNameText.text = "未选择";
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
    }

    public void ShowRecordsPanel()
    {
        SetPanelActive(recordsPanel, true);
        SetPanelActive(characterPanel, false);
        RefreshRecordsDisplay();
    }

    public void ShowCharacterPanel()
    {
        SetPanelActive(recordsPanel, false);
        SetPanelActive(characterPanel, true);

        if (gameDataManager != null)
        {
            CharacterData[] allCharacters = gameDataManager.GetAllCharacters();

            if (currentSelectedCharacterIndex >= 0 && currentSelectedCharacterIndex < allCharacters.Length)
            {
                if (characterDetailManager != null)
                {
                    characterDetailManager.ShowCharacterDetail(allCharacters[currentSelectedCharacterIndex]);
                }
                HighlightCharacterButton(currentSelectedCharacterIndex);
            }
            else if (gameDataManager.CurrentCharacter != null)
            {
                for (int i = 0; i < allCharacters.Length; i++)
                {
                    if (allCharacters[i] != null &&
                        allCharacters[i].characterName == gameDataManager.CurrentCharacter.characterName)
                    {
                        currentSelectedCharacterIndex = i;
                        if (characterDetailManager != null)
                        {
                            characterDetailManager.ShowCharacterDetail(allCharacters[i]);
                        }
                        HighlightCharacterButton(i);
                        break;
                    }
                }
            }
            else if (characterDetailManager != null)
            {
                // 未选择角色时，默认预览第一个角色（梅），但不标记为已选
                if (allCharacters.Length > 0)
                    characterDetailManager.ShowCharacterDetail(allCharacters[0]);
                else
                    characterDetailManager.ClearPanel();
            }
        }
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

    // ==================== 返回主菜单 ====================

    public void GoToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    // ==================== 难度选择 ====================

    public void SelectEasy()
    {
        SelectDifficulty(easyConfig, "简单");
    }

    public void SelectNormal()
    {
        SelectDifficulty(normalConfig, "普通");
    }

    public void SelectHard()
    {
        SelectDifficulty(hardConfig, "困难");
    }

    public void SelectInfinite()
    {
        SelectDifficulty(infiniteConfig, "无限");
    }

    void SelectDifficulty(DifficultySettings difficulty, string difficultyName)
    {
        if (difficulty == null)
        {
            Debug.LogWarning($"⚠️ 【{difficultyName}】难度配置为空！该功能尚未开发，请先在 Inspector 中配置对应的 DifficultySettings。");
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
        PlayerPrefs.SetString("SelectedScene", difficulty.sceneName);

        PlayerPrefs.SetInt("GameMode", (int)difficulty.mode);
        PlayerPrefs.SetFloat("TimeLimit", difficulty.timeLimit);
        PlayerPrefs.SetFloat("SpawnInterval", difficulty.spawnInterval);
        PlayerPrefs.SetInt("SpawnPerInterval", difficulty.spawnPerInterval);
        PlayerPrefs.SetInt("MaxEnemyCount", difficulty.maxEnemyCount);
        PlayerPrefs.SetInt("MaxScalingLevel", difficulty.maxScalingLevel);
        PlayerPrefs.SetFloat("ScalingInterval", difficulty.scalingInterval);
        PlayerPrefs.SetInt("EnableScaling", difficulty.enableScaling ? 1 : 0);

        PlayerPrefs.Save();

        Debug.Log($"🎮 开始游戏！难度: {difficulty.difficultyName}，" +
                  $"模式: {difficulty.mode}，" +
                  $"场景: {difficulty.sceneName}，" +
                  $"角色: {gameDataManager.CurrentCharacter.characterName}");

        if (!string.IsNullOrEmpty(difficulty.sceneName))
        {
            SceneManager.LoadScene(difficulty.sceneName);
        }
        else
        {
            Debug.LogError($"❌ 难度 {difficulty.difficultyName} 的场景名未设置！请检查 DifficultySettings。");
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

        if (gameDataManager.IsCharacterUnlocked(character))
        {
            gameDataManager.SelectCharacter(character);
            RefreshAllUI();

            if (characterDetailManager != null)
            {
                characterDetailManager.ShowCharacterDetail(character);
            }
            return;
        }

        if (gameDataManager.TotalCoins < character.unlockCost)
        {
            Debug.Log($"金币不足！需要 {character.unlockCost}，当前 {gameDataManager.TotalCoins}");
            return;
        }

        pendingUnlockCharacter = character;
        ShowUnlockConfirmPanel(character);
    }

    // ==================== 解锁确认面板 ====================

    void ShowUnlockConfirmPanel(CharacterData character)
    {
        if (unlockConfirmPanel == null)
        {
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
            gameDataManager.SelectCharacter(character);
            RefreshAllUI();

            if (characterDetailManager != null)
            {
                characterDetailManager.ShowCharacterDetail(character);
            }

            Debug.Log($"解锁并选择角色: {character.characterName}");
        }
        else
        {
            Debug.Log($"解锁失败！");
        }
    }

    // ==================== 更新角色按钮（只有头像） ====================

    public void UpdateCharacterButtons()
    {
        if (gameDataManager == null || characterButtons == null) return;

        CharacterData[] allCharacters = gameDataManager.GetAllCharacters();

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

            // 设置头像（未解锁时显示锁定头像）
            Sprite displaySprite = character.avatarSprite;
            if (!gameDataManager.IsCharacterUnlocked(character) && character.lockedAvatarSprite != null)
                displaySprite = character.lockedAvatarSprite;

            if (characterButtonAvatars != null &&
                characterButtonAvatars.Length > i &&
                characterButtonAvatars[i] != null &&
                displaySprite != null)
            {
                characterButtonAvatars[i].sprite = displaySprite;
                characterButtonAvatars[i].preserveAspect = true;
            }

            // 所有按钮都可点击
            characterButtons[i].interactable = true;
        }
    }

    public GameDataManager GetGameDataManager()
    {
        return gameDataManager;
    }

    public void RefreshCharacterDetail()
    {
        if (characterDetailManager != null && gameDataManager.CurrentCharacter != null)
        {
            characterDetailManager.ShowCharacterDetail(gameDataManager.CurrentCharacter);
        }
    }
}