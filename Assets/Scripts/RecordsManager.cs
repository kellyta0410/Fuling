using UnityEngine;
using TMPro;

public class RecordsManager : MonoBehaviour
{
    [Header("记录显示")]
    public TextMeshProUGUI easyCoinsText;
    public TextMeshProUGUI easyKillsText;
    public TextMeshProUGUI easyTimeText;
    public TextMeshProUGUI normalCoinsText;
    public TextMeshProUGUI normalKillsText;
    public TextMeshProUGUI normalTimeText;
    public TextMeshProUGUI infiniteCoinsText;
    public TextMeshProUGUI infiniteKillsText;
    public TextMeshProUGUI infiniteTimeText;

    [Header("设置")]
    public bool autoRefreshOnEnable = true;

    private GameDataManager dataManager;

    void Start()
    {
        dataManager = GameDataManager.Instance;
        if (autoRefreshOnEnable)
        {
            RefreshRecords();
        }
    }

    void OnEnable()
    {
        if (autoRefreshOnEnable)
        {
            RefreshRecords();
        }
    }

    public void RefreshRecords()
    {
        if (dataManager == null)
        {
            dataManager = GameDataManager.Instance;
            if (dataManager == null)
            {
                Debug.LogWarning("GameDataManager 未找到");
                return;
            }
        }

        UpdateRecordUI("简单", easyCoinsText, easyKillsText, easyTimeText);
        UpdateRecordUI("普通", normalCoinsText, normalKillsText, normalTimeText);
        UpdateRecordUI("无限", infiniteCoinsText, infiniteKillsText, infiniteTimeText);
    }

    void UpdateRecordUI(string difficultyName, TextMeshProUGUI coinsText, TextMeshProUGUI killsText, TextMeshProUGUI timeText)
    {
        GameRecord record = dataManager.GetRecord(difficultyName);

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

    public void ClearAllRecords()
    {
        if (dataManager == null) return;

        string[] difficulties = { "简单", "普通", "无限" };
        foreach (string diff in difficulties)
        {
            PlayerPrefs.DeleteKey(diff + "_BestCoins");
            PlayerPrefs.DeleteKey(diff + "_BestKills");
            PlayerPrefs.DeleteKey(diff + "_BestTime");
            // 🔥 新增：清除完整一局的数据
            PlayerPrefs.DeleteKey(diff + "_RecordCoins");
            PlayerPrefs.DeleteKey(diff + "_RecordKills");
            PlayerPrefs.DeleteKey(diff + "_RecordTime");
        }
        PlayerPrefs.Save();
        RefreshRecords();
        Debug.Log("所有记录已清空");
    }
}