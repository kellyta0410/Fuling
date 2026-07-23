using UnityEngine;
using TMPro;

public class RecordsManager : MonoBehaviour
{
    [Header("简单难度")]
    public TextMeshProUGUI easyCoinsText;
    public TextMeshProUGUI easyKillsText;
    public TextMeshProUGUI easyTimeText;

    [Header("普通难度")]
    public TextMeshProUGUI normalCoinsText;
    public TextMeshProUGUI normalKillsText;
    public TextMeshProUGUI normalTimeText;

    [Header("困难难度")]
    public TextMeshProUGUI hardCoinsText;
    public TextMeshProUGUI hardKillsText;
    public TextMeshProUGUI hardTimeText;

    [Header("无限难度")]
    public TextMeshProUGUI infiniteCoinsText;
    public TextMeshProUGUI infiniteKillsText;
    public TextMeshProUGUI infiniteTimeText;

    [Header("设置")]
    public bool autoRefreshOnEnable = true;

    void OnEnable()
    {
        if (autoRefreshOnEnable)
        {
            RefreshRecords();
        }
    }

    public void RefreshRecords()
    {
        Debug.Log("刷新记录面板");

        UpdateRecordUI("简单", easyCoinsText, easyKillsText, easyTimeText);
        UpdateRecordUI("普通", normalCoinsText, normalKillsText, normalTimeText);
        UpdateRecordUI("困难", hardCoinsText, hardKillsText, hardTimeText);
        UpdateRecordUI("无限", infiniteCoinsText, infiniteKillsText, infiniteTimeText);
    }

    void UpdateRecordUI(string difficultyName, TextMeshProUGUI coinsText, TextMeshProUGUI killsText, TextMeshProUGUI timeText)
    {
        int coins = PlayerPrefs.GetInt(difficultyName + "_BestCoins", 0);
        int kills = PlayerPrefs.GetInt(difficultyName + "_BestKills", 0);
        float time = PlayerPrefs.GetFloat(difficultyName + "_BestTime", 0f);

        if (coinsText != null)
            coinsText.text = coins.ToString();

        if (killsText != null)
            killsText.text = kills.ToString();

        if (timeText != null)
        {
            if (time > 0)
            {
                int minutes = Mathf.FloorToInt(time / 60);
                int seconds = Mathf.FloorToInt(time % 60);
                timeText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
            else
            {
                timeText.text = "--:--";
            }
        }
    }

    public void ClearAllRecords()
    {
        string[] difficulties = { "简单", "普通", "困难", "无限" };

        foreach (string diff in difficulties)
        {
            PlayerPrefs.DeleteKey(diff + "_BestCoins");
            PlayerPrefs.DeleteKey(diff + "_BestKills");
            PlayerPrefs.DeleteKey(diff + "_BestTime");
        }

        PlayerPrefs.Save();
        RefreshRecords();

        Debug.Log("所有记录已清空");
    }
}