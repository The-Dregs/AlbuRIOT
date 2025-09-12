using UnityEngine;
using TMPro;

[System.Serializable]
public class Quest
{
    public string questName;
    public string description;
    public bool isCompleted;
    public int objectiveID;
}

public class QuestManager : MonoBehaviour
{
    public Quest[] quests;
    public int currentQuestIndex = 0;
    public TextMeshProUGUI questText;

    public void StartQuest(int index)
    {
        currentQuestIndex = index;
        quests[index].isCompleted = false;
        UpdateQuestUI();
    }

    public void CompleteQuest(int index)
    {
        quests[index].isCompleted = true;
        UpdateQuestUI();
        if (index + 1 < quests.Length)
            StartQuest(index + 1);
    }

    public Quest GetCurrentQuest()
    {
        return quests[currentQuestIndex];
    }

    public void UpdateQuestUI()
    {
        if (questText != null)
        {
            questText.text = quests[currentQuestIndex].questName + "\n" + quests[currentQuestIndex].description;
        }
    }
}
