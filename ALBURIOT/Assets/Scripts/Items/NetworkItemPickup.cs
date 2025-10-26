using Photon.Pun;
using UnityEngine;

// Multiplayer-safe pickup: local owner presses E, we add to their inventory and destroy the pickup across the network.
[RequireComponent(typeof(Collider))]
public class NetworkItemPickup : MonoBehaviourPun
{
    public ItemData itemData;
    public int quantity = 1;
    [Tooltip("deprecated: now using PlayerInteractHUD found on the local player. kept for legacy world prompts.")] public GameObject pickupPrompt;
    [Tooltip("message shown on the player's interact HUD. {0} will be replaced with the item name")] public string promptFormat = "Press E to pick up {0}";
    private bool playerInRange = false;
    private GameObject localPlayer;

    void Awake()
    {
        var c = GetComponent<Collider>(); if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var player = GetPlayerRoot(other);
        if (player == null) return;
        var pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine) return; // only show prompt for local player
        localPlayer = player; playerInRange = true;
        // prefer local player's HUD prompt
        var hud = player.GetComponentInChildren<PlayerInteractHUD>(true);
        if (hud != null)
        {
            string itemName = itemData != null && !string.IsNullOrEmpty(itemData.itemName) ? itemData.itemName : "item";
            hud.Show(string.Format(promptFormat, itemName));
        }
        else if (pickupPrompt != null)
        {
            // fallback to old world prompt if HUD is not available
            pickupPrompt.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        var player = GetPlayerRoot(other);
        if (player == null) return;
        var pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine) return;
        playerInRange = false; localPlayer = null;
        var hud = player.GetComponentInChildren<PlayerInteractHUD>(true);
        if (hud != null) hud.Hide();
        if (pickupPrompt != null) pickupPrompt.SetActive(false);
    }

    void Update()
    {
        if (!playerInRange || localPlayer == null) return;
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryPickup(localPlayer);
        }
    }

    private void TryPickup(GameObject player)
    {
        var inv = player.GetComponent<Inventory>();
        if (inv != null && itemData != null)
        {
            if (inv.AddItem(itemData, quantity))
            {
                // also notify quest collect
                var qm = FindFirstObjectByType<QuestManager>();
                if (qm != null) qm.AddProgress_Collect(itemData.itemName, quantity);

                // hide prompt on success for local player
                var hud = player.GetComponentInChildren<PlayerInteractHUD>(true);
                if (hud != null) hud.Hide();
                if (pickupPrompt != null) pickupPrompt.SetActive(false);

                // destroy network-wide (master or owner handles)
                if (PhotonNetwork.IsConnected && photonView != null)
                {
                    if (photonView.AmOwner)
                        PhotonNetwork.Destroy(gameObject);
                    else
                        photonView.RPC(nameof(RPC_RequestDestroy), RpcTarget.MasterClient);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }

    [PunRPC]
    private void RPC_RequestDestroy()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    private GameObject GetPlayerRoot(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        return ps != null ? ps.gameObject : null;
    }
}
