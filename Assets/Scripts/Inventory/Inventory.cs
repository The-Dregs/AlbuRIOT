using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class InventorySlot
{
    public ItemData item;
    public int quantity;

    public InventorySlot(ItemData item, int quantity)
    {
        this.item = item;
        this.quantity = quantity;
    }
    
    public bool IsEmpty => item == null || quantity <= 0;
}

public class Inventory : MonoBehaviourPun, IPunObservable
{
    [Header("Inventory Configuration")]
    public const int SLOT_COUNT = 12;
    [SerializeField] private InventorySlot[] slots = new InventorySlot[SLOT_COUNT];
    
    [Header("Events")]
    public System.Action OnInventoryChanged;
    public System.Action<ItemData, int> OnItemAdded;
    public System.Action<ItemData, int> OnItemRemoved;
    public System.Action<int> OnSlotChanged;
    
    [Header("Network Sync")]
    public bool syncWithNetwork = true;
    
    public int SlotCount => SLOT_COUNT;
    
    /// <summary>
    /// Finds the local player's inventory. Safe for multiplayer (returns Inventory belonging to local PhotonView).
    /// Returns null if no local player found (offline mode fallback) or in single-player.
    /// </summary>
    public static Inventory FindLocalInventory()
    {
        var stats = UnityEngine.Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var stat in stats)
        {
            var pv = stat.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null) return stat.GetComponent<Inventory>(); // offline
            if (pv.IsMine) return stat.GetComponent<Inventory>();
        }
        // Fallback: try to find any inventory if offline
        return UnityEngine.Object.FindFirstObjectByType<Inventory>();
    }
    
    public InventorySlot GetSlot(int index)
    {
        if (slots == null) return null;
        int len = slots.Length;
        if (index < 0 || index >= len) return null;
        return slots[index];
    }

    public int FindFirstEmptySlot()
    {
        EnsureSize();
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty) return i;
        }
        return -1;
    }
    
    public int FindItemSlot(ItemData item)
    {
        if (item == null) return -1;
        EnsureSize();
        
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && s.quantity < item.maxStack) return i;
        }
        return -1;
    }

    void Awake()
    {
        // photonView is provided by MonoBehaviourPun
        EnsureSize();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureSize();
    }
#endif

    private void EnsureSize()
    {
        if (slots == null || slots.Length != SLOT_COUNT)
        {
            var old = slots;
            slots = new InventorySlot[SLOT_COUNT];
            if (old != null)
            {
                int copy = Mathf.Min(old.Length, SLOT_COUNT);
                for (int i = 0; i < copy; i++) slots[i] = old[i];
            }
        }
    }

    public bool AddItem(ItemData item, int quantity = 1)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return false;
        if (item == null || quantity <= 0) return false;
        EnsureSize();

        int remainingQuantity = quantity;
        bool isStackable = item.maxStack > 1;
        bool isUnique = item.uniqueInstance || item.itemType == ItemType.Unique;

        if (isUnique)
        {
            // Unique: always insert individual instances into empty slots
            for (int addCount = 0; addCount < quantity; addCount++)
            {
                bool placed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || slots[i].IsEmpty)
                    {
                        slots[i] = new InventorySlot(item, 1);
                        OnSlotChanged?.Invoke(i);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                    return false; // No slot for one of the items
            }
            OnInventoryChanged?.Invoke();
            OnItemAdded?.Invoke(item, quantity);
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_AddItem", RpcTarget.Others, item.itemName, quantity);
            }
            return true;
        }

        // Previous stack/merge logic for regular items...
        // 1) Stack into current slots (for stackable >1, or merge all equipment into a single slot)
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && (isStackable ? s.quantity < item.maxStack : true))
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack - s.quantity) : remainingQuantity;
                s.quantity += addable;
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                // For equipment (maxStack==1) always stack all into one slot
                if (!isStackable) remainingQuantity = 0;
            }
        }

        // 2) New slot for leftovers (for stackables)
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty)
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack) : 1;
                slots[i] = new InventorySlot(item, addable);
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                if (!isStackable) break; // Only ever one slot for equipment!
            }
        }

        bool success = remainingQuantity == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            OnItemAdded?.Invoke(item, quantity);
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_AddItem", RpcTarget.Others, item.itemName, quantity);
            }
        }
        return success;
    }
    
    [PunRPC]
    public void RPC_AddItem(string itemName, int quantity)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        ItemData item = GetItemDataByName(itemName);
        if (item != null)
        {
            AddItemLocal(item, quantity);
        }
    }
    
    private void AddItemLocal(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return;
        EnsureSize();

        int remainingQuantity = quantity;
        
        // Same logic as AddItem but without network sync
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        
        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && s.quantity < item.maxStack)
            {
                int addable = Mathf.Min(remainingQuantity, item.maxStack - s.quantity);
                s.quantity += addable;
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
            }
        }

        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty)
            {
                int addable = Mathf.Min(remainingQuantity, item.maxStack);
                slots[i] = new InventorySlot(item, addable);
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
            }
        }

        if (remainingQuantity == 0)
        {
            OnInventoryChanged?.Invoke();
            OnItemAdded?.Invoke(item, quantity);
        }
    }

    public bool RemoveItem(ItemData item, int quantity = 1)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return false;
        if (item == null || quantity <= 0) return false;
        EnsureSize();

        int remainingQuantity = quantity;
        
        // Remove from slots left-to-right
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
            {
                if (s.quantity > remainingQuantity)
                {
                    s.quantity -= remainingQuantity;
                    remainingQuantity = 0;
                }
                else
                {
                    remainingQuantity -= s.quantity;
                    // clear slot fully
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
            }
        }

        bool success = remainingQuantity <= 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            OnItemRemoved?.Invoke(item, quantity);
            
            // Sync with other players
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_RemoveItem", RpcTarget.Others, item.itemName, quantity);
            }
        }
        return success;
    }
    
    [PunRPC]
    public void RPC_RemoveItem(string itemName, int quantity)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        ItemData item = GetItemDataByName(itemName);
        if (item != null)
        {
            RemoveItemLocal(item, quantity);
        }
    }
    
    private void RemoveItemLocal(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return;
        EnsureSize();

        int remainingQuantity = quantity;
        
        // Same logic as RemoveItem but without network sync
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
            {
                if (s.quantity > remainingQuantity)
                {
                    s.quantity -= remainingQuantity;
                    remainingQuantity = 0;
                }
                else
                {
                    remainingQuantity -= s.quantity;
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
            }
        }

        if (remainingQuantity <= 0)
        {
            OnInventoryChanged?.Invoke();
            OnItemRemoved?.Invoke(item, quantity);
        }
    }

    public bool HasItem(ItemData item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return false;
        int count = 0;
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
                count += s.quantity;
        }
        return count >= quantity;
    }
    
    public int GetItemCount(ItemData item)
    {
        if (item == null) return 0;
        int count = 0;
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
                count += s.quantity;
        }
        return count;
    }
    
    public void SwapSlots(int fromIndex, int toIndex)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return;
        if (fromIndex < 0 || fromIndex >= SLOT_COUNT || toIndex < 0 || toIndex >= SLOT_COUNT) return;
        
        var temp = slots[fromIndex];
        slots[fromIndex] = slots[toIndex];
        slots[toIndex] = temp;
        
        OnSlotChanged?.Invoke(fromIndex);
        OnSlotChanged?.Invoke(toIndex);
        OnInventoryChanged?.Invoke();
        
        // Sync with other players
        if (photonView != null && photonView.IsMine && syncWithNetwork)
        {
            photonView.RPC("RPC_SwapSlots", RpcTarget.Others, fromIndex, toIndex);
        }
    }
    
    [PunRPC]
    public void RPC_SwapSlots(int fromIndex, int toIndex)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        var temp = slots[fromIndex];
        slots[fromIndex] = slots[toIndex];
        slots[toIndex] = temp;
        
        OnSlotChanged?.Invoke(fromIndex);
        OnSlotChanged?.Invoke(toIndex);
        OnInventoryChanged?.Invoke();
    }
    
    private ItemData GetItemDataByName(string itemName)
    {
        if (ItemManager.Instance != null)
        {
            return ItemManager.Instance.GetItemDataByName(itemName);
        }
        return null;
    }
    
    // Network synchronization
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send inventory data
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.item != null)
                {
                    stream.SendNext(slot.item.itemName);
                    stream.SendNext(slot.quantity);
                }
                else
                {
                    stream.SendNext("");
                    stream.SendNext(0);
                }
            }
        }
        else
        {
            // Receive inventory data
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                string itemName = (string)stream.ReceiveNext();
                int quantity = (int)stream.ReceiveNext();
                
                if (!string.IsNullOrEmpty(itemName))
                {
                    ItemData item = GetItemDataByName(itemName);
                    if (item != null)
                    {
                        slots[i] = new InventorySlot(item, quantity);
                    }
                    else
                    {
                        slots[i] = null;
                    }
                }
                else
                {
                    slots[i] = null;
                }
            }
            
            OnInventoryChanged?.Invoke();
        }
    }

    // Removes exactly from a specific slot instance (needed for unique items in separate slots, not by value)
    public bool RemoveSpecificSlot(InventorySlot specificSlot, int quantity = 1)
    {
        if (specificSlot == null || specificSlot.item == null) return false;
        EnsureSize();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == specificSlot)
            {
                int preQuantity = slots[i].quantity;
                string itemName = slots[i].item != null ? slots[i].item.itemName : "null";
                if (slots[i].quantity > quantity)
                {
                    slots[i].quantity -= quantity;
                    Debug.Log($"[inventory] RemoveSpecificSlot | slot={i} | item={itemName} | pre={preQuantity} | post={slots[i].quantity} | partial-removal");
                }
                else
                {
                    Debug.Log($"[inventory] RemoveSpecificSlot | slot={i} | item={itemName} | pre={preQuantity} | post=0 | slot-cleared");
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
                OnInventoryChanged?.Invoke();
                OnItemRemoved?.Invoke(specificSlot.item, quantity);
                return true;
            }
        }
        Debug.Log($"[inventory] RemoveSpecificSlot | SLOT NOT FOUND | item={(specificSlot.item != null ? specificSlot.item.itemName : "null")} | quantity={quantity} | FAIL");
        return false;
    }
}
