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
    public const int SLOT_COUNT = 6;
    [SerializeField] private InventorySlot[] slots = new InventorySlot[SLOT_COUNT];
    
    [Header("Events")]
    public System.Action OnInventoryChanged;
    public System.Action<ItemData, int> OnItemAdded;
    public System.Action<ItemData, int> OnItemRemoved;
    public System.Action<int> OnSlotChanged;
    
    [Header("Network Sync")]
    public bool syncWithNetwork = true;
    
    public int SlotCount => SLOT_COUNT;
    
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
        
        // 1) Try to stack into existing slots
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

        // 2) Fill empty slots from lowest index upwards
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

        bool success = remainingQuantity == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            OnItemAdded?.Invoke(item, quantity);
            
            // Sync with other players
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
}
