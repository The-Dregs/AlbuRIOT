using UnityEngine;

public class EquipmentPickup : MonoBehaviour
{
    public Equipment equipment;
    public GameObject pickupPrompt; // Assign your UI text GameObject here

    private bool playerInRange = false;
    private GameObject player;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.gameObject;
            Debug.Log($"Player entered pickup range for {gameObject.name}");
            if (pickupPrompt != null)
            {
                pickupPrompt.SetActive(true);
                Debug.Log("Pickup prompt enabled.");
            }
            else
            {
                Debug.LogWarning("Pickup prompt reference is missing!");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            player = null;
            Debug.Log($"Player exited pickup range for {gameObject.name}");
            if (pickupPrompt != null)
            {
                pickupPrompt.SetActive(false);
                Debug.Log("Pickup prompt disabled.");
            }
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log($"E pressed to pick up {equipment?.itemName ?? "unknown equipment"}");
            EquipmentManager manager = player.GetComponent<EquipmentManager>();
            if (manager != null && equipment != null)
            {
                manager.Equip(equipment);
                Debug.Log($"Equipped {equipment.itemName} on player {player.name}");
                if (pickupPrompt != null)
                {
                    pickupPrompt.SetActive(false);
                    Debug.Log("Pickup prompt disabled after pickup.");
                }
                Destroy(gameObject);
            }
            else
            {
                Debug.LogWarning("EquipmentManager or equipment reference missing!");
            }
        }
    }
}
