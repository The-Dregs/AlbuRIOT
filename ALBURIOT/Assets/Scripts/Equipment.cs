using UnityEngine;

[CreateAssetMenu(fileName = "NewEquipment", menuName = "Equipment/Equipment")]
public class Equipment : ScriptableObject
{
    public string itemName;
    public int healthModifier;
    public int staminaModifier;
    public int damageModifier;
    // add more modifiers as needed
}
