using UnityEngine;

public class Coin : MonoBehaviour
{
    [Header("金币设置")]
    public int coinValue = 5;
    public float autoCollectRadius = 3f;
    public float autoCollectSpeed = 5f;
    public float lifeTime = 10f; // 自动吸附延迟

    private PlayerController player;
    private bool isCollecting = false;
    private float timer = 0f;

    void Start()
    {
        player = FindObjectOfType<PlayerController>();
        timer = lifeTime;

        // 设置标签
        gameObject.tag = "Coin";
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);

        // 如果距离小于自动吸附范围，开始吸附
        if (distance <= autoCollectRadius)
        {
            isCollecting = true;
        }
        else
        {
            // 如果玩家距离较远，减少计时器
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                // 时间到，自动吸附
                isCollecting = true;
            }
        }

        // 如果玩家在范围内，重置计时器
        if (distance <= autoCollectRadius)
        {
            timer = lifeTime;
        }

        // 吸附到玩家
        if (isCollecting)
        {
            Vector3 direction = (player.transform.position - transform.position).normalized;
            transform.position += direction * autoCollectSpeed * Time.deltaTime;

            // 如果到达玩家位置，收集金币
            if (distance < 0.5f)
            {
                Collect();
            }
        }

        // 旋转动画
        transform.Rotate(Vector3.up * 100f * Time.deltaTime);
    }

    public void SetValue(int value)
    {
        coinValue = value;
    }

    void Collect()
    {
        if (player != null)
        {
            player.AddCoin(coinValue);
            Debug.Log($"💰 收集 {coinValue} 金币");
        }
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, autoCollectRadius);
    }
}