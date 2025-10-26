using UnityEngine;

public class NPCDialogueTrigger : MonoBehaviour
{
    public NPCDialogueData dialogue;
    public GameObject interactPrompt; // Assign your "Press E" UI text here
    [Tooltip("identifier used by quest objectives for talk-to tasks")] public string npcId;
    [Header("quest gating")]
    [Tooltip("when on, only allows interaction if current quest is TalkTo this npcId")] public bool requireMatchingTalkObjective = false;
    [Tooltip("when on, marks TalkTo progress on dialogue end instead of start")] public bool completeOnDialogueEnd = true;
    private bool playerInRange = false;

    private bool IsLocalPlayer(GameObject go)
    {
        var pv = go.GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv == null) return true; // offline
        return pv.IsMine;
    }

    private bool IsTalkObjectiveActive()
    {
        if (!requireMatchingTalkObjective) return true;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        return q.objectiveType == ObjectiveType.TalkTo && !string.IsNullOrEmpty(npcId) && string.Equals(q.targetId, npcId, System.StringComparison.OrdinalIgnoreCase);
    }

    void OnTriggerEnter(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = true;
        // show prompt only for local player via their HUD if available
        if (IsLocalPlayer(playerRoot))
        {
            if (!requireMatchingTalkObjective || IsTalkObjectiveActive())
            {
                var hud = playerRoot.GetComponentInChildren<PlayerInteractHUD>(true);
                if (hud != null)
                {
                    hud.Show("Press E");
                }
                else if (interactPrompt != null)
                {
                    // fallback: world prompt
                    interactPrompt.SetActive(true);
                }
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = false;
        var hud = playerRoot.GetComponentInChildren<PlayerInteractHUD>(true);
        if (hud != null) hud.Hide();
        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    void OnTriggerStay(Collider other)
    {
        // handle cases where player starts inside the trigger (scene start)
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        if (!IsLocalPlayer(playerRoot)) return;
        if (!playerInRange) playerInRange = true;
        if (!requireMatchingTalkObjective || IsTalkObjectiveActive())
        {
            var hud = playerRoot.GetComponentInChildren<PlayerInteractHUD>(true);
            if (hud != null)
            {
                if (!(hud.gameObject.activeInHierarchy && hud.enabled)) hud.Show("Press E");
            }
            else if (interactPrompt != null && !interactPrompt.activeSelf)
            {
                interactPrompt.SetActive(true);
            }
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log($"npc trigger received E press for npcId='{npcId}' (requireGate={requireMatchingTalkObjective})");
            // only local player can trigger; ensure a local player exists in the scene
            var local = FindLocalPlayer();
            if (local == null) return;
            if (requireMatchingTalkObjective && !IsTalkObjectiveActive())
            {
                Debug.Log($"interaction with npc {npcId} blocked: not current TalkTo objective");
                return;
            }

            var dm = FindFirstObjectByType<NPCDialogueManager>();
            if (dm == null)
            {
                Debug.Log("no NPCDialogueManager found in scene. creating one at runtime.");
                var go = new GameObject("NPCDialogueManager_Auto");
                dm = go.AddComponent<NPCDialogueManager>();
            }
            if (dm != null)
            {
                if (completeOnDialogueEnd)
                {
                    // subscribe once for end
                    System.Action<DialogueData> onEnd = null;
                    onEnd = (dlg) =>
                    {
                        if (dm != null) dm.OnDialogueEnded -= onEnd;
                        ApplyTalkProgress();
                    };
                    dm.OnDialogueEnded += onEnd;
                }
                else
                {
                    ApplyTalkProgress();
                }
                dm.StartDialogue(dialogue, local.transform, transform);
                // hide local HUD prompt if present
                var hud = local.GetComponentInChildren<PlayerInteractHUD>(true);
                if (hud != null) hud.Hide();
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
            if (pv == null) return s.gameObject; // offline
            if (pv.IsMine) return s.gameObject;
        }
        return GameObject.FindGameObjectWithTag("Player");
    }

    private GameObject GetPlayerRoot(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        return ps != null ? ps.gameObject : null;
    }

    private void ApplyTalkProgress()
    {
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm != null && !string.IsNullOrEmpty(npcId))
        {
            qm.AddProgress_TalkTo(npcId);
            Debug.Log($"quest talk progress updated for npc: {npcId}");
        }
    }
}
