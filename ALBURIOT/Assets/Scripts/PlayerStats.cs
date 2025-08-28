using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;
    public int maxStamina = 100;
    public int currentStamina;
    public float staminaRegenRate = 10f;

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    void Update()
    {
        if (currentStamina < maxStamina)
        {
            int before = currentStamina;
            currentStamina += Mathf.RoundToInt(staminaRegenRate * Time.deltaTime);
            if (currentStamina > maxStamina) currentStamina = maxStamina;
            if (currentStamina != before)
                Debug.Log($"Stamina regen: {before} -> {currentStamina}");
        }
    }

    public void TakeDamage(int amount)
    {
        int before = currentHealth;
        currentHealth -= amount;
        Debug.Log($"Player took {amount} damage: {before} -> {currentHealth}");
        if (currentHealth < 0) currentHealth = 0;
    }

    public bool UseStamina(int amount)
    {
        if (currentStamina >= amount)
        {
            int before = currentStamina;
            currentStamina -= amount;
            Debug.Log($"Stamina used: {before} -> {currentStamina}");
            return true;
        }
        Debug.Log("Tried to use stamina but not enough!");
        return false;
    }

    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }
}
