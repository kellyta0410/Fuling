using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 商店房间：玩家靠近时显示按钮，离开时隐藏
public class ShopItem : MonoBehaviour
{
    [Header("交互")]
    public float interactRadius = 3f;
    [Header("UI")]
    [Tooltip("拖入你的按钮预制体（需有 Button 组件）")]
    public GameObject buttonPrefab;
    [Tooltip("按钮放到哪个父物体下（留空则放 Canvas 下）")]
    public Transform buttonParent;

    private List<BuffDataSO> buffPool;
    private int roomCost;
    private bool playerNear = false;
    private GameObject promptUI;

    public void Setup(List<BuffDataSO> pool, DungeonManager manager, int cost, GameObject btnPrefab = null, Transform btnParent = null)
    {
        buffPool = pool;
        roomCost = cost;
        if (btnPrefab != null) buttonPrefab = btnPrefab;
        if (btnParent != null) buttonParent = btnParent;

        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
        }

        BuildPrompt();
        if (promptUI != null) promptUI.SetActive(false);

        // 玩家一开始就在触发器内的情况（OnTriggerEnter 不会再触发）
        CheckPlayerAlreadyInside();
    }

    void CheckPlayerAlreadyInside()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Collider[] hits = Physics.OverlapBox(col.bounds.center, col.bounds.extents);
        foreach (var h in hits)
        {
            if (h.CompareTag("Player"))
            {
                playerNear = true;
                if (promptUI != null) promptUI.SetActive(true);
                break;
            }
        }
    }

    void BuildPrompt()
    {
        if (buttonPrefab == null) return;

        Transform parent = buttonParent != null ? buttonParent : FindObjectOfType<Canvas>().transform;
        promptUI = Instantiate(buttonPrefab, parent);
        var button = promptUI.GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OpenShop);
    }

    void OpenShop()
    {
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null) ui.OpenShop(buffPool, roomCost);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNear = true;
            if (promptUI != null) promptUI.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNear = false;
            if (promptUI != null) promptUI.SetActive(false);
        }
    }
}
