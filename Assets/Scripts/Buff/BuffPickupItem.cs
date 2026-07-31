using UnityEngine;

public class BuffPickupItem : MonoBehaviour
{
    [Header("绑定数据")]
    public BuffDataSO buffData;

    void Start()
    {
        // 确保有碰撞器
        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.8f;
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
                Destroy(gameObject);
            }
        }
    }
}