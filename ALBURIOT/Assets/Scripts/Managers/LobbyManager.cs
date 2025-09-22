using UnityEngine;
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

    public string startDialogueScene = "startDIALOGUE";
    private string pendingJoinCode = null;
    private string createdRoomCode = "";
    private bool isReady = false; // restored ready state

    [PunRPC]
    public void StartDialogueForAll()
    {
    Photon.Pun.PhotonNetwork.LoadLevel(startDialogueScene);
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
        statusText.text = "Connecting to Photon...";
        PhotonNetwork.ConnectUsingSettings();
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

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
        else
            OnStartGameClicked();
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

        if (joinCodeInput != null && !string.IsNullOrEmpty(joinCodeInput.text))
        {
            pendingJoinCode = joinCodeInput.text;
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
        ShowLoading("Connected to server!");
        statusText.text = "Connected to server!";
        UpdatePlayerList();
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
        ShowLoading("Creating Room...");
        statusText.text = "Creating Room...";
        string roomName = GenerateLobbyCode();
        createdRoomCode = roomName;
        PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions { MaxPlayers = 4 }, TypedLobby.Default);
        // When host starts game, trigger dialogue for all
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonView photonView = PhotonView.Get(this);
            photonView.RPC("StartDialogueForAll", RpcTarget.All);
        }
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
        if (PhotonNetwork.CurrentRoom.PlayerCount > 4)
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
        yield return new WaitForSeconds(0.7f);
        isLoadingTriggeredByUser = false;
        HideLoading();
        statusText.text = "Joined Room! Waiting for players...";
        if (roomCodeText != null)
        {
            string code = !string.IsNullOrEmpty(createdRoomCode) ? createdRoomCode : PhotonNetwork.CurrentRoom.Name;
            roomCodeText.text = "Lobby Code: " + code;
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
    if (readyButton != null) readyButton.gameObject.SetActive(!isHost);

    // update ready button text for local player
    if (readyButton != null)
    {
        var btnText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            bool localReady = PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Ready") && (bool)PhotonNetwork.LocalPlayer.CustomProperties["Ready"];
            btnText.text = localReady ? "Unready" : "Ready";
        }
    }

        if (isHost)
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
            startGameButton.interactable = (joinerCount == 0) || allJoinersReady;
        }
}

    public void OnReadyClicked()
{
    if (isReady) Debug.LogWarning("Ready logic called twice! Check inspector assignments.");
    isReady = !isReady;
    ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
    props["Ready"] = isReady;
    PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    PhotonView photonView = PhotonView.Get(this);
    photonView.RPC("RPC_RefreshPlayerList", RpcTarget.All);
    photonView.RPC("RPC_DebugReadyState", RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.NickName, isReady);
}

    [PunRPC]
    public void RPC_DebugReadyState(string playerName, bool ready)
    {
        Debug.Log($"[Lobby] Player '{playerName}' is now {(ready ? "READY" : "UNREADY")}");
    }

    [PunRPC]
    public void RPC_RefreshPlayerList()
    {
        UpdatePlayerList();
        UpdateLobbyUI();
    }

    bool AllPlayersReady()
    {
        foreach (var player in PhotonNetwork.PlayerListOthers)
        {
            if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Ready") || !(bool)player.CustomProperties["Ready"])
                return false;
        }
        return true;
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        isLoadingTriggeredByUser = false;
        ShowLoading("Room code not found or join failed.");
        StartCoroutine(ShowJoinOrCreatePanelWithDelay());
    }

    private IEnumerator ShowJoinOrCreatePanelWithDelay()
    {
        yield return new WaitForSeconds(1f);
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
            bool ready = players[i].CustomProperties != null && players[i].CustomProperties.ContainsKey("Ready") && (bool)players[i].CustomProperties["Ready"];
            bool isHost = players[i].ActorNumber == PhotonNetwork.MasterClient.ActorNumber;
            string hostTag = isHost ? " (Host)" : "";
            string readyTag = ready ? " (Ready)" : "";
            playerSlots[i].text = name + hostTag + readyTag;
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
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        if (roomCodeText != null) roomCodeText.text = "";
        ClearPlayerSlots();
        createdRoomCode = "";
        pendingJoinCode = null;
    }
}