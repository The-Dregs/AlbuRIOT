using UnityEngine;
using Photon.Pun;

public class EquipmentManager : MonoBehaviourPun
{
    public PlayerStats playerStats;
    public ItemData equippedItem;
    public Inventory playerInventory; // Assign in Inspector or via script

    [Header("Equipment Model Handling")]
    public Transform handTransform; // Assign this in the Inspector to your character's hand bone
    private GameObject equippedModelInstance;

    // Centralized entry point for UI: equip exactly one from a specific inventory slot
    public bool TryEquipFromInventorySlot(Inventory inventory, InventorySlot slot)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return false;
        if (inventory == null || slot == null || slot.item == null) return false;
        // remove strictly from the given slot to avoid cross-slot merges
        bool removed = inventory.RemoveSpecificSlot(slot, 1);
        if (!removed) return false;
        Equip(slot.item);
        return true;
    }

    // Centralized entry for world pickups: prefer equip if hands free; else store in inventory
    public void HandlePickup(ItemData item)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        if (item == null) return;
        if (equippedItem == null)
        {
            Equip(item);
        }
        else if (playerInventory != null)
        {
            playerInventory.AddItem(item, 1);
        }
    }

    public void Equip(ItemData item)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        // Only unequip previous (which returns it to inventory) before equipping new
        if (equippedItem != null)
        {
            Unequip();
        }
        equippedItem = item;
        if (playerStats != null && item != null) playerStats.ApplyEquipment(item);
        Debug.Log($"[equipment] Equip| equipped item={(item != null ? item.itemName : "null")}");
        // Broadcast model equip to all clients using itemName as id
        if (item != null)
        {
            string id = item.itemName;
            if (pv != null && (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode))
                pv.RPC(nameof(RPC_EquipModel), RpcTarget.All, id);
            else
                RPC_EquipModel(id);
        }
    }

    public void Unequip()
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        if (equippedItem != null)
        {
            if (playerInventory != null)
            {
                Debug.Log($"[equipment] Unequip| returning item={equippedItem.itemName} to inventory");
                playerInventory.AddItem(equippedItem, 1);
            }
            if (playerStats != null) playerStats.RemoveEquipment(equippedItem);
            Debug.Log($"[equipment] Unequip| unequipped item={(equippedItem != null ? equippedItem.itemName : "null")}");
            equippedItem = null;
        }
        // Remove visuals across clients
        if (pv != null && (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode))
            pv.RPC(nameof(RPC_ClearModel), RpcTarget.All);
        else
            RPC_ClearModel();
    }

    [PunRPC]
    private void RPC_EquipModel(string itemName)
    {
        // clear previous
        RPC_ClearModel();
        var db = ItemDatabase.Load();
        var item = db != null ? db.FindByName(itemName) : null;
        if (item != null && item.modelPrefab != null && handTransform != null)
        {
            equippedModelInstance = Instantiate(item.modelPrefab, handTransform);
            // by default, keep prefab's local pose; apply only scale. allow optional overrides from ItemData
            if (item.overrideTransform)
            {
                equippedModelInstance.transform.localPosition = item.modelLocalPosition;
                equippedModelInstance.transform.localRotation = Quaternion.Euler(item.modelLocalEulerAngles);
            }
            // always apply scale
            equippedModelInstance.transform.localScale = item.modelScale;
        }
    }

    [PunRPC]
    private void RPC_ClearModel()
    {
        if (equippedModelInstance != null)
        {
            Destroy(equippedModelInstance);
            equippedModelInstance = null;
        }
    }
}
