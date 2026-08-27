using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

// 全局 BGM 管理器 + 音量读取 + SFX 播放池。
// SFX(攻击/技能/金币/buff) 的 Clip 放在各自脚本/Prefab 上，需要播放时调用 PlaySFX（走复用池，无 GC）。
// 场景里必须放一个挂本脚本的对象（建议 MainMenu），它 DontDestroyOnLoad 跨场景常驻。
// 兜底：单独直接播放任意场景（如 Easy / Medium / Infinite 调试）时，若场景里没有 AudioManager，
// 启动时会自动创建一个运行时实例（BGM 从 Resources 读取），SFX 照常工作；与场景真实实例并存时
// 由 Awake 的单例守卫自动去重，不会冲突。
public class AudioManager : MonoBehaviour
{
    // 任何场景单独播放（测试）时，场景没有配置好的 AudioManager 就自动补一个运行时实例。
    // AfterSceneLoad 保证在场景内所有对象的 Awake 之后、Start 之前执行：
    // 若场景里已有真实实例（Awake 已注册 Instance），这里不会重复创建。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreateIfMissing()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("AudioManager (Runtime)");
        go.AddComponent<AudioManager>();
    }

    public static AudioManager Instance { get; private set; }

    [Header("BGM")]
    [Tooltip("主菜单 / 加载 / 选关 共用一段 BGM")]
    public AudioClip menuBGM;
    [Tooltip("Easy / Medium 游戏内共用一段 BGM")]
    public AudioClip gameplayBGM;
    [Tooltip("地牢模式专属 BGM（进入地牢时覆盖游戏内 BGM）")]
    public AudioClip dungeonBGM;

    [Header("SFX 播放池")]
    [Tooltip("预创建多少个 AudioSource 循环复用，避免每次拾取/播放都 new 临时对象")]
    public int sfxPoolSize = 16;
    [Tooltip("音效是否 2D（=1 完全 2D，不受距离影响；=0 完全 3D 带距离衰减）")]
    [Range(0f, 1f)]
    public float sfxSpatialBlend = 1f;
    [Tooltip("SFX 音量增益倍率（调大让整体音效更响；结果会被钳制到 0~1）")]
    [Range(0.1f, 3f)]
    public float sfxVolumeGain = 1.5f;

    [Header("Mixer 控制")]
    [Tooltip("MasterMixer：BGM 走 Music 组、所有 SFX（含按钮点击音）走 Sfx 组，音量统一由暴露参数控制")]
    public AudioMixer masterMixer;

    private AudioSource bgmSource;
    private AudioSource[] sfxPool;
    private int sfxPoolCursor = 0;

    // Mixer 暴露参数最近一次写入的 dB 值（避免每帧重复 SetFloat）
    private float lastMixerMusicDB = float.PositiveInfinity;
    private float lastMixerSFXDB = float.PositiveInfinity;

    // 主菜单系场景（共用一个 BGM）
    private static readonly string[] MenuScenes = { "MainMenu", "Loading", "Selection", "DifficultySelection", "Story" };
    // 游戏内场景（共用另一个 BGM），可按需要自行追加
    private static readonly string[] GameplayScenes = { "Easy", "Medium", "Infinite" };

    void Awake()
    {
        Debug.Log("[Boot] AudioManager.Awake begin");
        if (Instance != null && Instance != this)
        {
            // 只销毁重复的 AudioManager 组件，保留 GameObject 及其上的点击音 AudioSource：
            // 返回主菜单时场景会再放一个挂本脚本 + 点击音 AudioSource 的物体，
            // 如果连 GameObject 一起销毁，按钮上 UnityEvent 引用的那个 AudioSource 就变成空，
            // 点击音会消失。保留外壳即可让按钮点击音继续可用。
            Destroy(this);
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

        // 统一走 Mixer：BGM → Music 组，SFX 池 → Sfx 组
        SetupOutputGroups();

        // 兜底：场景没配 Clip（例如运行时自建的实例）时从 Resources 读，
        // 保证任意场景单独播放（测试）也能出 BGM。已有场景配置的不受影响。
        if (menuBGM == null) menuBGM = Resources.Load<AudioClip>("Audio/BGM MAIN MENU");
        if (gameplayBGM == null) gameplayBGM = Resources.Load<AudioClip>("Audio/BGM GAMEPLAY");
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
        Debug.Log("[Boot] AudioManager.Start begin");
        PlayBGMForScene(SceneManager.GetActiveScene().name);
        Debug.Log("[Boot] AudioManager.Start end");
    }

    void Update()
    {
        if (masterMixer != null)
        {
            // 统一走 Mixer：Music/Sfx 组音量由暴露参数控制
            float musicDB = LinearToDecibels(GetMusicVolume());
            float sfxDB = LinearToDecibels(Mathf.Clamp01(GetSFXVolume() * sfxVolumeGain));

            if (Mathf.Abs(musicDB - lastMixerMusicDB) > 0.01f)
            {
                lastMixerMusicDB = musicDB;
                masterMixer.SetFloat("MusicVolume", musicDB);
            }
            if (Mathf.Abs(sfxDB - lastMixerSFXDB) > 0.01f)
            {
                lastMixerSFXDB = sfxDB;
                masterMixer.SetFloat("SFXVolume", sfxDB);
            }
        }
        else
        {
            // 兜底（没有 Mixer 时）：直接同步 BGM 源音量
            float v = GetMusicVolume();
            if (Mathf.Abs(bgmSource.volume - v) > 0.001f)
                bgmSource.volume = v;
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayBGMForScene(scene.name);
    }

    // BGM → Music 组，SFX 池 → Sfx 组
    void SetupOutputGroups()
    {
        if (masterMixer == null) return;

        AudioMixerGroup[] music = masterMixer.FindMatchingGroups("Music");
        if (music != null && music.Length > 0)
            bgmSource.outputAudioMixerGroup = music[0];

        AudioMixerGroup[] sfx = masterMixer.FindMatchingGroups("Sfx");
        if (sfx != null && sfx.Length > 0)
        {
            for (int i = 0; i < sfxPool.Length; i++)
                sfxPool[i].outputAudioMixerGroup = sfx[0];
        }
    }

    // 线性 0~1 音量 → dB（0.0001≈-80dB 静音，1≈0dB 全音量）
    static float LinearToDecibels(float linear)
    {
        linear = Mathf.Clamp(linear, 0.0001f, 1f);
        return Mathf.Log10(linear) * 20f;
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
        bgmSource.volume = 1f;
        bgmSource.Play();
    }

    // 地牢模式专属 BGM：进入地牢时覆盖普通游戏内 BGM
    public void PlayDungeonBGM()
    {
        if (dungeonBGM == null) dungeonBGM = Resources.Load<AudioClip>("Audio/BGM DUNGEON");
        if (dungeonBGM != null) PlayBGM(dungeonBGM);
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
        if (masterMixer != null)
            src.PlayOneShot(clip);
        else
            src.PlayOneShot(clip, Mathf.Clamp01(GetSFXVolume() * sfxVolumeGain));
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