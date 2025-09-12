using UnityEngine;

public class EquipmentPickup : MonoBehaviour
{
    public ItemData itemData;
    public GameObject pickupPrompt; // Assign your UI text GameObject here

    private bool playerInRange = false;
    private GameObject player;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.gameObject;
            if (pickupPrompt != null)
                pickupPrompt.SetActive(true);
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
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            EquipmentManager manager = player.GetComponent<EquipmentManager>();
            if (manager != null && itemData != null)
            {
                manager.Equip(itemData);
                if (pickupPrompt != null)
                    pickupPrompt.SetActive(false);
                Destroy(gameObject);
            }
        }
    }
}
