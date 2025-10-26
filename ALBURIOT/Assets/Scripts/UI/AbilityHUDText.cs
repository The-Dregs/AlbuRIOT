using TMPro;
using UnityEngine;

// Shows the current Slot 1 ability and cooldown on a HUD TextMeshProUGUI.
// Attach this to a TextMeshProUGUI under your Canvas.
public class AbilityHUDText : MonoBehaviour
{
    [Header("references")]
    public TextMeshProUGUI targetText; // assign the TMP text on your Canvas
    public AlbuRIOT.Abilities.PlayerAbilityController controller; // optional; will auto-find local player if null

    [Header("display")]
    public string noAbilityText = "[1] Ability: None";
    public string readyFormat = "[1] {0}  Ready";          // {0} = ability name
    public string cooldownFormat = "[1] {0}  CD: {1:F1}s"; // {0} = ability name, {1} = seconds remaining
    public float updateInterval = 0.1f;

    private float timer = 0f;

    void Reset()
    {
        targetText = GetComponent<TextMeshProUGUI>();
    }

    void Awake()
    {
        if (targetText == null) targetText = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = updateInterval;

        if (controller == null)
        {
            controller = FindLocalAbilityController();
        }

        if (targetText == null) return;

        if (controller == null || controller.slot1 == null)
        {
            targetText.text = noAbilityText;
            return;
        }

        var a = controller.slot1;
        float cdRemain = Mathf.Max(0f, (a.lastUseTime + a.cooldown) - Time.time);
        targetText.text = cdRemain > 0.01f ? string.Format(cooldownFormat, a.abilityName, cdRemain) : string.Format(readyFormat, a.abilityName);
    }

    private AlbuRIOT.Abilities.PlayerAbilityController FindLocalAbilityController()
    {
        // try Photon-aware search first
        var all = GameObject.FindObjectsOfType<AlbuRIOT.Abilities.PlayerAbilityController>();
        foreach (var c in all)
        {
            var pv = c.GetComponent<Photon.Pun.PhotonView>();
            // if in a room, take the local player's controller; otherwise accept the first
            if (pv == null) return c;
            if (!Photon.Pun.PhotonNetwork.InRoom) return c;
            if (pv.IsMine) return c;
        }
        return null;
    }
}
