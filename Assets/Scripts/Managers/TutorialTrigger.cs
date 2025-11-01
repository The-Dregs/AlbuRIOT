using UnityEngine;

public class TutorialTrigger : MonoBehaviour
{
    [Header("Optional: Activate this GameObject on trigger")]
    public GameObject objectToActivate;

    public int triggerIndex = 0; // Set this in Inspector for each trigger
    public TutorialManager tutorialManager; // Assign in Inspector or find at runtime

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var tutorialManager = Object.FindFirstObjectByType<TutorialManager>();
            if (tutorialManager != null)
            {
                tutorialManager.ShowTutorialPanel(triggerIndex); // Call locally only, NOT via RPC
            }

            // activate object for all players (multiplayer safe)
            if (objectToActivate != null)
            {
                objectToActivate.SetActive(true);
            }

            Destroy(gameObject);
        }
    }
}
