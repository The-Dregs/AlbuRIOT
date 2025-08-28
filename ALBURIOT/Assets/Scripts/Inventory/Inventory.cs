using System.Collections.Generic;
using UnityEngine;

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
}

public class Inventory : MonoBehaviour
{
    public int maxSlots = 20;
    public List<InventorySlot> slots = new List<InventorySlot>();

    public bool AddItem(ItemData item, int quantity = 1)
    {
        // Check if item already exists and can stack
        foreach (var slot in slots)
        {
            if (slot.item == item && slot.quantity < item.maxStack)
            {
                int addable = Mathf.Min(quantity, item.maxStack - slot.quantity);
                slot.quantity += addable;
                quantity -= addable;
                if (quantity <= 0) return true;
            }
        }
        // Add new slot if space
        while (quantity > 0 && slots.Count < maxSlots)
        {
            int addable = Mathf.Min(quantity, item.maxStack);
            slots.Add(new InventorySlot(item, addable));
            quantity -= addable;
        }
        return quantity == 0;
    }

    public bool RemoveItem(ItemData item, int quantity = 1)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (slot.item == item)
            {
                if (slot.quantity > quantity)
                {
                    slot.quantity -= quantity;
                    return true;
                }
                else
                {
                    quantity -= slot.quantity;
                    slots.RemoveAt(i);
                    if (quantity <= 0) return true;
                }
            }
        }
        return false;
    }

    public bool HasItem(ItemData item, int quantity = 1)
    {
        int count = 0;
        foreach (var slot in slots)
        {
            if (slot.item == item)
                count += slot.quantity;
        }
        return count >= quantity;
    }
}
