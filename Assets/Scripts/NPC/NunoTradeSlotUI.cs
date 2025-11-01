using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Text;

public class NunoTradeSlotUI : MonoBehaviour
{
    [Header("UI References")]
    public Image rewardIcon;
    public TextMeshProUGUI rewardText;
    public TextMeshProUGUI requiredText;
    public Button tradeButton;
    public GameObject disabledOverlay;
    
    private ShopTradeData trade;
    private int tradeIndex;
    private NunoShopManager shopManager;
    
    public void Initialize(ShopTradeData tradeData, int index, NunoShopManager manager)
    {
        trade = tradeData;
        tradeIndex = index;
        shopManager = manager;
        
        if (trade == null) return;
        
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        if (trade == null) return;
        
        // Display reward item
        if (rewardIcon != null && trade.rewardItem != null)
            rewardIcon.sprite = trade.rewardItem.icon;
        
        if (rewardText != null)
        {
            if (trade.rewardItem != null)
                rewardText.text = $"{trade.rewardQuantity}x {trade.rewardItem.itemName}";
            else
                rewardText.text = "Invalid Reward";
        }
        
        // Display required items
        if (requiredText != null)
        {
            StringBuilder sb = new StringBuilder();
            if (trade.requiredItems != null && trade.requiredQuantities != null)
            {
                for (int i = 0; i < trade.requiredItems.Length; i++)
                {
                    if (trade.requiredItems[i] == null) continue;
                    sb.Append($"{trade.requiredQuantities[i]}x {trade.requiredItems[i].itemName}");
                    if (i < trade.requiredItems.Length - 1)
                        sb.Append(", ");
                }
            }
            requiredText.text = sb.ToString();
        }
        
        // Check if player can afford trade
        bool canAfford = CanAfford();
        if (tradeButton != null)
        {
            tradeButton.interactable = canAfford && trade.CanTrade;
        }
        
        if (disabledOverlay != null)
        {
            disabledOverlay.SetActive(!canAfford || !trade.CanTrade);
        }
    }
    
    private bool CanAfford()
    {
        if (shopManager == null) return false;
        
        var inventory = Inventory.FindLocalInventory();
        if (inventory == null || trade == null || trade.requiredItems == null) return false;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            if (!inventory.HasItem(trade.requiredItems[i], trade.requiredQuantities[i]))
                return false;
        }
        
        return true;
    }
    
    public void OnTradeButton()
    {
        if (shopManager != null)
        {
            shopManager.ExecuteTrade(tradeIndex);
            UpdateUI();
        }
    }
}

