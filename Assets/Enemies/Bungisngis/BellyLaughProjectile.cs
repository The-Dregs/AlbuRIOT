using UnityEngine;

public class BellyLaughProjectile : MonoBehaviour
{
    public int damage = 15;
    public BungisngisAI owner;

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