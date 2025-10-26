using UnityEngine;
using UnityEngine.UI;

public class DamageOverlayUI : MonoBehaviour
{
    [Header("overlay")]
    public Image overlayImage; // full-screen red image (alpha 0 by default)
    [Range(0f, 1f)] public float maxAlpha = 0.6f;
    public float fadeOutPerSecond = 2.5f; // how fast the overlay fades per second
    public float minAlphaStep = 0.1f; // minimum alpha on a pulse even for tiny hits

    private float currentAlpha = 0f;
    private Color baseColor = Color.red;

    [Header("auto")]
    [Tooltip("If true, will enable the overlay GameObject & component at runtime even if disabled in the inspector.")]
    public bool autoEnableOverlay = true;
    [Tooltip("If true and overlayImage is unassigned, will try to find a child named 'RedImageForDamage'.")]
    public bool autoFindByName = true;
    [Tooltip("Child name to look for when autoFindByName is enabled.")]
    public string overlayChildName = "RedImageForDamage";

    void Awake()
    {
        TryAutoFind();
        PrepareOverlay();
    }

    void Update()
    {
        if (overlayImage == null) return;
        if (currentAlpha > 0f)
        {
            currentAlpha = Mathf.Max(0f, currentAlpha - fadeOutPerSecond * Time.deltaTime);
            SetAlpha(currentAlpha);
        }
    }

    // amount01: 0..1 proportion of effect (we clamp internally)
    public void Pulse(float amount01)
    {
        if (overlayImage == null) return;
        if (autoEnableOverlay && !overlayImage.gameObject.activeSelf)
            overlayImage.gameObject.SetActive(true);
        float add = Mathf.Clamp01(amount01) * maxAlpha;
        if (add < minAlphaStep) add = minAlphaStep; // ensure visible feedback
        currentAlpha = Mathf.Clamp01(Mathf.Max(currentAlpha, add));
        SetAlpha(currentAlpha);
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
        // search inactive children too
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
        // start fully transparent and component disabled
        SetAlpha(0f);
    }

    void OnValidate()
    {
        // Keep base color in sync in editor if possible
        if (overlayImage != null)
            baseColor = overlayImage.color;
    }
}
