using UnityEngine;
using TMPro;
using Photon.Pun;


public class TutorialManager : MonoBehaviourPunCallbacks
{
    [Header("Quest UI")]
    public GameObject questUI; // assign your quest UI GameObject (top right) in Inspector
    public int questUITriggerIndex = 1; // set this to the trigger index that should show the quest UI
    [Header("Health Bar Trigger")]
    public int healthBarTriggerIndex = 0; // Set this in Inspector to match the trigger index that should enable health bar

    [Header("Tutorial Dialogue Panels")]
    public GameObject[] dialoguePanels; // assign all your tutorial panels here
    public GameObject[] continuePrompts; // assign matching continue prompts for each panel
    public float continueDelay = 1.2f;
    private bool waitingForContinue = false;
    private int activePanelIndex = -1;

    [Header("Tutorial Trigger Areas")]
    public Collider[] tutorialTriggers;

    [Header("Player Prefab Tag")]
    public string playerTag = "Player";

    private int tutorialStep = 0;

    private void Start()
    {
        // hide quest UI at start
        if (questUI != null) questUI.SetActive(false);
        // hide all dialogue panels at start
        if (dialoguePanels != null)
        {
            foreach (var panel in dialoguePanels)
                if (panel != null) panel.SetActive(false);
        }
        // hide all continue prompts at start
        if (continuePrompts != null)
        {
            foreach (var prompt in continuePrompts)
                if (prompt != null) prompt.SetActive(false);
        }
        DisableAllPlayerUIs();
    }

    private void DisableAllPlayerUIs()
    {
        var players = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var p in players)
        {
            var ui = p.GetComponent<PlayerUIController>();
            if (ui != null) ui.healthBarRoot?.SetActive(false);
            if (ui != null) ui.skillUIRoot?.SetActive(false);
        }
    }

    // Call this when a tutorial trigger is hit to show dialogue only

    // Call this to show a specific tutorial dialogue panel by index
    public void ShowTutorialPanel(int panelIndex)
    {
        ApplyShowTutorialPanel(panelIndex);
        // Sync with other players (buffered for persistence)
        photonView.RPC("RPC_ShowTutorialPanel", RpcTarget.AllBuffered, panelIndex);
    }

    [PunRPC]
    public void RPC_ShowTutorialPanel(int panelIndex)
    {
        ApplyShowTutorialPanel(panelIndex);
    }
    
    private void ApplyShowTutorialPanel(int panelIndex)
    {
        HideAllDialoguePanels();
        if (dialoguePanels != null && panelIndex >= 0 && panelIndex < dialoguePanels.Length)
        {
            var panel = dialoguePanels[panelIndex];
            var prompt = (continuePrompts != null && panelIndex < continuePrompts.Length) ? continuePrompts[panelIndex] : null;
            if (panel != null) panel.SetActive(true);
            if (prompt != null) prompt.SetActive(false);
            waitingForContinue = false;
            StartCoroutine(ShowContinuePromptAfterDelay(panelIndex));
        }
    }
    // Example: Call this from your trigger logic
    public void OnTutorialTrigger(int triggerIndex)
    {
        ShowTutorialPanel(triggerIndex);
        if (triggerIndex == healthBarTriggerIndex)
        {
            EnableHealthBarUIForAllPlayers();
        }
        if (triggerIndex == questUITriggerIndex && questUI != null)
        {
            questUI.SetActive(true);
        }
    }

    private void HideAllDialoguePanels()
    {
        if (dialoguePanels != null)
        {
            foreach (var panel in dialoguePanels)
                if (panel != null) panel.SetActive(false);
        }
        if (continuePrompts != null)
        {
            foreach (var prompt in continuePrompts)
                if (prompt != null) prompt.SetActive(false);
        }
        waitingForContinue = false;
        activePanelIndex = -1;
    }


    private System.Collections.IEnumerator ShowContinuePromptAfterDelay(int panelIndex)
    {
        activePanelIndex = panelIndex;
        yield return new WaitForSeconds(continueDelay);
        if (continuePrompts != null && panelIndex >= 0 && panelIndex < continuePrompts.Length)
        {
            var prompt = continuePrompts[panelIndex];
            if (prompt != null) prompt.SetActive(true);
        }
        waitingForContinue = true;
    }


    private void Update()
    {
        if (waitingForContinue && activePanelIndex >= 0 && dialoguePanels != null && activePanelIndex < dialoguePanels.Length)
        {
            var panel = dialoguePanels[activePanelIndex];
            if (panel != null && panel.activeSelf && Input.GetMouseButtonDown(0)) // left click only
            {
                CloseDialoguePanel(activePanelIndex);
            }
        }
    }

    private void CloseDialoguePanel(int panelIndex)
    {
        if (dialoguePanels != null && panelIndex >= 0 && panelIndex < dialoguePanels.Length)
        {
            var panel = dialoguePanels[panelIndex];
            if (panel != null) panel.SetActive(false);
        }
        if (continuePrompts != null && panelIndex >= 0 && panelIndex < continuePrompts.Length)
        {
            var prompt = continuePrompts[panelIndex];
            if (prompt != null) prompt.SetActive(false);
        }
        waitingForContinue = false;
        activePanelIndex = -1;
    }


    // Call this to enable health bar UI separately
    public void EnableHealthBarUIForAllPlayers()
    {
        var players = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var p in players)
        {
            var ui = p.GetComponent<PlayerUIController>();
            if (ui != null && ui.healthBarRoot != null) ui.healthBarRoot.SetActive(true);
        }
    }
}
