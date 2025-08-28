using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    public ItemData itemData;
    public int quantity = 1;
    public GameObject pickupPrompt;

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
            Inventory inventory = player.GetComponent<Inventory>();
            if (inventory != null && itemData != null)
            {
                if (inventory.AddItem(itemData, quantity))
                {
                    if (pickupPrompt != null)
                        pickupPrompt.SetActive(false);
                    Destroy(gameObject);
                }
            }
        }
    }
}
