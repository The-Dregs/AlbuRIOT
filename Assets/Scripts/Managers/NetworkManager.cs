using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Network Configuration")]
    public string gameVersion = "1.0";
    public int maxPlayersPerRoom = 4;
    [Tooltip("When true, connects to Photon on Start(). Disable for dev to default to Offline mode.")]
    public bool autoConnectOnStart = false;
    
    [Header("Player Management")]
    public GameObject playerPrefab;
    public Transform[] spawnPoints;
    
    [Header("Game State")]
    public bool isGameStarted = false;
    public bool isGamePaused = false;
    
    [Header("Managers")]
    public QuestManager questManager;
    public ShrineManager shrineManager;
    public MovesetManager movesetManager;
    
    // Events (renamed to avoid name collision with Photon callbacks)
    public event Action ConnectedToMasterEvent;
    public event Action JoinedRoomEvent;
    public event Action LeftRoomEvent;
    public event Action<Player> OnPlayerJoined;
    public event Action<Player> OnPlayerLeft;
    public event Action OnGameStarted;
    public event Action OnGamePaused;
    public event Action OnGameResumed;
    
    // Singleton pattern
    public static NetworkManager Instance { get; private set; }
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Auto-find managers
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
        if (movesetManager == null)
            movesetManager = FindFirstObjectByType<MovesetManager>();
    }
    
    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        // log Photon Server Settings to help debug build/connect issues (AppId presence, master-server usage)
        try
        {
            var ss = Photon.Pun.PhotonNetwork.PhotonServerSettings;
            if (ss != null && ss.AppSettings != null)
            {
                Debug.Log($"NetworkManager: PhotonServerSettings found. AppIdRealtime: '{ss.AppSettings.AppIdRealtime}' IsMasterServerAddress: {ss.AppSettings.IsMasterServerAddress}");
            }
            else
            {
                Debug.LogWarning("NetworkManager: PhotonServerSettings or AppSettings is null. Check PhotonServerSettings asset in Resources/PhotonServerSettings.asset");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("NetworkManager: Exception while reading PhotonServerSettings: " + ex.Message);
        }

        // dev-friendly default: offline unless explicitly connecting
        if (!autoConnectOnStart)
        {
            if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                PhotonNetwork.OfflineMode = true;
                Debug.Log("NetworkManager: Started in Offline Mode (dev)");
                // spawn immediately for singleplayer
                SpawnPlayer();
            }
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    // optional: allow singleplayer/offline sessions without a network connection
    public void StartOfflineSession()
    {
        if (PhotonNetwork.IsConnected)
        {
            Debug.LogWarning("Cannot start offline session while connected. Leave room/disconnect first.");
            return;
        }
        PhotonNetwork.OfflineMode = true;
        Debug.Log("Offline mode enabled. Spawning local player.");
        SpawnPlayer();
    }
    
    #region Photon Callbacks
    
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        ConnectedToMasterEvent?.Invoke();
    }
    
    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        JoinedRoomEvent?.Invoke();
        
        // Spawn player
        SpawnPlayer();
        
        // Sync game state
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    public override void OnLeftRoom()
    {
        Debug.Log("Left room");
        LeftRoomEvent?.Invoke();
    }
    
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"Player {newPlayer.NickName} joined the room");
        OnPlayerJoined?.Invoke(newPlayer);
        
        // Sync game state with new player
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"Player {otherPlayer.NickName} left the room");
        OnPlayerLeft?.Invoke(otherPlayer);
    }
    
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"Master client switched to: {newMasterClient.NickName}");
        
        // Sync game state with new master
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    #endregion
    
    #region Player Management
    
    public void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }
        
        Vector3 spawnPosition = GetSpawnPosition();
        GameObject player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPosition, Quaternion.identity);
        
        Debug.Log($"Player spawned at: {spawnPosition}");
    }
    
    private Vector3 GetSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnIndex = PhotonNetwork.PlayerList.Length - 1;
            if (spawnIndex < spawnPoints.Length)
            {
                return spawnPoints[spawnIndex].position;
            }
        }
        
        // Default spawn position
        return Vector3.zero;
    }
    
    #endregion
    
    #region Game State Management
    
    public void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can start the game!");
            return;
        }
        
        isGameStarted = true;
        Debug.Log("Game started!");
        OnGameStarted?.Invoke();
        
        // Sync with all players (buffered for late-joiners and offline parity)
        photonView.RPC("RPC_StartGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_StartGame()
    {
        isGameStarted = true;
        OnGameStarted?.Invoke();
    }
    
    public void PauseGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can pause the game!");
            return;
        }
        
        isGamePaused = true;
        Debug.Log("Game paused!");
        OnGamePaused?.Invoke();
        
        // Sync with all players (buffered)
        photonView.RPC("RPC_PauseGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_PauseGame()
    {
        isGamePaused = true;
        OnGamePaused?.Invoke();
    }
    
    public void ResumeGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can resume the game!");
            return;
        }
        
        isGamePaused = false;
        Debug.Log("Game resumed!");
        OnGameResumed?.Invoke();
        
        // Sync with all players (buffered)
        photonView.RPC("RPC_ResumeGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_ResumeGame()
    {
        isGamePaused = false;
        OnGameResumed?.Invoke();
    }
    
    private void SyncGameState()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Sync quest state
        if (questManager != null)
        {
            photonView.RPC("RPC_SyncQuestState", RpcTarget.AllBuffered, questManager.currentQuestIndex);
        }
        
        // Sync shrine state
        if (shrineManager != null)
        {
            // Sync shrine states if needed
        }
        
        // Sync moveset state
        if (movesetManager != null)
        {
            string movesetName = movesetManager.CurrentMoveset != null ? movesetManager.CurrentMoveset.movesetName : "";
            photonView.RPC("RPC_SyncMovesetState", RpcTarget.AllBuffered, movesetName);
        }
    }
    
    [PunRPC]
    public void RPC_SyncQuestState(int questIndex)
    {
        if (questManager != null)
        {
            questManager.StartQuest(questIndex);
        }
    }
    
    [PunRPC]
    public void RPC_SyncMovesetState(string movesetName)
    {
        if (movesetManager != null && !string.IsNullOrEmpty(movesetName))
        {
            movesetManager.SetMoveset(movesetManager.GetMovesetByName(movesetName));
        }
    }
    
    #endregion
    
    #region Room Management
    
    public void CreateRoom(string roomName)
    {
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true
        };
        
        PhotonNetwork.CreateRoom(roomName, roomOptions);
    }
    
    // convenience: connect online (disabling OfflineMode) then create the room
    public void ConnectAndCreateRoom(string roomName)
    {
        StartCoroutine(Co_ConnectThen(() => CreateRoom(roomName)));
    }

    public void JoinRoom(string roomName)
    {
        PhotonNetwork.JoinRoom(roomName);
    }

    public void ConnectAndJoinRoom(string roomName)
    {
        StartCoroutine(Co_ConnectThen(() => JoinRoom(roomName)));
    }
    
    public void LeaveRoom()
    {
        PhotonNetwork.LeaveRoom();
    }
    
    #endregion
    
    #region Utility Methods
    
    public bool IsConnected()
    {
        return PhotonNetwork.IsConnected;
    }
    
    public bool IsInRoom()
    {
        return PhotonNetwork.InRoom;
    }
    
    public bool IsMasterClient()
    {
        return PhotonNetwork.IsMasterClient;
    }
    
    public Player[] GetPlayers()
    {
        return PhotonNetwork.PlayerList;
    }
    
    public int GetPlayerCount()
    {
        return PhotonNetwork.PlayerList.Length;
    }
    
    public string GetRoomName()
    {
        return PhotonNetwork.CurrentRoom?.Name ?? "";
    }

    public bool IsNetworkReady()
    {
        return PhotonNetwork.InRoom || PhotonNetwork.OfflineMode;
    }

    private System.Collections.IEnumerator Co_ConnectThen(System.Action onReady)
    {
        if (PhotonNetwork.OfflineMode) PhotonNetwork.OfflineMode = false;
        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
            yield return new WaitUntil(() => PhotonNetwork.IsConnectedAndReady);
        }
        onReady?.Invoke();
    }
    
    #endregion
    
    #region Chat System
    
    public void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        
        string formattedMessage = $"[{PhotonNetwork.LocalPlayer.NickName}]: {message}";
        photonView.RPC("RPC_ChatMessage", RpcTarget.AllBuffered, formattedMessage);
    }
    
    [PunRPC]
    public void RPC_ChatMessage(string message)
    {
        // Handle chat message display
        Debug.Log($"Chat: {message}");
        
        // You can integrate this with a UI chat system
        // For example: ChatUI.Instance.DisplayMessage(message);
    }
    
    #endregion
    
    #region Item and Quest Sync
    
    public void SyncItemPickup(string itemName, int quantity)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        photonView.RPC("RPC_SyncItemPickup", RpcTarget.AllBuffered, itemName, quantity);
    }
    
    [PunRPC]
    public void RPC_SyncItemPickup(string itemName, int quantity)
    {
        // Handle item pickup sync
        Debug.Log($"Item pickup synced: {quantity}x {itemName}");
    }
    
    public void SyncQuestProgress(int questIndex, int objectiveIndex, int progress)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        photonView.RPC("RPC_SyncQuestProgress", RpcTarget.AllBuffered, questIndex, objectiveIndex, progress);
    }
    
    [PunRPC]
    public void RPC_SyncQuestProgress(int questIndex, int objectiveIndex, int progress)
    {
        // Handle quest progress sync
        Debug.Log($"Quest progress synced: Quest {questIndex}, Objective {objectiveIndex}, Progress {progress}");
    }
    
    #endregion
}

