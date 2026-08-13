using UnityEngine;

// 蛇头攻击判定：攻击动画期间由 SnakeEnemy 启用，跟随蛇头变形后的世界位置。
// 只有玩家接触到这个蛇头球体才造成伤害，身体/尾巴不再拥有攻击判定。
public class SnakeHeadHitbox : MonoBehaviour
{
    private SnakeEnemy owner;
    private bool damageDealt = false;

    public void SetOwner(SnakeEnemy o) { owner = o; }

    // 每次攻击开始时由 SnakeEnemy 调用，重置为本轮攻击是否已造成伤害
    public void BeginAttack() { damageDealt = false; }

    // 突进速度较快时首帧进入也可能被物理错过，Enter + Stay 双保险
    void OnTriggerEnter(Collider other) { TryHit(other); }
    void OnTriggerStay(Collider other) { TryHit(other); }

    void TryHit(Collider other)
    {
        if (damageDealt || owner == null) return;
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        if (owner.TryDealHeadDamage(pc)) damageDealt = true;
    }
}