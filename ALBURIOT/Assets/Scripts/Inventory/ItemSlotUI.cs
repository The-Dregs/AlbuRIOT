using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSlotUI : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI quantityText;
    public UnityEngine.UI.Button equipButton;
    [Tooltip("optional: background/image to show only when there is an item")] public GameObject filledVisual;
    private InventorySlot slot;
    private InventoryUI inventoryUI; // cached from parent for multiplayer safety

    void Awake()
    {
        // find the InventoryUI in parents so each player's UI talks to its own manager
        inventoryUI = GetComponentInParent<InventoryUI>(true);
    }

    public void SetSlot(InventorySlot slot)
    {
        this.slot = slot;
        if (iconImage != null)
            iconImage.sprite = slot.item.icon;
        if (quantityText != null)
            quantityText.text = slot.quantity >= 1 ? ($"{slot.quantity}x") : "";
        if (equipButton != null) equipButton.interactable = slot != null && slot.item != null;
        if (filledVisual != null) filledVisual.SetActive(true);
    }

    public void Clear()
    {
        slot = null;
        if (iconImage != null) iconImage.sprite = null;
        if (quantityText != null) quantityText.text = "";
        if (equipButton != null) equipButton.interactable = false;
        if (filledVisual != null) filledVisual.SetActive(false);
    }

    public void OnEquipButton()
    {
        if (inventoryUI == null) inventoryUI = GetComponentInParent<InventoryUI>(true);
        if (inventoryUI == null) return;
        if (slot == null || slot.item == null) return;
        inventoryUI.TryEquipFromSlot(slot);
    }
}
