using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class PlayerStatsUI : MonoBehaviourPun
{
    public PlayerStats playerStats;
    public Slider healthSlider;
    public Slider staminaSlider;

    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        if (playerStats != null)
        {
            healthSlider.value = (float)playerStats.currentHealth / playerStats.maxHealth;
            staminaSlider.value = (float)playerStats.currentStamina / playerStats.maxStamina;
        }
    }
}
