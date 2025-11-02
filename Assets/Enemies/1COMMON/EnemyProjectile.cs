using UnityEngine;

public class EnemyProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    public int damage = 10;
    public GameObject owner;
    public LayerMask hitMask = 0; // if zero, defaults to Player layer at runtime
    public bool destroyOnHit = true;

    [Header("Projectile Movement")]
    public float speed = 10f;
    public float lifetime = 2f;
    public float maxDistance = 50f;
    public float destroyDelay = 0.05f;
    public bool useHoming = false;
    public Transform homingTarget;
    public float homingTurnRateDeg = 360f;

    private Vector3 startPosition;
    private bool initialized = false;

    public void Initialize(GameObject newOwner, int newDamage, float newSpeed, float newLifetime, Transform target = null)
    {
        owner = newOwner;
        damage = newDamage;
        speed = newSpeed;
        lifetime = newLifetime;
        homingTarget = target;
        useHoming = target != null;
        initialized = true;
    }

    private void Awake()
    {
        if ((hitMask.value == 0))
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) hitMask = 1 << playerLayer;
        }
        if (owner == null)
        {
            var parentAI = GetComponentInParent<MonoBehaviour>();
            if (parentAI != null)
                owner = parentAI.gameObject;
        }
        startPosition = transform.position;
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (useHoming && homingTarget != null)
        {
            Vector3 to = homingTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(to.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, homingTurnRateDeg * Time.deltaTime);
            }
        }
        transform.position += transform.forward * speed * Time.deltaTime;

        if (maxDistance > 0f)
        {
            if (Vector3.SqrMagnitude(transform.position - startPosition) > (maxDistance * maxDistance))
                Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;
        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps != null)
        {
            DamageRelay.ApplyToPlayer(ps.gameObject, damage);
            if (destroyOnHit) Destroy(gameObject, destroyDelay);
        }
    }
}
