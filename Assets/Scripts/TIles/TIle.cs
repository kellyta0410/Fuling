using UnityEngine;
using System.Collections.Generic;

public enum TileType
{
    Type0,
    Type1,
    Type2
}

public class Tile : MonoBehaviour
{
    [Header("Tile设置")]
    public TileType tileType;
    public Vector2Int gridPosition;
    public float tileSize = 20f;

    [Header("生成点")]
    public Transform spawnPoint;  // 单个生成点

    [Header("状态")]
    public bool isActive = false;

    private List<GameObject> activeEnemies = new List<GameObject>();

    void Awake()
    {
        // 如果没有手动在 Inspector 中赋值，自动查找或创建子对象
        if (spawnPoint == null)
        {
            spawnPoint = transform.Find("SpawnPoint");
            if (spawnPoint == null)
            {
                GameObject go = new GameObject("SpawnPoint");
                go.transform.SetParent(transform);
                go.transform.localPosition = Vector3.zero;
                spawnPoint = go.transform;
            }
        }
    }

    public void Initialize(Vector2Int position, int typeIndex)
    {
        gridPosition = position;
        tileType = (TileType)typeIndex;
        gameObject.name = $"Tile_{position.x}_{position.y}_Type{typeIndex}";
    }

    public void Activate()
    {
        if (isActive) return;
        isActive = true;
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        if (!isActive) return;
        isActive = false;
        // ⭐ 修正：Deactivate 仅仅是暂停/隐藏，不要清理敌人，防止敌人凭空消失
        gameObject.SetActive(false);
    }

    public void RegisterEnemy(GameObject enemy)
    {
        if (enemy == null) return;

        if (!activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);

            EnemyAI enemyAI = enemy.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.ownerTile = this;
            }
        }
    }

    public void UnregisterEnemy(GameObject enemy)
    {
        if (enemy != null && activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
        }
    }

    public void ClearAllEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] != null)
            {
                Destroy(activeEnemies[i]);
            }
        }
        activeEnemies.Clear();
    }

    public List<GameObject> GetEnemies()
    {
        // ⭐ 修正：获取列表前过滤已被销毁的敌人
        activeEnemies.RemoveAll(e => e == null);
        return activeEnemies;
    }

    public bool HasEnemies()
    {
        activeEnemies.RemoveAll(e => e == null);
        return activeEnemies.Count > 0;
    }

    void OnDestroy()
    {
        // 当 Tile 距离玩家过远被彻底 Destroy 销毁时，才清理附着的敌人
        ClearAllEnemies();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (spawnPoint != null)
        {
            Gizmos.color = isActive ? Color.green : Color.gray;
            Gizmos.DrawSphere(spawnPoint.position, 0.5f);
        }
    }
#endif
}