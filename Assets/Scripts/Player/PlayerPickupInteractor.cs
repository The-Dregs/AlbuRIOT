using UnityEngine;

public class PlayerPickupInteractor : MonoBehaviour
{
    public float pickupRadius = 2f;
    public LayerMask pickupLayer;

    private PlayerInteractHUD playerHUD;
    private ItemPickup nearbyPickup;

    void Start()
    {
        playerHUD = GetComponentInChildren<PlayerInteractHUD>(true);
    }

    void Update()
    {
        // Only allow local player to interact in multiplayer
        var pv = GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && !pv.IsMine) return;
        
        // Don't allow pickups if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (nearbyPickup != null && playerHUD != null)
            {
                playerHUD.Hide();
                nearbyPickup = null;
            }
            return;
        }

        CheckNearbyPickups();

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (nearbyPickup != null && !nearbyPickup.IsPickedUp)
            {
                nearbyPickup.ForcePickup(gameObject);
                // Hide the HUD immediately after pickup (notification system handles the pickup message)
                if (playerHUD != null)
                {
                    playerHUD.Hide();
                }
            }
        }
    }

    void CheckNearbyPickups()
    {
        Collider[] pickups = Physics.OverlapSphere(transform.position, pickupRadius, pickupLayer);
        ItemPickup closest = null;
        float minDist = float.MaxValue;
        
        foreach (var col in pickups)
        {
            var pickup = col.GetComponent<ItemPickup>();
            if (pickup == null || pickup.IsPickedUp) continue;
            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = pickup;
            }
        }

        if (closest != nearbyPickup)
        {
            if (playerHUD != null)
            {
                CancelInvoke(nameof(HideHUD));
                if (nearbyPickup != null)
                {
                    playerHUD.Hide();
                }
            }

            nearbyPickup = closest;

            if (nearbyPickup != null && playerHUD != null && !nearbyPickup.IsPickedUp)
            {
                string itemName = nearbyPickup.ItemData != null ? nearbyPickup.ItemData.itemName : "item";
                playerHUD.Show($"Press E to pick up {itemName}");
            }
        }
    }

    void HideHUD()
    {
        if (playerHUD != null)
        {
            playerHUD.Hide();
        }
    }
}
