using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a blinking red transparent overlay when the player is downed (multiplayer death state).
/// </summary>
public class DownedOverlayUI : MonoBehaviour
{
    [Header("Overlay")]
    public Image overlayImage; // Full-screen red image
    [Range(0f, 1f)] public float maxAlpha = 0.7f;
    [Range(0f, 1f)] public float minAlpha = 0.3f;
    [Tooltip("Blink speed (cycles per second)")]
    public float blinkSpeed = 1.5f;
    
    [Header("Auto")]
    [Tooltip("If true, will enable the overlay GameObject at runtime even if disabled in inspector.")]
    public bool autoEnableOverlay = true;
    [Tooltip("If true and overlayImage is unassigned, will try to find a child named 'DownedOverlayImage'.")]
    public bool autoFindByName = true;
    [Tooltip("Child name to look for when autoFindByName is enabled.")]
    public string overlayChildName = "DownedOverlayImage";
    
    private Color baseColor = Color.red;
    private float blinkPhase = 0f;
    private bool isDowned = false;
    private PlayerStats playerStats;
    
    void Awake()
    {
        TryAutoFind();
        PrepareOverlay();
        
        // Find local player stats
        playerStats = FindLocalPlayerStats();
    }
    
    void Update()
    {
        if (overlayImage == null) return;
        
        // Check if local player is downed
        if (playerStats == null)
        {
            playerStats = FindLocalPlayerStats();
        }
        
        bool wasDowned = isDowned;
        isDowned = playerStats != null && playerStats.IsDowned;
        
        if (isDowned)
        {
            // Blinking effect
            blinkPhase += blinkSpeed * Time.deltaTime * Mathf.PI * 2f;
            if (blinkPhase > Mathf.PI * 2f) blinkPhase -= Mathf.PI * 2f;
            
            // Sin wave for smooth blinking (0 to 1)
            float blinkValue = (Mathf.Sin(blinkPhase) + 1f) * 0.5f; // 0 to 1
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, blinkValue);
            
            SetAlpha(alpha);
            
            if (!overlayImage.gameObject.activeSelf)
            {
                overlayImage.gameObject.SetActive(true);
            }
        }
        else if (wasDowned && !isDowned)
        {
            // Player was revived or respawned, hide overlay
            SetAlpha(0f);
            overlayImage.enabled = false;
        }
        else
        {
            // Not downed, ensure overlay is hidden
            if (overlayImage.enabled && overlayImage.color.a > 0f)
            {
                SetAlpha(0f);
                overlayImage.enabled = false;
            }
        }
    }
    
    private void SetAlpha(float a)
    {
        if (overlayImage == null) return;
        var c = baseColor;
        c.a = a;
        overlayImage.color = c;
        overlayImage.enabled = a > 0f;
    }
    
    private void TryAutoFind()
    {
        if (overlayImage != null) return;
        if (!autoFindByName || string.IsNullOrEmpty(overlayChildName)) return;
        
        // Search inactive children too
        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t != null && t.name == overlayChildName)
            {
                overlayImage = t.GetComponent<Image>();
                if (overlayImage != null) break;
            }
        }
    }
    
    private void PrepareOverlay()
    {
        if (overlayImage == null) return;
        if (autoEnableOverlay && !overlayImage.gameObject.activeSelf)
            overlayImage.gameObject.SetActive(true);
        
        baseColor = overlayImage.color;
        // Start fully transparent
        SetAlpha(0f);
        overlayImage.enabled = false;
    }
    
    private PlayerStats FindLocalPlayerStats()
    {
        // Find local player (in multiplayer, find player with IsMine)
        PlayerStats[] allPlayers = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var stats in allPlayers)
        {
            if (stats == null) continue;
            var pv = stats.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null) return stats; // Offline mode
            if (pv.IsMine) return stats;
        }
        // Fallback: return first found (for offline)
        return allPlayers.Length > 0 ? allPlayers[0] : null;
    }
    
    void OnValidate()
    {
        // Keep base color in sync in editor if possible
        if (overlayImage != null)
            baseColor = overlayImage.color;
    }
}

