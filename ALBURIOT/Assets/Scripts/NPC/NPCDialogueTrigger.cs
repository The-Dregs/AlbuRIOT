using UnityEngine;

public class NPCDialogueTrigger : MonoBehaviour
{
    public DialogueData dialogue;
    public GameObject interactPrompt; // Assign your "Press E" UI text here
    private bool playerInRange = false;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            if (interactPrompt != null)
                interactPrompt.SetActive(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (interactPrompt != null)
                interactPrompt.SetActive(false);
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
              FindObjectOfType<NPCDialogueManager>().StartDialogue(dialogue);
            if (interactPrompt != null)
                interactPrompt.SetActive(false);
        }
    }
}
