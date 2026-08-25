using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// ⭐ 剧情场景控制器：负责记录”，并提供 → 选关”按钮入口。
// 场景先留空，等美术/策划往里加剧情内容；放一个挂本脚本的 StoryManager 空物体即可跑通流程。
public class StorySceneManager : MonoBehaviour
{
    // 与 MainMenuManager 共用的剧情完成标记键
    public const string StoryFinishedKey = "StoryFinished";

    [Tooltip("剧情场景里的“继续”按钮：拖进来即可，代码自动绑定 onClick → ContinueToSelection")]
    public Button continueButton;

    [Tooltip("剧情 mp4 影片播放器：拖入后，continue 按钮会等影片播完(loopPointReached)才出现；不填则按钮直接可用")]
    public VideoPlayer storyVideo;

    [Tooltip("重看剧情时直接出现的“关闭/返回主菜单”按钮；与 continueButton 互斥（重看时不出现 continue）")]
    public Button closeButton;

    // 影片播放结束（或外部手动调用）后显示 continue 按钮
    public void RevealContinue()
    {
        if (continueButton != null) continueButton.gameObject.SetActive(true);
    }

    // ⭐ 按钮点击音播放时长：切场景前等这么久，让 AudioSource.Play 播完
    private const float buttonClickDelay = 0.2f;

    // 看完剧情后去哪个场景：默认选关(Selection)；手动从主菜单“剧情”按钮进来的设为 MainMenu。
    // 进剧情前由 MainMenuManager 设置，看完后读取并复位，避免影响下一次进入。
    public static string returnSceneName = "Selection";

    void Start()
    {
        if (continueButton != null)
            continueButton.onClick.AddListener(ContinueToSelection);
        if (closeButton != null)
            closeButton.onClick.AddListener(ContinueToSelection);

        // 入口区分：returnSceneName=="MainMenu" 表示主菜单“剧情重看”进入；
        // 否则是新手自动看剧情（去 Selection）。
        bool isReplay = returnSceneName == "MainMenu";

        if (isReplay)
        {
            // 重看：直接显示关闭按钮回主菜单；continue 按钮即使影片播完也不出现
            if (closeButton != null) closeButton.gameObject.SetActive(true);
            if (continueButton != null) continueButton.gameObject.SetActive(false);
        }
        else
        {
            // 新手：continue 按钮等影片播完才出现；无影片则直接可用
            if (closeButton != null) closeButton.gameObject.SetActive(false);
            if (continueButton != null)
            {
                if (storyVideo != null)
                {
                    continueButton.gameObject.SetActive(false);
                    storyVideo.loopPointReached += (_) => RevealContinue();
                }
                else
                {
                    continueButton.gameObject.SetActive(true);
                }
            }
        }
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
