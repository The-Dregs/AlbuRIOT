using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public QuestManager questManager;
    public GameObject arrow;
    public Transform[] spawnPoints;
    public GameObject playerPrefab;
    public Transform toolsLocation;
    public Transform bananaTreeLocation;
    public Transform colonizerLocation;
    public Transform overhearZoneLocation;

    void Start()
    {
        // Multiplayer spawn
        int playerIndex = Random.Range(0, spawnPoints.Length);
        Instantiate(playerPrefab, spawnPoints[playerIndex].position, Quaternion.identity);
        questManager.StartQuest(0);
    arrow.GetComponent<ObjectiveArrow>().SetTarget(colonizerLocation);
    }

    public void OnToolsPickedUp()
    {
        questManager.CompleteQuest(0);
    arrow.GetComponent<ObjectiveArrow>().SetTarget(toolsLocation);
    }

    public void OnBananaTreeAttacked()
    {
        questManager.CompleteQuest(1);
    arrow.GetComponent<ObjectiveArrow>().SetTarget(bananaTreeLocation);
    }

    public void OnBananaPickedUp()
    {
        questManager.CompleteQuest(2);
    arrow.GetComponent<ObjectiveArrow>().SetTarget(colonizerLocation);
    }

    public void OnOverhearZoneEntered()
    {
        // Trigger overheard dialogue
    }

    public void OnBananaGivenToColonizer()
    {
        questManager.CompleteQuest(3);
        // Final dialogue
    }
}
