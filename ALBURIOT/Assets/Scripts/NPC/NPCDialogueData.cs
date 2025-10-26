using UnityEngine;

[CreateAssetMenu(fileName = "NewNPCDialogue", menuName = "Dialogue/NPC Dialogue Data")]
public class NPCDialogueData : ScriptableObject
{
    [System.Serializable]
    public class Line
    {
        public string speaker;
        [TextArea] public string text;
    }

    public Line[] lines;
}
