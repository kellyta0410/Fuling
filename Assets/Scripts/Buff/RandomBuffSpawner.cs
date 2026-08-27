using UnityEngine;
using System.Collections.Generic;

// Buff 掉落管理器：不再按定时在场景里刷新 Buff，
// 改为每击杀 buffKillInterval 个敌人必定掉落一个随机 Buff（EnemyAI.Die() → RandomBuffSpawner.Instance.OnEnemyKilled）。
// 掉落物仍是 Buff prefab，拾取方式不变（BuffPickupItem.OnTriggerEnter）。
public class RandomBuffSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BuffEntry
    {
        public BuffDataSO buffData;
        [Range(0, 100)]
        public int weight = 1;
    }

    // 供 EnemyAI 静态调用（场景里有且只有一个 Buff Manager）
    public static RandomBuffSpawner Instance { get; private set; }

    [Header("掉落配置")]
    public BuffEntry[] buffPool;
    [Tooltip("每击杀多少个敌人必定掉落一个随机 Buff")]
    public int buffKillInterval = 3;
    [Header("拾取音效兜底")]
    [Tooltip("pickupPrefab 上没有配 collectSFX 时，用这个作为默认拾取音效")]
    public AudioClip defaultBuffSFX;

    [Header("自动销毁配置")]
    public Transform player;
    public float despawnDistance = 30f;
    public float buffLifeTime = 30f;
    public float cleanupInterval = 2f;

    private float cleanupTimer;
    private int killCounter;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Update()
    {
        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= cleanupInterval)
        {
            cleanupTimer = 0f;
            CleanupBuffs();
        }
    }

    // ⭐ 每击杀 buffKillInterval 个敌人必定掉落一个随机 Buff（由 EnemyAI.Die() 调用）
    public void OnEnemyKilled(Vector3 dropPos)
    {
        // ⭐ 地牢（无尽）模式：怪物不掉落 Buff，玩家只能在商店购买
        if (GameManager.Instance != null && GameManager.Instance.IsDungeonMode()) return;
        if (!enabled) return;
        killCounter++;
        if (killCounter < buffKillInterval) return;
        killCounter = 0;

        SpawnBuff(dropPos);
    }

    // 在死亡位置掉落一个随机 Buff（像金币一样弹出），拾取方式不变（BuffPickupItem）
    void SpawnBuff(Vector3 dropPos)
    {
        if (buffPool == null || buffPool.Length == 0) return;

        BuffDataSO selected = PickRandomBuff();
        if (selected == null || selected.pickupPrefab == null) return;

        Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0.5f, Random.Range(-0.3f, 0.3f));
        GameObject newBuff = Instantiate(selected.pickupPrefab, dropPos + offset, Quaternion.Euler(0, Random.Range(0f, 360f), 0));

        BuffPickupItem pickup = newBuff.GetComponent<BuffPickupItem>();
        if (pickup == null) pickup = newBuff.AddComponent<BuffPickupItem>();
        pickup.buffData = selected;
        // 运行时补挂的 BuffPickupItem 没有 pickupPrefab 上的 collectSFX，用兜底音效
        if (pickup.collectSFX == null)
        {
            pickup.collectSFX = defaultBuffSFX;
        }

        AutoDestroyBuff autoDestroy = newBuff.GetComponent<AutoDestroyBuff>();
        if (autoDestroy == null) autoDestroy = newBuff.AddComponent<AutoDestroyBuff>();
        autoDestroy.Initialize(buffLifeTime);

        Rigidbody rb = newBuff.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 randomDir = new Vector3(Random.Range(-0.5f, 0.5f), 1, Random.Range(-0.5f, 0.5f)).normalized;
            rb.AddForce(randomDir * 1.5f, ForceMode.Impulse);
        }
    }

    private BuffDataSO PickRandomBuff()
    {
        List<BuffEntry> candidates = new List<BuffEntry>();
        int totalWeight = 0;
        foreach (var entry in buffPool)
        {
            if (entry.buffData != null && entry.weight > 0)
            {
                candidates.Add(entry);
                totalWeight += entry.weight;
            }
        }
        if (candidates.Count == 0 || totalWeight == 0) return null;

        int randomValue = Random.Range(0, totalWeight);
        int accumulated = 0;
        foreach (var entry in candidates)
        {
            accumulated += entry.weight;
            if (randomValue < accumulated) return entry.buffData;
        }
        return candidates[0].buffData;
    }

    void CleanupBuffs()
    {
        if (player == null) return;

        BuffPickupItem[] buffs = FindObjectsOfType<BuffPickupItem>();
        foreach (var buff in buffs)
        {
            if (buff == null) continue;

            float distance = Vector3.Distance(player.position, buff.transform.position);
            if (distance > despawnDistance)
            {
                Destroy(buff.gameObject);
            }
        }
    }

    // 由 GameManager 开局调用：控制本局是否掉 Buff、掉落物的存活时间。
    public void ApplyDifficultySettings(DifficultySettings settings)
    {
        if (settings == null) return;

        buffLifeTime = settings.buffLifeTime;
        enabled = settings.enableBuffSpawning;
        if (!settings.enableBuffSpawning)
        {
            BuffPickupItem[] buffs = FindObjectsOfType<BuffPickupItem>();
            foreach (BuffPickupItem buff in buffs)
            {
                if (buff != null) Destroy(buff.gameObject);
            }
            Debug.Log("🎁 当前难度关闭了 Buff 掉落");
        }
    }
}