using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class DialogueManager : MonoBehaviourPunCallbacks
{
    // parameterless version for Unity Invoke
    public void StartDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        if (dialogueLines.Length > 0)
        {
            currentLine = 0;
            DisplayNextLine();
        }
    }
    [Header("Dialogue UI")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI hostControlText; // assign in inspector
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

        // Set up button listeners for host only
        if (PhotonNetwork.IsMasterClient)
        {
            if (continueButton != null)
                continueButton.onClick.AddListener(OnHostContinueClicked);
            if (skipButton != null)
                skipButton.onClick.AddListener(OnHostSkipClicked);
            if (hostControlText != null)
                hostControlText.gameObject.SetActive(false);
        }
        else
        {
            if (continueButton != null)
                continueButton.interactable = false;
            if (skipButton != null)
                skipButton.interactable = false;
            if (hostControlText != null)
            {
                hostControlText.gameObject.SetActive(true);
                hostControlText.text = "Host is controlling the dialogue.";
            }
        }

        // Start dialogue after a short delay
        Invoke("StartDialogue", 1f);
    }

    public void StartDialogue(DialogueData dialogue)
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

    // called by host only
    public void OnHostContinueClicked()
    {
        photonView.RPC("RPC_ContinueDialogue", RpcTarget.All);
    }

    [PunRPC]
    public void RPC_ContinueDialogue()
    {
        ContinueDialogueInternal();
    }

    private void ContinueDialogueInternal()
    {
        Debug.Log($"ContinueDialogueInternal called. isTyping={isTyping}, currentLine={currentLine}");
        if (isTyping)
        {
            // If still typing, complete the current line and do NOT advance
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = dialogueLines[currentLine];
            isTyping = false;
            // Do not advance yet, wait for next click
        }
        else
        {
            // Move to next line
            currentLine++;
            DisplayNextLine();
        }
    }

    // called by host only
    public void OnHostSkipClicked()
    {
        photonView.RPC("RPC_SkipDialogue", RpcTarget.All);
    }

    [PunRPC]
    public void RPC_SkipDialogue()
    {
        SkipDialogueInternal();
    }

    private void SkipDialogueInternal()
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
        Photon.Pun.PhotonNetwork.LoadLevel("TUTORIAL");
    }
}
