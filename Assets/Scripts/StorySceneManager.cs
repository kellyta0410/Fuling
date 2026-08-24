using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ⭐ 剧情场景控制器：负责记录”，并提供 → 选关”按钮入口。
// 场景先留空，等美术/策划往里加剧情内容；放一个挂本脚本的 StoryManager 空物体即可跑通流程。
public class StorySceneManager : MonoBehaviour
{
    // 与 MainMenuManager 共用的剧情完成标记键
    public const string StoryFinishedKey = "StoryFinished";

    [Tooltip("剧情场景里的“继续”按钮：拖进来即可，代码自动绑定 onClick → ContinueToSelection")]
    public Button continueButton;

    // ⭐ 按钮点击音播放时长：切场景前等这么久，让 AudioSource.Play 播完
    private const float buttonClickDelay = 0.2f;

    // 看完剧情后去哪个场景：默认选关(Selection)；手动从主菜单“剧情”按钮进来的设为 MainMenu。
    // 进剧情前由 MainMenuManager 设置，看完后读取并复位，避免影响下一次进入。
    public static string returnSceneName = "Selection";

    void Start()
    {
        if (continueButton != null)
            continueButton.onClick.AddListener(ContinueToSelection);
    }

    // ⭐ 继续按钮：标记剧情已看完（主菜单的因此解锁），然后去 returnSceneName
    public void ContinueToSelection()
    {
        PlayerPrefs.SetInt(StoryFinishedKey, 1);
        PlayerPrefs.Save();

        string target = returnSceneName;
        returnSceneName = "Selection"; // 复位，避免影响下一次进入
        Debug.Log($"剧情已看完 → 进入 {target}");
        StartCoroutine(LoadSceneAfterButtonClick(target));
    }

    IEnumerator LoadSceneAfterButtonClick(string sceneName)
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
        SceneManager.LoadScene(sceneName);
    }
}
