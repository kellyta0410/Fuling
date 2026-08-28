using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 商店房间：玩家走近房间中央的商店触发范围时，
// 在 3D 空间里显示一个朝向相机的可点击 UI 提示（世界空间 Canvas + 按钮），
// 点击它即可打开购买 Buff 的商店面板（屏幕 UI）。
public class ShopItem : MonoBehaviour
{
    [Header("交互")]
    public float interactRadius = 3f;

    private List<BuffDataSO> buffPool;
    private int roomCost;
    private bool playerNear = false;
    private GameObject promptCanvas;

    public void Setup(List<BuffDataSO> pool, DungeonManager manager, int cost)
    {
        buffPool = pool;
        roomCost = cost;

        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
        }

        // 用一个明显的发光小球标示商店位置
        GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.transform.SetParent(transform);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localScale = Vector3.one * 0.8f;
        var r = vis.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = Color.yellow;
            r.material.color = c;
            r.material.SetColor("_EmissionColor", c);
        }
        Destroy(vis.GetComponent<Collider>());

        BuildPrompt();
        if (promptCanvas != null) promptCanvas.SetActive(false);
    }

    // 在房间中央的 3D 空间里生成一个朝向相机的 UI 提示（世界空间 Canvas + 按钮）
    void BuildPrompt()
    {
        promptCanvas = new GameObject("ShopPrompt");
        promptCanvas.transform.SetParent(transform, false);
        promptCanvas.transform.localPosition = new Vector3(0f, 2.2f, 0f);

        Canvas c = promptCanvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) c.worldCamera = Camera.main;
        c.sortingOrder = 20;
        promptCanvas.AddComponent<GraphicRaycaster>();   // 让世界空间按钮可被点击

        RectTransform crt = c.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(320f, 130f);
        crt.localScale = new Vector3(0.01f, 0.01f, 0.01f); // 世界空间：300px→3m

        // 背景面板
        GameObject panel = new GameObject("Bg");
        panel.transform.SetParent(promptCanvas.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        var img = panel.AddComponent<Image>();
        img.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);

        // 按钮：点击打开商店
        GameObject btn = new GameObject("OpenBtn");
        btn.transform.SetParent(panel.transform, false);
        RectTransform brt = btn.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.08f, 0.12f);
        brt.anchorMax = new Vector2(0.92f, 0.88f);
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        var btnImg = btn.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.6f, 0.9f, 1f);
        var button = btn.AddComponent<Button>();
        button.targetGraphic = btnImg;
        var txt = btn.AddComponent<TextMeshProUGUI>();
        txt.text = "打开商店";
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontSize = 30;
        txt.color = Color.white;
        button.onClick.AddListener(OpenShop);
    }

    void OpenShop()
    {
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null) ui.OpenShop(buffPool, roomCost);
        if (promptCanvas != null) promptCanvas.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNear = true;
            if (promptCanvas != null) promptCanvas.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNear = false;
            if (promptCanvas != null) promptCanvas.SetActive(false);
        }
    }

    void Update()
    {
        // 提示始终朝向相机（广告牌效果），方便阅读与点击
        if (playerNear && promptCanvas != null && Camera.main != null)
        {
            promptCanvas.transform.LookAt(Camera.main.transform);
        }
    }
}
