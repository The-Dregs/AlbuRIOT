using UnityEngine;

public class EnemyStats : MonoBehaviour
{
    public int maxHealth = 50;
    public int currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        // Show health bar if EnemyHealthBar exists
        EnemyHealthBar healthBar = GetComponent<EnemyHealthBar>();
        if (healthBar != null)
        {
            healthBar.ShowHealthBar();
        }
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Destroy(gameObject);
    }
}
