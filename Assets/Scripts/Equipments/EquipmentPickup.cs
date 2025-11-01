using UnityEngine;

public class EquipmentPickup : MonoBehaviour
{
    public ItemData itemData;
    public GameObject pickupPrompt; // Assign your UI text GameObject here

    private bool playerInRange = false;
    private GameObject player;
    private PlayerInteractHUD playerHUD; // optional HUD on player

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.gameObject;
            if (pickupPrompt != null)
                pickupPrompt.SetActive(true);
            // show player HUD prompt if available (local player check lives inside HUD)
            playerHUD = player.GetComponentInChildren<PlayerInteractHUD>(true);
            if (playerHUD != null)
            {
                string name = (itemData != null ? itemData.itemName : "item");
                playerHUD.Show($"Press E to pick up {name}");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            player = null;
            if (pickupPrompt != null)
                pickupPrompt.SetActive(false);
            if (playerHUD != null)
            {
                playerHUD.Hide();
                playerHUD = null;
            }
        }
    }

    void Update()
    {
        // Don't allow pickups if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (playerHUD != null) playerHUD.Hide();
            playerInRange = false;
            return;
        }

        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            EquipmentManager manager = player.GetComponent<EquipmentManager>();
            if (manager != null && itemData != null)
            {
                Debug.Log($"[pickup] EquipmentPickup | HandlePickup | item={(itemData != null ? itemData.itemName : "null")}");
                manager.HandlePickup(itemData);
                if (pickupPrompt != null)
                    pickupPrompt.SetActive(false);
                if (playerHUD != null) playerHUD.Hide();
                Destroy(gameObject);
            }
        }
    }
}
