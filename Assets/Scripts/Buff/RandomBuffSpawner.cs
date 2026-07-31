using UnityEngine;
using System.Collections.Generic;

public class RandomBuffSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BuffEntry
    {
        public BuffDataSO buffData;
        [Range(0, 100)]
        public int weight = 1;
    }

    [Header("生成配置")]
    public BuffEntry[] buffPool;
    public float spawnRadius = 12f;
    public float spawnInterval = 6f;
    public int maxBuffCount = 5;

    [Header("玩家引用")]
    public Transform player;

    [Header("碰撞检测优化")]
    public LayerMask obstacleMask;
    public LayerMask groundMask;
    public float checkRadius = 0.8f;
    public int maxRetries = 10;

    [Header("自动销毁配置")]
    public float despawnDistance = 30f;
    public float buffLifeTime = 30f;
    public float cleanupInterval = 2f;

    private float timer;
    private float cleanupTimer;

    void Start()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;

        for (int i = 0; i < 2; i++) SpawnBuff();
    }

    void Update()
    {
        if (player == null) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnBuff();
        }

        cleanupTimer += Time.deltaTime;
        if (cleanupTimer >= cleanupInterval)
        {
            cleanupTimer = 0f;
            CleanupBuffs();
        }
    }

    void SpawnBuff()
    {
        BuffPickupItem[] existing = FindObjectsOfType<BuffPickupItem>();
        if (existing.Length >= maxBuffCount) return;

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
        if (candidates.Count == 0 || totalWeight == 0) return;

        int randomValue = Random.Range(0, totalWeight);
        int accumulated = 0;
        BuffDataSO selected = null;
        foreach (var entry in candidates)
        {
            accumulated += entry.weight;
            if (randomValue < accumulated)
            {
                selected = entry.buffData;
                break;
            }
        }
        if (selected == null) selected = candidates[0].buffData;

        if (selected.pickupPrefab == null)
        {
            Debug.LogWarning($"Buff {selected.buffName} 没有指定 pickupPrefab！");
            return;
        }

        Vector3 finalSpawnPos = Vector3.zero;
        bool foundValidSpot = false;

        for (int i = 0; i < maxRetries; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 candidatePos = new Vector3(
                player.position.x + randomCircle.x,
                player.position.y + 5f,
                player.position.z + randomCircle.y
            );

            if (Physics.Raycast(candidatePos, Vector3.down, out RaycastHit hit, 20f, groundMask))
            {
                candidatePos.y = hit.point.y + 0.6f;
            }
            else
            {
                continue;
            }

            if (!Physics.CheckSphere(candidatePos, checkRadius, obstacleMask))
            {
                Vector3 dirToPlayer = (player.position - candidatePos).normalized;
                float distToPlayer = Vector3.Distance(player.position, candidatePos);
                if (!Physics.Raycast(candidatePos, dirToPlayer, distToPlayer, obstacleMask))
                {
                    finalSpawnPos = candidatePos;
                    foundValidSpot = true;
                    break;
                }
            }
        }

        if (!foundValidSpot)
        {
            Debug.LogWarning("未找到安全的生成位置，本次跳过生成");
            return;
        }

        GameObject newBuff = Instantiate(selected.pickupPrefab, finalSpawnPos, Quaternion.identity);
        BuffPickupItem pickup = newBuff.GetComponent<BuffPickupItem>();
        if (pickup == null) pickup = newBuff.AddComponent<BuffPickupItem>();
        pickup.buffData = selected;

        // 【新增】添加AutoDestroyBuff组件
        AutoDestroyBuff autoDestroy = newBuff.GetComponent<AutoDestroyBuff>();
        if (autoDestroy == null) autoDestroy = newBuff.AddComponent<AutoDestroyBuff>();
        autoDestroy.Initialize(buffLifeTime);

        newBuff.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

        Rigidbody rb = newBuff.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 randomDir = new Vector3(Random.Range(-0.5f, 0.5f), 1, Random.Range(-0.5f, 0.5f)).normalized;
            rb.AddForce(randomDir * 1.5f, ForceMode.Impulse);
        }
    }

    void CleanupBuffs()
    {
        if (player == null) return;

        BuffPickupItem[] buffs = FindObjectsOfType<BuffPickupItem>();
        List<GameObject> toDestroy = new List<GameObject>();

        foreach (var buff in buffs)
        {
            if (buff == null) continue;

            float distance = Vector3.Distance(player.position, buff.transform.position);

            if (distance > despawnDistance)
            {
                toDestroy.Add(buff.gameObject);
                Debug.Log($"销毁远处Buff: {buff.buffData?.buffName ?? "Unknown"} (距离: {distance:F1})");
            }
        }

        foreach (var obj in toDestroy)
        {
            Destroy(obj);
        }

        if (toDestroy.Count > 0)
        {
            BuffPickupItem[] remaining = FindObjectsOfType<BuffPickupItem>();
            if (remaining.Length < maxBuffCount)
            {
                SpawnBuff();
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (player != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(player.position, spawnRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(player.position, despawnDistance);
        }
    }
}