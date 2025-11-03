using UnityEngine;
using Photon.Pun;
using System.Collections;

public class DestructiblePlant : MonoBehaviourPun, IEnemyDamageable
{
    [Header("Health")]
    [SerializeField] private int maxHits = 2;
    [SerializeField] private int currentHits = 0;
    
    [Header("Hitbox")]
    [Tooltip("Collider GameObject for the hitbox. If null, will use collider on this object. This must be on the same GameObject or a child for damage detection to work.")]
    [SerializeField] private GameObject hitboxObject;
    [Tooltip("Plant visual model. If null, will use this object's transform. This is what pops when hit.")]
    [SerializeField] private Transform plantModel;
    [Tooltip("Offset for the hitbox position (relative to this GameObject's position)")]
    [SerializeField] private Vector3 hitboxOffset = new Vector3(0, -0.13f, 0);
    [Tooltip("Scale multiplier for the hitbox size (affects collider radius/size)")]
    [SerializeField] private Vector3 hitboxScale = Vector3.one;
    
    [Header("Hit Effect")]
    [SerializeField] private float hitPopScale = 1.4f;
    [SerializeField] private float hitPopDuration = 0.2f;
    
    [Header("Item Drops")]
    [SerializeField] private ItemData[] dropItems;
    [Tooltip("Min/Max quantity range for each drop item. X = minimum, Y = maximum. Random value between min and max (inclusive) will be dropped.")]
    [SerializeField] private Vector2Int[] dropQuantityRanges;
    
    [Header("VFX/SFX")]
    [SerializeField] private GameObject hitEffect;
    [SerializeField] private GameObject destroyEffect;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip destroySound;
    
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitTrigger = "Hit";
    [SerializeField] private string destroyTrigger = "Destroy";
    
    [Header("Destroy Effect")]
    [Tooltip("Time it takes for the model to sink through the ground when destroyed")]
    [SerializeField] private float sinkDuration = 0.2f;
    [Tooltip("How far below ground the model sinks before being destroyed")]
    [SerializeField] private float sinkDistance = 1.5f;
    
    private AudioSource audioSource;
    private bool isDestroyed = false;
    private GameObject lastHitSource = null;
    private Vector3 originalModelScale;
    private Coroutine hitPopCoroutine;
    private Coroutine sinkCoroutine;
    
    private Collider hitboxCollider;
    private Vector3 originalColliderCenter;
    private float originalSphereRadius = -1f;
    private Vector3 originalBoxSize = Vector3.zero;
    private float originalCapsuleRadius = -1f;
    private float originalCapsuleHeight = -1f;
    private bool originalValuesStored = false;
    
    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
            
        if (animator == null)
            animator = GetComponent<Animator>();
        
        if (plantModel == null)
            plantModel = transform;
        
        originalModelScale = plantModel.localScale;
        
        SetupHitbox();
        ApplyHitboxTransform();
            
        if (dropItems == null || dropItems.Length == 0)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} has no drop items configured!");
        }
        
        if (dropQuantityRanges == null || dropQuantityRanges.Length != dropItems.Length)
        {
            dropQuantityRanges = new Vector2Int[dropItems != null ? dropItems.Length : 0];
            for (int i = 0; i < dropQuantityRanges.Length; i++)
            {
                dropQuantityRanges[i] = new Vector2Int(1, 1);
            }
        }
    }
    
    private void SetupHitbox()
    {
        hitboxCollider = null;
        
        if (hitboxObject != null)
        {
            hitboxCollider = hitboxObject.GetComponent<Collider>();
            if (hitboxCollider == null)
            {
                hitboxCollider = hitboxObject.GetComponentInChildren<Collider>();
            }
        }
        
        if (hitboxCollider == null)
        {
            hitboxCollider = GetComponent<Collider>();
            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponentInChildren<Collider>();
            }
        }
        
        if (hitboxCollider == null)
        {
            Debug.LogError($"[DestructiblePlant] {gameObject.name} has no collider found! Damage detection will not work. Make sure the plant has a collider (CapsuleCollider, SphereCollider, or BoxCollider) on this GameObject or a child.");
            return;
        }
        
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0 && gameObject.layer != enemyLayer)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} is not on the 'Enemy' layer (current: {LayerMask.LayerToName(gameObject.layer)}). Player attacks may not detect this plant. Consider changing the layer to 'Enemy'.");
        }
        
        if (!originalValuesStored)
        {
            StoreOriginalColliderValues();
        }
        
        ApplyHitboxTransform();
    }
    
    private void StoreOriginalColliderValues()
    {
        if (hitboxCollider == null || originalValuesStored) return;
        
        if (hitboxCollider is SphereCollider sc)
        {
            originalColliderCenter = sc.center;
            originalSphereRadius = sc.radius;
            originalValuesStored = true;
        }
        else if (hitboxCollider is BoxCollider bc)
        {
            originalColliderCenter = bc.center;
            originalBoxSize = bc.size;
            originalValuesStored = true;
        }
        else if (hitboxCollider is CapsuleCollider cc)
        {
            originalColliderCenter = cc.center;
            originalCapsuleRadius = cc.radius;
            originalCapsuleHeight = cc.height;
            originalValuesStored = true;
        }
    }
    
    private void ApplyHitboxTransform()
    {
        if (hitboxCollider == null) return;
        
        if (hitboxObject != null)
        {
            hitboxObject.transform.localPosition = hitboxOffset;
        }
        
        if (hitboxCollider is SphereCollider sc)
        {
            if (originalSphereRadius >= 0f)
            {
                sc.center = originalColliderCenter + hitboxOffset;
                sc.radius = originalSphereRadius * hitboxScale.x;
            }
        }
        else if (hitboxCollider is BoxCollider bc)
        {
            if (originalBoxSize.sqrMagnitude > 0f)
            {
                bc.center = originalColliderCenter + hitboxOffset;
                bc.size = new Vector3(
                    originalBoxSize.x * hitboxScale.x,
                    originalBoxSize.y * hitboxScale.y,
                    originalBoxSize.z * hitboxScale.z
                );
            }
        }
        else if (hitboxCollider is CapsuleCollider cc)
        {
            if (originalCapsuleRadius >= 0f && originalCapsuleHeight >= 0f)
            {
                cc.center = originalColliderCenter + hitboxOffset;
                float avgScale = (hitboxScale.x + hitboxScale.y + hitboxScale.z) / 3f;
                cc.radius = originalCapsuleRadius * avgScale;
                cc.height = originalCapsuleHeight * hitboxScale.y;
            }
        }
    }
    
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            if (hitboxCollider == null)
            {
                SetupHitbox();
            }
            else
            {
                if (!originalValuesStored)
                {
                    StoreOriginalColliderValues();
                }
                ApplyHitboxTransform();
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
        lastHitSource = source;
        
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
        
        StartHitPopEffect();
        
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
    
    private void StartHitPopEffect()
    {
        if (hitPopCoroutine != null)
            StopCoroutine(hitPopCoroutine);
        hitPopCoroutine = StartCoroutine(CoHitPopEffect());
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_HitPopEffect", RpcTarget.Others);
        }
    }
    
    [PunRPC]
    private void RPC_HitPopEffect()
    {
        if (hitPopCoroutine != null)
            StopCoroutine(hitPopCoroutine);
        hitPopCoroutine = StartCoroutine(CoHitPopEffect());
    }
    
    private IEnumerator CoHitPopEffect()
    {
        float elapsed = 0f;
        float halfDuration = hitPopDuration * 0.5f;
        
        while (elapsed < hitPopDuration)
        {
            elapsed += Time.deltaTime;
            
            if (elapsed < halfDuration)
            {
                float t = elapsed / halfDuration;
                float scale = Mathf.Lerp(1f, hitPopScale, t);
                plantModel.localScale = originalModelScale * scale;
            }
            else
            {
                float t = (elapsed - halfDuration) / halfDuration;
                float scale = Mathf.Lerp(hitPopScale, 1f, t);
                plantModel.localScale = originalModelScale * scale;
            }
            
            yield return null;
        }
        
        plantModel.localScale = originalModelScale;
        hitPopCoroutine = null;
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
        
        bool isMasterClient = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        bool isOffline = photonView == null || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom;
        
        if (isMasterClient || isOffline)
        {
            DropItems();
        }
        
        StartCoroutine(SinkAndDestroy());
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_StartSinkAnimation", RpcTarget.Others);
        }
    }
    
    [PunRPC]
    private void RPC_StartSinkAnimation()
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
        
        StartCoroutine(SinkAndDestroy());
    }
    
    private IEnumerator SinkAndDestroy()
    {
        if (hitboxCollider != null)
            hitboxCollider.enabled = false;
        
        Vector3 startPos = plantModel.position;
        Vector3 endPos = startPos - Vector3.up * sinkDistance;
        float elapsed = 0f;
        
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sinkDuration;
            plantModel.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        
        plantModel.position = endPos;
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
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
                Vector2Int range = (i < dropQuantityRanges.Length) ? dropQuantityRanges[i] : new Vector2Int(1, 1);
                int quantity = Random.Range(range.x, range.y + 1);
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
                Vector2Int range = (i < dropQuantityRanges.Length) ? dropQuantityRanges[i] : new Vector2Int(1, 1);
                int quantity = Random.Range(range.x, range.y + 1);
                ItemManager.Instance.SpawnItem(dropItems[i], dropPosition, quantity);
                
                Vector3 offset = Random.insideUnitCircle * 0.5f;
                offset.z = offset.y;
                offset.y = 0;
                dropPosition += offset;
            }
        }
    }
    
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        
        Collider col = hitboxCollider;
        if (col == null && hitboxObject != null)
            col = hitboxObject.GetComponent<Collider>();
        if (col == null)
            col = GetComponent<Collider>();
        
        if (col != null)
        {
            Vector3 worldPos = hitboxObject != null ? hitboxObject.transform.position : transform.position;
            worldPos += hitboxOffset;
            
            if (col is SphereCollider sc)
            {
                float radius = originalSphereRadius > 0 ? originalSphereRadius * hitboxScale.x : sc.radius;
                Gizmos.DrawWireSphere(worldPos, radius);
            }
            else if (col is BoxCollider bc)
            {
                Vector3 size = originalBoxSize.sqrMagnitude > 0 
                    ? new Vector3(originalBoxSize.x * hitboxScale.x, originalBoxSize.y * hitboxScale.y, originalBoxSize.z * hitboxScale.z)
                    : bc.size;
                Gizmos.DrawWireCube(worldPos, size);
            }
            else if (col is CapsuleCollider cc)
            {
                float avgScale = (hitboxScale.x + hitboxScale.y + hitboxScale.z) / 3f;
                float radius = originalCapsuleRadius >= 0f ? originalCapsuleRadius * avgScale : cc.radius;
                float height = originalCapsuleHeight >= 0f ? originalCapsuleHeight * hitboxScale.y : cc.height;
                
                Vector3 center = worldPos;
                Vector3 top = center + Vector3.up * (height * 0.5f - radius);
                Vector3 bottom = center - Vector3.up * (height * 0.5f - radius);
                Gizmos.DrawWireSphere(top, radius);
                Gizmos.DrawWireSphere(bottom, radius);
                Gizmos.DrawLine(top + Vector3.right * radius, bottom + Vector3.right * radius);
                Gizmos.DrawLine(top - Vector3.right * radius, bottom - Vector3.right * radius);
                Gizmos.DrawLine(top + Vector3.forward * radius, bottom + Vector3.forward * radius);
                Gizmos.DrawLine(top - Vector3.forward * radius, bottom - Vector3.forward * radius);
            }
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position + hitboxOffset, 1f);
        }
    }
}

