using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSelectionManager : MonoBehaviour
{
    [Header("难度配置")]
    public DifficultySettings easyConfig;
    public DifficultySettings normalConfig;
    public DifficultySettings hardConfig;
    public DifficultySettings infiniteConfig;

    [Header("记录面板")]
    public GameObject recordsPanel;

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

        PlayerPrefs.SetString("SelectedDifficulty", difficulty.difficultyName);
        PlayerPrefs.Save();

        Debug.Log("选择难度: " + difficulty.difficultyName + "，加载场景: " + difficulty.sceneName);

        if (!string.IsNullOrEmpty(difficulty.sceneName))
        {
            SceneManager.LoadScene(difficulty.sceneName);
        }
        else
        {
            Debug.LogError(difficulty.difficultyName + " 的 Scene Name 为空");
        }
    }

    public void OpenRecordsPanel()
    {
        if (recordsPanel != null)
        {
            recordsPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("RecordsPanel 未设置");
        }
    }

    public void CloseRecordsPanel()
    {
        if (recordsPanel != null)
        {
            recordsPanel.SetActive(false);
        }
    }

    public void ToggleRecordsPanel()
    {
        if (recordsPanel != null)
        {
            recordsPanel.SetActive(!recordsPanel.activeSelf);
        }
    }
}