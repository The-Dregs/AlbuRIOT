
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    public GameObject lobbyPanel; // Assign your lobby panel here
    // Panel references for navigation
    public GameObject mainMenuPanel;
    public GameObject joinOrCreatePanel;
    public GameObject loadingPanel;
    private bool isLoadingTriggeredByUser = false;
    public Button startGameButton;
    public TextMeshProUGUI loadingStatusText;
    public Button continueButton;
    public Button readyButton;
    public Button createGameButton;
    public Button joinGameButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI[] playerSlots; // Assign 4 TMP text elements for player names
    public TextMeshProUGUI roomCodeText; // Display room code
    public TMP_InputField joinCodeInput; // Assign for join game panel
    public TMP_InputField nameInput; // Assign for player name input

    public string startDialogueScene = "startDIALOGUE";
    private string pendingJoinCode = null;
    private string createdRoomCode = "";

        // --- Multiplayer Dialogue Start ---
    [PunRPC]
    public void StartDialogueForAll()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(startDialogueScene);
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
        HideLoading(); // Hide loading panel on startup
        ShowMainMenu();
        statusText.text = "Connecting to Photon...";
        PhotonNetwork.ConnectUsingSettings();
        startGameButton.interactable = false;
        ClearPlayerSlots();
        if (roomCodeText != null) roomCodeText.text = "";

        // Add validation for name input
        if (nameInput != null && createGameButton != null && joinGameButton != null)
        {
            nameInput.onValueChanged.AddListener(OnNameInputChanged);
            OnNameInputChanged(nameInput.text);
        }
        HideLoading(); // Hide after initial setup
    }
    // --- Panel Navigation Methods (from MenuManager) ---
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
            PhotonView photonView = PhotonView.Get(this);
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
        ShowLoading("Connected! Joining Lobby...");
        statusText.text = "Connected! Joining Lobby...";
        PhotonNetwork.JoinLobby();
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
        StartCoroutine(ShowLobbyWithDelay());
    }

    private IEnumerator ShowLobbyWithDelay()
    {
        yield return new WaitForSeconds(0.7f); // 0.7 second delay
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
        if (isHost)
        {
            startGameButton.interactable = AllPlayersReady();
        }
    }

    public void OnReadyClicked()
    {
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        props["Ready"] = true;
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        readyButton.interactable = false;
        statusText.text = "Ready! Waiting for host...";
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
        yield return new WaitForSeconds(1f); // Show error for 1.2 seconds
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
                playerSlots[i].text = name + (ready ? " (Ready)" : "");
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
        // Do NOT disconnect from Photon here, so you can create/join new lobbies
        if (roomCodeText != null) roomCodeText.text = "";
        ClearPlayerSlots();
        createdRoomCode = "";
        pendingJoinCode = null;
    }
}