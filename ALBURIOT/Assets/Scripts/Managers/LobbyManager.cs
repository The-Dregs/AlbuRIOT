using UnityEngine;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    public GameObject lobbyPanel;
    public GameObject mainMenuPanel;
    public GameObject joinOrCreatePanel;
    public GameObject loadingPanel;
    private bool isLoadingTriggeredByUser = false;
    public Button startGameButton;
    public TextMeshProUGUI loadingStatusText;
    public Button continueButton;
    public Button readyButton; // restored readyButton
    public Button createGameButton;
    public Button joinGameButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI[] playerSlots;
    public TextMeshProUGUI roomCodeText;
    public TMP_InputField joinCodeInput;
    public TMP_InputField nameInput;

    [Header("connection options")]
    // number of seconds to wait for Photon to connect before falling back to offline mode
    public float connectionTimeoutSeconds = 5f;
    // if true, skip waiting and fallback to offline immediately when create is pressed and connection isn't ready
    public bool immediateOfflineFallback = false;

    [Header("offline UI")]
    // optional small toast text to inform player when fallback happens
    public TextMeshProUGUI offlineToastText;
    public float offlineToastDuration = 3f;

    public string startDialogueScene = "startDIALOGUE";
    private string pendingJoinCode = null;
    private string createdRoomCode = "";
    private bool isReady = false; // restored ready state
    private bool forceOffline = false; // runtime-detected offline fallback
    // connection flow helpers
    private bool shouldJoinLobbyOnConnect = false;
    private bool shouldCreateRoomOnConnect = false;
    private int currentReconnectAttempts = 0;
    private int maxReconnectAttempts = 3;
    private float reconnectBackoffSeconds = 2f;
    private bool triedProtocolFallback = false;
    private bool isCreatingRoom = false;

    // offline helpers
    bool HasInternet()
    {
        // simple reachability check; if behind captive portal or blocked, OnDisconnected will also trigger fallback
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    void ActivateOfflineMode(string reason = null)
    {
        if (!PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = true;
            Debug.Log("[Lobby] Activating Offline Mode" + (string.IsNullOrEmpty(reason) ? string.Empty : $": {reason}"));
        }
        forceOffline = true;
        if (statusText != null) statusText.text = "Offline Mode: starting solo session";
        if (loadingStatusText != null && isLoadingTriggeredByUser) loadingStatusText.text = "Offline Mode: creating local room...";
        // show small toast if configured
        ShowOfflineToast(reason);
    }

    [PunRPC]
    public void StartDialogueForAll()
    {
        // use SceneLoader when available so we show a loading UI during the transition
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(startDialogueScene);
        }
        else
        {
            // fallback to PhotonNetwork.LoadLevel if connected, otherwise local load
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode)
                PhotonNetwork.LoadLevel(startDialogueScene);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(startDialogueScene);
        }
    }

    void ShowLoading(string message)
    {
        if (!isLoadingTriggeredByUser) return;
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = message;
    }

    void HideLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    void Start()
    {
        isLoadingTriggeredByUser = false;
        HideLoading();
        ShowMainMenu();
        // try to connect only if we appear online; otherwise stay offline until user hosts/joins
        if (HasInternet())
        {
            statusText.text = "Connecting to Photon...";
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            ActivateOfflineMode("no internet detected");
        }
        startGameButton.interactable = false;
        ClearPlayerSlots();
        if (roomCodeText != null) roomCodeText.text = "";

        // Set name input max length to 10
        if (nameInput != null)
            nameInput.characterLimit = 10;

        // Add validation for name input
        if (nameInput != null && createGameButton != null && joinGameButton != null)
        {
            nameInput.onValueChanged.AddListener(OnNameInputChanged);
            OnNameInputChanged(nameInput.text);
        }
        HideLoading();
        if (offlineToastText != null)
        {
            offlineToastText.gameObject.SetActive(false);
        }
    }

    public void ShowMainMenu()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    public void ShowJoinOrCreatePanel()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    public void ShowLobbyPanel()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    public void OnStartGameMenuClicked()
    {
        ShowJoinOrCreatePanel();
    }

    public void OnCreateGameMenuClicked()
    {
        ShowLobbyPanel();
        HostLobby();
    }

    public void OnJoinGameMenuClicked()
    {
        ShowLobbyPanel();
        OnJoinGamePanelJoinClicked();
    }

    public void OnOptionsClicked()
    {
        // Show options panel if you have one
    }

    public void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnBackFromLobby()
    {
        LeaveLobby();
        ShowMainMenu();
    }

    public void OnBackFromJoinOrCreate()
    {
        ShowMainMenu();
    }

    public void OnNewGameClicked()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("Host is starting the game. Loading startDIALOGUE for all players.");
            PhotonView photonView = PhotonView.Get(this);
            if (photonView == null)
            {
                Debug.LogError("PhotonView component missing on LobbyManager GameObject. Please add a PhotonView.");
                return;
            }
            photonView.RPC("StartDialogueForAll", RpcTarget.All);
        }
    }

    void OnNameInputChanged(string value)
    {
        bool hasName = !string.IsNullOrEmpty(value);
        createGameButton.interactable = hasName;
        joinGameButton.interactable = hasName;
    }

    public void HostLobby()
    {
        if (nameInput != null && !string.IsNullOrEmpty(nameInput.text))
            PhotonNetwork.NickName = nameInput.text;
        else
            PhotonNetwork.NickName = "Player";

        // if offline or no internet, don't try to connect; go offline flow
        if (!HasInternet())
        {
            ActivateOfflineMode("no internet for hosting");
            OnStartGameClicked();
            return;
        }

        // ensure we create the room once connected
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            shouldCreateRoomOnConnect = true;
            statusText.text = "Connecting to Photon...";
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            OnStartGameClicked();
        }
    }

    public void OnJoinGamePanelJoinClicked()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        isLoadingTriggeredByUser = true;
        ShowLoading("Joining Room...");
        if (nameInput != null && !string.IsNullOrEmpty(nameInput.text))
            PhotonNetwork.NickName = nameInput.text;
        else
            PhotonNetwork.NickName = "Player";

        // if offline or no internet, fallback to solo session
        if (!HasInternet())
        {
            ActivateOfflineMode("no internet for joining");
            // create a local room and continue
            pendingJoinCode = null;
            OnStartGameClicked();
            return;
        }

        if (joinCodeInput != null && !string.IsNullOrEmpty(joinCodeInput.text))
        {
            pendingJoinCode = joinCodeInput.text;
            // request to join lobby once connected
            shouldJoinLobbyOnConnect = true;
            if (!PhotonNetwork.IsConnected)
                PhotonNetwork.ConnectUsingSettings();
            else
                PhotonNetwork.JoinLobby();
            if (statusText != null) statusText.text = "Connecting to lobby...";
        }
        else
        {
            ShowLoading("Please enter a lobby code.");
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Lobby] OnConnectedToMaster called. Connected and ready for matchmaking.");
        ShowLoading("Connected to server!");
        statusText.text = "Connected to server!";
        currentReconnectAttempts = 0; // reset reconnect attempts on success
        UpdatePlayerList();

        // handle deferred actions requested while connecting
        if (shouldJoinLobbyOnConnect)
        {
            shouldJoinLobbyOnConnect = false;
            Debug.Log("[Lobby] Joining lobby after connect (deferred).");
            PhotonNetwork.JoinLobby();
        }
        if (shouldCreateRoomOnConnect)
        {
            shouldCreateRoomOnConnect = false;
            Debug.Log("[Lobby] Creating room after connect (deferred).");
            OnStartGameClicked();
        }
    }

    public override void OnJoinedLobby()
    {
        if (!string.IsNullOrEmpty(pendingJoinCode))
        {
            ShowLoading("Joining room...");
            PhotonNetwork.JoinRoom(pendingJoinCode);
            if (statusText != null) statusText.text = "Joining room...";
            pendingJoinCode = null;
        }
        else
        {
            ShowLoading("No lobby code entered.");
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
        }
        startGameButton.interactable = true;
        if (roomCodeText != null) roomCodeText.text = "Lobby Code:";
        statusText.text = "In Lobby. Ready to start!";
    }

    public void OnStartGameClicked()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        isLoadingTriggeredByUser = true;
        // guard against re-entrancy
        if (isCreatingRoom)
        {
            Debug.LogWarning("OnStartGameClicked called while already creating a room.");
            return;
        }
        isCreatingRoom = true;

        // If offline mode already set, create a local room immediately
        if (PhotonNetwork.OfflineMode)
        {
            ShowLoading("Creating Local Room...");
            statusText.text = "Creating Local Room...";
            string roomName = "OFFLINE";
            createdRoomCode = roomName;
            RoomOptions ro = new RoomOptions { MaxPlayers = 1 };
            PhotonNetwork.CreateRoom(roomName, ro, null);
            return;
        }

        // if connected to Photon already, proceed to create/join a networked room
        if (PhotonNetwork.IsConnectedAndReady)
        {
            ShowLoading("Creating Room...");
            statusText.text = "Creating Room...";
            string roomName = GenerateLobbyCode();
            createdRoomCode = roomName;
            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions { MaxPlayers = 4 }, TypedLobby.Default);
            return;
        }

        // not connected but we appear to have internet: attempt to connect and defer room creation
        if (HasInternet())
        {
            ShowLoading("Connecting to Photon... Creating room when ready...");
            statusText.text = "Connecting to Photon...";
            shouldCreateRoomOnConnect = true;
            PhotonNetwork.ConnectUsingSettings();
            // start a short timeout: if connect doesn't happen, fallback to offline mode
            float t = immediateOfflineFallback ? 0f : connectionTimeoutSeconds;
            StartCoroutine(Co_WaitForConnectionThenFallback(t));
            return;
        }

        // no internet -> switch straight to offline mode and create a local room
        ActivateOfflineMode("no internet for creating room");
        // call OnStartGameClicked again; offline path will create the room
        isCreatingRoom = false; // reset before re-entering
        OnStartGameClicked();
    }

    string GenerateLobbyCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        System.Random random = new System.Random();
        char[] code = new char[6];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = chars[random.Next(chars.Length)];
        }
        return new string(code);
    }

    public override void OnJoinedRoom()
    {
        // If room is full, leave and show error
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.CurrentRoom.PlayerCount > 4)
        {
            ShowLoading("Lobby is full. Returning to join panel...");
            PhotonNetwork.LeaveRoom();
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
            return;
        }
        StartCoroutine(ShowLobbyWithDelay());
    }

    private IEnumerator ShowLobbyWithDelay()
    {
        yield return new WaitForSeconds(0.5f);
        // creation finished (either online or offline) — allow subsequent create attempts
        isCreatingRoom = false;
        isLoadingTriggeredByUser = false;
        HideLoading();
        statusText.text = PhotonNetwork.OfflineMode ? "Joined Local Session." : "Joined Room! Waiting for players...";
        if (roomCodeText != null)
        {
            string code = !string.IsNullOrEmpty(createdRoomCode) ? createdRoomCode : PhotonNetwork.CurrentRoom.Name;
            roomCodeText.text = PhotonNetwork.OfflineMode ? "OFFLINE" : code;
            GUIUtility.systemCopyBuffer = code;
        }
        UpdatePlayerList();
        UpdateLobbyUI();
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    void UpdateLobbyUI()
    {
        bool isHost = PhotonNetwork.IsMasterClient;
        if (startGameButton != null) startGameButton.gameObject.SetActive(isHost);
        if (continueButton != null) continueButton.gameObject.SetActive(isHost);
        if (readyButton != null) readyButton.gameObject.SetActive(!isHost && !PhotonNetwork.OfflineMode);

        // update ready button text for local player
        if (readyButton != null)
        {
            var btnText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                bool localReady = PhotonNetwork.LocalPlayer.CustomProperties != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Ready") && (bool)PhotonNetwork.LocalPlayer.CustomProperties["Ready"];
                btnText.text = localReady ? "Unready" : "Ready";
            }
        }

        if (isHost && !PhotonNetwork.OfflineMode)
        {
            // Enable start if all joiners are ready, or if host is alone
            int joinerCount = PhotonNetwork.PlayerListOthers.Length;
            bool allJoinersReady = true;
            foreach (var player in PhotonNetwork.PlayerListOthers)
            {
                if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Ready") || !(bool)player.CustomProperties["Ready"])
                {
                    allJoinersReady = false;
                    break;
                }
            }
            if (startGameButton != null) startGameButton.interactable = (joinerCount == 0) || allJoinersReady;
        }
        else
        {
            if (startGameButton != null) startGameButton.interactable = false;
        }
    }

    private IEnumerator ShowJoinOrCreatePanelWithDelay()
    {
        yield return new WaitForSeconds(0.5f);
        HideLoading();
        ShowJoinOrCreatePanel();
        statusText.text = "Please enter a valid lobby code.";
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdatePlayerList();
        UpdateLobbyUI();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdatePlayerList();
        UpdateLobbyUI();
    }

    void UpdatePlayerList()
{
    ClearPlayerSlots();
    var players = PhotonNetwork.PlayerList;
    for (int i = 0; i < playerSlots.Length; i++)
    {
        if (i < players.Length)
        {
            string name = !string.IsNullOrEmpty(players[i].NickName) ? players[i].NickName : ($"Player {i + 1}");
            bool ready = false;
            if (players[i].CustomProperties != null && players[i].CustomProperties.ContainsKey("Ready"))
            {
                object readyObj = players[i].CustomProperties["Ready"];
                if (readyObj is bool)
                    ready = (bool)readyObj;
                else if (readyObj is int)
                    ready = ((int)readyObj) != 0;
            }
            bool isHost = players[i].ActorNumber == PhotonNetwork.MasterClient.ActorNumber;
            string hostTag = isHost ? " (Host)" : "";
            string readyTag = ready ? " (Ready)" : "";
            playerSlots[i].text = name + hostTag + readyTag;
            Debug.Log($"UpdatePlayerList: {name} host={isHost} ready={ready}");
        }
        else
        {
            playerSlots[i].text = "Waiting...";
        }
    }
}

    void ClearPlayerSlots()
    {
        if (playerSlots == null) return;
        foreach (var slot in playerSlots)
        {
            if (slot != null) slot.text = "Waiting...";
        }
    }

    public void OnBackClicked()
    {
        LeaveLobby();
        ShowJoinOrCreatePanel();
        statusText.text = "Left lobby. Returning to join panel.";
    }

    public void LeaveLobby()
    {
        isLoadingTriggeredByUser = false;
        HideLoading();
        ShowMainMenu();
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode) PhotonNetwork.LeaveRoom();
        if (roomCodeText != null) roomCodeText.text = "";
        ClearPlayerSlots();
        createdRoomCode = "";
        pendingJoinCode = null;
    }

    // connection/disconnection handling
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[Lobby] Disconnected from Photon: {cause}");

        // if no internet, go offline immediately
        if (!HasInternet())
        {
            ActivateOfflineMode(cause.ToString());
            return;
        }

        // try automatic reconnect for common transient disconnects
        if (currentReconnectAttempts < maxReconnectAttempts)
        {
            currentReconnectAttempts++;
            float wait = reconnectBackoffSeconds * currentReconnectAttempts;
            Debug.LogWarning($"[Lobby] Attempting reconnect #{currentReconnectAttempts} in {wait}s (cause: {cause}).");
            StartCoroutine(Co_ReconnectAfterDelay(wait));
            if (statusText != null) statusText.text = $"Disconnected. Reconnecting... ({currentReconnectAttempts}/{maxReconnectAttempts})";
            return;
        }

        // If we haven't tried switching protocols (e.g. UDP -> WebSocket), try that once before giving up
        if (!triedProtocolFallback)
        {
            triedProtocolFallback = true;
            currentReconnectAttempts = 0; // reset attempts for new protocol
            Debug.LogWarning("[Lobby] Attempting protocol fallback to WebSocket and reconnecting.");
            try
            {
                var ss = Photon.Pun.PhotonNetwork.PhotonServerSettings;
                if (ss != null && ss.AppSettings != null)
                {
                    ss.AppSettings.Protocol = ConnectionProtocol.WebSocket;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Lobby] Protocol fallback setup failed: " + ex.Message);
            }
            StartCoroutine(Co_ReconnectAfterDelay(reconnectBackoffSeconds));
            if (statusText != null) statusText.text = "Disconnected. Switching protocol and reconnecting...";
            return;
        }

        // fallback to offline mode if reconnect attempts exhausted or non-transient issue
        ActivateOfflineMode(cause.ToString());
        // if user initiated a join/create, allow fallback flow to continue
        if (isLoadingTriggeredByUser)
        {
            OnStartGameClicked();
        }
    }

    private System.Collections.IEnumerator Co_ReconnectAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!HasInternet())
        {
            ActivateOfflineMode("no internet on reconnect");
            yield break;
        }
        Debug.Log("[Lobby] Reconnect: calling PhotonNetwork.ConnectUsingSettings()");
        PhotonNetwork.ConnectUsingSettings();
    }

    private System.Collections.IEnumerator Co_WaitForConnectionThenFallback(float timeoutSeconds)
    {
        float start = Time.time;
        // wait until connected or timeout
        while (Time.time - start < timeoutSeconds)
        {
            if (PhotonNetwork.IsConnectedAndReady)
            {
                // connected successfully; no fallback needed
                yield break;
            }
            yield return null;
        }

        // timed out without becoming connected -> fallback to offline mode
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning("[Lobby] Connection timeout - falling back to offline mode.");
            ActivateOfflineMode("connection timeout");
            // allow create attempts again and start offline create if user requested
            isCreatingRoom = false;
            if (isLoadingTriggeredByUser)
            {
                OnStartGameClicked();
            }
        }
    }

    void ShowOfflineToast(string reason = null)
    {
        if (offlineToastText == null) return;
        // compose message
        string msg = string.IsNullOrEmpty(reason) ? "No connection — switched to Offline Mode." : $"No connection — switched to Offline Mode: {reason}";
        offlineToastText.text = msg;
        offlineToastText.gameObject.SetActive(true);
        // start hide timer
        StartCoroutine(Co_ShowOfflineToast());
    }

    private IEnumerator Co_ShowOfflineToast()
    {
        yield return new WaitForSeconds(offlineToastDuration);
        if (offlineToastText != null)
        {
            offlineToastText.gameObject.SetActive(false);
        }
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Lobby] CreateRoom failed: {returnCode} {message}");
        // allow retrying create
        isCreatingRoom = false;
        if (!HasInternet())
        {
            ActivateOfflineMode($"create failed: {message}");
            OnStartGameClicked();
            return;
        }
        ShowLoading("Create room failed.");
        StartCoroutine(ShowJoinOrCreatePanelWithDelay());
    }
}