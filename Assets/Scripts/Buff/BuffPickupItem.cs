using UnityEngine;

public class BuffPickupItem : MonoBehaviour
{
    [Header("漂浮与旋转参数")]
    public float rotateSpeed = 60f;
    public float floatAmplitude = 0.3f;
    public float floatFrequency = 1.5f;

    [Header("绑定数据")]
    public BuffDataSO buffData;

    private Vector3 startPosition;
    private Rigidbody rb;
    private bool isGrounded;

    void Start()
    {
        startPosition = transform.position;
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);

        if (GetComponent<Collider>() == null)
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.8f;
        }
    }

    void Update()
    {
        transform.Rotate(Vector3.right, rotateSpeed * Time.deltaTime, Space.World);

        if (isGrounded || rb.velocity.magnitude < 0.1f)
        {
            float newY = startPosition.y + Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            isGrounded = true;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isGrounded && rb.velocity.magnitude < 1f)
        {
            isGrounded = true;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
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