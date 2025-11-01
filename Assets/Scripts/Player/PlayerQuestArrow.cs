using UnityEngine;
using Photon.Pun;

// places/rotates an arrow (quad/mesh) around the local player pointing to the current quest target
public class PlayerQuestArrow : MonoBehaviour
{
    [Header("arrow visuals")]
    [Tooltip("child GameObject with your arrow quad/mesh/VFX")]
    public Transform arrowRoot;
    [Tooltip("radius offset from player center for the arrow visual")]
    public float radius = 0.8f;
    [Tooltip("vertical offset from ground")]
    public float height = 0.05f;
    [Tooltip("smoothing for rotation and repositioning")]
    public float smooth = 12f;
    [Tooltip("hide arrow if no target found")]
    public bool hideWhenNoTarget = true;

    [Header("target refresh")]
    [Tooltip("how often (seconds) to re-scan scene to find the target transform for the current quest")]
    public float recheckInterval = 1.0f;

    private Transform target;
    private QuestManager questManager;
    private float recheckTimer = 0f;
    private PhotonView pv;

    void Awake()
    {
        pv = GetComponent<PhotonView>();
        questManager = FindFirstObjectByType<QuestManager>();
    }

    void OnEnable()
    {
        // initial resolve
        ResolveTarget();
        UpdateVisibility();
        SubscribeToQuestEvents(true);
    }

    void LateUpdate()
    {
        // local player only (if Photon is present)
        if (pv != null && PhotonNetwork.IsConnected && !pv.IsMine)
        {
            if (arrowRoot != null) arrowRoot.gameObject.SetActive(false);
            return;
        }

        // periodically refresh target based on current quest
        recheckTimer -= Time.deltaTime;
        if (recheckTimer <= 0f)
        {
            recheckTimer = recheckInterval;
            ResolveTarget();
            UpdateVisibility();
        }

        if (arrowRoot == null) return;
        if (target == null)
        {
            if (hideWhenNoTarget) arrowRoot.gameObject.SetActive(false);
            return;
        }

        // compute direction on horizontal plane
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
        {
            // target is basically on top; keep visible but no rotation change
            return;
        }

        // world direction toward target
        Vector3 dir = to.normalized;
        // convert to local space to avoid jitter from parent movement/rotation
        Vector3 localDir = transform.InverseTransformDirection(dir);
        // set local position directly (no smoothing) to eliminate wobble
        arrowRoot.localPosition = localDir * radius + Vector3.up * height;
        // rotate in world to face the target smoothly
        Quaternion desiredRot = Quaternion.LookRotation(dir, Vector3.up);
        if (smooth > 0f)
            arrowRoot.rotation = Quaternion.Slerp(arrowRoot.rotation, desiredRot, Time.deltaTime * smooth);
        else
            arrowRoot.rotation = desiredRot;
        if (!arrowRoot.gameObject.activeSelf) arrowRoot.gameObject.SetActive(true);
    }

    // allows manual overriding of the target if needed by other systems
    public void SetTarget(Transform t)
    {
        target = t;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (arrowRoot == null) return;
        bool show = target != null || !hideWhenNoTarget;
        arrowRoot.gameObject.SetActive(show);
    }

    private void ResolveTarget()
    {
        if (questManager == null) questManager = FindFirstObjectByType<QuestManager>();
        if (questManager == null)
        {
            target = null; return;
        }
        var q = questManager.GetCurrentQuest();
        if (q == null)
        {
            target = null; return;
        }
        if (q.isCompleted)
        {
            // hide if the current quest is already completed
            target = null; return;
        }
        // Prefer new multi-objective system
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            switch (obj.objectiveType)
            {
                case ObjectiveType.ReachArea:
                    target = FindQuestAreaTransform(obj.targetId);
                    return;
                case ObjectiveType.TalkTo:
                    target = FindNpcTransform(obj.targetId);
                    return;
                case ObjectiveType.Kill:
                case ObjectiveType.Collect:
                default:
                    target = null; return;
            }
        }
        // Legacy single-objective fallback
        switch (q.objectiveType)
        {
            case ObjectiveType.ReachArea:
                target = FindQuestAreaTransform(q.targetId);
                break;
            case ObjectiveType.TalkTo:
                target = FindNpcTransform(q.targetId);
                break;
            default:
                target = null;
                break;
        }
    }

    private Transform FindQuestAreaTransform(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return null;
        var areas = FindObjectsByType<QuestAreaTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var a in areas)
        {
            if (a != null && string.Equals(a.areaId, areaId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (a.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = a.transform; }
            }
        }
        return best;
    }

    private Transform FindNpcTransform(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        var npcs = FindObjectsByType<NPCDialogueTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var n in npcs)
        {
            if (n != null && string.Equals(n.npcId, npcId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (n.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = n.transform; }
            }
        }
        return best;
    }

    private void SubscribeToQuestEvents(bool subscribe)
    {
        if (questManager == null) questManager = FindFirstObjectByType<QuestManager>();
        if (questManager == null) return;
        if (subscribe)
        {
            questManager.OnQuestStarted += OnQuestChanged;
            questManager.OnQuestUpdated += OnQuestChanged;
            questManager.OnQuestCompleted += OnQuestChanged;
        }
        else
        {
            questManager.OnQuestStarted -= OnQuestChanged;
            questManager.OnQuestUpdated -= OnQuestChanged;
            questManager.OnQuestCompleted -= OnQuestChanged;
        }
    }

    private void OnQuestChanged(Quest q)
    {
        // whenever quest state changes, resolve target and update visibility
        ResolveTarget();
        UpdateVisibility();
    }

    void OnDisable()
    {
        SubscribeToQuestEvents(false);
    }
}
