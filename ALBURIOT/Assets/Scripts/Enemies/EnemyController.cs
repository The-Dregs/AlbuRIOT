using UnityEngine;

// Ensures a CharacterController is present
[RequireComponent(typeof(CharacterController))]
public class EnemyController : MonoBehaviour
{
    [Header("Damage Text")]
    public GameObject damageTextPrefab;
    public Transform damageTextSpawnPoint;
    public EnemyStats stats;
    public Transform player;
    private CharacterController controller;
    private float lastAttackTime;
    private int currentHealth;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        currentHealth = stats.maxHealth;
    }

    void Update()
    {
        if (player == null) return;
        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= stats.detectionRange)
        {
            MoveTowardsPlayer(distance);
            if (distance <= stats.attackRange)
            {
                TryAttack();
            }
        }
    }

    void MoveTowardsPlayer(float distance)
    {
        if (distance > stats.attackRange)
        {
            Vector3 dir = (player.position - transform.position).normalized;
            controller.SimpleMove(dir * stats.moveSpeed);
            transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));
        }
    }

    void TryAttack()
    {
        if (Time.time - lastAttackTime >= stats.attackCooldown)
        {
            lastAttackTime = Time.time;
            if (stats.isMelee)
            {
                // Melee attack
                var playerStats = player.GetComponent<PlayerStats>();
                if (playerStats != null)
                    playerStats.TakeDamage(stats.damage);
            }
            else
            {
                // Ranged attack (simple example: instant hit)
                var playerStats = player.GetComponent<PlayerStats>();
                if (playerStats != null)
                    playerStats.TakeDamage(stats.damage);
                // You can expand this to spawn projectiles, etc.
            }
        }
    }

    public void TakeDamage(int amount)
    {
        // Show damage text above health bar
        if (damageTextPrefab != null)
        {
            Vector3 spawnPos = damageTextSpawnPoint != null ? damageTextSpawnPoint.position : transform.position + Vector3.up * 2f;
            GameObject dmgTextObj = Instantiate(damageTextPrefab, spawnPos, Quaternion.identity);
            var dmgText = dmgTextObj.GetComponent<DamageText>();
            if (dmgText != null)
                dmgText.ShowDamage(amount);
        }
        currentHealth -= amount;
        // Show health bar if EnemyHealthBar exists
        EnemyHealthBar healthBar = GetComponent<EnemyHealthBar>();
        if (healthBar != null)
        {
            healthBar.ShowHealthBar();
        }
        if (currentHealth <= 0)
            Die();
    }

    public int GetCurrentHealth() { return currentHealth; }
    public int GetMaxHealth() { return stats != null ? stats.maxHealth : 0; }

    void Die()
    {
        Destroy(gameObject);
    }
}
