using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public PlayerStats playerStats;
    public Equipment equippedItem;

    public void Equip(Equipment item)
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
