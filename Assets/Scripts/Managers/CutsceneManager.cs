using UnityEngine;
using UnityEngine.Playables;
using System.Collections;
using Photon.Pun;

public class CutsceneManager : MonoBehaviourPunCallbacks
{
    [Header("Cutscene Timeline")]
    public PlayableDirector cutsceneDirector;
    [Header("Player Spawner")]
    public TutorialSpawnManager spawnManager;
    [Header("UI")]
    public GameObject skipButton; // assign in inspector
    public CanvasGroup fadeOverlay; // assign in inspector (UI Image with CanvasGroup)
    public float fadeDuration = 1f;
    public float skipButtonDelay = 2f;

    private bool cutsceneSkipped = false;

    void Start()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 1f; // start fully black
            fadeOverlay.gameObject.SetActive(true);
        }
        if (skipButton != null)
        {
            skipButton.SetActive(false);
            UnityEngine.UI.Button btn = skipButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
                btn.onClick.AddListener(OnSkipButtonClicked);
        }
        
        // Unified handling for both online and offline
        if (PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            // Create room for offline mode, then trigger cutscene
            PhotonNetwork.CreateRoom("OfflineRoom");
            StartCoroutine(WaitForOfflineRoomThenStart());
        }
        else if (PhotonNetwork.InRoom && photonView != null)
        {
            // Already in a room - start cutscene via RPC
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.AllBuffered);
        }
        else if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && photonView != null)
        {
            // Already in offline room - start cutscene via RPC
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.AllBuffered);
        }
        else
        {
            // Fallback: direct call if no network
            StartCoroutine(FadeInThenPlayCutscene());
        }
    }
    
    private IEnumerator WaitForOfflineRoomThenStart()
    {
        // Wait for room creation to complete
        while (!PhotonNetwork.InRoom)
        {
            yield return null;
        }
        // Small delay to ensure everything is initialized
        yield return new WaitForSeconds(0.1f);
        if (photonView != null)
        {
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.AllBuffered);
        }
        else
        {
            // Fallback if photonView not ready
            StartCoroutine(FadeInThenPlayCutscene());
        }
    }

    public override void OnJoinedRoom()
    {
        // Called when joining a room (both online and offline)
        if (photonView != null)
        {
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.AllBuffered);
        }
        else
        {
            // Fallback if photonView not ready
            StartCoroutine(FadeInThenPlayCutscene());
        }
    }

    IEnumerator ShowSkipButtonWithDelay()
    {
        yield return new WaitForSeconds(skipButtonDelay);
        
        // Only show skip button to host (MasterClient)
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (skipButton != null && isHost)
            skipButton.SetActive(true);
    }

    [PunRPC]
    public void RPC_FadeInThenPlayCutscene()
    {
        StartCoroutine(FadeInThenPlayCutscene());
    }

    IEnumerator FadeInThenPlayCutscene()
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = 0f;
        
        // Show skip button after fade in
        StartCoroutine(ShowSkipButtonWithDelay());
        
        StartCoroutine(PlayCutsceneThenSpawn());
    }

    public void OnSkipButtonClicked()
    {
        // Only host can skip (button only visible to host anyway)
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (isHost && photonView != null)
        {
            photonView.RPC("RPC_SkipCutscene", RpcTarget.AllBuffered);
        }
    }

    [PunRPC]
    public void RPC_SkipCutscene()
    {
        cutsceneSkipped = true;
        if (cutsceneDirector != null)
        {
            cutsceneDirector.Stop();
        }
        StopAllCoroutines();
        
        StartCoroutine(PlayCutsceneThenSpawnImmediate());
    }

    IEnumerator FadeOutCutscene()
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = 0f;
        fadeOverlay.gameObject.SetActive(false);
    }

    IEnumerator PlayCutsceneThenSpawn()
    {
        if (cutsceneDirector != null && !cutsceneSkipped)
        {
            cutsceneDirector.Play();
            yield return new WaitForSeconds((float)cutsceneDirector.duration);
        }
        
        // No fade out - just spawn player directly
        // Spawn player and wait for setup to complete
        yield return StartCoroutine(SpawnAndSetupPlayerCoroutine());
        
        if (skipButton != null) skipButton.SetActive(false);
        
        // Small delay before destroying to ensure everything is set up
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    IEnumerator PlayCutsceneThenSpawnImmediate()
    {
        // No fade out when skipping - just spawn player directly
        // Spawn player and wait for setup to complete
        yield return StartCoroutine(SpawnAndSetupPlayerCoroutine());
        
        if (skipButton != null) skipButton.SetActive(false);
        
        // Small delay before destroying to ensure everything is set up
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }
    
    private IEnumerator SpawnAndSetupPlayerCoroutine()
    {
        if (spawnManager != null)
        {
            spawnManager.SpawnPlayerForThisClient();
            // Wait for player to spawn
            yield return StartCoroutine(WaitForPlayerAndSetup());
        }
        else
        {
            Debug.LogError("[CutsceneManager] SpawnManager is not assigned! Cannot spawn player.");
        }
    }

    private void SpawnAndSetupPlayer()
    {
        // This method is kept for backward compatibility but shouldn't be called directly
        // Use SpawnAndSetupPlayerCoroutine() in coroutines instead
        StartCoroutine(SpawnAndSetupPlayerCoroutine());
    }
    
    private IEnumerator WaitForPlayerAndSetup()
    {
        // Wait a frame for the player to spawn
        yield return null;
        
        // Try to find the player, with multiple attempts
        GameObject player = null;
        int maxAttempts = 10;
        int attempts = 0;
        
        while (player == null && attempts < maxAttempts)
        {
            player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                yield return new WaitForSeconds(0.1f);
                attempts++;
            }
        }
        
        if (player != null)
        {
            Debug.Log("[CutsceneManager] Player found, setting up camera.");
            
            // Setup camera
            Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.tag = "MainCamera";
            }
            
            // Setup camera orbit
            var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
            if (cameraOrbit != null)
            {
                Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
                if (cameraPivot != null)
                {
                    cameraOrbit.AssignTargets(player.transform, cameraPivot);
                }
                // Ensure camera rotation is unlocked after cutscene
                cameraOrbit.SetRotationLocked(false);
            }
            
            // Ensure LocalInputLocker has correct state after spawn
            var controller = player.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.SetCanMove(true);
                controller.SetCanControl(true);
            }
            
            // Force gameplay cursor state
            LocalInputLocker.Ensure().ForceGameplayCursor();
            
            // Wait an extra frame to ensure all LateUpdates have completed
            // This prevents LocalInputLocker from re-applying locks after we unlock
            yield return null;
            
            // Double-check camera is unlocked (defensive)
            if (cameraOrbit != null)
            {
                cameraOrbit.SetRotationLocked(false);
            }
        }
        else
        {
            Debug.LogWarning("[CutsceneManager] Player not found after spawn attempt. It may have been spawned elsewhere.");
        }
    }
}
