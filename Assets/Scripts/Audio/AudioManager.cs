using UnityEngine;
using UnityEngine.SceneManagement;

// 全局 BGM 管理器 + 音量读取 + SFX 播放池。
// SFX(攻击/技能/金币/buff) 的 Clip 放在各自脚本/Prefab 上，需要播放时调用 PlaySFX（走复用池，无 GC）。
// 注意：本单例不做懒创建（避免空壳假实例顶替场景里配置好的真实例）。
// 场景里必须放一个挂本脚本的对象（建议 MainMenu），它 DontDestroyOnLoad 跨场景常驻。
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("BGM")]
    [Tooltip("主菜单 / 加载 / 选关 共用一段 BGM")]
    public AudioClip menuBGM;
    [Tooltip("Easy / Medium 游戏内共用一段 BGM")]
    public AudioClip gameplayBGM;

    [Header("SFX 播放池")]
    [Tooltip("预创建多少个 AudioSource 循环复用，避免每次拾取/播放都 new 临时对象")]
    public int sfxPoolSize = 8;
    [Tooltip("音效是否 2D（=1 完全 2D，不受距离影响；=0 完全 3D 带距离衰减）")]
    [Range(0f, 1f)]
    public float sfxSpatialBlend = 1f;

    private AudioSource bgmSource;
    private AudioSource[] sfxPool;
    private int sfxPoolCursor = 0;

    // 主菜单系场景（共用一个 BGM）
    private static readonly string[] MenuScenes = { "MainMenu", "Loading", "Selection", "DifficultySelection" };
    // 游戏内场景（共用另一个 BGM），可按需要自行追加
    private static readonly string[] GameplayScenes = { "Easy", "Medium" };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;

        // 预创建 SFX 播放池
        sfxPool = new AudioSource[Mathf.Max(1, sfxPoolSize)];
        for (int i = 0; i < sfxPool.Length; i++)
        {
            sfxPool[i] = gameObject.AddComponent<AudioSource>();
            sfxPool[i].playOnAwake = false;
            sfxPool[i].spatialBlend = sfxSpatialBlend;
        }
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
        float v = GetMusicVolume();
        if (Mathf.Abs(bgmSource.volume - v) > 0.001f)
            bgmSource.volume = v;
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

    // ---------- SFX 播放（对象池复用，无临时对象 GC） ----------

    public void PlaySFX(AudioClip clip)
    {
        PlaySFX(clip, transform.position);
    }

    public void PlaySFX(AudioClip clip, Vector3 position)
    {
        if (clip == null) return;

        AudioSource src = GetFreeSource();
        src.spatialBlend = sfxSpatialBlend;
        src.transform.position = position;
        src.volume = 1f;
        src.PlayOneShot(clip, GetSFXVolume());
    }

    private AudioSource GetFreeSource()
    {
        // 优先找一个空闲的；全都在播就轮转覆盖最早的
        for (int i = 0; i < sfxPool.Length; i++)
        {
            int idx = (sfxPoolCursor + i) % sfxPool.Length;
            if (!sfxPool[idx].isPlaying)
            {
                sfxPoolCursor = (idx + 1) % sfxPool.Length;
                return sfxPool[idx];
            }
        }
        AudioSource src = sfxPool[sfxPoolCursor];
        src.Stop();
        sfxPoolCursor = (sfxPoolCursor + 1) % sfxPool.Length;
        return src;
    }

    // ---------- 音量读取（直接读 PlayerPrefs，不依赖 SettingsManager 是否在场） ----------
    // SettingsManager 写的就是同一个 PlayerPrefs key，两者天然一致；
    // 这样即使跳过主菜单直接进游戏场景，音量也是用户上次保存的值。

    public static float GetMusicVolume()
    {
        return PlayerPrefs.GetFloat("MusicVolume", 0.8f);
    }

    public static float GetSFXVolume()
    {
        return PlayerPrefs.GetFloat("SFXVolume", 0.8f);
    }
}