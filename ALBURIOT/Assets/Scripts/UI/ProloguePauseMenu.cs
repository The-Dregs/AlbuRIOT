using UnityEngine;
using UnityEngine.SceneManagement;
// Explicitly reference PlayerSpawnManager static class

public class ProloguePauseMenu : MonoBehaviour
{
    public GameObject pauseMenuUI;
    public string mainMenuSceneName = "MainMenu";
    public string mainSceneName = "MAIN";
    public Transform player;
    public Vector3 mainSceneSpawnPosition = new Vector3(0, 2, 0); // Set as needed

    private bool isPaused = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (pauseMenuUI != null)
            {
                bool open = !pauseMenuUI.activeSelf;
                pauseMenuUI.SetActive(open);
                isPaused = open;
            }
        }
    }

    public void Resume()
    {
        pauseMenuUI.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
    }

    void Pause()
    {
        pauseMenuUI.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;
    }

    public void LoadMainMenu()
    {
    Time.timeScale = 1f;
    Photon.Pun.PhotonNetwork.LoadLevel(mainMenuSceneName);
    }

    public void TeleportToMainScene()
    {
    // Store the desired spawn position for the MAIN scene
    PlayerSpawnManager.nextSpawnPosition = mainSceneSpawnPosition;
    Time.timeScale = 1f;
    Photon.Pun.PhotonNetwork.LoadLevel(mainSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
