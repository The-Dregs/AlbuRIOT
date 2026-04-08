using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using System.Collections;

public class SceneSpawnHandler : MonoBehaviourPunCallbacks
{
    [Header("Configuration")]
    [Tooltip("If true, positions existing players at spawn points when scene loads")]
    public bool handleExistingPlayers = true;
    [Tooltip("Delay before positioning player (allows scene to fully initialize)")]
    public float positionDelay = 0.5f;
    [Tooltip("If true, prevents spawning new players if one already exists (for scene transitions)")]
    public bool preventDuplicateSpawns = true;

    private static bool hasHandledSpawnThisScene = false;

    void Start()
    {
        hasHandledSpawnThisScene = false;
        if (handleExistingPlayers)
        {
            StartCoroutine(HandlePlayerSpawnCoroutine());
        }
    }

    private IEnumerator HandlePlayerSpawnCoroutine()
    {
        yield return new WaitForSeconds(positionDelay);

        // If transition flow already positioned the player, do not override it here.
        if (PlayerSpawnManager.hasTeleportedByLoader)
        {
            hasHandledSpawnThisScene = true;
            yield break;
        }
        
        GameObject existingPlayer = GameObject.FindWithTag("Player");
        if (existingPlayer != null)
        {
            PositionExistingPlayer(existingPlayer);
        }
        else if (preventDuplicateSpawns)
        {
            hasHandledSpawnThisScene = true;
        }
    }

    private void PositionExistingPlayer(GameObject player)
    {
        if (player == null) return;

        PhotonView pv = player.GetComponent<PhotonView>();
        bool isOwner = pv == null || pv.IsMine;

        if (!isOwner) return;

        Vector3 targetPosition = Vector3.zero;
        Vector3 faceDirection = Vector3.forward;
        if (!PlayerSpawnCoordinator.TryGetBestSpawnPosition(out targetPosition, out faceDirection, out string source, requireSpawnMarkers: false))
        {
            return;
        }

        if (targetPosition != Vector3.zero)
        {
            PlayerSpawnCoordinator.TeleportAndSetupPlayer(player, targetPosition, faceDirection);
            Debug.Log($"[SceneSpawnHandler] Positioned existing player at {targetPosition} (source: {source})");
        }
    }

    public override void OnJoinedRoom()
    {
        if (handleExistingPlayers)
        {
            StartCoroutine(HandlePlayerSpawnCoroutine());
        }
    }
}

