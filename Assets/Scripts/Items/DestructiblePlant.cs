using UnityEngine;
using Photon.Pun;
using System.Collections;

public class DestructiblePlant : MonoBehaviourPun, IEnemyDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHits = 3;
    [SerializeField] private int currentHits = 0;
    
    [Header("Item Drops")]
    [SerializeField] private ItemData[] dropItems;
    [SerializeField] private int[] dropQuantities;
    
    [Header("VFX/SFX")]
    [SerializeField] private GameObject hitEffect;
    [SerializeField] private GameObject destroyEffect;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip destroySound;
    
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitTrigger = "Hit";
    [SerializeField] private string destroyTrigger = "Destroy";
    
    private AudioSource audioSource;
    private bool isDestroyed = false;
    private GameObject lastHitSource = null; // Track who dealt the final blow
    
    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
            
        if (animator == null)
            animator = GetComponent<Animator>();
            
        if (dropItems == null || dropItems.Length == 0)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} has no drop items configured!");
        }
        
        if (dropQuantities == null || dropQuantities.Length != dropItems.Length)
        {
            dropQuantities = new int[dropItems != null ? dropItems.Length : 0];
            for (int i = 0; i < dropQuantities.Length; i++)
            {
                dropQuantities[i] = 1;
            }
        }
    }
    
    public void TakeEnemyDamage(int amount, GameObject source)
    {
        if (isDestroyed) return;
        
        var photonView = GetComponent<PhotonView>();
        bool isNetworked = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        
        if (isNetworked)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                ApplyHit(source);
            }
            else if (photonView != null)
            {
                int sourceViewId = -1;
                if (source != null)
                {
                    var srcPv = source.GetComponent<PhotonView>();
                    if (srcPv != null) sourceViewId = srcPv.ViewID;
                }
                photonView.RPC("RPC_ApplyHit", RpcTarget.MasterClient, sourceViewId);
            }
        }
        else
        {
            ApplyHit(source);
        }
    }
    
    [PunRPC]
    private void RPC_ApplyHit(int sourceViewId)
    {
        GameObject source = null;
        if (sourceViewId >= 0)
        {
            var srcPv = PhotonView.Find(sourceViewId);
            if (srcPv != null) source = srcPv.gameObject;
        }
        ApplyHit(source);
    }
    
    private void ApplyHit(GameObject source)
    {
        if (isDestroyed) return;
        
        currentHits++;
        lastHitSource = source; // Update last hitter
        
        if (audioSource != null && hitSound != null)
            audioSource.PlayOneShot(hitSound);
            
        if (hitEffect != null)
        {
            GameObject fx = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        
        if (animator != null && !string.IsNullOrEmpty(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
        }
        
        if (currentHits >= maxHits)
        {
            DestroyPlant();
        }
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            int sourceViewId = -1;
            if (source != null)
            {
                var srcPv = source.GetComponent<PhotonView>();
                if (srcPv != null) sourceViewId = srcPv.ViewID;
            }
            photonView.RPC("RPC_SyncHitState", RpcTarget.Others, currentHits, sourceViewId);
        }
    }
    
    [PunRPC]
    private void RPC_SyncHitState(int hits, int sourceViewId)
    {
        currentHits = hits;
        
        if (sourceViewId >= 0)
        {
            var srcPv = PhotonView.Find(sourceViewId);
            if (srcPv != null) lastHitSource = srcPv.gameObject;
        }
        
        if (animator != null && !string.IsNullOrEmpty(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
        }
        
        if (hits >= maxHits && !isDestroyed)
        {
            DestroyPlant();
        }
    }
    
    private void DestroyPlant()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        
        if (audioSource != null && destroySound != null)
            audioSource.PlayOneShot(destroySound);
            
        if (destroyEffect != null)
        {
            GameObject fx = Instantiate(destroyEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        
        if (animator != null && !string.IsNullOrEmpty(destroyTrigger))
        {
            animator.SetTrigger(destroyTrigger);
        }
        
        DropItems();
        
        StartCoroutine(DestroyAfterDelay(0.5f));
    }
    
    private void DropItems()
    {
        if (dropItems == null || dropItems.Length == 0) return;
        
        GameObject targetPlayer = lastHitSource;
        
        if (targetPlayer == null)
        {
            targetPlayer = FindNearestPlayer();
        }
        
        if (targetPlayer == null)
        {
            Debug.LogWarning("[DestructiblePlant] No player found to grant items to! Falling back to spawning pickups.");
            SpawnItemsAsPickups();
            return;
        }
        
        Inventory inventory = targetPlayer.GetComponent<Inventory>();
        if (inventory == null)
        {
            inventory = Inventory.FindLocalInventory();
            if (inventory == null)
            {
                Debug.LogWarning("[DestructiblePlant] No inventory found! Falling back to spawning pickups.");
                SpawnItemsAsPickups();
                return;
            }
        }
        
        for (int i = 0; i < dropItems.Length; i++)
        {
            if (dropItems[i] != null)
            {
                int quantity = (i < dropQuantities.Length) ? dropQuantities[i] : 1;
                bool added = inventory.AddItem(dropItems[i], quantity);
                
                if (!added)
                {
                    Debug.LogWarning($"[DestructiblePlant] Failed to add {dropItems[i].itemName} x{quantity} to inventory (inventory full). Falling back to spawning pickup.");
                    if (ItemManager.Instance != null)
                    {
                        Vector3 dropPosition = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.5f;
                        ItemManager.Instance.SpawnItem(dropItems[i], dropPosition, quantity);
                    }
                }
                else
                {
                    Debug.Log($"[DestructiblePlant] Granted {dropItems[i].itemName} x{quantity} to {targetPlayer.name}");
                }
            }
        }
    }
    
    private GameObject FindNearestPlayer()
    {
        PlayerStats[] players = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (players == null || players.Length == 0) return null;
        
        GameObject nearest = null;
        float nearestDist = float.MaxValue;
        
        foreach (var player in players)
        {
            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = player.gameObject;
            }
        }
        
        return nearest;
    }
    
    private void SpawnItemsAsPickups()
    {
        if (ItemManager.Instance == null) return;
        
        Vector3 dropPosition = transform.position + Vector3.up * 0.5f;
        
        for (int i = 0; i < dropItems.Length; i++)
        {
            if (dropItems[i] != null)
            {
                int quantity = (i < dropQuantities.Length) ? dropQuantities[i] : 1;
                ItemManager.Instance.SpawnItem(dropItems[i], dropPosition, quantity);
                
                Vector3 offset = Random.insideUnitCircle * 0.5f;
                offset.z = offset.y;
                offset.y = 0;
                dropPosition += offset;
            }
        }
    }
    
    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}

