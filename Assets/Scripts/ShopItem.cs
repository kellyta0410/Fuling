using System.Collections.Generic;
using UnityEngine;

// 商店房间的购买型 Buff：玩家靠近后点击，弹出随机 3 个 Buff 供选择购买。
// 恢复(Heal)为即时生效；其他为永久生效并可叠加（上限见 BuffDataSO.maxStack）。
public class ShopItem : MonoBehaviour
{
    [Header("交互")]
    public float interactRadius = 3f;

    private List<BuffDataSO> buffPool;
    private int roomCost;
    private bool playerNear = false;

    public void Setup(List<BuffDataSO> pool, DungeonManager manager, int cost)
    {
        buffPool = pool;
        roomCost = cost;

        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;
        }

        // 用一个明显的发光小球表示商店
        GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.transform.SetParent(transform);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localScale = Vector3.one * 0.8f;
        var r = vis.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = Color.yellow;
            r.material.color = c;
            r.material.SetColor("_EmissionColor", c);
        }
        Destroy(vis.GetComponent<Collider>());
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) playerNear = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) playerNear = false;
    }

    void OnMouseDown()
    {
        if (!playerNear) return;
        if (buffPool == null || buffPool.Count == 0) return;

        // 把整个池交给 UIManager，由它随机抽 3 个并支持刷新
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null) ui.OpenShop(buffPool, roomCost);
    }
}
