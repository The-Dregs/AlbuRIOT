using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;
    public int maxStamina = 100;
    public int currentStamina;
    public float staminaRegenRate = 10f;
    public int baseDamage = 25;
    public float baseSpeed = 6f;
    public float speedModifier = 0f;
    public int staminaCostModifier = 0;

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    void Update()
    {
        if (currentStamina < maxStamina)
        {
            currentStamina += Mathf.RoundToInt(staminaRegenRate * Time.deltaTime);
            if (currentStamina > maxStamina) currentStamina = maxStamina;
        }
    }

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth < 0) currentHealth = 0;
    }

    public bool UseStamina(int amount)
    {
        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            return true;
        }
        return false;
    }

    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }

    public void ApplyEquipment(ItemData item)
    {
        maxHealth += item.healthModifier;
        maxStamina += item.staminaModifier;
        baseDamage += item.damageModifier;
        speedModifier += item.speedModifier;
        staminaCostModifier += item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }

    public void RemoveEquipment(ItemData item)
    {
        maxHealth -= item.healthModifier;
        maxStamina -= item.staminaModifier;
        baseDamage -= item.damageModifier;
        speedModifier -= item.speedModifier;
        staminaCostModifier -= item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }
}
