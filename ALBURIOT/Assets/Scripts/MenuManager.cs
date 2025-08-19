using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("Scene Names")]
    public string dialogueScene = "Dialogue"; // Add dialogue scene
    public string mainGameScene = "MAIN";
    public string optionsScene = "Options";
    
    [Header("Settings")]
    public float sceneTransitionDelay = 0.5f;
    
    public void StartGame()
    {
        Debug.Log("Starting dialogue sequence...");
        // Load dialogue scene first, then it will transition to main game
        Invoke("LoadDialogueScene", sceneTransitionDelay);
    }
    
    public void OpenOptions()
    {
        Debug.Log("Opening options...");
        // You can either load a new scene or show a UI panel
        // For now, just log the action
    }
    
    public void ExitGame()
    {
        Debug.Log("Exiting game...");
        
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
    
    private void LoadDialogueScene()
    {
        SceneManager.LoadScene(dialogueScene);
    }
    
    // Optional: Add fade transition
    public void StartGameWithFade()
    {
        // You can add a fade effect here
        StartGame();
    }
}
