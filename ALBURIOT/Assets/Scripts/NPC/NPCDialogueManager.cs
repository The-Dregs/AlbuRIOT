using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NPCDialogueManager : MonoBehaviour
{
    public float lookDuration = 0.5f; // seconds for smooth look
    public Transform playerTransform;
    public Transform cameraTransform;
    public Transform npcTransform;
    public GameObject dialoguePanel;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI dialogueText;
    public Button nextButton;

    private DialogueData currentDialogue;
    private int currentLine = 0;

    void Start()
    {
        dialoguePanel.SetActive(false);
        nextButton.onClick.AddListener(NextLine);
    }

    public void StartDialogue(DialogueData dialogue)
    {
        StartCoroutine(SmoothLookAndPause(dialogue));
    }

    private System.Collections.IEnumerator SmoothLookAndPause(DialogueData dialogue)
    {
        float elapsed = 0f;
        Quaternion startPlayerRot = playerTransform != null ? playerTransform.rotation : Quaternion.identity;
        Quaternion targetPlayerRot = startPlayerRot;
        if (playerTransform != null && npcTransform != null)
        {
            Vector3 direction = npcTransform.position - playerTransform.position;
            direction.y = 0f;
            if (direction != Vector3.zero)
                targetPlayerRot = Quaternion.LookRotation(direction, Vector3.up);
        }
        Quaternion startCamRot = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;
        Quaternion targetCamRot = startCamRot;
        if (cameraTransform != null && npcTransform != null)
        {
            Vector3 camDir = npcTransform.position - cameraTransform.position;
            camDir.y = 0f;
            if (camDir != Vector3.zero)
                targetCamRot = Quaternion.LookRotation(camDir, Vector3.up);
        }
        while (elapsed < lookDuration)
        {
            float t = elapsed / lookDuration;
            if (playerTransform != null)
                playerTransform.rotation = Quaternion.Slerp(startPlayerRot, targetPlayerRot, t);
            if (cameraTransform != null)
                cameraTransform.rotation = Quaternion.Slerp(startCamRot, targetCamRot, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (playerTransform != null)
            playerTransform.rotation = targetPlayerRot;
        if (cameraTransform != null)
            cameraTransform.rotation = targetCamRot;
        currentDialogue = dialogue;
        currentLine = 0;
        dialoguePanel.SetActive(true);
        Time.timeScale = 0f; // Pause the game
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowLine();
    }

    void ShowLine()
    {
        if (currentDialogue != null && currentLine < currentDialogue.lines.Length)
        {
            speakerText.text = currentDialogue.lines[currentLine].speaker;
            dialogueText.text = currentDialogue.lines[currentLine].text;
        }
        else
        {
            EndDialogue();
        }
    }

    public void NextLine()
    {
        currentLine++;
        ShowLine();
    }

    void EndDialogue()
    {
        dialoguePanel.SetActive(false);
        currentDialogue = null;
        Time.timeScale = 1f; // Resume the game
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
