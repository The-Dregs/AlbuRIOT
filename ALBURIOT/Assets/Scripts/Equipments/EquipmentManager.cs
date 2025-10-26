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

    public void Equip(ItemData item)
    {
        // owner-only applies stats and inventory changes
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
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
        if (playerStats != null && item != null) playerStats.ApplyEquipment(item);

        // broadcast model equip to all clients using itemName as id
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
            // return the item to inventory when unequipping
            if (playerInventory != null)
            {
                playerInventory.AddItem(equippedItem, 1);
            }
            if (playerStats != null) playerStats.RemoveEquipment(equippedItem);
            equippedItem = null;
        }
        // remove visuals across clients
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
