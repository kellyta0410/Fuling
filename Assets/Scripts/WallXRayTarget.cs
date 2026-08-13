using UnityEngine;

// 方案B辅助：挂在"无 collider 的纯网格墙"上（或代码生成的墙上），
// 让 CinemachineWallXRay 能通过注册表发现它——即使它被大碰撞体盖住、没有自己的 collider，
// 只要处于相机与玩家之间就会一起 X-Ray 半透明。
public class WallXRayTarget : MonoBehaviour
{
    [SerializeField] private Renderer _renderer;

    public Renderer Renderer
    {
        get
        {
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();
            return _renderer;
        }
    }

    void OnEnable()
    {
        CinemachineWallXRay.RegisterWall(this);
    }

    void OnDisable()
    {
        CinemachineWallXRay.UnregisterWall(this);
    }
}