using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class SceneSelectionManager : MonoBehaviour
{
    // ⭐ 按钮点击音播放时长：切场景前等这么久，让 AudioSource.Play 播完
    private const float buttonClickDelay = 0.2f;

    [Header("===== 数据管理 =====")]
    public GameDataManager gameDataManager;

    [Header("===== 难度配置（未开发的可以留空，不会报错）=====")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings infiniteConfig;
    [Tooltip("普通模式按钮（未开放，启动时置灰但仍可点击弹提示）")]
    public Button normalModeButton;
    [Tooltip("无尽模式按钮（需通关普通模式才解锁）")]
    public Button infiniteModeButton;

    [Header("===== UI 面板 =====")]
    public GameObject mainPanel;
    public GameObject recordsPanel;
    public GameObject characterPanel;
    [Tooltip("新手引导面板：首次进入自动打开，之后仅通过 Tutorial 按钮手动打开")]
    public GameObject tutorialPanel;
    [Tooltip("引导页容器：每个子物体是一页，翻页时逐个切换显示")]
    public RectTransform tutorialPagesContainer;
    [Tooltip("引导上一页按钮：第一页时自动隐藏")]
    public Button tutorialPrevButton;
    [Tooltip("引导下一页按钮：最后一页时自动隐藏")]
    public Button tutorialNextButton;

    [Header("===== 玩家信息 =====")]
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI playerNameText;
    public Image playerAvatarImage;

    [Header("===== 记录显示 =====")]
    public TextMeshProUGUI easyCoinsText;
    public TextMeshProUGUI easyKillsText;
    public TextMeshProUGUI easyTimeText;
    public TextMeshProUGUI normalCoinsText;
    public TextMeshProUGUI normalKillsText;
    public TextMeshProUGUI normalTimeText;
    public TextMeshProUGUI infiniteKillsText;
    public TextMeshProUGUI infiniteTimeText;
    [Tooltip("无限模式的金币整行（含标签），在代码里隐藏")]
    public GameObject infiniteCoinsRow;
    [Tooltip("记录翻页容器：每个子物体是一页，翻页时逐个切换显示（内容你自己加）")]
    public RectTransform recordsPagesContainer;
    [Tooltip("上一页按钮：第一页时自动隐藏")]
    public Button recordsPrevButton;
    [Tooltip("下一页按钮：最后一页时自动隐藏")]
    public Button recordsNextButton;

    [Header("===== 角色按钮（左侧一排，只有头像图片）=====")]
    public Button[] characterButtons;
    public Image[] characterButtonAvatars;  // 头像图片

    [Header("===== 角色详情面板（右侧）=====")]
    public CharacterDetailManager characterDetailManager;

    [Header("===== 提示气泡（可选）=====")]
    [Tooltip("显示“未开放/金币不足”等提示的文本，留空则仅打印日志")]
    public TextMeshProUGUI hintToastText;
    [Tooltip("提示文字后面的背景 Panel（可选），显示提示时一起打开")]
    public GameObject hintToastPanel;
    private Coroutine hintToastCoroutine;

    private int currentSelectedCharacterIndex = -1;

    void Start()
    {
        // 无条件使用单例，避免引用到场景里被销毁的 GameDataManager 对象
        gameDataManager = GameDataManager.Instance;

        if (gameDataManager == null)
        {
            Debug.LogError("GameDataManager 不存在！");
            return;
        }

        gameDataManager.OnDataChanged += RefreshAllUI;

        BindCharacterButtons();

        SetupNormalModeButton();
        SetupInfiniteModeButton();

        ShowMainPanel();
        RefreshAllUI();

        SetupTutorialOnFirstEntry();
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
        if (gameDataManager == null)
            gameDataManager = GameDataManager.Instance;

        if (gameDataManager != null)
        {
            RefreshAllUI();
            SetupNormalModeButton(); // 游戏结束返回时刷新普通按钮解锁状态
            SetupInfiniteModeButton(); // 刷新无尽按钮解锁状态
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
        if (gameDataManager == null) return;

        CharacterData current = gameDataManager.CurrentCharacter;
        bool hasCharacter = current != null;

        if (playerNameText != null)
            playerNameText.text = hasCharacter ? current.characterName : "未选择";

        if (playerAvatarImage != null)
        {
            // 始终显示角色头像（未选择角色时隐藏）
            playerAvatarImage.gameObject.SetActive(hasCharacter);
            if (hasCharacter)
            {
                // 优先使用角色大图（characterImage），无大图时回退到头像
                playerAvatarImage.sprite = current.characterImage != null
                    ? current.characterImage
                    : current.GetAvatarSprite(true);
                playerAvatarImage.color = Color.white;
                playerAvatarImage.preserveAspect = true;
            }
        }
    }

    public void RefreshRecordsDisplay()
    {
        if (gameDataManager == null) return;

        UpdateRecordUI("简单", easyCoinsText, easyKillsText, easyTimeText);
        UpdateRecordUI("普通", normalCoinsText, normalKillsText, normalTimeText);
        // 无限模式：不显示金币（coin text 已移除），用“通关数量”代替计时
        UpdateRecordUI("无限模式", null, infiniteKillsText, infiniteTimeText,
            useRoomsInsteadOfTime: true);

        // 直接把无限模式的金币整行（含标签）隐藏
        if (infiniteCoinsRow != null) infiniteCoinsRow.SetActive(false);
    }

    public void ClearAllRecords()
    {
        if (gameDataManager == null) return;

        string[] difficulties = { "简单", "普通", "无限模式" };
        foreach (string diff in difficulties)
        {
            PlayerPrefs.DeleteKey(diff + "_BestCoins");
            PlayerPrefs.DeleteKey(diff + "_BestKills");
            PlayerPrefs.DeleteKey(diff + "_BestTime");
            PlayerPrefs.DeleteKey(diff + "_BestRooms");   // 通关数量
            PlayerPrefs.DeleteKey(diff + "_RecordCoins");
            PlayerPrefs.DeleteKey(diff + "_RecordKills");
            PlayerPrefs.DeleteKey(diff + "_RecordTime");
            PlayerPrefs.DeleteKey(diff + "_RecordRooms");
        }
        PlayerPrefs.Save();
        RefreshRecordsDisplay();
        Debug.Log("所有记录已清空");
    }

    void UpdateRecordUI(string difficultyName, TextMeshProUGUI coinsText, TextMeshProUGUI killsText, TextMeshProUGUI timeText,
        bool useRoomsInsteadOfTime = false)
    {
        GameRecord record = gameDataManager.GetRecord(difficultyName);

        if (coinsText != null)
            coinsText.text = record.HasRecord ? record.bestCoins.ToString() : "--";

        if (killsText != null)
            killsText.text = record.HasRecord ? record.bestKills.ToString() : "--";

        if (timeText != null)
        {
            if (useRoomsInsteadOfTime)
            {
                timeText.text = record.HasRecord ? record.bestRooms.ToString() : "--";
            }
            else if (record.HasRecord && record.bestTime > 0)
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
        ShowRecordsPage(0);
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
            if (!active) HintToast.Hide(); // 关闭/切换面板时提示直接消失
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

    // ==================== 新手引导面板 ====================

    const string TutorialShownKey = "SelectionTutorialShown";
    private int currentTutorialPage = 0;

    void SetupTutorialOnFirstEntry()
    {
        if (tutorialPanel == null) return;

        // 首次进入 Selection 场景时自动打开引导面板
        if (PlayerPrefs.GetInt(TutorialShownKey, 0) == 0)
        {
            PlayerPrefs.SetInt(TutorialShownKey, 1);
            PlayerPrefs.Save();
            ShowTutorialPanel();
        }
        else
        {
            SetPanelActive(tutorialPanel, false);
        }
    }

    // Tutorial 按钮点击时调用：始终可手动打开
    public void ShowTutorialPanel()
    {
        SetPanelActive(tutorialPanel, true);
        ShowTutorialPage(0);
    }

    public void CloseTutorialPanel()
    {
        SetPanelActive(tutorialPanel, false);
    }

    // 上一页 / 下一页（箭头按钮绑定）
    public void TutorialPrevPage()
    {
        int total = GetTutorialPageCount();
        if (total <= 0) return;
        ShowTutorialPage(Mathf.Max(0, currentTutorialPage - 1));
    }

    public void TutorialNextPage()
    {
        int total = GetTutorialPageCount();
        if (total <= 0) return;
        ShowTutorialPage(Mathf.Min(total - 1, currentTutorialPage + 1));
    }

    int GetTutorialPageCount()
    {
        return tutorialPagesContainer != null ? tutorialPagesContainer.childCount : 0;
    }

    void ShowTutorialPage(int index)
    {
        if (tutorialPagesContainer == null) return;

        int total = tutorialPagesContainer.childCount;
        if (total <= 0) return;

        currentTutorialPage = index;
        for (int i = 0; i < total; i++)
        {
            tutorialPagesContainer.GetChild(i).gameObject.SetActive(i == currentTutorialPage);
        }

        // 第一页隐藏“上一页”，最后一页隐藏“下一页”
        if (tutorialPrevButton != null) tutorialPrevButton.gameObject.SetActive(currentTutorialPage > 0);
        if (tutorialNextButton != null) tutorialNextButton.gameObject.SetActive(currentTutorialPage < total - 1);
    }

    // ==================== 记录面板翻页 ====================
    private int currentRecordsPage = 0;

    public void RecordsPrevPage()
    {
        int total = GetRecordsPageCount();
        if (total <= 0) return;
        ShowRecordsPage(Mathf.Max(0, currentRecordsPage - 1));
    }

    public void RecordsNextPage()
    {
        int total = GetRecordsPageCount();
        if (total <= 0) return;
        ShowRecordsPage(Mathf.Min(total - 1, currentRecordsPage + 1));
    }

    int GetRecordsPageCount()
    {
        return recordsPagesContainer != null ? recordsPagesContainer.childCount : 0;
    }

    void ShowRecordsPage(int index)
    {
        if (recordsPagesContainer == null) return;

        int total = recordsPagesContainer.childCount;
        if (total <= 0) return;

        currentRecordsPage = index;
        for (int i = 0; i < total; i++)
        {
            recordsPagesContainer.GetChild(i).gameObject.SetActive(i == currentRecordsPage);
        }

        // 第一页隐藏“上一页”，最后一页隐藏“下一页”
        if (recordsPrevButton != null) recordsPrevButton.gameObject.SetActive(currentRecordsPage > 0);
        if (recordsNextButton != null) recordsNextButton.gameObject.SetActive(currentRecordsPage < total - 1);
    }

    // ==================== 返回主菜单 ====================

    public void GoToMainMenu()
    {
        StartCoroutine(LoadSceneAfterButtonClick("MainMenu"));
    }

    // ⭐ 等 0.2s 让按钮点击音放完再切场景
    IEnumerator LoadSceneAfterButtonClick(string sceneName)
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
        SceneManager.LoadScene(sceneName);
    }

    // ==================== 难度选择 ====================

    public void SelectEasy()
    {
        SelectDifficulty(easyConfig, "简单");
    }

    public void SelectNormal()
    {
        if (gameDataManager == null) return;

        if (!gameDataManager.IsNormalUnlocked)
        {
            ShowHint("先完成一局【简单】模式即可解锁！");
            return;
        }

        SelectDifficulty(normalConfig, "普通");
    }

    public void SelectInfinite()
    {
        if (gameDataManager == null) return;
        if (!gameDataManager.IsInfiniteUnlocked)
        {
            ShowHint("先完成一局【普通】模式即可解锁！");
            return;
        }
        SelectDifficulty(infiniteConfig, "无限模式");
    }

    void SelectDifficulty(DifficultySettings difficulty, string difficultyName)
    {
        if (difficulty == null)
        {
            Debug.LogWarning($"⚠️ 【{difficultyName}】难度配置为空！该功能尚未开发，请先在 Inspector 中配置对应的 DifficultySettings。");
            return;
        }

        // 跨场景记录选中的难度（GameManager 非持久单例，切场景会丢，用静态 holder 携带到地牢场景）
        SelectedDifficultyHolder.Current = difficulty;
        if (GameManager.Instance != null) GameManager.Instance.currentDifficulty = difficulty;

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
            // ⭐ 先进 Loading 场景，把目标关卡场景异步加载完再切换（避免切场景卡顿/白屏）
            // 先等 0.2s 让按钮点击音放完再切，否则一进 Loading 点击音被掐断
            StartCoroutine(LoadSceneAfterButtonClick("Loading"));
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
            ShowHint($"金币不足！解锁需 {character.unlockCost} 金币（当前 {gameDataManager.TotalCoins}）");
            return;
        }

        UnlockCharacterDirect(character);
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

    // ==================== 普通模式按钮（玩过一局简单才解锁） ====================

    void SetupNormalModeButton()
    {
        if (normalModeButton == null) return;

        normalModeButton.onClick.RemoveListener(SelectNormal);
        normalModeButton.onClick.AddListener(SelectNormal);

        bool unlocked = gameDataManager != null && gameDataManager.IsNormalUnlocked;

        normalModeButton.interactable = true;
        if (normalModeButton.targetGraphic != null)
            normalModeButton.targetGraphic.color = unlocked
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.55f, 0.55f, 0.55f, 0.7f);
    }

    void SetupInfiniteModeButton()
    {
        if (infiniteModeButton == null) return;

        infiniteModeButton.onClick.RemoveListener(SelectInfinite);
        infiniteModeButton.onClick.AddListener(SelectInfinite);

        bool unlocked = gameDataManager != null && gameDataManager.IsInfiniteUnlocked;

        infiniteModeButton.interactable = true;
        if (infiniteModeButton.targetGraphic != null)
            infiniteModeButton.targetGraphic.color = unlocked
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.55f, 0.55f, 0.55f, 0.7f);
    }

    // ==================== 提示气泡 ====================

    public void ShowHint(string message)
    {
        TMP_FontAsset font = playerNameText != null ? playerNameText.font : null;
        HintToast.Show(message, font, hintToastText, hintToastPanel);
    }

    public void RefreshCharacterDetail()
    {
        if (characterDetailManager != null && gameDataManager.CurrentCharacter != null)
        {
            characterDetailManager.ShowCharacterDetail(gameDataManager.CurrentCharacter);
        }
    }
}