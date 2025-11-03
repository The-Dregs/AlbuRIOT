using UnityEngine;
using Photon.Pun;
using System.Collections;

/// <summary>
/// Handles player resurrection in multiplayer. Other players can interact with downed players to revive them.
/// </summary>
public class PlayerResurrection : MonoBehaviourPun
{
    [Header("Resurrection Settings")]
    [SerializeField] private float resurrectionRange = 2f;
    [SerializeField] private float resurrectionDuration = 3f; // Time to hold E
    [SerializeField] private LayerMask playerLayer;
    
    [Header("UI")]
    [SerializeField] private PlayerInteractHUD interactHUD;
    
    private PlayerStats playerStats;
    private PlayerStats nearbyDownedPlayer;
    private float resurrectionProgress = 0f;
    private bool isResurrecting = false;
    private Coroutine resurrectionCoroutine;
    
    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        if (interactHUD == null)
            interactHUD = GetComponentInChildren<PlayerInteractHUD>(true);
        
        if (playerLayer == 0)
            playerLayer = LayerMask.GetMask("Player");
    }
    
    void Update()
    {
        // Only local player can interact
        if (photonView == null || !photonView.IsMine) return;
        
        // Don't allow resurrection if player is dead or downed themselves
        if (playerStats == null || playerStats.IsDead || playerStats.IsDowned) return;
        
        // Check for nearby downed players
        CheckForDownedPlayers();
        
        // Handle resurrection input
        if (nearbyDownedPlayer != null)
        {
            if (Input.GetKey(KeyCode.E) && !isResurrecting)
            {
                if (resurrectionCoroutine == null)
                    resurrectionCoroutine = StartCoroutine(CoResurrect(nearbyDownedPlayer));
            }
            else if (!Input.GetKey(KeyCode.E) && isResurrecting)
            {
                // Cancel resurrection
                if (resurrectionCoroutine != null)
                {
                    StopCoroutine(resurrectionCoroutine);
                    resurrectionCoroutine = null;
                }
                isResurrecting = false;
                resurrectionProgress = 0f;
                if (interactHUD != null)
                    interactHUD.Hide();
            }
        }
    }
    
    void CheckForDownedPlayers()
    {
        PlayerStats[] allPlayers = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        PlayerStats closestDowned = null;
        float closestDist = float.MaxValue;
        
        foreach (var player in allPlayers)
        {
            if (player == null || player == playerStats) continue;
            if (!player.IsDowned) continue;
            
            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist < resurrectionRange && dist < closestDist)
            {
                closestDist = dist;
                closestDowned = player;
            }
        }
        
        if (closestDowned != nearbyDownedPlayer)
        {
            nearbyDownedPlayer = closestDowned;
            
            if (nearbyDownedPlayer != null && interactHUD != null)
            {
                interactHUD.Show("Hold E to revive");
            }
            else if (interactHUD != null)
            {
                interactHUD.Hide();
            }
        }
    }
    
    IEnumerator CoResurrect(PlayerStats targetPlayer)
    {
        isResurrecting = true;
        resurrectionProgress = 0f;
        
        while (resurrectionProgress < 1f)
        {
            if (!Input.GetKey(KeyCode.E) || targetPlayer == null || !targetPlayer.IsDowned)
            {
                // Cancel resurrection
                isResurrecting = false;
                resurrectionProgress = 0f;
                if (interactHUD != null)
                    interactHUD.Hide();
                yield break;
            }
            
            float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (dist > resurrectionRange)
            {
                // Too far away
                isResurrecting = false;
                resurrectionProgress = 0f;
                if (interactHUD != null)
                    interactHUD.Hide();
                yield break;
            }
            
            resurrectionProgress += Time.deltaTime / resurrectionDuration;
            resurrectionProgress = Mathf.Clamp01(resurrectionProgress);
            
            if (interactHUD != null)
            {
                int percent = Mathf.RoundToInt(resurrectionProgress * 100f);
                interactHUD.Show($"Reviving... {percent}%");
            }
            
            yield return null;
        }
        
        // Resurrection complete
        isResurrecting = false;
        resurrectionProgress = 0f;
        
        // Send RPC to revive the player
        var targetPv = targetPlayer.GetComponent<PhotonView>();
        if (targetPv != null)
        {
            targetPv.RPC("RPC_Revive", targetPv.Owner);
        }
        
        if (interactHUD != null)
            interactHUD.Hide();
        
        resurrectionCoroutine = null;
    }
}

