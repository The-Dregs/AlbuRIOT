using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class QuestJsonLoader : MonoBehaviour
{
    [Tooltip("Name of the JSON file in Resources/Quests (without extension)")]
    public string questJsonFile = "Chapter2Quest";
    public QuestManager questManager;

    [System.Serializable]
    public class QuestObjectiveData {
        public string objectiveName;
        public string description;
        public string objectiveType;
        public string targetId;
        public int requiredCount;
        // For multi-item Collect objectives
        public string[] collectItemIds;
        public int[] collectQuantities;
    }
    [System.Serializable]
    public class QuestData {
        public string questName;
        public string description;
        public bool requiresAllObjectives = true;
        public bool autoAdvanceObjectives = true;
        // Optional: where to start in the objectives list (e.g., 1 to skip the first for playtests)
        public int startObjectiveIndex = 0;
        public QuestObjectiveData[] objectives;
    }

    void Awake() {
        if (!questManager) questManager = FindObjectOfType<QuestManager>();
        LoadAndApplyQuest();
    }

    public void LoadAndApplyQuest() {
        if (questManager == null) { Debug.LogError("QuestManager not found!"); return; }
        TextAsset file = Resources.Load<TextAsset>("Quests/"+questJsonFile);
        if (!file) { Debug.LogError("Could not load quest JSON: " + questJsonFile); return; }
        QuestData questData = JsonUtility.FromJson<QuestData>(file.text);
        if (questData == null) { Debug.LogError("Failed to parse quest"); return; }
        Quest newQuest = new Quest {
            questName = questData.questName,
            description = questData.description,
            requiresAllObjectives = questData.requiresAllObjectives,
            autoAdvanceObjectives = questData.autoAdvanceObjectives,
            objectives = new QuestObjective[questData.objectives.Length],
        };
        for (int i = 0; i < questData.objectives.Length; i++) {
            var qd = questData.objectives[i];
            ObjectiveType type;
            if (!System.Enum.TryParse(qd.objectiveType, true, out type)) type = ObjectiveType.Custom;
            var obj = new QuestObjective {
                objectiveName = qd.objectiveName,
                description = qd.description,
                objectiveType = type,
                targetId = qd.targetId,
                requiredCount = qd.requiredCount
            };
            // Load multi-item Collect if provided
            if (type == ObjectiveType.Collect && qd.collectItemIds != null && qd.collectItemIds.Length > 0)
            {
                obj.collectItemIds = qd.collectItemIds;
                obj.collectQuantities = qd.collectQuantities != null && qd.collectQuantities.Length == qd.collectItemIds.Length 
                    ? qd.collectQuantities 
                    : new int[qd.collectItemIds.Length];
                obj.collectProgress = new int[qd.collectItemIds.Length];
                // Set requiredCount to sum if not explicitly set
                if (qd.requiredCount <= 1)
                {
                    int sum = 0;
                    foreach (int q in obj.collectQuantities) sum += q;
                    obj.requiredCount = sum;
                }
            }
            newQuest.objectives[i] = obj;
        }
        // Respect optional starting objective index from JSON
        if (newQuest.objectives != null && newQuest.objectives.Length > 0)
            newQuest.currentObjectiveIndex = Mathf.Clamp(questData.startObjectiveIndex, 0, newQuest.objectives.Length - 1);
        questManager.quests = new Quest[] { newQuest };
        questManager.currentQuestIndex = 0;
        questManager.UpdateQuestUI();
    }
}
