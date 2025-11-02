using UnityEngine;
using TMPro;
using Photon.Pun;
using System.Collections.Generic;

public class TutorialManager : MonoBehaviourPunCallbacks
{
    [Header("Tutorial Data")]
    [Tooltip("Tutorial data definitions - each entry defines a complete tutorial step")]
    public TutorialData[] tutorialSteps;

    [Header("Legacy Support (for backwards compatibility)")]
    [Tooltip("Old dialogue panels array - will be migrated to TutorialData")]
    public GameObject[] dialoguePanels;
    [Tooltip("Old continue prompts array")]
    public GameObject[] continuePrompts;
    public float continueDelay = 1.2f;

    [Header("Global UI (optional - for legacy quest UI)")]
    public GameObject questUI;

    [Header("Player Settings")]
    public string playerTag = "Player";

    // Per-player dialogue tracking: PhotonView ViewID -> active tutorial data
    private Dictionary<int, TutorialData> activePlayerTutorials = new Dictionary<int, TutorialData>();
    private Dictionary<int, Coroutine> activeContinueCoroutines = new Dictionary<int, Coroutine>();
    private Dictionary<int, bool> waitingForContinue = new Dictionary<int, bool>();
    private Dictionary<int, GameObject> activeDialoguePanels = new Dictionary<int, GameObject>(); // Track panel GameObjects per player

    private void Start()
    {
        // Hide all legacy panels if they exist
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

        // Hide global quest UI
        if (questUI != null) questUI.SetActive(false);
    }

    /// <summary>
    /// Show tutorial for a specific player (only that player will see it)
    /// </summary>
    public void ShowTutorialForPlayer(GameObject player, int tutorialIndex)
    {
        if (player == null) return;

        var pv = player.GetComponent<PhotonView>();
        // Use ViewID as unique identifier (works offline too, just uses instance ID)
        int playerID = pv != null ? pv.ViewID : player.GetInstanceID();

        // Only show for local player in multiplayer
        if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !pv.IsMine)
            return;

        // Use TutorialData if available, fall back to legacy
        TutorialData tutorialData = null;
        GameObject dialoguePanel = null;
        GameObject continuePrompt = null;
        float delay = continueDelay;

        if (tutorialSteps != null && tutorialIndex >= 0 && tutorialIndex < tutorialSteps.Length)
        {
            tutorialData = tutorialSteps[tutorialIndex];
            if (tutorialData != null)
            {
                dialoguePanel = tutorialData.dialoguePanel;
                continuePrompt = tutorialData.continuePrompt;
                delay = tutorialData.continueDelay;
            }
        }
        else if (dialoguePanels != null && tutorialIndex >= 0 && tutorialIndex < dialoguePanels.Length)
        {
            // Legacy support
            dialoguePanel = dialoguePanels[tutorialIndex];
            continuePrompt = (continuePrompts != null && tutorialIndex < continuePrompts.Length) 
                ? continuePrompts[tutorialIndex] : null;
        }

        if (dialoguePanel == null) return;

        // Hide any existing dialogue for this player
        HideTutorialForPlayer(player);

        // Show dialogue panel
        dialoguePanel.SetActive(true);
        if (continuePrompt != null) continuePrompt.SetActive(false);

        // Track active tutorial
        activePlayerTutorials[playerID] = tutorialData;
        activeDialoguePanels[playerID] = dialoguePanel;
        waitingForContinue[playerID] = false;

        // Start continue prompt coroutine
        if (activeContinueCoroutines.ContainsKey(playerID) && activeContinueCoroutines[playerID] != null)
        {
            StopCoroutine(activeContinueCoroutines[playerID]);
        }
        activeContinueCoroutines[playerID] = StartCoroutine(ShowContinuePromptAfterDelay(playerID, continuePrompt, delay));

        // Apply UI actions
        if (tutorialData != null)
        {
            ApplyTutorialUIActions(player, tutorialData);
        }
        else
        {
            // Legacy UI actions
            ApplyLegacyUIActions(tutorialIndex);
        }
    }

    private void ApplyTutorialUIActions(GameObject player, TutorialData data)
    {
        var uiController = player.GetComponent<PlayerUIController>();
        
        if (data.enableHealthBar && uiController != null && uiController.healthBarRoot != null)
        {
            uiController.healthBarRoot.SetActive(true);
        }

        if (data.enableSkillUI && uiController != null && uiController.skillUIRoot != null)
        {
            uiController.skillUIRoot.SetActive(true);
        }

        if (data.showQuestUI)
        {
            GameObject targetQuestUI = data.questUI != null ? data.questUI : questUI;
            if (targetQuestUI != null) targetQuestUI.SetActive(true);
        }
    }

    private void ApplyLegacyUIActions(int triggerIndex)
    {
        // Legacy health bar logic (enable for all players - but should be per-player)
        var players = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var p in players)
        {
            var ui = p.GetComponent<PlayerUIController>();
            if (ui != null && ui.healthBarRoot != null) ui.healthBarRoot.SetActive(true);
        }

        // Legacy quest UI (global)
        if (questUI != null) questUI.SetActive(true);
    }

    private System.Collections.IEnumerator ShowContinuePromptAfterDelay(int playerID, GameObject prompt, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (prompt != null && waitingForContinue.ContainsKey(playerID))
        {
            prompt.SetActive(true);
            waitingForContinue[playerID] = true;
        }
    }

    /// <summary>
    /// Hide tutorial for a specific player
    /// </summary>
    public void HideTutorialForPlayer(GameObject player)
    {
        if (player == null) return;

        var pv = player.GetComponent<PhotonView>();
        int playerID = pv != null ? pv.ViewID : player.GetInstanceID();

        // Only hide for local player in multiplayer
        if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !pv.IsMine)
            return;

        CloseDialogueForPlayer(playerID);
    }

    private void Update()
    {
        // Check for continue input for each active player (only local player can continue)
        var keysToRemove = new List<int>();
        
        foreach (var kvp in waitingForContinue)
        {
            int playerID = kvp.Key;
            bool isWaiting = kvp.Value;

            if (isWaiting)
            {
                // Get the panel for this player
                GameObject panel = null;
                if (activeDialoguePanels.ContainsKey(playerID))
                {
                    panel = activeDialoguePanels[playerID];
                }
                else if (dialoguePanels != null && playerID >= 0 && playerID < dialoguePanels.Length)
                {
                    // Legacy fallback
                    panel = dialoguePanels[playerID];
                }

                if (panel != null && panel.activeSelf && Input.GetMouseButtonDown(0))
                {
                    CloseDialogueForPlayer(playerID);
                    keysToRemove.Add(playerID);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            waitingForContinue.Remove(key);
        }
    }

    private void CloseDialogueForPlayer(int playerID)
    {
        // Hide tutorial data panel
        if (activePlayerTutorials.ContainsKey(playerID))
        {
            var data = activePlayerTutorials[playerID];
            if (data != null)
            {
                if (data.dialoguePanel != null) data.dialoguePanel.SetActive(false);
                if (data.continuePrompt != null) data.continuePrompt.SetActive(false);
            }
            activePlayerTutorials.Remove(playerID);
        }

        // Hide tracked panel
        if (activeDialoguePanels.ContainsKey(playerID))
        {
            var panel = activeDialoguePanels[playerID];
            if (panel != null) panel.SetActive(false);
            activeDialoguePanels.Remove(playerID);
        }

        // Legacy support
        if (dialoguePanels != null && playerID >= 0 && playerID < dialoguePanels.Length)
        {
            var panel = dialoguePanels[playerID];
            if (panel != null) panel.SetActive(false);
        }
        if (continuePrompts != null && playerID >= 0 && playerID < continuePrompts.Length)
        {
            var prompt = continuePrompts[playerID];
            if (prompt != null) prompt.SetActive(false);
        }

        // Stop coroutine
        if (activeContinueCoroutines.ContainsKey(playerID) && activeContinueCoroutines[playerID] != null)
        {
            StopCoroutine(activeContinueCoroutines[playerID]);
            activeContinueCoroutines.Remove(playerID);
        }

        waitingForContinue.Remove(playerID);
    }

    // Legacy method for backwards compatibility
    public void OnTutorialTrigger(int triggerIndex)
    {
        // Find local player
        var localPlayer = FindLocalPlayer();
        if (localPlayer != null)
        {
            ShowTutorialForPlayer(localPlayer, triggerIndex);
        }
    }

    // Legacy method
    public void ShowTutorialPanel(int panelIndex)
    {
        var localPlayer = FindLocalPlayer();
        if (localPlayer != null)
        {
            ShowTutorialForPlayer(localPlayer, panelIndex);
        }
    }

    private GameObject FindLocalPlayer()
    {
        var players = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var player in players)
        {
            var pv = player.GetComponent<PhotonView>();
            if (pv == null || !PhotonNetwork.IsConnected || pv.IsMine)
            {
                return player;
            }
        }
        return players.Length > 0 ? players[0] : null;
    }
}