using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public PlayerStats playerStats;
    public ItemData equippedItem;
    public Inventory playerInventory; // Assign in Inspector or via script

    [Header("Equipment Model Handling")]
    public Transform handTransform; // Assign this in the Inspector to your character's hand bone
    private GameObject equippedModelInstance;

    public void Equip(ItemData item)
    {
        // If something is already equipped, return it to inventory before unequipping
        if (equippedItem != null)
        {
            if (playerInventory != null)
            {
                playerInventory.AddItem(equippedItem, 1);
            }
            Unequip();
        }

        equippedItem = item;
        playerStats.ApplyEquipment(item);

        // Handle 3D model
        if (item.modelPrefab != null && handTransform != null)
        {
            equippedModelInstance = Instantiate(item.modelPrefab, handTransform);
            equippedModelInstance.transform.localPosition = Vector3.zero;
            equippedModelInstance.transform.localRotation = Quaternion.identity;
            equippedModelInstance.transform.localScale = item.modelScale;
        }
    }

    public void Unequip()
    {
        if (equippedItem != null)
        {
            playerStats.RemoveEquipment(equippedItem);
            equippedItem = null;
        }
        // Remove 3D model
        if (equippedModelInstance != null)
        {
            Destroy(equippedModelInstance);
            equippedModelInstance = null;
        }
    }
}
