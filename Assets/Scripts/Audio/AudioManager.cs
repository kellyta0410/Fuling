using UnityEngine;
using UnityEngine.SceneManagement;

// 全局 BGM 管理器 + 音量读取。SFX(攻击/技能/金币/buff) 的 Clip 放在各自脚本/Prefab 上，
// 需要音量时统一读这里/直接读 SettingsManager。
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("AudioManager");
                _instance = go.AddComponent<AudioManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    private static AudioManager _instance;

    [Header("BGM")]
    [Tooltip("主菜单 / 加载 / 选关 共用一段 BGM")]
    public AudioClip menuBGM;
    [Tooltip("Easy / Medium 游戏内共用一段 BGM")]
    public AudioClip gameplayBGM;

    private AudioSource bgmSource;

    // 主菜单系场景（共用一个 BGM）
    private static readonly string[] MenuScenes = { "MainMenu", "Loading", "Selection", "DifficultySelection" };
    // 游戏内场景（共用另一个 BGM），可按需要自行追加
    private static readonly string[] GameplayScenes = { "Easy", "Medium" };

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        PlayBGMForScene(SceneManager.GetActiveScene().name);
    }

    void Update()
    {
        // 实时同步音乐音量（设置面板滑动时立即生效）
        if (SettingsManager.Instance != null)
        {
            bgmSource.volume = SettingsManager.Instance.GetMusicVolume();
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayBGMForScene(scene.name);
    }

    void PlayBGMForScene(string sceneName)
    {
        AudioClip clip = null;
        for (int i = 0; i < MenuScenes.Length; i++)
        {
            if (MenuScenes[i] == sceneName) { clip = menuBGM; break; }
        }
        if (clip == null)
        {
            for (int i = 0; i < GameplayScenes.Length; i++)
            {
                if (GameplayScenes[i] == sceneName) { clip = gameplayBGM; break; }
            }
        }
        PlayBGM(clip);
    }

    public void PlayBGM(AudioClip clip)
    {
        if (clip == null) return;
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.volume = GetMusicVolume();
        bgmSource.Play();
    }

    public void StopBGM()
    {
        bgmSource.Stop();
    }

    // ---------- 音量读取（供各脚本自带 AudioSource / PlayClipAtPoint 使用） ----------

    public static float GetMusicVolume()
    {
        return SettingsManager.Instance != null ? SettingsManager.Instance.GetMusicVolume() : 1f;
    }

    public static float GetSFXVolume()
    {
        return SettingsManager.Instance != null ? SettingsManager.Instance.GetSFXVolume() : 1f;
    }
}