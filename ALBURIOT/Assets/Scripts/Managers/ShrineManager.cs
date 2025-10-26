using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class ShrineManager : MonoBehaviourPun
{
    [Header("Shrine Configuration")]
    public ShrineData[] shrineData;
    
    [Header("UI References")]
    public GameObject shrineUI;
    public UnityEngine.UI.Button[] offeringButtons;
    public TMPro.TextMeshProUGUI shrineDescriptionText;
    public TMPro.TextMeshProUGUI shrineNameText;
    
    private ShrineData currentShrine;
    private Inventory playerInventory;
    private QuestManager questManager;
    
    // Events
    public event Action<ShrineData> OnShrineActivated;
    public event Action<ShrineData, ItemData, int> OnOfferingMade;
    
    void Awake()
    {
        // Auto-find components
        if (playerInventory == null)
            playerInventory = FindFirstObjectByType<Inventory>();
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
            
        // Hide UI initially
        if (shrineUI != null)
            shrineUI.SetActive(false);
    }
    
    public void ActivateShrine(string shrineId)
    {
        ShrineData shrine = GetShrineData(shrineId);
        if (shrine == null) return;
        
        currentShrine = shrine;
        
        // Show UI
        if (shrineUI != null)
        {
            shrineUI.SetActive(true);
            UpdateShrineUI();
        }
        
        OnShrineActivated?.Invoke(shrine);
        
        // Sync with other players (only when networking is ready)
        if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
        {
            photonView.RPC("RPC_ActivateShrine", RpcTarget.Others, shrineId);
        }
    }
    
    [PunRPC]
    public void RPC_ActivateShrine(string shrineId)
    {
        if (photonView != null && !photonView.IsMine) return;
        ActivateShrine(shrineId);
    }
    
    public void DeactivateShrine()
    {
        if (shrineUI != null)
            shrineUI.SetActive(false);
        currentShrine = null;
    }
    
    public void MakeOffering(ItemData item, int quantity)
    {
        if (currentShrine == null || item == null || quantity <= 0) return;
        if (playerInventory == null || !playerInventory.HasItem(item, quantity)) return;
        
        // Remove items from inventory
        if (playerInventory.RemoveItem(item, quantity))
        {
            Debug.Log($"Offering made: {quantity}x {item.itemName} to {currentShrine.shrineName}");
            
            // Trigger quest progress
            if (questManager != null)
            {
                questManager.AddProgress_ShrineOffering(currentShrine.shrineId, item, quantity);
            }
            
            OnOfferingMade?.Invoke(currentShrine, item, quantity);
            
            // Sync with other players (only when networking is ready)
            if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
            {
                photonView.RPC("RPC_MakeOffering", RpcTarget.Others, currentShrine.shrineId, item.itemName, quantity);
            }
            
            // Check if shrine is satisfied
            CheckShrineCompletion();
        }
    }
    
    [PunRPC]
    public void RPC_MakeOffering(string shrineId, string itemName, int quantity)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        ShrineData shrine = GetShrineData(shrineId);
        ItemData item = GetItemDataByName(itemName);
        
        if (shrine != null && item != null)
        {
            OnOfferingMade?.Invoke(shrine, item, quantity);
        }
    }
    
    private void CheckShrineCompletion()
    {
        if (currentShrine == null) return;
        
        // Check if all required offerings have been made
        bool allOfferingsMade = true;
        if (currentShrine.requiredOfferings != null && currentShrine.offeringQuantities != null)
        {
            for (int i = 0; i < currentShrine.requiredOfferings.Length && i < currentShrine.offeringQuantities.Length; i++)
            {
                if (playerInventory != null && !playerInventory.HasItem(currentShrine.requiredOfferings[i], currentShrine.offeringQuantities[i]))
                {
                    allOfferingsMade = false;
                    break;
                }
            }
        }
        
        if (allOfferingsMade)
        {
            CompleteShrine();
        }
    }
    
    private void CompleteShrine()
    {
        if (currentShrine == null) return;
        
        Debug.Log($"Shrine completed: {currentShrine.shrineName}");
        
        // Give shrine rewards
        if (currentShrine.rewardItems != null && currentShrine.rewardQuantities != null && playerInventory != null)
        {
            for (int i = 0; i < currentShrine.rewardItems.Length && i < currentShrine.rewardQuantities.Length; i++)
            {
                if (currentShrine.rewardItems[i] != null)
                {
                    playerInventory.AddItem(currentShrine.rewardItems[i], currentShrine.rewardQuantities[i]);
                }
            }
        }
        
        // Deactivate shrine
        DeactivateShrine();
    }
    
    private void UpdateShrineUI()
    {
        if (currentShrine == null) return;
        
        if (shrineNameText != null)
            shrineNameText.text = currentShrine.shrineName;
            
        if (shrineDescriptionText != null)
            shrineDescriptionText.text = currentShrine.description;
        
        // Update offering buttons
        if (offeringButtons != null && currentShrine.acceptedOfferings != null)
        {
            for (int i = 0; i < offeringButtons.Length && i < currentShrine.acceptedOfferings.Length; i++)
            {
                if (offeringButtons[i] != null)
                {
                    offeringButtons[i].gameObject.SetActive(currentShrine.acceptedOfferings[i] != null);
                    
                    var buttonText = offeringButtons[i].GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (buttonText != null && currentShrine.acceptedOfferings[i] != null)
                    {
                        buttonText.text = $"Offer {currentShrine.acceptedOfferings[i].itemName}";
                    }
                }
            }
        }
    }
    
    private ShrineData GetShrineData(string shrineId)
    {
        if (shrineData == null) return null;
        
        foreach (var shrine in shrineData)
        {
            if (string.Equals(shrine.shrineId, shrineId, StringComparison.OrdinalIgnoreCase))
                return shrine;
        }
        return null;
    }
    
    private ItemData GetItemDataByName(string itemName)
    {
        if (ItemManager.Instance != null)
        {
            return ItemManager.Instance.GetItemDataByName(itemName);
        }
        return null;
    }
    
    // Public methods for UI buttons
    public void OnOfferingButtonClicked(int buttonIndex)
    {
        if (currentShrine == null || currentShrine.acceptedOfferings == null) return;
        if (buttonIndex < 0 || buttonIndex >= currentShrine.acceptedOfferings.Length) return;
        
        ItemData item = currentShrine.acceptedOfferings[buttonIndex];
        if (item != null && playerInventory != null)
        {
            int quantity = 1; // Default quantity, could be made configurable
            MakeOffering(item, quantity);
        }
    }
    
    public void OnCloseShrineUI()
    {
        DeactivateShrine();
    }
}
