using UnityEngine;

public class BuffPickupItem : MonoBehaviour
{
    [Header("绑定数据")]
    public BuffDataSO buffData;
    [Header("音效")]
    [Tooltip("拾取音效（Clip 放这里，音量读 SettingsManager）")]
    public AudioClip collectSFX;

    void Start()
    {
        // 确保有碰撞器
        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.8f;
        }

        // 自动挂载地面光圈（漂浮时的指示物，颜色随 buff 类型）
        if (GetComponent<BuffGroundRing>() == null)
        {
            gameObject.AddComponent<BuffGroundRing>();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            BuffHandler handler = other.GetComponent<BuffHandler>();
            if (handler != null && buffData != null)
            {
                handler.ApplyBuff(buffData);
                Debug.Log($"拾取了 {buffData.buffName}！");
                if (collectSFX != null)
                {
                    AudioSource.PlayClipAtPoint(collectSFX, transform.position, AudioManager.GetSFXVolume());
                }
                Destroy(gameObject);
            }
        }
    }
}