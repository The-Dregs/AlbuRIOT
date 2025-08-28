using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    public int attackDamage = 25;
    public float attackRange = 2f;
    public float attackRate = 1f;
    public int attackStaminaCost = 20;
    public LayerMask enemyLayers;

    private float nextAttackTime = 0f;
    private PlayerStats stats;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
    }

    void Update()
    {
        if (Time.time >= nextAttackTime)
        {
            if (Input.GetMouseButtonDown(0)) // Left mouse button
            {
                if (stats.UseStamina(attackStaminaCost))
                {
                    Attack();
                    nextAttackTime = Time.time + 1f / attackRate;
                }
            }
        }
    }

    void Attack()
    {
        // Play attack animation here if you have one
        Collider[] hitEnemies = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f, enemyLayers);
        foreach (Collider enemy in hitEnemies)
        {
            EnemyStats enemyStats = enemy.GetComponent<EnemyStats>();
            if (enemyStats != null)
            {
                enemyStats.TakeDamage(attackDamage);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f);
    }
}
