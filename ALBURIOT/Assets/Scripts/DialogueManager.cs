using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class DialogueManager : MonoBehaviour
{
    [Header("Dialogue UI")]
    public TextMeshProUGUI dialogueText;
    public GameObject dialoguePanel;
    public Button continueButton;
    public Button skipButton;
    
    [Header("Dialogue Content")]
    [TextArea(3, 10)]
    public string[] dialogueLines;
    
    [Header("Settings")]
    public float textSpeed = 0.05f;
    public string prologueScene = "Prologue"; // Change to prologue scene
    public float sceneTransitionDelay = 1f;
    
    private int currentLine = 0;
    private bool isTyping = false;
    private Coroutine typingCoroutine;
    
    void Start()
    {
        // Hide dialogue panel initially
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
            
        // Set up button listeners
        if (continueButton != null)
            continueButton.onClick.AddListener(ContinueDialogue);
            
        if (skipButton != null)
            skipButton.onClick.AddListener(SkipDialogue);
            
        // Start dialogue after a short delay
        Invoke("StartDialogue", 1f);
    }
    
    void StartDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);
            
        if (dialogueLines.Length > 0)
        {
            DisplayNextLine();
        }
    }
    
    void DisplayNextLine()
    {
        if (currentLine < dialogueLines.Length)
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
                
            typingCoroutine = StartCoroutine(TypeText(dialogueLines[currentLine]));
        }
        else
        {
            // Dialogue finished, transition to main game
            EndDialogue();
        }
    }
    
    IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";
        
        foreach (char c in text.ToCharArray())
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(textSpeed);
        }
        
        isTyping = false;
    }
    
    public void ContinueDialogue()
    {
        if (isTyping)
        {
            // If still typing, complete the current line
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = dialogueLines[currentLine];
            isTyping = false;
        }
        else
        {
            // Move to next line
            currentLine++;
            DisplayNextLine();
        }
    }
    
    public void SkipDialogue()
    {
        // Skip directly to main game
        EndDialogue();
    }
    
    void EndDialogue()
    {
        Debug.Log("Dialogue finished, loading main game...");
        Invoke("LoadMainGame", sceneTransitionDelay);
    }
    
    void LoadMainGame()
    {
        SceneManager.LoadScene(prologueScene);
    }
}
