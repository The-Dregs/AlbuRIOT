using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public PlayerStats playerStats;
    public ItemData equippedItem;

    public void Equip(ItemData item)
    {
        if (equippedItem != null)
            Unequip();

        equippedItem = item;
        playerStats.ApplyEquipment(item);
    }

    public void Unequip()
    {
        if (equippedItem != null)
        {
            playerStats.RemoveEquipment(equippedItem);
            equippedItem = null;
        }
    }
}
