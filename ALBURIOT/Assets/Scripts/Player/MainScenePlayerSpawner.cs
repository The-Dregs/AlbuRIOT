using UnityEngine;

public class MainScenePlayerSpawner : MonoBehaviour
{
    public GameObject playerPrefab;

    void Start()
    {
        if (PlayerSpawnManager.nextSpawnPosition.HasValue)
        {
            Vector3 spawnPos = PlayerSpawnManager.nextSpawnPosition.Value;
            Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            PlayerSpawnManager.nextSpawnPosition = null;
        }
        else
        {
            // Spawn at default position if no spawn position is set
            Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        }
    }
}
