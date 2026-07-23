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
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadAllSkills();
            LoadData();
            InitializeDefaultData();
        }
        else
        {
            Destroy(gameObject);
        }
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

    // ==================== 加载技能（保留） ====================

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

    public GameRecord GetRecord(string difficultyName)
    {
        return new GameRecord
        {
            difficultyName = difficultyName,
            bestCoins = PlayerPrefs.GetInt($"{difficultyName}_BestCoins", 0),
            bestKills = PlayerPrefs.GetInt($"{difficultyName}_BestKills", 0),
            bestTime = PlayerPrefs.GetFloat($"{difficultyName}_BestTime", 0f)
        };
    }

    public void UpdateRecord(string difficultyName, int coins, int kills, float time)
    {
        string coinsKey = $"{difficultyName}_BestCoins";
        string killsKey = $"{difficultyName}_BestKills";
        string timeKey = $"{difficultyName}_BestTime";

        int bestCoins = PlayerPrefs.GetInt(coinsKey, 0);
        int bestKills = PlayerPrefs.GetInt(killsKey, 0);
        float bestTime = PlayerPrefs.GetFloat(timeKey, 0f);

        bool isNew = false;

        if (coins > bestCoins) { PlayerPrefs.SetInt(coinsKey, coins); isNew = true; }
        if (kills > bestKills) { PlayerPrefs.SetInt(killsKey, kills); isNew = true; }
        if (time > bestTime) { PlayerPrefs.SetFloat(timeKey, time); isNew = true; }

        if (isNew)
        {
            PlayerPrefs.Save();
            OnDataChanged?.Invoke();
            Debug.Log($"新纪录！{difficultyName}");
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
    }

    void LoadData()
    {
        if (!PlayerPrefs.HasKey("PlayerData")) return;

        string json = PlayerPrefs.GetString("PlayerData");
        PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(json);

        if (data != null)
        {
            totalCoins = data.totalCoins;
            unlockedCharacterNames = data.unlockedCharacters ?? new List<string>();
            selectedCharacterName = data.selectedCharacter ?? "";
            skillLevels = data.skills ?? new List<SkillSaveEntry>();
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

[System.Serializable]
public class GameRecord
{
    public string difficultyName;
    public int bestCoins;
    public int bestKills;
    public float bestTime;

    public bool HasRecord => bestCoins > 0 || bestKills > 0 || bestTime > 0f;
}