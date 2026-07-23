using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSelectionManager : MonoBehaviour
{
    [Header("难度配置（拖入你的配置文件）")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    // ⭐ 点击"简单"按钮
    public void SelectEasy()
    {
        SelectDifficulty(easyConfig);
    }

    // ⭐ 点击"普通"按钮
    public void SelectNormal()
    {
        SelectDifficulty(normalConfig);
    }

    // ⭐ 点击"困难"按钮
    public void SelectHard()
    {
        SelectDifficulty(hardConfig);
    }

    // ⭐ 点击"无限"按钮
    public void SelectInfinite()
    {
        SelectDifficulty(infiniteConfig);
    }

    // ⭐ 核心方法：保存难度并加载场景
    void SelectDifficulty(DifficultySettings difficulty)
    {
        if (difficulty == null)
        {
            Debug.LogError("❌ 难度配置为空！请检查 Inspector 拖入");
            return;
        }

        // 1. 保存选中的难度名称（供 GameManager 读取）
        PlayerPrefs.SetString("SelectedDifficulty", difficulty.difficultyName);
        PlayerPrefs.Save();

        Debug.Log($"✅ 选择难度: {difficulty.difficultyName}，加载场景: {difficulty.sceneName}");

        // 2. 加载对应的游戏场景
        if (!string.IsNullOrEmpty(difficulty.sceneName))
        {
            SceneManager.LoadScene(difficulty.sceneName);
        }
        else
        {
            Debug.LogError($"❌ {difficulty.difficultyName} 的 Scene Name 为空！请在配置文件中填写");
        }
    }
}