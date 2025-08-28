using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSlotUI : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI quantityText;
    private InventorySlot slot;

    public void SetSlot(InventorySlot slot)
    {
        this.slot = slot;
        if (iconImage != null)
            iconImage.sprite = slot.item.icon;
        if (quantityText != null)
            quantityText.text = slot.quantity > 1 ? slot.quantity.ToString() : "";
    }

    public void OnEquipButton()
    {
        EquipmentManager manager = FindFirstObjectByType<EquipmentManager>();
        InventoryUI inventoryUI = FindFirstObjectByType<InventoryUI>();
        if (manager != null && slot != null && slot.item != null && inventoryUI != null)
        {
            // Remove from inventory
            if (inventoryUI.playerInventory != null)
                inventoryUI.playerInventory.RemoveItem(slot.item, 1);
            manager.Equip(slot.item);
            inventoryUI.RefreshUI();
        }
    }
}
