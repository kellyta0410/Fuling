using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 加载过渡场景：难度选择后先进入本场景，把目标关卡场景异步加载完再切换。
// 进入关卡前先加载完场景资源，避免直接切场景时的卡顿/白屏。
public class LoadingSceneManager : MonoBehaviour
{
    [Tooltip("加载失败时兜底进入的场景（不设置则回主菜单）")]
    public string fallbackScene = "MainMenu";

    [Header("UI")]
    [Tooltip("加载标题文字（留空则显示目标场景名）")]
    public string loadingTitle = "加载中...";

    private Image progressFill;
    private Text progressText;
    private string targetScene;

    void Start()
    {
        // 难度选择时已写入 PlayerPrefs（SceneSelectionManager.SelectDifficulty）
        targetScene = PlayerPrefs.GetString("SelectedScene", "");
        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning("[Loading] 未找到 SelectedScene，回退主菜单");
            targetScene = fallbackScene;
        }

        CreateLoadingUI();
        StartCoroutine(LoadTargetScene());
    }

    IEnumerator LoadTargetScene()
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(targetScene);
        if (op == null)
        {
            Debug.LogError($"[Loading] 无法加载场景 '{targetScene}'，回退主菜单");
            op = SceneManager.LoadSceneAsync(fallbackScene);
        }
        if (op == null)
        {
            Debug.LogError("[Loading] 连回退场景都无法加载！");
            yield break;
        }

        // 所有场景资源加载完成后才允许切换（progress 到 ~0.9 时 resources 已就绪，
        // 剩下的 0.1 是激活场景本身，立刻置 true 让它完成）
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            float p = op.progress / 0.9f;
            if (progressFill != null) progressFill.fillAmount = p;
            if (progressText != null) progressText.text = $"{(int)(p * 100f)}%";
            yield return null;
        }

        if (progressFill != null) progressFill.fillAmount = 1f;
        if (progressText != null) progressText.text = "100%";

        // 强制多等一小帧让进度条刷到底，再激活目标场景
        yield return null;
        op.allowSceneActivation = true;
    }

    void CreateLoadingUI()
    {
        GameObject canvasGO = new GameObject("LoadingCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 半透明黑底
        RectTransform bgRt = NewRect(canvasGO.transform, "LoadingBackground");
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        Image bg = bgRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.92f);
        bg.raycastTarget = false;

        // 标题
        RectTransform titleRt = NewRect(bgRt, "LoadingTitle");
        titleRt.anchorMin = new Vector2(0.5f, 0.7f);
        titleRt.anchorMax = new Vector2(0.5f, 0.7f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(800f, 80f);
        Text title = titleRt.gameObject.AddComponent<Text>();
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.text = string.IsNullOrEmpty(loadingTitle) ? $"Loading {targetScene}" : loadingTitle;
        title.fontSize = 48;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.raycastTarget = false;

        // 进度条底槽
        RectTransform barBgRt = NewRect(bgRt, "ProgressBarBG");
        barBgRt.anchorMin = new Vector2(0.5f, 0.5f);
        barBgRt.anchorMax = new Vector2(0.5f, 0.5f);
        barBgRt.pivot = new Vector2(0.5f, 0.5f);
        barBgRt.sizeDelta = new Vector2(600f, 40f);
        Image barBg = barBgRt.gameObject.AddComponent<Image>();
        barBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        barBg.raycastTarget = false;

        // 进度填充（Filled 从左往右）
        RectTransform fillRt = NewRect(barBgRt, "ProgressFill");
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        progressFill = fillRt.gameObject.AddComponent<Image>();
        progressFill.color = new Color(0.2f, 0.75f, 1f, 1f);
        progressFill.raycastTarget = false;
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = 0f;

        // 进度文字
        RectTransform pctRt = NewRect(bgRt, "ProgressText");
        pctRt.anchorMin = new Vector2(0.5f, 0.42f);
        pctRt.anchorMax = new Vector2(0.5f, 0.42f);
        pctRt.pivot = new Vector2(0.5f, 0.5f);
        pctRt.sizeDelta = new Vector2(300f, 40f);
        progressText = pctRt.gameObject.AddComponent<Text>();
        progressText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        progressText.text = "0%";
        progressText.fontSize = 30;
        progressText.alignment = TextAnchor.MiddleCenter;
        progressText.color = Color.white;
        progressText.raycastTarget = false;
    }

    RectTransform NewRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }
}
