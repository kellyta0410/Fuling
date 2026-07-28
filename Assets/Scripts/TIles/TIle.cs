// 保存到 Assets/Scripts/World/Tile.cs
using UnityEngine;

public class Tile : MonoBehaviour
{
    public TileType tileType;
    public Vector2Int gridPosition;
    public bool isActive = false;
    public Transform[] spawnPoints;

    public void Initialize(Vector2Int position, TileType type)
    {
        gridPosition = position;
        tileType = type;
        gameObject.name = $"{type}_Tile_{position.x}_{position.y}";
    }

    public void Activate()
    {
        isActive = true;
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        isActive = false;
        gameObject.SetActive(false);
    }
}