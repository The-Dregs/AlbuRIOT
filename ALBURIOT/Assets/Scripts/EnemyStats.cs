using UnityEngine;

public class EnemyStats : MonoBehaviour
{
    public int maxHealth = 50;
    public int currentHealth;
    public EnemyHealthBar healthBar;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        Debug.Log($"{gameObject.name} took {amount} damage. Current health: {currentHealth}");
        if (healthBar != null)
            healthBar.ShowBar();
        if (currentHealth <= 0)
        {
            Debug.Log($"{gameObject.name} died!");
            Die();
        }
    }

    void Die()
    {
        Destroy(gameObject);
    }
}
