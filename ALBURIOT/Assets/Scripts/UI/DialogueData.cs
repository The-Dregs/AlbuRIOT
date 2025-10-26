using UnityEngine;
using UnityEngine.Video;

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue/DialogueData")]
public class DialogueData : ScriptableObject
{
    [System.Serializable]
    public class DialogueLine
    {
        public string speaker;
        [TextArea] public string text;
        // optional cinematic fields (used by cutscene DialogueManager)
        public Sprite backgroundSprite;
        public VideoClip backgroundClip;
        public VideoClip foregroundClip;
        [Min(0f)] public float delayBeforeText = 0f;
        public bool loopClips = false;
    }

    public DialogueLine[] lines;
}
