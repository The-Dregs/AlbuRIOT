using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    [TextArea]
    public string description;
    public int maxStack = 1;

    // Universal modifiers for all items
    public int healthModifier;
    public int staminaModifier;
    public int damageModifier;
    public float speedModifier;
    public int staminaCostModifier;
    // Add more fields as needed (e.g., itemType, stats, etc.)
}
