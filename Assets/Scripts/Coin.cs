using UnityEngine;

public class Coin : MonoBehaviour
{
    [Header("金币设置")]
    public int coinValue = 5;
    public float autoCollectRadius = 3f;
    public float autoCollectSpeed = 10f;
    public float lifeTime = 10f; // 自动吸附延迟
    [Header("音效")]
    [Tooltip("拾取音效（Clip 放这里，音量读 SettingsManager）")]
    public AudioClip collectSFX;

    private PlayerController player;
    private bool isCollecting = false;
    private float timer = 0f;

    void Start()
    {
        player = FindObjectOfType<PlayerController>();
        timer = lifeTime;

        // 设置标签
        gameObject.tag = "Coin";

        // 金币生成后立即飞向玩家，不停留等待（掉落即飞）
        isCollecting = true;
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);

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
        }
        // 用 PlayClipAtPoint：物体随即销毁，声音不会被带走（自带临时 AudioSource，播完自删）
        if (collectSFX != null)
        {
            AudioSource.PlayClipAtPoint(collectSFX, transform.position, AudioManager.GetSFXVolume());
        }
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, autoCollectRadius);
    }
}