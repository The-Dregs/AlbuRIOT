using UnityEngine;
using Photon.Pun;

public class ItemPickup : MonoBehaviourPun
{
    [Header("Item Configuration")]
    public ItemData itemData;
    public int quantity = 1;
    
    [Header("Visual Effects")]
    public GameObject pickupEffect;
    public float rotationSpeed = 50f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.5f;
    
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip pickupSound;
    
    private Vector3 startPosition;
    private bool isPickedUp = false;
    
    // Events
    public System.Action<ItemPickup> OnPickedUp;
    
    void Start()
    {
        startPosition = transform.position;
        
        // Set up visual representation
        SetupVisuals();
        
        // Auto-find audio source
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }
    
    void Update()
    {
        if (isPickedUp) return;
        
        // Rotate the item
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        
        // Bob up and down
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = startPosition + Vector3.up * bobOffset;
    }
    
    private void SetupVisuals()
    {
        if (itemData != null && itemData.icon != null)
        {
            // Set up sprite renderer or mesh renderer based on your setup
            var spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = itemData.icon;
            }
        }
    }
    
    public void SetItem(ItemData item, int qty)
    {
        itemData = item;
        quantity = qty;
        SetupVisuals();
    }
    
    // (Remove/Comment) void OnTriggerEnter(Collider other)
    // The pickup logic will be triggered explicitly from PlayerPickupInteractor.cs (E key)
    
    private void PickupItem(GameObject player)
    {
        if (isPickedUp || itemData == null) return;
        
        Debug.Log($"[ItemPickup] PickupItem called for {itemData.itemName} by {player.name}");
        
        // Get player's inventory
        var inventory = player.GetComponent<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning("Player doesn't have an inventory component!");
            return;
        }
        
        // Try to add item to inventory
        if (inventory.AddItem(itemData, quantity))
        {
            Debug.Log($"[ItemPickup] Successfully added {itemData.itemName} x{quantity} to inventory");
            isPickedUp = true;
            
            // Play pickup sound
            PlayPickupSound();
            
            // Play pickup effect
            PlayPickupEffect();
            
            // Update quest progress
            UpdateQuestProgress();
            
            // Notify other clients to remove the item from their world
            if (photonView != null && PhotonNetwork.IsConnected)
            {
                Debug.Log($"[ItemPickup] Sending RPC_PickupItem to others for {itemData.itemName}");
                photonView.RPC("RPC_PickupItem", RpcTarget.Others);
            }
            else
            {
                Debug.Log($"[ItemPickup] Not networked, skipping RPC for {itemData.itemName}");
            }
            
            // Always destroy locally for the picker-upper
            Debug.Log($"[ItemPickup] Destroying pickup locally: {itemData.itemName}");
            DestroyPickup();
            
            // Notify listeners
            OnPickedUp?.Invoke(this);
        }
        else
        {
            Debug.Log("Inventory full! Cannot pick up item.");
        }
    }
    
    [PunRPC]
    public void RPC_PickupItem()
    {
        // This RPC is called on OTHER clients to sync the pickup destruction
        Debug.Log($"[ItemPickup] RPC_PickupItem received for {itemData?.itemName ?? "unknown"}");
        
        if (isPickedUp)
        {
            Debug.Log($"[ItemPickup] Already picked up, ignoring RPC for {itemData?.itemName ?? "unknown"}");
            return; // Already picked up, avoid double destruction
        }
        
        isPickedUp = true;
        PlayPickupEffect();
        Debug.Log($"[ItemPickup] Destroying pickup via RPC: {itemData?.itemName ?? "unknown"}");
        DestroyPickup();
    }
    
    private void PlayPickupSound()
    {
        if (audioSource != null)
        {
            AudioClip soundToPlay = pickupSound != null ? pickupSound : itemData.pickupSound;
            if (soundToPlay != null)
            {
                audioSource.PlayOneShot(soundToPlay);
            }
        }
    }
    
    private void PlayPickupEffect()
    {
        GameObject effectToPlay = pickupEffect != null ? pickupEffect : itemData.pickupEffect;
        if (effectToPlay != null)
        {
            Instantiate(effectToPlay, transform.position, Quaternion.identity);
        }
    }
    
    private void UpdateQuestProgress()
    {
        if (itemData == null) return;
        
        // Update quest progress for item collection
        var questManager = FindFirstObjectByType<QuestManager>();
        if (questManager != null)
        {
            // Use questId if provided, otherwise fall back to itemName
            string identifier = !string.IsNullOrEmpty(itemData.questId) ? itemData.questId : itemData.itemName;
            questManager.AddProgress_Collect(identifier, quantity);
        }
    }
    
    private void DestroyPickup()
    {
        // Delay destruction to allow sound/effects to play
        // Use PhotonNetwork.Destroy if networked, otherwise regular Destroy
        if (photonView != null && PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject, 0.1f);
        }
    }
    
    // Public getters
    public ItemData ItemData => itemData;
    public int Quantity => quantity;
    public bool IsPickedUp => isPickedUp;
    
    // Method to manually trigger pickup (for testing or special cases)
    public void ForcePickup(GameObject player)
    {
        PickupItem(player);
    }
}