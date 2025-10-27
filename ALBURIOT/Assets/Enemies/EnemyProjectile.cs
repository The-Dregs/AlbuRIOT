using UnityEngine;

public class EnemyProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    public int damage = 10;
    public GameObject owner;
    [Header("Projectile Movement")]
    public float speed = 10f;
    public float lifetime = 2f;
    public float destroyDelay = 0.1f;

    private void Awake()
    {
        // Try to auto-assign owner if not set
        if (owner == null)
        {
            var parentAI = GetComponentInParent<MonoBehaviour>();
            if (parentAI != null)
                owner = parentAI.gameObject;
        }
        // destroy after lifetime
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // move forward
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps != null)
        {
            DamageRelay.ApplyToPlayer(ps.gameObject, damage);
            Destroy(gameObject, destroyDelay);
        }
    }
}
