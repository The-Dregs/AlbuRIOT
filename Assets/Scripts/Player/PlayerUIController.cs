using UnityEngine;

public class PlayerUIController : MonoBehaviour
{
    [Header("Assign your health bar root GameObject")]
    public GameObject healthBarRoot;
    [Header("Assign your skill UI root GameObject")]
    public GameObject skillUIRoot;

    private void Awake()
    {
        if (healthBarRoot != null) healthBarRoot.SetActive(false);
        if (skillUIRoot != null) skillUIRoot.SetActive(false);
    }

    // Called by TutorialManager when tutorial allows UI
    public void EnablePlayerUI()
    {
        if (healthBarRoot != null) healthBarRoot.SetActive(true);
        if (skillUIRoot != null) skillUIRoot.SetActive(true);
    }
}
