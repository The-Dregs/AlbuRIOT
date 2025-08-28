using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    public Transform itemListParent;
    public GameObject itemSlotPrefab;
    public Inventory playerInventory;
    public ThirdPersonController playerController;
    public PlayerCombat playerCombat;
    public ThirdPersonCameraOrbit cameraOrbit;
    public EquipmentManager equipmentManager;
    public TMPro.TextMeshProUGUI equippedItemText;

    private void Start()
    {
        inventoryPanel.SetActive(false);
        RefreshUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            bool open = !inventoryPanel.activeSelf;
            inventoryPanel.SetActive(open);
            if (open)
            {
                RefreshUI();
                if (playerController != null)
                    playerController.SetCanControl(false);
                if (playerCombat != null)
                    playerCombat.SetCanControl(false);
                if (cameraOrbit != null)
                    cameraOrbit.SetCameraControlActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (playerController != null)
                    playerController.SetCanControl(true);
                if (playerCombat != null)
                    playerCombat.SetCanControl(true);
                if (cameraOrbit != null)
                    cameraOrbit.SetCameraControlActive(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public void RefreshUI()
    {
        foreach (Transform child in itemListParent)
            Destroy(child.gameObject);

        foreach (var slot in playerInventory.slots)
        {
            // Don't show equipped item in inventory slots
            if (equipmentManager != null && equipmentManager.equippedItem == slot.item)
                continue;
            GameObject slotGO = Instantiate(itemSlotPrefab, itemListParent);
            ItemSlotUI slotUI = slotGO.GetComponent<ItemSlotUI>();
            if (slotUI != null)
                slotUI.SetSlot(slot);
        }

        // Show currently equipped item
        if (equipmentManager != null && equippedItemText != null)
        {
            if (equipmentManager.equippedItem != null)
                equippedItemText.text = $"Equipped: {equipmentManager.equippedItem.itemName}";
            else
                equippedItemText.text = "Equipped: None";
        }
    }
}
