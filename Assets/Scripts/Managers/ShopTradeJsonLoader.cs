using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class ShopTradeJsonLoader : MonoBehaviour
{
    [Tooltip("Name of the JSON file in Resources/Trades (without extension)")]
    public string tradeJsonFile = "NunoTrades";
    public NunoShopManager shopManager;
    
    [System.Serializable]
    public class ShopTradeDataJson
    {
        public string tradeName;
        public string description;
        public string[] requiredItemNames; // Item names instead of ItemData references
        public int[] requiredQuantities;
        public string rewardItemName;
        public int rewardQuantity = 1;
        public int maxUses = -1; // -1 for unlimited
        public bool requiresUnlock = false;
        public string[] requiredShrineIds;
        public string[] requiredQuestIds;
    }
    
    [System.Serializable]
    public class ShopTradeDataContainer
    {
        public ShopTradeDataJson[] trades;
    }

    void Awake()
    {
        LoadAndApplyTrades();
    }

    public void LoadAndApplyTrades()
    {
        if (shopManager == null) shopManager = NunoShopManager.Instance;
        if (shopManager == null)
        {
            Debug.LogError("ShopTradeJsonLoader: NunoShopManager.Instance not found!");
            return;
        }
        
        TextAsset file = Resources.Load<TextAsset>("Trades/" + tradeJsonFile);
        if (file == null)
        {
            Debug.LogError($"ShopTradeJsonLoader: Could not load trade JSON: {tradeJsonFile}");
            return;
        }
        
        ShopTradeDataContainer container = JsonUtility.FromJson<ShopTradeDataContainer>(file.text);
        if (container == null || container.trades == null)
        {
            Debug.LogError("ShopTradeJsonLoader: Failed to parse trade JSON");
            return;
        }
        
        // Convert JSON data to ShopTradeData ScriptableObjects
        ShopTradeData[] shopTrades = new ShopTradeData[container.trades.Length];
        ItemManager itemManager = ItemManager.Instance;
        
        if (itemManager == null)
        {
            Debug.LogError("ShopTradeJsonLoader: ItemManager.Instance not found!");
            return;
        }
        
        for (int i = 0; i < container.trades.Length; i++)
        {
            ShopTradeDataJson tradeJson = container.trades[i];
            
            // Find required items by name
            ItemData[] requiredItems = null;
            if (tradeJson.requiredItemNames != null && tradeJson.requiredItemNames.Length > 0)
            {
                requiredItems = new ItemData[tradeJson.requiredItemNames.Length];
                for (int j = 0; j < tradeJson.requiredItemNames.Length; j++)
                {
                    requiredItems[j] = itemManager.GetItemDataByName(tradeJson.requiredItemNames[j]);
                    if (requiredItems[j] == null)
                    {
                        Debug.LogWarning($"ShopTradeJsonLoader: Item '{tradeJson.requiredItemNames[j]}' not found in ItemManager");
                    }
                }
            }
            
            // Find reward item by name
            ItemData rewardItem = itemManager.GetItemDataByName(tradeJson.rewardItemName);
            if (rewardItem == null)
            {
                Debug.LogWarning($"ShopTradeJsonLoader: Reward item '{tradeJson.rewardItemName}' not found in ItemManager");
            }
            
            // Create runtime ShopTradeData object
            ShopTradeData trade = CreateRuntimeShopTrade(tradeJson, requiredItems, rewardItem);
            shopTrades[i] = trade;
        }
        
        shopManager.availableTrades = shopTrades;
        Debug.Log($"ShopTradeJsonLoader: Successfully loaded {shopTrades.Length} trades from {tradeJsonFile}");
    }
    
    private ShopTradeData CreateRuntimeShopTrade(ShopTradeDataJson json, ItemData[] requiredItems, ItemData rewardItem)
    {
        // Create a ScriptableObject instance at runtime
        ShopTradeData trade = ScriptableObject.CreateInstance<ShopTradeData>();
        
        trade.tradeName = json.tradeName;
        trade.description = json.description;
        trade.requiredItems = requiredItems;
        trade.requiredQuantities = json.requiredQuantities;
        trade.rewardItem = rewardItem;
        trade.rewardQuantity = json.rewardQuantity;
        trade.maxUses = json.maxUses;
        trade.currentUses = 0;
        trade.requiresUnlock = json.requiresUnlock;
        trade.requiredShrineIds = json.requiredShrineIds;
        trade.requiredQuestIds = json.requiredQuestIds;
        trade.isUnlocked = !json.requiresUnlock; // Start unlocked if no unlock required
        
        return trade;
    }
}

