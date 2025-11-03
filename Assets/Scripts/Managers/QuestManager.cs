using UnityEngine;
using TMPro;
using System;
using System.Collections.Generic;
using Photon.Pun;
using AlbuRIOT.Abilities;

public enum ObjectiveType { Kill, Collect, TalkTo, ReachArea, FindArea, ShrineOffering, PowerSteal, Custom }

[System.Serializable]
public class QuestObjective
{
    [Header("Objective Details")]
    public string objectiveName;
    public string description;
    public ObjectiveType objectiveType = ObjectiveType.Custom;
    
    [Header("Target Information")]
    [Tooltip("for Kill: enemy name; for Collect: item name; for TalkTo/ReachArea/FindArea: id string; for ShrineOffering: shrine id")] 
    public string targetId;
    [Tooltip("how many actions required to complete the objective")] 
    public int requiredCount = 1;
    [Tooltip("runtime counter; do not edit at runtime")] 
    public int currentCount = 0;
    
    [Header("Rewards")]
    public ItemData rewardItem;
    public int rewardQuantity = 1;
    public AbilityBase rewardAbility;
    
    [Header("Shrine Specific")]
    [Tooltip("Items required for shrine offering")] 
    public ItemData[] requiredOfferings;
    [Tooltip("Quantity of each required offering")] 
    public int[] offeringQuantities;
    
    [Header("Collect Specific (Multi-Item)")]
    [Tooltip("For Collect objectives: multiple item IDs to collect")] 
    public string[] collectItemIds;
    [Tooltip("Required quantity for each collectItemId (must match length)")] 
    public int[] collectQuantities;
    [Tooltip("Runtime progress per item (do not edit at runtime)")] 
    public int[] collectProgress; // tracks currentCount per item
    
    public bool IsCompleted => currentCount >= requiredCount;
    
    public bool IsMultiItemCollect()
    {
        return objectiveType == ObjectiveType.Collect && collectItemIds != null && collectItemIds.Length > 1;
    }
    
    public bool IsMultiItemCollectComplete()
    {
        if (!IsMultiItemCollect()) return false;
        if (collectProgress == null || collectProgress.Length != collectQuantities.Length) return false;
        for (int i = 0; i < collectQuantities.Length; i++)
        {
            if (collectProgress[i] < collectQuantities[i]) return false;
        }
        return true;
    }
}

[System.Serializable]
public class Quest
{
    [Header("Quest Information")]
    public string questName;
    public string description;
    public bool isCompleted;
    public int questID;
    
    [Header("Objectives")]
    public QuestObjective[] objectives;
    public int currentObjectiveIndex = 0;
    
    [Header("Rewards")]
    public ItemData[] rewardItems;
    public int[] rewardQuantities;
    public AbilityBase[] rewardAbilities;
    
    [Header("Quest Flow")]
    public bool requiresAllObjectives = true; // if false, any objective completion completes quest
    public bool autoAdvanceObjectives = true; // if true, automatically advance to next objective
    
    // Legacy support
    public int objectiveID; // legacy id, not used by new system
    public ObjectiveType objectiveType = ObjectiveType.Custom;
    public string targetId;
    public int requiredCount = 1;
    public int currentCount = 0;
    
    public QuestObjective GetCurrentObjective()
    {
        if (objectives == null || objectives.Length == 0) return null;
        if (currentObjectiveIndex < 0 || currentObjectiveIndex >= objectives.Length) return null;
        return objectives[currentObjectiveIndex];
    }
    
    public bool IsAllObjectivesCompleted()
    {
        if (objectives == null || objectives.Length == 0) return true;
        
        if (requiresAllObjectives)
        {
            foreach (var obj in objectives)
            {
                bool complete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                if (!complete) return false;
            }
            return true;
        }
        else
        {
            foreach (var obj in objectives)
            {
                bool complete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                if (complete) return true;
            }
            return false;
        }
    }
}

public class QuestManager : MonoBehaviourPun
{
    [Header("Quest Configuration")]
    public Quest[] quests;
    public int currentQuestIndex = 0;
    public TextMeshProUGUI questText;
    [Header("Quest HUD UI")]
    public TextMeshProUGUI questTitleText; // assign for top HUD
    public TextMeshProUGUI questDescriptionText; // assign for top HUD
    [Header("Quest UI Integration")]
    public GameObject disableOnQuestUIOpen; // assign GameObject to disable when quest UI is open
    
    [Header("Shrine Integration")]
    public ShrineManager shrineManager;
    
    [Header("Inventory Integration")]
    public Inventory playerInventory;
    

    // quest events for ui or other systems
    public event Action<Quest> OnQuestStarted;
    public event Action<Quest> OnQuestUpdated;
    public event Action<Quest> OnQuestCompleted;
    public event Action<QuestObjective> OnObjectiveCompleted;
    public event Action<QuestObjective> OnObjectiveUpdated;

    void Awake()
    {
        // ensure index in bounds and ui reflects initial state
        if (quests != null && quests.Length > 0)
        {
            currentQuestIndex = Mathf.Clamp(currentQuestIndex, 0, quests.Length - 1);
            UpdateQuestUI();
        }
        
        // Auto-find components if not assigned
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
    }
    
    private void EnsurePlayerInventory()
    {
        if (playerInventory == null)
            playerInventory = Inventory.FindLocalInventory();
    }

    public void StartQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        
        ApplyStartQuest(index);
        
        // Host may still trigger quest start for all, but progress is per-player unless group ReachArea.
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_StartQuest", RpcTarget.AllBuffered, index);
        }
    }

    private void ApplyStartQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;

        currentQuestIndex = index;
        Quest quest = quests[index];
        quest.isCompleted = false;
        
        // Reset all objectives
        if (quest.objectives != null)
        {
            foreach (var objective in quest.objectives)
            {
                objective.currentCount = 0;
            }
            // Set initial objective to index 1 for playtest/demo
            quest.currentObjectiveIndex = Mathf.Clamp(1, 0, quest.objectives.Length - 1);
        }
        
        // Legacy support
        quest.currentCount = 0;
        
        Debug.Log($"Quest started: {quest.questName}");
        UpdateQuestUI();
        OnQuestStarted?.Invoke(quest);
    }
    
    [PunRPC]
    public void RPC_StartQuest(int index)
    {
        ApplyStartQuest(index);
    }

    public void CompleteQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        ApplyCompleteQuest(index);

        // Sync with other players (buffered)
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_CompleteQuest", RpcTarget.AllBuffered, index);
        }
    }
    
    private void ApplyCompleteQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        Quest quest = quests[index];
        if (quest.isCompleted) return;

        quest.isCompleted = true;
        Debug.Log($"Quest completed: {quest.questName}");

        // Give rewards
        GiveQuestRewards(quest);

        UpdateQuestUI();
        OnQuestCompleted?.Invoke(quest);

        // Auto-start next quest
        if (index + 1 < quests.Length)
            StartQuest(index + 1);
    }

    [PunRPC]
    public void RPC_CompleteQuest(int index)
    {
        ApplyCompleteQuest(index);
    }
    
    private void GiveQuestRewards(Quest quest)
    {
        EnsurePlayerInventory();
        
        // Give item rewards
        if (quest.rewardItems != null && quest.rewardQuantities != null && playerInventory != null)
        {
            for (int i = 0; i < quest.rewardItems.Length && i < quest.rewardQuantities.Length; i++)
            {
                if (quest.rewardItems[i] != null)
                {
                    playerInventory.AddItem(quest.rewardItems[i], quest.rewardQuantities[i]);
                    Debug.Log($"Quest reward: {quest.rewardQuantities[i]}x {quest.rewardItems[i].itemName}");
                }
            }
        }
        
        // Give ability rewards
        // ability rewards now handled by PowerStealManager/PlayerSkillSlots system
    }

    public Quest GetCurrentQuest()
    {
        if (quests == null || quests.Length == 0) return null;
        if (currentQuestIndex < 0 || currentQuestIndex >= quests.Length) return null;
        return quests[currentQuestIndex];
    }
    
    public Quest GetQuestByID(int questID)
    {
        if (quests == null || quests.Length == 0) return null;
        foreach (var quest in quests)
        {
            if (quest != null && quest.questID == questID)
                return quest;
        }
        return null;
    }
    
    public QuestObjective GetCurrentObjective()
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null) return null;
        return currentQuest.GetCurrentObjective();
    }
    
    public void UpdateObjectiveProgress(ObjectiveType type, string targetId, int amount = 1)
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        bool isGroupArea = (type == ObjectiveType.ReachArea || type == ObjectiveType.FindArea) && QuestAreaTrigger_GroupRequiresAll();
        // Only master/client relays group area; other objectives are local-only.
        if (currentQuest.objectives != null && currentQuest.objectives.Length > 0)
        {
            bool objectiveUpdated = false;
            
            for (int idx = 0; idx < currentQuest.objectives.Length; idx++)
            {
                var objective = currentQuest.objectives[idx];
                if (objective.objectiveType != type) continue;
                
                bool matched = false;
                // Handle multi-item Collect
                if (type == ObjectiveType.Collect && objective.IsMultiItemCollect())
                {
                    if (objective.collectItemIds != null && objective.collectProgress != null)
                    {
                        for (int i = 0; i < objective.collectItemIds.Length; i++)
                        {
                            if (string.Equals(objective.collectItemIds[i], targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                int max = objective.collectQuantities != null && i < objective.collectQuantities.Length 
                                    ? objective.collectQuantities[i] : int.MaxValue;
                                objective.collectProgress[i] = Mathf.Clamp(objective.collectProgress[i] + amount, 0, max);
                                matched = true;
                                objectiveUpdated = true;
                                // Update total currentCount (sum of progress)
                                objective.currentCount = 0;
                                foreach (int p in objective.collectProgress) objective.currentCount += p;
                                Debug.Log($"Objective progress ({type}): {targetId} -> {objective.collectProgress[i]}/{max} (total: {objective.currentCount}/{objective.requiredCount})");
                                OnObjectiveUpdated?.Invoke(objective);
                                break;
                            }
                        }
                    }
                }
                // Single-item Collect or other types (legacy)
                else if (string.IsNullOrEmpty(objective.targetId) || string.Equals(objective.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    objective.currentCount = Mathf.Clamp(objective.currentCount + amount, 0, objective.requiredCount);
                    matched = true;
                    objectiveUpdated = true;
                    Debug.Log($"Objective progress ({type}): {targetId} -> {objective.currentCount}/{objective.requiredCount}");
                    OnObjectiveUpdated?.Invoke(objective);
                }
                
                // Check completion (use multi-item check if applicable)
                bool isComplete = objective.IsMultiItemCollect() ? objective.IsMultiItemCollectComplete() : objective.IsCompleted;
                if (matched && isComplete)
                {
                    Debug.Log($"Objective completed: {objective.objectiveName}");
                    OnObjectiveCompleted?.Invoke(objective);
                    
                    // Give objective rewards
                    EnsurePlayerInventory();
                    if (objective.rewardItem != null && playerInventory != null)
                    {
                        playerInventory.AddItem(objective.rewardItem, objective.rewardQuantity);
                    }
                    // ability rewards now handled by PowerStealManager/PlayerSkillSlots system

                    // Auto-advance to the next incomplete objective if configured
                    if (currentQuest.autoAdvanceObjectives)
                    {
                        // advance to next index (or stay if none left)
                        int nextIndex = idx;
                        for (int j = idx + 1; j < currentQuest.objectives.Length; j++)
                        {
                            if (!currentQuest.objectives[j].IsCompleted) { nextIndex = j; break; }
                        }
                        // if all after are completed, keep current index at the last completed one so UI shows completion until quest completes
                        currentQuest.currentObjectiveIndex = Mathf.Clamp(nextIndex, 0, currentQuest.objectives.Length - 1);
                    }
                }
            }
            
            if (objectiveUpdated)
            {
                OnQuestUpdated?.Invoke(currentQuest);
                
                // Check if quest should be completed
                if (currentQuest.IsAllObjectivesCompleted())
                {
                    CompleteQuest(currentQuestIndex);
                }
                else
                {
                    UpdateQuestUI();
                }
                
                // DON'T SYNC unless group ReachArea
                if (isGroupArea && PhotonNetwork.IsMasterClient)
                {
                    photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.AllBuffered, (int)type, targetId, amount);
                }
            }
        }
        else
        {
            // Legacy single objective system
            if (currentQuest.objectiveType == type && 
                (string.IsNullOrEmpty(currentQuest.targetId) || string.Equals(currentQuest.targetId, targetId, StringComparison.OrdinalIgnoreCase)))
            {
                currentQuest.currentCount = Mathf.Clamp(currentQuest.currentCount + amount, 0, currentQuest.requiredCount);
                Debug.Log($"Quest progress ({type}): {targetId} -> {currentQuest.currentCount}/{currentQuest.requiredCount}");
                OnQuestUpdated?.Invoke(currentQuest);
                
                if (currentQuest.currentCount >= currentQuest.requiredCount)
                {
                    CompleteQuest(currentQuestIndex);
                }
                else
                {
                    UpdateQuestUI();
                }
                
                // Sync with other players (buffered)
                if (PhotonNetwork.IsMasterClient)
                {
                    photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.AllBuffered, (int)type, targetId, amount);
                }
            }
        }
    }
    
    [PunRPC]
    public void RPC_UpdateObjectiveProgress(int type, string targetId, int amount)
    {
        UpdateObjectiveProgress((ObjectiveType)type, targetId, amount);
    }

    public void UpdateQuestUI()
    {
        Quest currentQuest = GetCurrentQuest();
        if (questText != null)
        {
            if (currentQuest != null)
            {
                string questDisplay = FormatQuestDisplay(currentQuest);
                questText.text = questDisplay;
            }
            else
            {
                questText.text = "No active quest";
            }
        }
        // update HUD title/description
        if (questTitleText != null)
            questTitleText.text = currentQuest != null ? currentQuest.questName : "No active quest";
        if (questDescriptionText != null)
            questDescriptionText.text = currentQuest != null ? currentQuest.description : "";
    }

    // call this when opening the quest UI
    public void OnQuestUIOpened()
    {
        if (disableOnQuestUIOpen != null)
            disableOnQuestUIOpen.SetActive(false);
    }

    // call this when closing the quest UI
    public void OnQuestUIClosed()
    {
        if (disableOnQuestUIOpen != null)
            disableOnQuestUIOpen.SetActive(true);
    }
    
    private string FormatQuestDisplay(Quest quest)
    {
        if (quest.isCompleted)
        {
            return $"{quest.questName} (Completed)\n{quest.description}";
        }

        string display = $"{quest.questName}\n{quest.description}\n\n";

        // Handle new multi-objective system
        if (quest.objectives != null && quest.objectives.Length > 0)
        {
            display += "Objectives:\n";
            for (int i = 0; i < quest.objectives.Length; i++)
            {
                var objective = quest.objectives[i];
                // Only show completed or current objective
                if (objective.IsCompleted || i == quest.currentObjectiveIndex)
                {
                    string status = objective.IsCompleted ? "✓" : "○";
                    string progress = objective.requiredCount > 1 ? $" [{objective.currentCount}/{objective.requiredCount}]" : "";
                    string inProgress = (!objective.IsCompleted && i == quest.currentObjectiveIndex) ? " in progress" : "";
                    display += $"{status} {objective.objectiveName}{progress}{inProgress}\n";
                }
                // Hide future objectives
            }
        }
        else
        {
            // Legacy single objective system
            string progress = quest.requiredCount > 1 ? $" [{quest.currentCount}/{quest.requiredCount}]" : "";
            string inProgress = !quest.isCompleted ? " in progress" : "";
            display += $"Progress:{progress}{inProgress}";
        }

        return display;
    }

    // ---- progress apis (legacy support) ----
    public void AddProgress_Kill(string enemyName)
    {
        UpdateObjectiveProgress(ObjectiveType.Kill, enemyName, 1);
    }

    public void AddProgress_Collect(string itemName, int amount = 1)
    {
        UpdateObjectiveProgress(ObjectiveType.Collect, itemName, amount);
    }

    public void AddProgress_TalkTo(string npcId)
    {
        UpdateObjectiveProgress(ObjectiveType.TalkTo, npcId, 1);
    }

    public void AddProgress_ReachArea(string areaId)
    {
        UpdateObjectiveProgress(ObjectiveType.ReachArea, areaId, 1);
    }
    
    public void AddProgress_FindArea(string areaId)
    {
        UpdateObjectiveProgress(ObjectiveType.FindArea, areaId, 1);
    }
    
    // ---- new objective types ----
    public void AddProgress_ShrineOffering(string shrineId, ItemData offeredItem, int quantity)
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        
        // Check if this shrine offering matches any objective
        if (currentQuest.objectives != null)
        {
            foreach (var objective in currentQuest.objectives)
            {
                if (objective.objectiveType == ObjectiveType.ShrineOffering && 
                    string.Equals(objective.targetId, shrineId, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if the offered item matches requirements
                    if (objective.requiredOfferings != null && objective.offeringQuantities != null)
                    {
                        for (int i = 0; i < objective.requiredOfferings.Length && i < objective.offeringQuantities.Length; i++)
                        {
                            if (objective.requiredOfferings[i] == offeredItem && 
                                objective.offeringQuantities[i] <= quantity)
                            {
                                UpdateObjectiveProgress(ObjectiveType.ShrineOffering, shrineId, 1);
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
    
    public void AddProgress_PowerSteal(string enemyName)
    {
        UpdateObjectiveProgress(ObjectiveType.PowerSteal, enemyName, 1);
    }

    // Helper for group ReachArea trigger:
    private bool QuestAreaTrigger_GroupRequiresAll()
    {
        var obj = GetCurrentObjective();
        if (obj == null) return false;
        // Find matching area trigger with requireAllPlayers set to true
        var triggers = FindObjectsOfType<QuestAreaTrigger>();
        foreach (var t in triggers)
        {
            if (t.areaId == obj.targetId && t.requireAllPlayers)
                return true;
        }
        return false;
    }
}
