using UnityEngine;

public class MainScenePlayerSpawner : MonoBehaviour
{
    public GameObject playerPrefab;

    void Start()
    {
        if (Photon.Pun.PhotonNetwork.IsConnected && Photon.Pun.PhotonNetwork.InRoom)
        {
            Vector3 spawnPos = PlayerSpawnManager.nextSpawnPosition.HasValue ? PlayerSpawnManager.nextSpawnPosition.Value : Vector3.zero;
            Photon.Pun.PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, Quaternion.identity);
            PlayerSpawnManager.nextSpawnPosition = null;
        }
    }
}
