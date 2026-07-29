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
    public BuffEntry[] buffPool; // 在Inspector里配置每个Buff和权重
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

    private float timer;

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
    }

    void SpawnBuff()
    {
        // 1. 总数限制
        BuffPickupItem[] existing = FindObjectsOfType<BuffPickupItem>();
        if (existing.Length >= maxBuffCount) return;

        // 2. 从 buffPool 中按权重随机选择（忽略权重为0的项）
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

        // 检查预制体
        if (selected.pickupPrefab == null)
        {
            Debug.LogWarning($"Buff {selected.buffName} 没有指定 pickupPrefab！");
            return;
        }

        // 3. 寻找安全生成点（带重试）
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

        // 4. 实例化
        GameObject newBuff = Instantiate(selected.pickupPrefab, finalSpawnPos, Quaternion.identity);
        BuffPickupItem pickup = newBuff.GetComponent<BuffPickupItem>();
        if (pickup == null) pickup = newBuff.AddComponent<BuffPickupItem>();
        pickup.buffData = selected;

        newBuff.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

        Rigidbody rb = newBuff.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 randomDir = new Vector3(Random.Range(-0.5f, 0.5f), 1, Random.Range(-0.5f, 0.5f)).normalized;
            rb.AddForce(randomDir * 1.5f, ForceMode.Impulse);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (player != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(player.position, spawnRadius);
        }
    }
}