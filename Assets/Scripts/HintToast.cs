using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 全局共享的提示 Toast（含背景 Panel）。全场景只用同一个 text + panel，只随调用方更换字体。
/// 可传可选 text/panel 覆盖（拖入的场景对象）；未拖入时自动生成居中的 Toast。
/// </summary>
public static class HintToast
{
    private static TextMeshProUGUI text;
    private static GameObject panel;
    private static bool built;
    private static Coroutine anim;
    private static Runner runner;

    private class Runner : MonoBehaviour { }

    private static Runner RunnerInstance
    {
        get
        {
            if (runner == null)
            {
                var g = new GameObject("HintToastRunner");
                Object.DontDestroyOnLoad(g);
                runner = g.AddComponent<Runner>();
            }
            return runner;
        }
    }

    public static void Show(string message, TMP_FontAsset font = null,
        TextMeshProUGUI textOverride = null, GameObject panelOverride = null)
    {
        if (textOverride != null) text = textOverride;
        if (panelOverride != null) panel = panelOverride;

        if (text == null && panel == null) Build();

        // 确保提示文字居中在 panel 正中间
        if (text != null && panel != null)
        {
            RectTransform trt = text.rectTransform;
            if (text.transform.parent != panel.transform)
                trt.SetParent(panel.transform);
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(Mathf.Max(trt.sizeDelta.x, 300f), 100f);
        }

        if (text == null)
        {
            Debug.Log(message);
            return;
        }

        if (font != null) text.font = font;
        if (panel != null) panel.SetActive(true);
        text.gameObject.SetActive(true);
        text.text = message;

        if (anim != null) RunnerInstance.StopCoroutine(anim);
        anim = RunnerInstance.StartCoroutine(Animate());
    }

    private static IEnumerator Animate()
    {
        RectTransform rt = text != null ? text.rectTransform : null;
        Vector2 basePos = rt != null ? rt.anchoredPosition : Vector2.zero;
        if (rt != null) rt.anchoredPosition = basePos + Vector2.down * 40f;

        float dur = 0.25f, t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / dur);
            if (rt != null) rt.anchoredPosition = basePos + Vector2.down * (40f * (1f - k));
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = basePos;

        yield return new WaitForSeconds(1.6f);
        if (text != null) text.gameObject.SetActive(false);
        if (panel != null) panel.SetActive(false);
        anim = null;
    }

    private static void Build()
    {
        if (built) return;
        built = true;

        GameObject canvasGO = new GameObject("HintToast", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas cv = canvasGO.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 20000;

        panel = new GameObject("HintPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.25f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(520f, 64f);
        Image pimg = panel.GetComponent<Image>();
        pimg.color = new Color(0f, 0f, 0f, 0.78f);

        GameObject textGO = new GameObject("HintText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(panel.transform, false);
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(500f, 60f);
        text = textGO.GetComponent<TextMeshProUGUI>();
        text.fontSize = 30f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;

        TMP_FontAsset f = null;
        var existing = (TextMeshProUGUI)Object.FindObjectOfType(typeof(TextMeshProUGUI));
        if (existing != null) f = existing.font;
        if (f != null) text.font = f;
    }
}