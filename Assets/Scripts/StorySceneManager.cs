using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// ⭐ 剧情场景控制器：负责记录”，并提供 → 选关”按钮入口。
// 场景先留空，等美术/策划往里加剧情内容；放一个挂本脚本的 StoryManager 空物体即可跑通流程。
public class StorySceneManager : MonoBehaviour
{
    // 与 MainMenuManager 共用的剧情完成标记键
    public const string StoryFinishedKey = "StoryFinished";

    [Tooltip("看完剧情后点“继续”进入的场景（默认选关）")]
    public string nextSceneName = "Selection";

    // ⭐ 按钮点击音播放时长：切场景前等这么久，让 AudioSource.Play 播完
    private const float buttonClickDelay = 0.2f;

    // ⭐ 继续按钮：标记剧情已看完（主菜单的因此解锁），然后去选关
    public void ContinueToSelection()
    {
        PlayerPrefs.SetInt(StoryFinishedKey, 1);
        PlayerPrefs.Save();
        Debug.Log("剧情已看完 → 进入选关");
        StartCoroutine(LoadSceneAfterButtonClick(nextSceneName));
    }

    IEnumerator LoadSceneAfterButtonClick(string sceneName)
    {
        yield return new WaitForSecondsRealtime(buttonClickDelay);
        SceneManager.LoadScene(sceneName);
    }
}
