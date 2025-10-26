using TMPro;
using UnityEngine;

namespace AlbuRIOT.UI
{
    [DisallowMultipleComponent]
    public class AbilityDebugUI : MonoBehaviour
    {
        [Header("target & text")]
        public Transform followTarget; // for world-space canvases
        public TextMeshProUGUI text;   // assign your TMP text on a Canvas

        [Header("position (world-space only)")]
        public Vector3 offset = new Vector3(0, 2.2f, 0);

        [Header("data source")]
        public AlbuRIOT.Abilities.PlayerAbilityController controller; // assign your local player's controller

        void Awake()
        {
            // if no controller assigned, try to find the local one
            if (controller == null)
            {
#if UNITY_2023_1_OR_NEWER
                var all = Object.FindObjectsByType<AlbuRIOT.Abilities.PlayerAbilityController>(FindObjectsSortMode.None);
#else
                var all = Object.FindObjectsOfType<AlbuRIOT.Abilities.PlayerAbilityController>();
#endif
                AlbuRIOT.Abilities.PlayerAbilityController local = null;
                foreach (var c in all)
                {
                    var pv = c.GetComponent<Photon.Pun.PhotonView>();
                    if (pv == null || pv.IsMine) { local = c; break; }
                }
                if (local == null && all.Length > 0) local = all[0];
                controller = local;
            }
        }

        void LateUpdate()
        {
            // only reposition for world-space canvas
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && followTarget != null)
            {
                transform.position = followTarget.position + offset;
            }
            UpdateText();
        }

        private void UpdateText()
        {
            if (text == null) return;
            string line = "ability: none";
            if (controller != null && controller.slot1 != null)
            {
                var a = controller.slot1;
                float cdRemain = Mathf.Max(0f, (a.lastUseTime + a.cooldown) - Time.time);
                line = $"[1] {a.abilityName}  CD: {cdRemain:F1}s";
            }
            text.text = line;
        }
    }
}
