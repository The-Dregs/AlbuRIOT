using UnityEngine;
using TMPro;
using System;
using System.Collections.Generic;
using Photon.Pun;
using AlbuRIOT.Abilities;

public enum ObjectiveType { Kill, Collect, TalkTo, ReachArea, ShrineOffering, PowerSteal, Custom }

[System.Serializable]
public class QuestObjective
{
    [Header("Objective Details")]
    public string objectiveName;
    public string description;
    public ObjectiveType objectiveType = ObjectiveType.Custom;
    
    [Header("Target Information")]
    [Tooltip("for Kill: enemy name; for Collect: item name; for TalkTo/ReachArea: id string; for ShrineOffering: shrine id")] 
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
    
    public bool IsCompleted => currentCount >= requiredCount;
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
                if (!obj.IsCompleted) return false;
            }
            return true;
        }
        else
        {
            foreach (var obj in objectives)
            {
                if (obj.IsCompleted) return true;
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
        if (playerInventory == null)
            playerInventory = FindFirstObjectByType<Inventory>();
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
    }

    public void StartQuest(int index)
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
            quest.currentObjectiveIndex = 0;
        }
        
        // Legacy support
        quest.currentCount = 0;
        
        Debug.Log($"Quest started: {quest.questName}");
        UpdateQuestUI();
        OnQuestStarted?.Invoke(quest);
        
        // Sync with other players (only when networking is ready)
        if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
        {
            photonView.RPC("RPC_StartQuest", RpcTarget.Others, index);
        }
    }
    
    [PunRPC]
    public void RPC_StartQuest(int index)
    {
        if (photonView != null && !photonView.IsMine) return;
        StartQuest(index);
    }

    public void CompleteQuest(int index)
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
        
        // Sync with other players (only when networking is ready)
        if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
        {
            photonView.RPC("RPC_CompleteQuest", RpcTarget.Others, index);
        }
        
        // Auto-start next quest
        if (index + 1 < quests.Length)
            StartQuest(index + 1);
    }
    
    [PunRPC]
    public void RPC_CompleteQuest(int index)
    {
        if (photonView != null && !photonView.IsMine) return;
        CompleteQuest(index);
    }
    
    private void GiveQuestRewards(Quest quest)
    {
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
        
        // Handle new multi-objective system
        if (currentQuest.objectives != null && currentQuest.objectives.Length > 0)
        {
            bool objectiveUpdated = false;
            
            foreach (var objective in currentQuest.objectives)
            {
                if (objective.objectiveType == type && 
                    (string.IsNullOrEmpty(objective.targetId) || string.Equals(objective.targetId, targetId, StringComparison.OrdinalIgnoreCase)))
                {
                    objective.currentCount = Mathf.Clamp(objective.currentCount + amount, 0, objective.requiredCount);
                    objectiveUpdated = true;
                    
                    Debug.Log($"Objective progress ({type}): {targetId} -> {objective.currentCount}/{objective.requiredCount}");
                    OnObjectiveUpdated?.Invoke(objective);
                    
                    if (objective.IsCompleted)
                    {
                        Debug.Log($"Objective completed: {objective.objectiveName}");
                        OnObjectiveCompleted?.Invoke(objective);
                        
                        // Give objective rewards
                        if (objective.rewardItem != null && playerInventory != null)
                        {
                            playerInventory.AddItem(objective.rewardItem, objective.rewardQuantity);
                        }
                        // ability rewards now handled by PowerStealManager/PlayerSkillSlots system
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
                
                // Sync with other players (only when networking is ready)
                if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
                {
                    photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.Others, (int)type, targetId, amount);
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
                
                // Sync with other players (only when networking is ready)
                if (photonView != null && photonView.IsMine && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
                {
                    photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.Others, (int)type, targetId, amount);
                }
            }
        }
    }
    
    [PunRPC]
    public void RPC_UpdateObjectiveProgress(int type, string targetId, int amount)
    {
        if (photonView != null && !photonView.IsMine) return;
        UpdateObjectiveProgress((ObjectiveType)type, targetId, amount);
    }

    public void UpdateQuestUI()
    {
        if (questText != null)
        {
            Quest currentQuest = GetCurrentQuest();
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
                string status = objective.IsCompleted ? "✓" : "○";
                string progress = objective.requiredCount > 1 ? $" [{objective.currentCount}/{objective.requiredCount}]" : "";
                display += $"{status} {objective.objectiveName}{progress}\n";
            }
        }
        else
        {
            // Legacy single objective system
            string progress = quest.requiredCount > 1 ? $" [{quest.currentCount}/{quest.requiredCount}]" : "";
            display += $"Progress:{progress}";
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
}
