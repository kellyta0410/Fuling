using UnityEngine;

public class Coin : MonoBehaviour
{
    [Header("金币设置")]
    public int coinValue = 5;
    public float autoCollectSpeed = 10f;
    [Tooltip("玩家尚未生成时金币的存活时间（秒），超时销毁避免残留")]
    public float despawnIfNoPlayer = 15f;
    [Header("音效")]
    [Tooltip("拾取音效（Clip 放这里，音量读 SettingsManager）")]
    public AudioClip collectSFX;

    private PlayerController player;
    private bool isCollecting = false;
    private float lastFindTime = -1f;
    private float missingTimer = 0f;

    void Start()
    {
        player = FindObjectOfType<PlayerController>();

        // 设置标签
        gameObject.tag = "Coin";

        // 金币生成后立即飞向玩家，不停留等待（掉落即飞）
        isCollecting = true;
    }

    void Update()
    {
        // 玩家还没生成（金币可能早于玩家出生）：周期性重找，超时就销毁避免永久残留
        if (player == null)
        {
            if (Time.time - lastFindTime > 0.5f)
            {
                lastFindTime = Time.time;
                player = FindObjectOfType<PlayerController>();
            }
            missingTimer += Time.deltaTime;
            if (missingTimer > despawnIfNoPlayer)
            {
                Destroy(gameObject);
            }
            return;
        }

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
        // 走 AudioManager 播放池：复用预建的 AudioSource，不产生临时对象 GC；
        // 音量读 AudioManager（PlayerPrefs 兜底，不依赖 SettingsManager 在场）
        if (collectSFX != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(collectSFX, transform.position);
        }
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}