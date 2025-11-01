using UnityEngine;
using Photon.Pun;

public class TutorialSpawnManager : MonoBehaviourPunCallbacks
{
    [Header("Assign 4 spawn points in inspector")]
    public Transform[] spawnPoints; // Size 4, assign in inspector
    [Header("Assign your player prefab (must be in Resources)")]
    public GameObject playerPrefab;

    [PunRPC]
    public void RPC_SpawnPlayerForThisClient()
    {
        ApplySpawnPlayerForThisClient();
    }
    
    private void ApplySpawnPlayerForThisClient()
    {
        Debug.Log($"[TutorialSpawnManager] ApplySpawnPlayerForThisClient called. Connected={PhotonNetwork.IsConnected}, InRoom={PhotonNetwork.InRoom}, OfflineMode={PhotonNetwork.OfflineMode}");
        
        // Check if player already exists
        GameObject existingPlayer = GameObject.FindWithTag("Player");
        if (existingPlayer != null)
        {
            Debug.Log("[TutorialSpawnManager] Player already exists, skipping spawn.");
            return;
        }
        
        int spawnIndex = 0;
        Vector3 spawnPos = spawnPoints.Length > 0 ? spawnPoints[spawnIndex].position : Vector3.zero;
        GameObject player = null;
        
        // Unified spawn logic for both online and offline
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            spawnIndex = Mathf.Clamp(actorNumber - 1, 0, spawnPoints.Length - 1); // 1-based to 0-based
            spawnPos = spawnPoints.Length > spawnIndex ? spawnPoints[spawnIndex].position : spawnPoints[0].position;
            Debug.Log($"[TutorialSpawnManager] Spawning player (network) at index {spawnIndex}, position {spawnPos}, prefab {playerPrefab.name}");
            player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, Quaternion.identity);
        }
        else
        {
            Debug.Log($"[TutorialSpawnManager] Spawning player (offline/local) at index {spawnIndex}, position {spawnPos}, prefab {playerPrefab.name}");
            player = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        }
        
        // camera setup after spawn
        if (player != null)
        {
            Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.tag = "MainCamera";
            }
            // assign camera orbit target if needed
            var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
            if (cameraOrbit != null)
            {
                Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
                if (cameraPivot != null)
                {
                    cameraOrbit.AssignTargets(player.transform, cameraPivot);
                }
            }
        }
        else
        {
            Debug.LogError("[TutorialSpawnManager] Failed to spawn player! Player prefab is null or spawn failed.");
        }
    }

	// public entrypoint used by other managers; triggers RPC so each client spawns themselves
	public void SpawnPlayerForThisClient()
	{
	    // In offline mode or when not in room, spawn directly without RPC
	    if (PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom || photonView == null)
	    {
	        Debug.Log("[TutorialSpawnManager] Spawning directly (offline/not in room)");
	        ApplySpawnPlayerForThisClient();
	    }
	    else
	    {
	        // In online mode, use RPC to sync spawn across all clients
	        photonView.RPC("RPC_SpawnPlayerForThisClient", RpcTarget.All);
	    }
	}
}
