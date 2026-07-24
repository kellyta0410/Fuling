using UnityEngine;
using TMPro;

public class ComboManager : MonoBehaviour
{
    public static ComboManager Instance;

    [Header("Combo 设置")]
    [SerializeField] private float comboTimeout = 10f;
    [SerializeField] private int comboThreshold = 5;

    [Header("UI")]
    [SerializeField] private GameObject comboUIPanel;
    [SerializeField] private TextMeshProUGUI comboText;
    [SerializeField] private TextMeshProUGUI multiplierText;

    // 状态
    private int currentCombo = 0;
    private float lastKillTime = 0f;
    private bool isComboActive = false;

    // 事件
    public System.Action<int> OnComboChanged;
    public System.Action<bool> OnComboStateChanged;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 初始隐藏 UI
        if (comboUIPanel != null)
            comboUIPanel.SetActive(false);

        UpdateUI();
    }

    void Update()
    {
        if (!isComboActive && currentCombo == 0) return;

        // 检查是否超时
        if (Time.time - lastKillTime >= comboTimeout)
        {
            ResetCombo();
        }
    }

    /// <summary>
    /// 击杀怪物时调用
    /// </summary>
    public void AddKill()
    {
        currentCombo++;
        lastKillTime = Time.time;

        // 检查是否达到触发阈值
        if (currentCombo >= comboThreshold && !isComboActive)
        {
            ActivateCombo();
        }

        // 触发事件
        if (OnComboChanged != null)
            OnComboChanged(currentCombo);

        UpdateUI();
        Debug.Log($"Combo: {currentCombo} | 激活: {isComboActive}");
    }

    /// <summary>
    /// 激活 Combo
    /// </summary>
    private void ActivateCombo()
    {
        isComboActive = true;

        if (OnComboStateChanged != null)
            OnComboStateChanged(true);

        if (comboUIPanel != null)
            comboUIPanel.SetActive(true);

        Debug.Log(" Combo 激活！Coin x2");
    }

    /// <summary>
    /// 重置 Combo
    /// </summary>
    public void ResetCombo()
    {
        if (currentCombo == 0 && !isComboActive) return;

        currentCombo = 0;
        isComboActive = false;
        lastKillTime = 0f;

        if (OnComboChanged != null)
            OnComboChanged(0);

        if (OnComboStateChanged != null)
            OnComboStateChanged(false);

        if (comboUIPanel != null)
            comboUIPanel.SetActive(false);

        UpdateUI();
        Debug.Log("Combo 已重置");
    }

    /// <summary>
    /// 玩家死亡时调用
    /// </summary>
    public void OnPlayerDeath()
    {
        ResetCombo();
    }

    /// <summary>
    /// 是否处于 Combo 激活状态
    /// </summary>
    public bool IsComboActive()
    {
        return isComboActive;
    }

    /// <summary>
    /// 获取当前连杀数
    /// </summary>
    public int GetComboCount()
    {
        return currentCombo;
    }

    /// <summary>
    /// 获取金币倍率
    /// </summary>
    public int GetGoldMultiplier()
    {
        return isComboActive ? 2 : 1;
    }

    /// <summary>
    /// 获取超时剩余时间（用于 UI 显示）
    /// </summary>
    public float GetRemainingComboTime()
    {
        if (!isComboActive) return 0f;
        return Mathf.Max(0f, comboTimeout - (Time.time - lastKillTime));
    }

    /// <summary>
    /// 更新 UI 显示
    /// </summary>
    private void UpdateUI()
    {
        if (comboText != null)
        {
            if (isComboActive)
                comboText.text = $" {currentCombo} COMBO";
            else if (currentCombo > 0)
                comboText.text = $"{currentCombo}";
            else
                comboText.text = "";
        }

        if (multiplierText != null)
        {
            if (isComboActive)
                multiplierText.text = "x2 coin!";
            else
                multiplierText.text = "";
        }
    }

    /// <summary>
    /// 编辑器调试
    /// </summary>
    void OnGUI()
    {
        if (Application.isPlaying && Debug.isDebugBuild)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 24;
            style.normal.textColor = Color.white;

            string comboInfo = $"Combo: {currentCombo} | 激活: {isComboActive}";
            if (isComboActive)
            {
                comboInfo += $" | 剩余: {GetRemainingComboTime():F1}s";
            }
            GUI.Label(new Rect(10, 60, 400, 30), comboInfo, style);
        }
    }
}