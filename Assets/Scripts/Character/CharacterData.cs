using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("===== 基本信息 =====")]
    public string characterName;
    [TextArea(3, 5)] public string characterDescription;

    [Header("===== 角色图片 =====")]
    public Sprite avatarSprite;
    public Sprite fullBodySprite;
    public Sprite lockedAvatarSprite;      // 未解锁时显示的头像

    [Header("===== 3D模型（可选）=====")]
    public GameObject modelPrefab;          // 3D模型预制体（用于替代 fullBodyImage）
    public Vector3 modelSpawnPosition = Vector3.zero;
    public Vector3 modelSpawnRotation = Vector3.zero;
    public string defaultAnimation = "Idle";

    [Header("===== 基础属性 =====")]
    public int baseAttack = 10;
    public float baseRange = 5f;
    public float baseSpeed = 3f;           // 后台计算，UI不显示
    public float baseCooldown = 2f;
    public float baseHealth = 100f;        // 生命值

    [Header("===== 升级配置 =====")]
    public UpgradeConfigSO normalAttackConfig;
    public UpgradeConfigSO skillAttackConfig;

    [Header("===== 解锁 =====")]
    public int unlockCost = 100;

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 获取角色显示名称（如果未解锁则显示 "???"）
    /// </summary>
    public string GetDisplayName(bool isUnlocked)
    {
        return isUnlocked ? characterName : "???";
    }

    /// <summary>
    /// 获取角色头像（根据解锁状态）
    /// </summary>
    public Sprite GetAvatarSprite(bool isUnlocked)
    {
        if (isUnlocked)
            return avatarSprite;
        else
            return lockedAvatarSprite != null ? lockedAvatarSprite : avatarSprite;
    }

    /// <summary>
    /// 获取角色全身图（根据解锁状态）
    /// </summary>
    public Sprite GetFullBodySprite(bool isUnlocked)
    {
        return isUnlocked ? fullBodySprite : null;
    }

    /// <summary>
    /// 获取角色介绍（根据解锁状态）
    /// </summary>
    public string GetDescription(bool isUnlocked)
    {
        if (!isUnlocked)
            return "未解锁此角色";
        return string.IsNullOrEmpty(characterDescription) ? "暂无介绍" : characterDescription;
    }

    /// <summary>
    /// 获取普通攻击当前等级
    /// </summary>
    public int GetNormalAttackLevel(GameDataManager dataManager)
    {
        if (dataManager == null) return 0;
        string id = $"NormalAttack_{characterName}";
        var entry = dataManager.skillLevels.Find(s => s.skillID == id);
        return entry != null ? entry.level : 0;
    }

    /// <summary>
    /// 获取技能攻击当前等级
    /// </summary>
    public int GetSkillAttackLevel(GameDataManager dataManager)
    {
        if (dataManager == null) return 0;
        string id = $"SkillAttack_{characterName}";
        var entry = dataManager.skillLevels.Find(s => s.skillID == id);
        return entry != null ? entry.level : 0;
    }
}