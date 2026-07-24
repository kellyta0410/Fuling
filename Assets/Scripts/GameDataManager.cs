using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance;

    [Header("===== 角色列表（直接在 Inspector 拖入） =====")]
    public List<CharacterData> allCharacters = new List<CharacterData>();

    [Header("玩家数据")]
    public int totalCoins = 0;
    public string selectedCharacterName = "";
    public List<string> unlockedCharacterNames = new List<string>();

    [Header("技能数据（暂时保留）")]
    public List<SkillSaveEntry> skillLevels = new List<SkillSaveEntry>();

    [Header("默认值")]
    public string defaultCharacterName = "Mei";
    public int startingCoins = 100;

    private CharacterData currentCharacter;
    private Dictionary<string, SkillData> skillCache = new Dictionary<string, SkillData>();

    public event System.Action OnDataChanged;

    // ==================== 属性 ====================

    public CharacterData CurrentCharacter
    {
        get
        {
            if (currentCharacter == null)
            {
                currentCharacter = GetCharacterData(selectedCharacterName);
            }
            return currentCharacter;
        }
    }

    public int TotalCoins
    {
        get => totalCoins;
        set
        {
            totalCoins = Mathf.Max(0, value);
            SaveData();
            OnDataChanged?.Invoke();
        }
    }

    // ==================== Unity ====================

    void Awake()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        int instanceID = gameObject.GetInstanceID();

        Debug.Log($"🎮 GameDataManager.Awake() - 场景: {sceneName}, 实例ID: {instanceID}, Instance: {(Instance != null ? Instance.gameObject.GetInstanceID().ToString() : "空")}");

        if (Instance != null)
        {
            if (Instance == this)
            {
                Debug.Log($"✅ GameDataManager 实例已存在，保留 (场景: {sceneName})");
                LoadData();
                return;
            }

            Debug.Log($"⚠️ GameDataManager 已存在，销毁当前对象 (场景: {sceneName})");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log($"✅ GameDataManager 实例创建 (场景: {sceneName})，DontDestroyOnLoad 已设置");

        LoadAllSkills();
        LoadData();
        InitializeDefaultData();

        Debug.Log($"✅ GameDataManager 初始化完成，总金币: {totalCoins}");
    }

    // ==================== 初始化 ====================

    void InitializeDefaultData()
    {
        if (unlockedCharacterNames.Count == 0)
        {
            unlockedCharacterNames.Add(defaultCharacterName);
            selectedCharacterName = defaultCharacterName;
            SaveData();
        }

        if (totalCoins == 0)
        {
            totalCoins = startingCoins;
            SaveData();
        }

        if (!string.IsNullOrEmpty(selectedCharacterName))
        {
            currentCharacter = GetCharacterData(selectedCharacterName);
        }
    }

    // ==================== 加载技能 ====================

    void LoadAllSkills()
    {
        skillCache.Clear();
        SkillData[] allSkills = Resources.LoadAll<SkillData>("Skills");

        foreach (SkillData skill in allSkills)
        {
            if (!skillCache.ContainsKey(skill.skillID))
            {
                skillCache.Add(skill.skillID, skill);
            }
        }

        Debug.Log($"加载了 {skillCache.Count} 个技能");
    }

    // ==================== 角色管理 ====================

    public CharacterData GetCharacterData(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return allCharacters.FirstOrDefault(c => c != null && c.characterName == name);
    }

    public CharacterData[] GetAllCharacters()
    {
        return allCharacters.Where(c => c != null).ToArray();
    }

    public bool IsCharacterUnlocked(CharacterData character)
    {
        if (character == null) return false;
        return unlockedCharacterNames.Contains(character.characterName);
    }

    public bool IsCharacterUnlocked(string characterName)
    {
        return unlockedCharacterNames.Contains(characterName);
    }

    public bool UnlockCharacter(CharacterData character)
    {
        if (character == null) return false;
        if (IsCharacterUnlocked(character))
        {
            return true;
        }

        if (TotalCoins < character.unlockCost)
        {
            Debug.Log($"金币不足！需要 {character.unlockCost}，当前 {TotalCoins}");
            return false;
        }

        TotalCoins -= character.unlockCost;
        unlockedCharacterNames.Add(character.characterName);
        SaveData();
        OnDataChanged?.Invoke();
        Debug.Log($"解锁角色: {character.characterName}");
        return true;
    }

    public void SelectCharacter(CharacterData character)
    {
        if (character == null) return;
        if (!IsCharacterUnlocked(character))
        {
            Debug.LogWarning($"角色 {character.characterName} 未解锁");
            return;
        }

        selectedCharacterName = character.characterName;
        currentCharacter = character;
        SaveData();
        OnDataChanged?.Invoke();
        Debug.Log($"选择角色: {character.characterName}");
    }

    // ==================== 记录管理 ====================

    /// <summary>
    /// 获取指定难度的最佳记录
    /// </summary>
    public GameRecord GetRecord(string difficultyName)
    {
        GameRecord record = new GameRecord
        {
            difficultyName = difficultyName,
            bestCoins = PlayerPrefs.GetInt($"{difficultyName}_BestCoins", 0),
            bestKills = PlayerPrefs.GetInt($"{difficultyName}_BestKills", 0),
            bestTime = PlayerPrefs.GetFloat($"{difficultyName}_BestTime", 0f)
        };

        // 🔥 新增：读取完整一局的数据
        record.recordCoins = PlayerPrefs.GetInt($"{difficultyName}_RecordCoins", record.bestCoins);
        record.recordKills = PlayerPrefs.GetInt($"{difficultyName}_RecordKills", record.bestKills);
        record.recordTime = PlayerPrefs.GetFloat($"{difficultyName}_RecordTime", record.bestTime);

        return record;
    }

    /// <summary>
    /// 更新记录 - 击杀优先 > 金币次之 > 时间越短越好
    /// </summary>
    public void UpdateRecord(string difficultyName, int coins, int kills, float time)
    {
        // 获取当前最佳记录
        GameRecord currentBest = GetRecord(difficultyName);

        bool isBetter = false;
        bool hasRecord = currentBest.HasRecord;

        if (!hasRecord)
        {
            // 没有记录 → 直接保存
            isBetter = true;
            Debug.Log($"📝 首次记录！难度: {difficultyName}");
        }
        else
        {
            // 🥇 第一优先级：击杀数（越多越好）
            if (kills > currentBest.bestKills)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀更多！{kills} > {currentBest.bestKills}");
            }
            // 🥈 第二优先级：金币数（越多越好）
            else if (kills == currentBest.bestKills && coins > currentBest.bestCoins)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀相同，金币更多！{coins} > {currentBest.bestCoins}");
            }
            // 🥉 第三优先级：存活时间（越短越好，效率更高）
            else if (kills == currentBest.bestKills && coins == currentBest.bestCoins && time < currentBest.bestTime)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀和金币相同，时间更短！{time:F2} < {currentBest.bestTime:F2}");
            }
            else
            {
                Debug.Log($"当前记录保持: 击杀: {currentBest.bestKills} | 金币: {currentBest.bestCoins} | 时间: {currentBest.bestTime:F2}秒");
            }
        }

        // 如果是新纪录，保存
        if (isBetter)
        {
            // 保存最佳值
            PlayerPrefs.SetInt($"{difficultyName}_BestCoins", coins);
            PlayerPrefs.SetInt($"{difficultyName}_BestKills", kills);
            PlayerPrefs.SetFloat($"{difficultyName}_BestTime", time);

            // 🔥 保存完整一局的数据（用于显示详细记录）
            PlayerPrefs.SetInt($"{difficultyName}_RecordCoins", coins);
            PlayerPrefs.SetInt($"{difficultyName}_RecordKills", kills);
            PlayerPrefs.SetFloat($"{difficultyName}_RecordTime", time);

            PlayerPrefs.Save();
            OnDataChanged?.Invoke();

            Debug.Log($"🎉 新纪录！{difficultyName} | 击杀: {kills} | 金币: {coins} | 时间: {time:F2}秒");
        }
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0) return;
        TotalCoins += amount;
        Debug.Log($"获得 {amount} 金币，当前总金币: {TotalCoins}");
    }

    public bool SpendCoins(int amount)
    {
        if (TotalCoins < amount)
        {
            Debug.Log($"金币不足！需要 {amount}，当前 {TotalCoins}");
            return false;
        }

        TotalCoins -= amount;
        return true;
    }

    public SkillData GetSkillData(string skillID)
    {
        skillCache.TryGetValue(skillID, out SkillData data);
        return data;
    }

    // ==================== 数据持久化 ====================

    [System.Serializable]
    class PlayerSaveData
    {
        public int totalCoins;
        public List<string> unlockedCharacters;
        public string selectedCharacter;
        public List<SkillSaveEntry> skills;
    }

    [System.Serializable]
    public class SkillSaveEntry
    {
        public string skillID;
        public int level;
    }

    void SaveData()
    {
        PlayerSaveData data = new PlayerSaveData
        {
            totalCoins = totalCoins,
            unlockedCharacters = unlockedCharacterNames,
            selectedCharacter = selectedCharacterName,
            skills = skillLevels
        };

        PlayerPrefs.SetString("PlayerData", JsonUtility.ToJson(data));
        PlayerPrefs.Save();

        Debug.Log($"💾 数据已保存，总金币: {totalCoins}");
    }

    void LoadData()
    {
        Debug.Log($"📂 LoadData 被调用，当前总金币: {totalCoins}");

        if (!PlayerPrefs.HasKey("PlayerData"))
        {
            Debug.Log("📂 没有找到保存的数据");
            return;
        }

        string json = PlayerPrefs.GetString("PlayerData");
        PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(json);

        if (data != null)
        {
            totalCoins = data.totalCoins;
            unlockedCharacterNames = data.unlockedCharacters ?? new List<string>();
            selectedCharacterName = data.selectedCharacter ?? "";
            skillLevels = data.skills ?? new List<SkillSaveEntry>();

            Debug.Log($"📂 数据加载完成，总金币: {totalCoins}");
        }
        else
        {
            Debug.LogError("❌ 数据反序列化失败！");
        }
    }

    public void ResetAllData()
    {
        PlayerPrefs.DeleteKey("PlayerData");
        totalCoins = startingCoins;
        unlockedCharacterNames.Clear();
        unlockedCharacterNames.Add(defaultCharacterName);
        selectedCharacterName = defaultCharacterName;
        skillLevels.Clear();
        SaveData();
        OnDataChanged?.Invoke();
        Debug.Log("所有数据已重置");
    }
}

// ==================== GameRecord 结构体 ====================

[System.Serializable]
public class GameRecord
{
    public string difficultyName;
    public int bestCoins;
    public int bestKills;
    public float bestTime;

    // 🔥 新增：完整一局的数据（用于显示详细记录）
    public int recordCoins;
    public int recordKills;
    public float recordTime;

    public bool HasRecord => bestCoins > 0 || bestKills > 0 || bestTime > 0f;
}