using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using TMPro;

// attach this to a trigger collider representing an area objective; set areaId to match quest targetId
[RequireComponent(typeof(Collider))]
public class QuestAreaTrigger : MonoBehaviourPunCallbacks
{
    [Tooltip("identifier used by quest objectives for reach-area tasks")] public string areaId;
    [Tooltip("if true, trigger will only fire once then disable itself once completed")] public bool oneShot = true;
    [Header("multiplayer gating")]
    [Tooltip("when true, requires all players to be inside before completing")] public bool requireAllPlayers = true;
    [Tooltip("updates a world-space counter (X/Y) when there are >1 players")] public TextMeshPro counterText;
    public bool billboardCounter = true;
    public float billboardHeight = 2f;

    private Collider _col;
    private Rigidbody _rb;
    private Camera _cam;

    private string InAreaKey => $"InArea_{areaId}";
    private string AreaDoneKey => $"AreaDone_{areaId}";

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col != null) _col.isTrigger = true;
        // ensure a rigidbody exists so trigger callbacks fire when players use CharacterController
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        _cam = Camera.main;
    }

    private void Start()
    {
        UpdateCounterText();
    }

    private void Update()
    {
        if (counterText != null)
        {
            bool multi = PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount > 1;
            counterText.gameObject.SetActive(multi);
            if (billboardCounter && multi)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    counterText.transform.position = transform.position + Vector3.up * billboardHeight;
                    counterText.transform.rotation = Quaternion.LookRotation(counterText.transform.position - _cam.transform.position, Vector3.up);
                }
            }
        }
    }

    private bool IsPlayerCollider(Collider other)
    {
        // accept if this collider OR any parent is tagged Player, or if a PlayerQuestRelay exists up the chain
        if (other.CompareTag("Player")) return true;
        Transform t = other.transform;
        while (t != null)
        {
            if (t.CompareTag("Player") || t.GetComponent<PlayerQuestRelay>() != null) return true;
            t = t.parent;
        }
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other)) return;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            // only the local player should write their own presence
            var pv = other.GetComponentInParent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                if (requireAllPlayers)
                {
                    // in coop count presence regardless of local objective so all players can be counted
                    SetLocalPresence(true);
                    // only the master will actually complete, gated by their objective in EvaluateAndMaybeComplete
                    EvaluateAndMaybeComplete();
                }
                else
                {
                    if (IsLocalQuestMatchingArea())
                    {
                        SetLocalPresence(true);
                        // multiplayer but not requiring all players: treat like offline for the local owner
                        var qm = FindFirstObjectByType<QuestManager>();
                        if (qm != null)
                        {
                            qm.AddProgress_ReachArea(areaId);
                            Debug.Log($"quest reach progress updated (mp solo) for area: {areaId}");
                            if (oneShot && _col != null) _col.enabled = false;
                        }
                        else
                        {
                            Debug.LogWarning($"QuestManager not found while processing reach area '{areaId}' in mp solo mode.");
                        }
                    }
                    else
                    {
                        // not our objective, don't count for solo mode
                        SetLocalPresence(false);
                        Debug.Log($"ignored area enter for {areaId}: not current reach objective");
                    }
                }
            }
        }
        else
        {
            // offline/single-player fallback
            if (IsLocalQuestMatchingArea())
            {
                var qm = FindFirstObjectByType<QuestManager>();
                if (qm != null)
                {
                    qm.AddProgress_ReachArea(areaId);
                    Debug.Log($"quest reach progress updated (offline) for area: {areaId}");
                    if (oneShot && _col != null) _col.enabled = false;
                }
            }
            else
            {
                Debug.Log($"ignored area enter (offline) for {areaId}: not current reach objective");
            }
        }
        UpdateCounterText();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayerCollider(other)) return;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            var pv = other.GetComponentInParent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                // clear presence on exit regardless; re-evaluate coop completion possibility
                SetLocalPresence(false);
                EvaluateAndMaybeComplete();
            }
        }
        UpdateCounterText();
    }

    private void SetLocalPresence(bool inside)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;
        Hashtable props = new Hashtable { [InAreaKey] = inside };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    private bool IsAllPlayersPresent()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        var players = PhotonNetwork.PlayerList;
        if (players == null || players.Length == 0) return false;
        foreach (var p in players)
        {
            if (!p.CustomProperties.TryGetValue(InAreaKey, out var val)) return false;
            if (!(val is bool b) || !b) return false;
        }
        return true;
    }

    private bool IsAlreadyCompleted()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(AreaDoneKey, out var val))
        {
            return val is bool b && b;
        }
        return false;
    }

    private void MarkCompleted()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;
        Hashtable roomProps = new Hashtable { [AreaDoneKey] = true };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
    }

    private void EvaluateAndMaybeComplete()
    {
        if (!requireAllPlayers)
        {
            // handled in OnTriggerEnter for mp-solo path
            return;
        }

        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;

        // only master evaluates and distributes completion
        if (!PhotonNetwork.IsMasterClient) return;
        if (IsAlreadyCompleted()) return;

        // gate: master only completes this area if their local quest matches this area
        // this prevents disabling areas out-of-order when different players are on different steps
        if (!IsLocalQuestMatchingArea())
        {
            return;
        }

        if (IsAllPlayersPresent())
        {
            // send progress to each owning client via their player quest relay
            var relays = FindObjectsByType<PlayerQuestRelay>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (relays != null && relays.Length > 0)
            {
                foreach (var relay in relays)
                {
                    var pv = relay.GetComponent<PhotonView>();
                    if (pv != null && pv.Owner != null)
                    {
                        pv.RPC("RPC_AddReachProgress", pv.Owner, areaId);
                    }
                }
            }
            else
            {
                // fallback: if no relay exists, at least progress the master's local quest
                var qm = FindFirstObjectByType<QuestManager>();
                if (qm != null)
                {
                    qm.AddProgress_ReachArea(areaId);
                    Debug.LogWarning($"no PlayerQuestRelay found; progressed master's quest for area {areaId} as fallback");
                }
            }
            Debug.Log($"quest reach area completed by all players: {areaId}");
            MarkCompleted();
            if (oneShot && _col != null) _col.enabled = false;
        }
        UpdateCounterText();
    }

    private bool IsLocalQuestMatchingArea()
    {
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        if (string.IsNullOrEmpty(areaId)) return false;
        // Prefer new multi-objective system
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            if (obj.objectiveType != ObjectiveType.ReachArea) return false;
            var a1 = (obj.targetId ?? string.Empty).Trim();
            var b1 = (areaId ?? string.Empty).Trim();
            if (a1.Length == 0 || b1.Length == 0) return false;
            return string.Equals(a1, b1, System.StringComparison.OrdinalIgnoreCase);
        }
        // Legacy fallback
        if (q.objectiveType != ObjectiveType.ReachArea) return false;
        var a = (q.targetId ?? string.Empty).Trim();
        var b = (areaId ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounterText()
    {
        if (counterText == null) return;
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom))
        {
            counterText.gameObject.SetActive(false);
            return;
        }
        int total = PhotonNetwork.CurrentRoom.PlayerCount;
        if (total <= 1)
        {
            counterText.gameObject.SetActive(false);
            return;
        }
        int present = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue(InAreaKey, out var val) && val is bool b && b) present++;
        }
        counterText.text = $"{present}/{total}";
        counterText.gameObject.SetActive(true);
    }

    // --- photon callbacks to keep counts fresh ---
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey(InAreaKey))
        {
            EvaluateAndMaybeComplete();
            UpdateCounterText();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateCounterText();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        // if someone left, re-evaluate (players remaining may all be present now)
        EvaluateAndMaybeComplete();
        UpdateCounterText();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // new master should re-evaluate state
        EvaluateAndMaybeComplete();
        UpdateCounterText();
    }
}
