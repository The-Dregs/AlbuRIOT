using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject startPanel;   // Start
    public GameObject choicePanel;  // New / Load
    public GameObject lobbyPanel;   // Invite / Start

    [Header("Scene Names")]
    public string startDialogueScene = "startDIALOGUE";
    public string prologueScene = "PROLOGUE";

    [Header("UI")]
    public Text noSaveText; // optional feedback on Choice panel

    [Header("Settings")]
    public float sceneTransitionDelay = 0.25f;

    void Awake()
    {
        ShowStart();
        if (noSaveText != null) noSaveText.gameObject.SetActive(false);
    }

    // Panel routing
    public void ShowStart()
    {
        if (startPanel != null) startPanel.SetActive(true);
        if (choicePanel != null) choicePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    // HOMESCREEN
    public void OnStartClicked()
    {
        // If a save exists, let the player choose New/Load; otherwise go straight to Lobby
        if (PlayerPrefs.HasKey("save_exists"))
        {
            ShowChoice();
        }
        else
        {
            ShowLobby();
        }
    }

    public void ShowChoice()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (choicePanel != null) choicePanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (noSaveText != null) noSaveText.gameObject.SetActive(false);
    }

    // START CHOICE
    public void OnChoiceNewGame()
    {
        PlayerPrefs.SetInt("save_exists", 1);
        PlayerPrefs.Save();
        ShowLobby();
    }

    public void OnChoiceLoadGame()
    {
        if (PlayerPrefs.HasKey("save_exists"))
        {
            ShowLobby();
        }
        else if (noSaveText != null)
        {
            noSaveText.gameObject.SetActive(true);
            noSaveText.text = "No save found.";
        }
    }

    public void ShowLobby()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (choicePanel != null) choicePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    // LOBBY
    public void OnLobbyStartClicked()
    {
        Invoke(nameof(GoToStartDialogue), sceneTransitionDelay);
    }

    // DIALOGUE → called at the end of dialogue to enter tutorial
    public void OnDialogueComplete()
    {
        Invoke(nameof(GoToPrologue), sceneTransitionDelay);
    }

    // Scene loaders
    void GoToStartDialogue() { SceneManager.LoadScene(startDialogueScene); }
    void GoToPrologue() { SceneManager.LoadScene(prologueScene); }

    // Navigation helpers
    public void OnBackFromChoice() { ShowStart(); }
    public void OnBackFromLobby() { ShowChoice(); }

    public void OpenOptions() { /* optional: toggle options panel */ }

    public void ExitGame()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
}
