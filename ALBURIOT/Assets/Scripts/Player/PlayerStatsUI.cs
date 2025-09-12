using UnityEngine;
using UnityEngine.UI;

public class PlayerStatsUI : MonoBehaviour
{
    public PlayerStats playerStats;
    public Slider healthSlider;
    public Slider staminaSlider;

    void Update()
    {
        if (playerStats != null)
        {
            healthSlider.value = (float)playerStats.currentHealth / playerStats.maxHealth;
            staminaSlider.value = (float)playerStats.currentStamina / playerStats.maxStamina;
        }
    }
}
