using UnityEngine;

public class RandomBuffSpawner : MonoBehaviour
{
    [Header("生成配置")]
    public BuffDataSO[] buffPool;
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

    // 新增：模型高度偏移（根据你的模型大小调整，确保模型底部刚好在地面上）
    [Header("生成高度调整")]
    public float heightOffset = 0.5f; // 模型中心到地面的距离

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
        BuffPickupItem[] existing = FindObjectsOfType<BuffPickupItem>();
        if (existing.Length >= maxBuffCount) return;

        if (buffPool == null || buffPool.Length == 0) return;
        BuffDataSO selected = buffPool[Random.Range(0, buffPool.Length)];
        if (selected == null || selected.pickupPrefab == null) return;

        Vector3 finalSpawnPos = Vector3.zero;
        bool foundValidSpot = false;

        for (int i = 0; i < maxRetries; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 candidatePos = new Vector3(
                player.position.x + randomCircle.x,
                player.position.y + 5f, // 从玩家上方5米开始射线
                player.position.z + randomCircle.y
            );

            // 射线检测地面
            if (Physics.Raycast(candidatePos, Vector3.down, out RaycastHit hit, 20f, groundMask))
            {
                // 地面高度 + 偏移量（让模型底部贴地）
                candidatePos.y = hit.point.y + heightOffset;
            }
            else
            {
                // 没打到地面 -> 放弃此次尝试
                continue;
            }

            // 障碍物检测
            if (!Physics.CheckSphere(candidatePos, checkRadius, obstacleMask))
            {
                // 可选：检测与玩家之间是否有遮挡
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

        // 实例化
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