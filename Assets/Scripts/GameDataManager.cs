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

    [Header("技能数据")]
    public List<SkillSaveEntry> skillLevels = new List<SkillSaveEntry>();

    [Header("默认值")]
    public string defaultCharacterName = "Mei";
    public int startingCoins = 100;

    private CharacterData currentCharacter;

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
        if (Instance != null)
        {
            if (Instance == this)
            {
                LoadData();
                return;
            }

            // 场景重载时新对象会被销毁。先让保留的 Instance 从存档刷新一次，
            // 保证跨场景（游戏结束返回）后解锁/选择/金币与存档一致。
            Instance.LoadData();
            Instance.LoadAllCharactersIfNeeded();
            Instance.InitializeDefaultData();
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadData();
        LoadAllCharactersIfNeeded();
        InitializeDefaultData();

        Debug.Log($"[GDM] 初始化完成 角色数={allCharacters.Count} 解锁=[{string.Join(",", unlockedCharacterNames)}] 金币={totalCoins} 已选={selectedCharacterName}");
    }

    // ==================== 角色加载 ====================

    // 若 Inspector 里没有拖入角色，运行时从 Resources/CharacterData 自动加载
    void LoadAllCharactersIfNeeded()
    {
        if (allCharacters == null) allCharacters = new List<CharacterData>();
        if (allCharacters.Count > 0) return;

        CharacterData[] loaded = Resources.LoadAll<CharacterData>("CharacterData");
        if (loaded != null && loaded.Length > 0)
        {
            allCharacters.AddRange(loaded);
            Debug.Log($"自动加载角色 {allCharacters.Count} 个");
        }
    }

    // ==================== 初始化 ====================

    void InitializeDefaultData()
    {
        // 默认角色（梅）始终默认解锁，不依赖存档
        if (!unlockedCharacterNames.Contains(defaultCharacterName))
        {
            unlockedCharacterNames.Add(defaultCharacterName);
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
        else
        {
            currentCharacter = null;
        }
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
        if (IsCharacterUnlocked(character)) return true;

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

    public GameRecord GetRecord(string difficultyName)
    {
        GameRecord record = new GameRecord
        {
            difficultyName = difficultyName,
            bestCoins = PlayerPrefs.GetInt($"{difficultyName}_BestCoins", 0),
            bestKills = PlayerPrefs.GetInt($"{difficultyName}_BestKills", 0),
            bestTime = PlayerPrefs.GetFloat($"{difficultyName}_BestTime", 0f)
        };

        record.recordCoins = PlayerPrefs.GetInt($"{difficultyName}_RecordCoins", record.bestCoins);
        record.recordKills = PlayerPrefs.GetInt($"{difficultyName}_RecordKills", record.bestKills);
        record.recordTime = PlayerPrefs.GetFloat($"{difficultyName}_RecordTime", record.bestTime);

        return record;
    }

    // 普通模式解锁条件：玩过一局"简单"（简单难度有任意记录）
    public bool IsNormalUnlocked
    {
        get => GetRecord("简单").HasRecord;
    }

    public void UpdateRecord(string difficultyName, int coins, int kills, float time)
    {
        GameRecord currentBest = GetRecord(difficultyName);

        bool isBetter = false;
        bool hasRecord = currentBest.HasRecord;

        if (!hasRecord)
        {
            isBetter = true;
            Debug.Log($"📝 首次记录！难度: {difficultyName}");
        }
        else
        {
            if (kills > currentBest.bestKills)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀更多！{kills} > {currentBest.bestKills}");
            }
            else if (kills == currentBest.bestKills && coins > currentBest.bestCoins)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀相同，金币更多！{coins} > {currentBest.bestCoins}");
            }
            else if (kills == currentBest.bestKills && coins == currentBest.bestCoins && time < currentBest.bestTime)
            {
                isBetter = true;
                Debug.Log($"🏆 击杀和金币相同，时间更短！{time:F2} < {currentBest.bestTime:F2}");
            }
        }

        if (isBetter)
        {
            PlayerPrefs.SetInt($"{difficultyName}_BestCoins", coins);
            PlayerPrefs.SetInt($"{difficultyName}_BestKills", kills);
            PlayerPrefs.SetFloat($"{difficultyName}_BestTime", time);
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

    // ==================== 通知数据变化 ====================

    public void NotifyDataChanged()
    {
        OnDataChanged?.Invoke();
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

    public void SaveData()
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

    // ==================== 获取技能总加成 ====================

    public UpgradeLevelData GetSkillTotalBonus(string skillType, string characterName, UpgradeConfigSO config)
    {
        var result = new UpgradeLevelData();
        if (config == null || string.IsNullOrEmpty(characterName)) return result;

        string skillID = $"{skillType}_{characterName}";
        var entry = skillLevels.Find(s => s.skillID == skillID);
        int level = entry != null ? entry.level : 0;

        if (level <= 0) return result;

        return config.GetTotalBonus(level);
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
    public int recordCoins;
    public int recordKills;
    public float recordTime;

    public bool HasRecord => bestCoins > 0 || bestKills > 0 || bestTime > 0f;
}