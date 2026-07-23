using UnityEngine;

/// <summary>
/// 投射物脚本 - 挂在投射物预制体上
/// </summary>
public class Projectile : MonoBehaviour
{
    private int damage;
    private float speed;
    private Vector3 direction;
    private GameObject owner;

    [Header("视觉效果")]
    public GameObject hitEffect;        // 命中特效
    public float lifeTime = 5f;         // 自动销毁时间

    public void Initialize(int damage, float speed, Vector3 direction, GameObject owner)
    {
        this.damage = damage;
        this.speed = speed;
        this.direction = direction.normalized;
        this.owner = owner;

        // 设置旋转朝向移动方向
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        // 自动销毁
        Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        // 移动
        transform.position += direction * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        // 忽略发射者
        if (other.gameObject == owner) return;

        // 命中敌人
        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null && !enemy.isDead)
        {
            enemy.TakeDamageImmediate(damage);
            Debug.Log($"🔥 投射物命中 {enemy.name}，造成 {damage} 伤害");
            OnHit();
            return;
        }

        // 撞墙销毁
        if (other.CompareTag("Wall") || other.CompareTag("Obstacle") || other.CompareTag("Ground"))
        {
            OnHit();
        }
    }

    void OnHit()
    {
        // 播放命中特效
        if (hitEffect != null)
        {
            Instantiate(hitEffect, transform.position, Quaternion.identity);
        }

        Destroy(gameObject);
    }
}