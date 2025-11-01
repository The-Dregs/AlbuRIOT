using UnityEngine;

public class NunoMerchantTrigger : MonoBehaviour
{
    [Header("Shop Configuration")]
    public ShopTradeData[] availableTrades;
    public GameObject interactPrompt;
    
    [Header("UI References")]
    public GameObject shopPanel;
    public Transform tradeListParent;
    public GameObject tradeSlotPrefab;
    
    [Header("Quest Integration")]
    [Tooltip("identifier used by quest objectives for talk-to tasks")] 
    public string npcId;
    [Tooltip("when on, only allows interaction if current quest is TalkTo this npcId")] 
    public bool requireMatchingTalkObjective = false;
    [Tooltip("when on, marks TalkTo progress on shop open instead of E press")] 
    public bool completeOnShopOpen = true;
    
    private bool playerInRange = false;
    private GameObject player;
    private PlayerInteractHUD playerHUD;
    
    private bool IsLocalPlayer(GameObject go)
    {
        var pv = go.GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv == null) return true;
        return pv.IsMine;
    }
    
    private bool IsTalkObjectiveActive()
    {
        if (!requireMatchingTalkObjective) return true;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            return obj.objectiveType == ObjectiveType.TalkTo && !string.IsNullOrEmpty(npcId) && string.Equals(obj.targetId, npcId, System.StringComparison.OrdinalIgnoreCase);
        }
        return q.objectiveType == ObjectiveType.TalkTo && !string.IsNullOrEmpty(npcId) && string.Equals(q.targetId, npcId, System.StringComparison.OrdinalIgnoreCase);
    }
    
    void OnTriggerEnter(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = true;
        player = playerRoot;
        
        if (IsLocalPlayer(playerRoot))
        {
            if (!requireMatchingTalkObjective || IsTalkObjectiveActive())
            {
                if (interactPrompt != null)
                    interactPrompt.SetActive(true);
                    
                playerHUD = playerRoot.GetComponentInChildren<PlayerInteractHUD>(true);
                if (playerHUD != null)
                    playerHUD.Show("Press \"E\" to trade");
            }
        }
    }
    
    GameObject GetPlayerRoot(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        return ps != null ? ps.gameObject : null;
    }
    
    void OnTriggerExit(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = false;
        player = null;
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
        if (playerHUD != null)
        {
            playerHUD.Hide();
            playerHUD = null;
        }
    }
    
    void Update()
    {
        // Don't allow trade if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (playerHUD != null)
            {
                playerHUD.Hide();
            }
            return;
        }
        
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            var local = FindLocalPlayer();
            if (local == null) return;
            
            if (requireMatchingTalkObjective && !IsTalkObjectiveActive())
            {
                Debug.Log($"interaction with nuno {npcId} blocked: not current TalkTo objective");
                return;
            }
            
            // Use singleton manager
            var manager = NunoShopManager.Instance;
            if (manager != null)
            {
                manager.OpenShop(availableTrades); // Pass NPC-specific trades to singleton
                
                if (completeOnShopOpen && !string.IsNullOrEmpty(npcId))
                {
                    ApplyTalkProgress();
                }
                
                if (playerHUD != null)
                    playerHUD.Hide();
            }
            if (interactPrompt != null)
                interactPrompt.SetActive(false);
        }
    }
    
    private GameObject FindLocalPlayer()
    {
        var stats = Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in stats)
        {
            var pv = s.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null) return s.gameObject;
            if (pv.IsMine) return s.gameObject;
        }
        return GameObject.FindGameObjectWithTag("Player");
    }
    
    private void ApplyTalkProgress()
    {
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm != null && !string.IsNullOrEmpty(npcId))
        {
            qm.AddProgress_TalkTo(npcId);
            Debug.Log($"quest talk progress updated for nuno: {npcId}");
        }
    }
}

