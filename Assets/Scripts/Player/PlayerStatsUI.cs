using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;

public class PlayerStatsUI : MonoBehaviourPun
{
    public PlayerStats playerStats;
    public Slider healthSlider;
    public Slider staminaSlider;
    [Header("cooldown ui (optional)")]
    public Slider attackCooldownSlider;
    [Header("debug text ui (UGUI Text)")]
    public Text speedText;
    public Text attackingText;
    public Text staminaRegenText;
    public Text staminaDelayText;
    public Text healthRegenText;
    public Text healthDelayText;

    [Header("debug text ui (TMP Text)")]
    public TMP_Text speedTMP;
    public TMP_Text attackingTMPTxt;
    public TMP_Text staminaRegenTMP;
    public TMP_Text staminaDelayTMP;
    public TMP_Text healthRegenTMP;
    public TMP_Text healthDelayTMP;

    private ThirdPersonController controller;
    private PlayerCombat combat;
    private PhotonView targetPV; // photon view of the bound player (not of this UI)

    void Start()
    {
        // try direct parent first (for per-player HUD under the player)
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();
        // if still null or bound to a remote player, resolve to the local player's stats
        ResolveBindingIfNeeded();
        controller = playerStats != null ? playerStats.GetComponent<ThirdPersonController>() : null;
        combat = playerStats != null ? playerStats.GetComponent<PlayerCombat>() : null;
    }

    void Update()
    {
        // if we have a player target and it's not ours, do not drive UI (prevents remote players affecting our HUD)
        if (targetPV != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !targetPV.IsMine)
        {
            return;
        }

        // if binding is missing or lost (e.g., scene spawn order), try to resolve again
        if (playerStats == null || targetPV == null)
        {
            ResolveBindingIfNeeded();
            controller = playerStats != null ? playerStats.GetComponent<ThirdPersonController>() : controller;
            combat = playerStats != null ? playerStats.GetComponent<PlayerCombat>() : combat;
        }

        if (playerStats != null)
        {
            if (healthSlider != null && playerStats.maxHealth > 0)
            {
                if (!Mathf.Approximately(healthSlider.maxValue, playerStats.maxHealth)) healthSlider.maxValue = playerStats.maxHealth;
                if (!Mathf.Approximately(healthSlider.value, playerStats.currentHealth)) healthSlider.value = playerStats.currentHealth;
            }
            if (staminaSlider != null && playerStats.maxStamina > 0)
            {
                if (!Mathf.Approximately(staminaSlider.maxValue, playerStats.maxStamina)) staminaSlider.maxValue = playerStats.maxStamina;
                if (!Mathf.Approximately(staminaSlider.value, playerStats.currentStamina)) staminaSlider.value = playerStats.currentStamina;
            }

            if (controller != null)
            {
                var charCtrl = controller.GetComponent<CharacterController>();
                float speed = charCtrl != null ? new Vector3(charCtrl.velocity.x, 0f, charCtrl.velocity.z).magnitude : 0f;
                if (speedText != null) speedText.text = $"speed: {speed:F2}";
                if (speedTMP != null) speedTMP.text = $"speed: {speed:F2}";
            }
            if (combat != null)
            {
                string atk = combat.IsAttacking ? "attacking: yes" : "attacking: no";
                if (attackingText != null) attackingText.text = atk;
                if (attackingTMPTxt != null) attackingTMPTxt.text = atk;

                if (attackCooldownSlider != null)
                {
                    attackCooldownSlider.value = combat.AttackCooldownProgress;
                }
            }
            {
                string regenState = playerStats.IsStaminaRegenerating ? "regenerating" : (playerStats.IsStaminaRegenBlocked ? "blocked" : (playerStats.currentStamina >= playerStats.maxStamina ? "full" : "waiting"));
                if (staminaRegenText != null) staminaRegenText.text = $"stamina: {regenState}";
                if (staminaRegenTMP != null) staminaRegenTMP.text = $"stamina: {regenState}";
            }
            {
                string delay = $"regen delay: {playerStats.StaminaRegenDelayRemaining:F2}s";
                if (staminaDelayText != null) staminaDelayText.text = delay;
                if (staminaDelayTMP != null) staminaDelayTMP.text = delay;
            }
            // optional: health regen state
            {
                string regenState = playerStats.IsHealthRegenerating ? "regenerating" : (playerStats.currentHealth >= playerStats.maxHealth ? "full" : (playerStats.HealthRegenDelayRemaining > 0f ? "waiting" : "paused"));
                if (healthRegenText != null) healthRegenText.text = $"health: {regenState}";
                if (healthRegenTMP != null) healthRegenTMP.text = $"health: {regenState}";
            }
            {
                string delay = $"health delay: {playerStats.HealthRegenDelayRemaining:F2}s";
                if (healthDelayText != null) healthDelayText.text = delay;
                if (healthDelayTMP != null) healthDelayTMP.text = delay;
            }
        }
    }

    private void ResolveBindingIfNeeded()
    {
        // prefer already assigned but ensure it belongs to the local player when networked
        if (playerStats != null)
        {
            targetPV = playerStats.GetComponent<PhotonView>();
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && targetPV != null && !targetPV.IsMine)
            {
                // assigned stats belong to a remote player; rebind to local
                playerStats = null;
                targetPV = null;
            }
        }

        if (playerStats == null)
        {
            // find local player's stats (works in both offline mode and connected rooms)
            var all = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            PlayerStats local = null;
            foreach (var ps in all)
            {
                var pv = ps.GetComponent<PhotonView>();
                if (pv == null || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                {
                    // offline/single-player: pick the first one (likely the only one)
                    local = ps;
                    break;
                }
                if (pv.IsMine)
                {
                    local = ps; break;
                }
            }
            if (local != null)
            {
                playerStats = local;
                targetPV = playerStats.GetComponent<PhotonView>();
                Debug.Log("playerstatsui bound to local player stats");
            }
        }
    }
}
