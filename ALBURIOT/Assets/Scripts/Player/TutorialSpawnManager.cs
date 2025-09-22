using UnityEngine;
using Photon.Pun;

public class TutorialSpawnManager : MonoBehaviourPunCallbacks
{
    [Header("Assign 4 spawn points in inspector")]
    public Transform[] spawnPoints; // Size 4, assign in inspector
    [Header("Assign your player prefab (must be in Resources)")]
    public GameObject playerPrefab;

    void Start()
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            int spawnIndex = Mathf.Clamp(actorNumber - 1, 0, spawnPoints.Length - 1); // 1-based to 0-based
            Vector3 spawnPos = spawnPoints[spawnIndex].position;
            PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, Quaternion.identity);
        }
    }
}
