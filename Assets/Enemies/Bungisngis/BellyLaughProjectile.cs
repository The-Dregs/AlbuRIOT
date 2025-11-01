using UnityEngine;

public class BellyLaughProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    public int damage = 15;
    public BungisngisAI owner;

    [Header("Movement")]
    public float speed = 18f;
    public float lifetime = 2.5f;

    private void Start()
    {
        // Auto-destroy after lifetime
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // Move forward each frame
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps != null)
        {
            DamageRelay.ApplyToPlayer(ps.gameObject, damage);
            Destroy(gameObject);
        }
    }
}