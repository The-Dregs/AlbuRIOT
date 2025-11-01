using UnityEngine;
using Photon.Pun;
using System;
using TMPro;
using UnityEngine.UI;

public class NunoShopManager : MonoBehaviour
{
    [Header("Shop Configuration")]
    public ShopTradeData[] availableTrades;
    
    [Header("UI References")]
    public GameObject shopPanel;
    public Transform tradeListParent;
    public GameObject tradeSlotPrefab;
    
    // Singleton pattern for shared UI across all NPC merchants
    private static NunoShopManager _instance;
    public static NunoShopManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var existing = FindFirstObjectByType<NunoShopManager>();
                if (existing != null)
                    _instance = existing;
                else
                {
                    var go = new GameObject("NunoShopManager_Singleton");
                    _instance = go.AddComponent<NunoShopManager>();
                }
            }
            return _instance;
        }
    }
    
    private Inventory playerInventory;
    private PhotonView playerPhotonView;
    private QuestManager questManager;
    private bool isOpen = false;
    private int inputLockToken = 0;
    private ShopTradeData[] currentActiveTrades; // Trades from the NPC that opened the shop
    
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning("Multiple NunoShopManager instances detected. Destroying duplicate.");
            Destroy(this);
            return;
        }
        
        if (shopPanel != null)
            shopPanel.SetActive(false);
        else
        {
            // Try to find existing UI in children
            var existing = transform.GetComponentInChildren<Canvas>(true);
            if (existing != null) shopPanel = existing.gameObject;
        }
    }
    
    void OnDestroy()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= OnInventoryChanged;
    }
    
    private void EnsureShopUI()
    {
        if (shopPanel != null && tradeListParent != null) return;
        
        // Try to find existing UI in children first
        if (shopPanel == null)
        {
            var existing = transform.GetComponentInChildren<Canvas>(true);
            if (existing != null)
            {
                shopPanel = existing.gameObject;
                if (tradeListParent == null)
                {
                    var list = shopPanel.transform.Find("TradeListParent");
                    if (list != null) tradeListParent = list;
                }
            }
        }
        
        // If still no UI found, create one
        if (shopPanel == null || tradeListParent == null)
        {
            Debug.LogWarning("NunoShopManager: Creating default shop UI at runtime. Consider setting up UI in editor.");
            CreateDefaultShopUI();
        }
    }
    
    private void CreateDefaultShopUI()
    {
        // Create canvas for local player (Screen Space - Overlay)
        var canvasGO = new GameObject("NunoShop_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        // Don't parent to manager (Screen Space - Overlay renders independently)
        shopPanel = canvasGO;
        
        // Create background panel
        var bgPanel = new GameObject("Background", typeof(Image));
        bgPanel.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgPanel.GetComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        var bgRect = bgPanel.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.2f, 0.1f);
        bgRect.anchorMax = new Vector2(0.8f, 0.9f);
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
        
        // Create title
        var titleGO = new GameObject("Title", typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(bgPanel.transform, false);
        var titleText = titleGO.GetComponent<TextMeshProUGUI>();
        titleText.text = "Nuno's Trade Shop";
        titleText.fontSize = 36;
        titleText.alignment = TextAlignmentOptions.Center;
        var titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.05f, 0.85f);
        titleRect.anchorMax = new Vector2(0.95f, 0.98f);
        titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
        
        // Create trade list parent
        var listGO = new GameObject("TradeListParent");
        listGO.transform.SetParent(bgPanel.transform, false);
        var vlg = listGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10;
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        var listRect = listGO.GetComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0.05f, 0.15f);
        listRect.anchorMax = new Vector2(0.95f, 0.8f);
        listRect.offsetMin = listRect.offsetMax = Vector2.zero;
        tradeListParent = listGO.transform;
        
        // Create close button
        var closeBtnGO = new GameObject("CloseButton", typeof(Button), typeof(Image));
        closeBtnGO.transform.SetParent(bgPanel.transform, false);
        var closeBtn = closeBtnGO.GetComponent<Button>();
        closeBtn.onClick.AddListener(CloseShop);
        var btnImg = closeBtnGO.GetComponent<Image>();
        btnImg.color = new Color(0.3f, 0.1f, 0.1f);
        var btnRect = closeBtnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.4f, 0.05f);
        btnRect.anchorMax = new Vector2(0.6f, 0.12f);
        btnRect.offsetMin = btnRect.offsetMax = Vector2.zero;
        
        var btnTextGO = new GameObject("Label", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(closeBtnGO.transform, false);
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Close (ESC)";
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.fontSize = 20;
        var textRect = btnTextGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        
        // Create a simple trade slot prefab if none exists
        if (tradeSlotPrefab == null)
        {
            Debug.LogWarning("NunoShopManager: tradeSlotPrefab not set. Creating minimal prefab at runtime.");
            CreateDefaultTradeSlotPrefab();
        }
    }
    
    private void CreateDefaultTradeSlotPrefab()
    {
        // Note: This creates a simple prefab reference for runtime use
        // For production, create a proper prefab in the editor
        var slotGO = new GameObject("DefaultTradeSlot");
        var image = slotGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.25f);
        
        var layout = slotGO.AddComponent<LayoutElement>();
        layout.preferredHeight = 100;
        
        // Add NunoTradeSlotUI component
        var slotUI = slotGO.AddComponent<NunoTradeSlotUI>();
        
        // Create reward icon
        var iconGO = new GameObject("RewardIcon", typeof(Image));
        iconGO.transform.SetParent(slotGO.transform, false);
        slotUI.rewardIcon = iconGO.GetComponent<Image>();
        var iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.02f, 0.1f);
        iconRect.anchorMax = new Vector2(0.15f, 0.9f);
        iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
        
        // Create reward text
        var rewardGO = new GameObject("RewardText", typeof(TextMeshProUGUI));
        rewardGO.transform.SetParent(slotGO.transform, false);
        slotUI.rewardText = rewardGO.GetComponent<TextMeshProUGUI>();
        slotUI.rewardText.fontSize = 18;
        var rewardRect = rewardGO.GetComponent<RectTransform>();
        rewardRect.anchorMin = new Vector2(0.18f, 0.5f);
        rewardRect.anchorMax = new Vector2(0.5f, 0.95f);
        rewardRect.offsetMin = rewardRect.offsetMax = Vector2.zero;
        
        // Create required text
        var reqGO = new GameObject("RequiredText", typeof(TextMeshProUGUI));
        reqGO.transform.SetParent(slotGO.transform, false);
        slotUI.requiredText = reqGO.GetComponent<TextMeshProUGUI>();
        slotUI.requiredText.fontSize = 14;
        var reqRect = reqGO.GetComponent<RectTransform>();
        reqRect.anchorMin = new Vector2(0.52f, 0.5f);
        reqRect.anchorMax = new Vector2(0.82f, 0.95f);
        reqRect.offsetMin = reqRect.offsetMax = Vector2.zero;
        
        // Create trade button
        var btnGO = new GameObject("TradeButton", typeof(Button), typeof(Image));
        btnGO.transform.SetParent(slotGO.transform, false);
        slotUI.tradeButton = btnGO.GetComponent<Button>();
        var btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(0.1f, 0.3f, 0.1f);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.84f, 0.2f);
        btnRect.anchorMax = new Vector2(0.98f, 0.8f);
        btnRect.offsetMin = btnRect.offsetMax = Vector2.zero;
        
        // Button text
        var btnTextGO = new GameObject("Label", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Trade";
        btnText.fontSize = 16;
        var textRect = btnTextGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        
        // Disabled overlay
        var overlayGO = new GameObject("DisabledOverlay", typeof(Image));
        overlayGO.transform.SetParent(slotGO.transform, false);
        overlayGO.transform.SetAsLastSibling();
        slotUI.disabledOverlay = overlayGO;
        var overlayImg = overlayGO.GetComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.7f);
        var overlayRect = overlayGO.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        
        // Store reference as template and hide it
        tradeSlotPrefab = slotGO;
        slotGO.SetActive(false); // Hide template, only instantiated copies are shown
    }
    
    private void EnsureReferences()
    {
        if (playerInventory == null)
        {
            playerInventory = Inventory.FindLocalInventory();
            if (playerInventory != null)
                playerInventory.OnInventoryChanged += OnInventoryChanged;
        }
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
    }
    
    private void OnInventoryChanged()
    {
        if (isOpen)
            RefreshTradeList();
    }
    
    public void OpenShop()
    {
        OpenShop(availableTrades);
    }
    
    public void OpenShop(ShopTradeData[] tradesToShow)
    {
        EnsureReferences();
        EnsureShopUI();
        
        if (!LocalUIManager.Ensure().TryOpen("NunoShop"))
        {
            Debug.Log("NunoShop: another UI is open; cannot open shop");
            return;
        }
        
        // Store the trades from the NPC that opened the shop
        currentActiveTrades = tradesToShow;
        
        isOpen = true;
        if (shopPanel != null)
            shopPanel.SetActive(true);
            
        CheckTradeUnlocks();
        RefreshTradeList();
        
        if (inputLockToken == 0)
            inputLockToken = LocalInputLocker.Ensure().Acquire("NunoShop", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
    }
    
    public void CloseShop()
    {
        isOpen = false;
        if (shopPanel != null)
            shopPanel.SetActive(false);
            
        LocalUIManager.Instance?.Close("NunoShop");
        
        if (inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(inputLockToken);
            inputLockToken = 0;
        }
        
        LocalInputLocker.Ensure().ForceGameplayCursor();
    }
    
    void Update()
    {
        if (isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
        }
    }
    
    public void ExecuteTrade(int tradeIndex)
    {
        EnsureReferences();
        
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (tradeIndex < 0 || tradeIndex >= currentActiveTrades.Length) return;
        
        // Check local player authority
        if (playerPhotonView == null && playerInventory != null)
            playerPhotonView = playerInventory.GetComponent<PhotonView>();
        if (playerPhotonView != null && !playerPhotonView.IsMine) return;
        
        ShopTradeData trade = currentActiveTrades[tradeIndex];
        if (trade == null || !trade.CanTrade) return;
        
        if (!HasRequiredItems(trade))
        {
            Debug.Log($"Cannot complete trade: missing required items");
            return;
        }
        
        RemoveRequiredItems(trade);
        AddRewardItem(trade);
        
        trade.RecordUse();
        RefreshTradeList();
        
        Debug.Log($"Completed trade: {trade.tradeName}");
    }
    
    public void OnCloseButton()
    {
        CloseShop();
    }
    
    private bool HasRequiredItems(ShopTradeData trade)
    {
        if (playerInventory == null || trade.requiredItems == null) return false;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            if (!playerInventory.HasItem(trade.requiredItems[i], trade.requiredQuantities[i]))
                return false;
        }
        
        return true;
    }
    
    private void RemoveRequiredItems(ShopTradeData trade)
    {
        if (playerInventory == null || trade.requiredItems == null) return;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            playerInventory.RemoveItem(trade.requiredItems[i], trade.requiredQuantities[i]);
        }
    }
    
    private void AddRewardItem(ShopTradeData trade)
    {
        if (playerInventory == null || trade.rewardItem == null) return;
        playerInventory.AddItem(trade.rewardItem, trade.rewardQuantity);
    }
    
    private void CheckTradeUnlocks()
    {
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (currentActiveTrades == null) return;
        
        foreach (var trade in currentActiveTrades)
        {
            if (trade == null || !trade.requiresUnlock) continue;
            
            trade.isUnlocked = CheckUnlockConditions(trade);
        }
    }
    
    private bool CheckUnlockConditions(ShopTradeData trade)
    {
        bool shrineUnlocked = true;
        if (trade.requiredShrineIds != null && trade.requiredShrineIds.Length > 0)
        {
            shrineUnlocked = false;
            // TODO: Check if shrines are cleansed (requires shrine completion tracking)
        }
        
        bool questUnlocked = true;
        if (trade.requiredQuestIds != null && trade.requiredQuestIds.Length > 0 && questManager != null)
        {
            questUnlocked = false;
            foreach (var questIdStr in trade.requiredQuestIds)
            {
                if (string.IsNullOrEmpty(questIdStr) || !int.TryParse(questIdStr, out int questId)) continue;
                var quest = questManager.GetQuestByID(questId);
                if (quest != null && quest.isCompleted)
                {
                    questUnlocked = true;
                    break;
                }
            }
        }
        
        return shrineUnlocked && questUnlocked;
    }
    
    private void RefreshTradeList()
    {
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (tradeListParent == null || tradeSlotPrefab == null || currentActiveTrades == null) return;
        
        foreach (Transform child in tradeListParent)
        {
            Destroy(child.gameObject);
        }
        
        for (int i = 0; i < currentActiveTrades.Length; i++)
        {
            ShopTradeData trade = currentActiveTrades[i];
            if (trade == null) continue;
            
            GameObject slot = Instantiate(tradeSlotPrefab, tradeListParent);
            var ui = slot.GetComponent<NunoTradeSlotUI>();
            if (ui != null)
            {
                ui.Initialize(trade, i, this);
            }
        }
    }
}

