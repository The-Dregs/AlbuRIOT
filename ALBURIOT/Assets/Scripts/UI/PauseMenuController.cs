using UnityEngine;
using TMPro;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI Panel")]
    public GameObject pausePanel;
    [Tooltip("Optional: sub-panel to show Options while paused")]
    public GameObject optionsPanel;

    [Header("Local Components")]
    public ThirdPersonController playerController;
    public PlayerCombat playerCombat;
    public ThirdPersonCameraOrbit cameraOrbit;

    private bool isOpen = false;
    private int _inputLockToken = 0;
    // We do not change Time.timeScale in multiplayer; gameplay keeps running.
    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu"; // used by LeaveGame
    [Header("Scene Shortcuts")]
    public string testingSceneName = "TESTING";
    public string firstMapSceneName = "FIRSTMAP";

    void Start()
    {
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
    }

    void Update()
    {
        // Only local player should control pause UI; if components exist with a PhotonView, require IsMine
        if (playerController != null)
        {
            var pv = playerController.GetComponentInParent<Photon.Pun.PhotonView>();
            if (pv != null && !pv.IsMine) return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!isOpen)
            {
                if (!LocalUIManager.Ensure().TryOpen("PauseMenu")) return;
                isOpen = true;
                if (pausePanel != null) pausePanel.SetActive(true);
                // lock combat and camera, but allow movement; cursor unlocked
                if (_inputLockToken == 0)
                    _inputLockToken = LocalInputLocker.Ensure().Acquire("PauseMenu", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
            }
            else
            {
                isOpen = false;
                if (pausePanel != null) pausePanel.SetActive(false);
                LocalUIManager.Instance.Close("PauseMenu");
                if (_inputLockToken != 0)
                {
                    LocalInputLocker.Ensure().Release(_inputLockToken);
                    _inputLockToken = 0;
                }
                LocalInputLocker.Ensure().ForceGameplayCursor();
                if (optionsPanel != null) optionsPanel.SetActive(false);
            }
        }
    }

    // UI Buttons
    public void OnResumeButton()
    {
        if (!isOpen) return;
        isOpen = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.Close("PauseMenu");
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        LocalInputLocker.Ensure().ForceGameplayCursor();
    }

    public void OnOptionsButton()
    {
        if (!isOpen) return;
        if (optionsPanel != null)
        {
            // Toggle an options subpanel while staying paused
            optionsPanel.SetActive(!optionsPanel.activeSelf);
        }
    }

    public void OnLeaveGameButton()
    {
        // Ensure we release locks before leaving (we do not change timescale in MP)
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        // switch into menu mode so HOMESCREEN shows cursor
        LocalInputLocker.Ensure().EnterMenuMode();
        // Load main menu via Photon so all players use the same loader
        Photon.Pun.PhotonNetwork.LoadLevel(mainMenuSceneName);
    }

    public void OnLoadTestingButton()
    {
        // transition to gameplay scene; release UI and locks, let gameplay re-lock cursor on spawn
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        Photon.Pun.PhotonNetwork.LoadLevel(testingSceneName);
    }

    public void OnLoadFirstMapButton()
    {
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        Photon.Pun.PhotonNetwork.LoadLevel(firstMapSceneName);
    }
}
