using UnityEngine;

public class AutoDestroyBuff : MonoBehaviour
{
    [Header("生命周期")]
    public float lifeTime = 30f;
    private float timer;
    private bool isInitialized = false;

    [Header("漂浮动画")]
    public float floatAmplitude = 0.3f;
    public float floatSpeed = 1.5f;
    private Vector3 startPosition;
    private float randomOffset;

    [Header("旋转")]
    public float rotateSpeed = 60f;

    [Header("闪烁效果")]
    public float flashStartTime = 5f;
    public float flashInterval = 0.3f;
    private float flashTimer;
    private bool isVisible = true;
    private Renderer[] renderers;

    [Header("消失特效")]
    public GameObject destroyEffectPrefab;

    void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();
        startPosition = transform.position;
        randomOffset = Random.Range(0f, Mathf.PI * 2f);

        if (!isInitialized)
        {
            Initialize(lifeTime);
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        timer += Time.deltaTime;
        float remainingTime = lifeTime - timer;

        if (remainingTime <= 0)
        {
            DestroyBuff();
            return;
        }

        // 漂浮动画
        float floatOffset = Mathf.Sin((timer + randomOffset) * floatSpeed) * floatAmplitude;
        Vector3 newPos = startPosition;
        newPos.y += floatOffset;
        transform.position = newPos;

        // 自转
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime);

        // 闪烁
        if (remainingTime <= flashStartTime)
        {
            flashTimer += Time.deltaTime;
            if (flashTimer >= flashInterval)
            {
                flashTimer = 0f;
                ToggleVisibility();
            }
        }
        else
        {
            if (!isVisible)
            {
                SetVisibility(true);
            }
        }
    }

    void ToggleVisibility()
    {
        isVisible = !isVisible;
        SetVisibility(isVisible);
    }

    void SetVisibility(bool visible)
    {
        foreach (var renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
        isVisible = visible;
    }

    void DestroyBuff()
    {
        if (destroyEffectPrefab != null)
        {
            Instantiate(destroyEffectPrefab, transform.position, Quaternion.identity);
        }

        Debug.Log($"Buff {gameObject.name} 因生命周期结束而销毁");
        Destroy(gameObject);
    }

    public void Initialize(float time)
    {
        lifeTime = time;
        timer = 0f;
        isInitialized = true;
    }

    // 如果被拾取，取消销毁（可选）
    void OnDestroy()
    {
        // 如果是被拾取而销毁，这里会执行
        // 但因为我们直接用Destroy(gameObject)，不会触发超时销毁的特效
    }
}