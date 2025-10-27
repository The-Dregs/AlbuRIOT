using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class QuestListUI : MonoBehaviour
{
    [Header("Bindings")]
    public GameObject panel;
    public Transform listParent;
    public GameObject rowPrefab; // prefab with two TMP texts: name/progress
    public KeyCode toggleKey = KeyCode.T;

    [Header("Data Source (optional)")]
    public QuestManager questManagerOverride;
    private QuestManager qm;
    private int _inputLockToken = 0;
    [Tooltip("When open, refresh the list each frame (useful while debugging)")]
    public bool refreshContinuouslyWhileOpen = false;

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        qm = FindFirstObjectByType<QuestManager>();
        WireRowPrefabIfMissing();
        // auto-create a minimal UI if not wired
        if (panel == null)
        {
            AutoCreateMinimalUI();
            if (panel != null) panel.SetActive(false); // ensure the runtime-created panel starts hidden
        }
        EnsureListLayout();
    }

    void Update()
    {
        // local-only: ignore if attached to a non-local player object
        if (Input.GetKeyDown(toggleKey))
        {
            bool open = panel != null && !panel.activeSelf;
            var ui = LocalUIManager.Ensure();
            if (open)
            {
                // enforce strict exclusivity: do not open if any other UI is open
                if (ui.IsAnyOpen && !ui.IsOwner("QuestList")) {
                    Debug.LogWarning("[questlist] cannot open: another UI is already open");
                    return;
                }
                if (!ui.TryOpen("QuestList")) return;
                if (panel != null) panel.SetActive(true);
                Refresh();
                // partial lock: allow movement, lock combat and camera, unlock cursor
                if (_inputLockToken == 0)
                    _inputLockToken = LocalInputLocker.Ensure().Acquire("QuestList", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
            }
            else
            {
                // avoid disabling ourselves if the panel is the same GameObject this script is on
                if (panel != null && panel != this.gameObject) panel.SetActive(false);
                if (LocalUIManager.Instance != null) LocalUIManager.Instance.Close("QuestList");
                if (_inputLockToken != 0)
                {
                    LocalInputLocker.Ensure().Release(_inputLockToken);
                    _inputLockToken = 0;
                }
                LocalInputLocker.Ensure().ForceGameplayCursor();
            }
        }

        if (refreshContinuouslyWhileOpen && panel != null && panel.activeInHierarchy)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (questManagerOverride != null) qm = questManagerOverride;
        if (qm == null) qm = FindFirstObjectByType<QuestManager>();
        if (qm == null)
        {
            var all = Object.FindObjectsByType<QuestManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all != null && all.Length > 0) qm = all[0];
        }
        if (qm == null || listParent == null) return;

        EnsureListLayout();

        foreach (Transform child in listParent)
            Destroy(child.gameObject);

        if (qm.quests == null) { Debug.Log("QuestListUI: QuestManager.quests is null"); return; }
        Debug.Log($"QuestListUI: rendering {qm.quests.Length} quests (current index {qm.currentQuestIndex})");
        for (int i = 0; i < qm.quests.Length; i++)
        {
            var q = qm.quests[i];
            var row = Instantiate(rowPrefab, listParent);
            EnsureRowLayout(row);
            TextMeshProUGUI nameText = null;
            TextMeshProUGUI progressText = null;
            TextMeshProUGUI descriptionText = null;
            // prefer named children if present (also support nested container named 'quest')
            var nameTf = row.transform.Find("Name") ?? row.transform.Find("quest/Name");
            var descTf = row.transform.Find("Description") ?? row.transform.Find("quest/Description");
            // progress can be a direct child named Progress or inside a 'right' container
            var progTf = row.transform.Find("Progress")
                         ?? row.transform.Find("right/Progress")
                         ?? row.transform.Find("Right/Progress");
            if (nameTf != null) nameText = nameTf.GetComponent<TextMeshProUGUI>();
            if (descTf != null) descriptionText = descTf.GetComponent<TextMeshProUGUI>();
            if (progTf != null) progressText = progTf.GetComponent<TextMeshProUGUI>();
            if (nameText == null || progressText == null)
            {
                // robust fallback: choose name/desc from 'quest' container, and progress as any TMP outside it
                var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                Transform questContainer = row.transform.Find("quest");
                // pick name/description if missing
                if (nameText == null)
                {
                    foreach (var t in allTexts)
                    {
                        if (questContainer != null && !t.transform.IsChildOf(questContainer)) continue;
                        nameText = t; break;
                    }
                }
                if (descriptionText == null)
                {
                    bool foundName = false;
                    foreach (var t in allTexts)
                    {
                        if (questContainer != null && !t.transform.IsChildOf(questContainer)) continue;
                        if (nameText != null && t == nameText) { foundName = true; continue; }
                        if (foundName) { descriptionText = t; break; }
                    }
                }
                if (progressText == null)
                {
                    foreach (var t in allTexts)
                    {
                        // prefer a TMP that is NOT under 'quest' container
                        if (questContainer != null && t.transform.IsChildOf(questContainer)) continue;
                        // and not the same as name/description
                        if (t == nameText || t == descriptionText) continue;
                        progressText = t; break;
                    }
                    // absolute fallback: last text in the row
                    if (progressText == null && allTexts.Length > 0)
                        progressText = allTexts[allTexts.Length - 1];
                }
            }
            if (nameText != null)
            {
                string currentMark = (i == qm.currentQuestIndex) ? " <color=yellow>(current)</color>" : "";
                if (descriptionText == null)
                {
                    // no separate description field; put it below the name
                    string desc = string.IsNullOrEmpty(q.description) ? string.Empty : $"\n<size=80%><color=#bbbbbb>{q.description}</color></size>";
                    nameText.text = q.questName + currentMark + desc;
                }
                else
                {
                    nameText.text = q.questName + currentMark;
                }
            }
            if (descriptionText != null)
            {
                descriptionText.text = q.description ?? string.Empty;
            }
            if (progressText != null)
            {
                string progress = q.isCompleted ? "<color=green>completed</color>" : FormatProgress(q);
                progressText.text = progress;
            }
        }
    }

    private string FormatProgress(Quest q)
    {
        if (q == null) return string.Empty;
        if (q.requiredCount > 1)
            return $"{Mathf.Clamp(q.currentCount, 0, Mathf.Max(1, q.requiredCount))}/{Mathf.Max(1, q.requiredCount)}";
        return q.isCompleted ? "completed" : "in progress";
    }

    private void WireRowPrefabIfMissing()
    {
        if (rowPrefab != null) return;
        // create a simple row prefab at runtime: two TMP texts in a horizontal layout
        var go = new GameObject("QuestRow", typeof(RectTransform));
        var nameGO = new GameObject("Name", typeof(TextMeshProUGUI));
        var progressGO = new GameObject("Progress", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(go.transform, false);
        progressGO.transform.SetParent(go.transform, false);
        var nameTMP = nameGO.GetComponent<TextMeshProUGUI>();
        var progTMP = progressGO.GetComponent<TextMeshProUGUI>();
        nameTMP.alignment = TextAlignmentOptions.Left;
        progTMP.alignment = TextAlignmentOptions.Right;
        rowPrefab = go;
    }

    private void AutoCreateMinimalUI()
    {
        var canvasGO = new GameObject("QuestList_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var img = panel.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0.6f);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.6f, 0.1f);
        rect.anchorMax = new Vector2(0.95f, 0.9f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var scrollGO = new GameObject("Scroll", typeof(ScrollRect));
        scrollGO.transform.SetParent(panel.transform, false);
        var scroll = scrollGO.GetComponent<ScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGO.transform, false);
        var viewportImg = viewport.GetComponent<Image>();
        if (viewportImg != null)
        {
            var c = viewportImg.color; c.a = 0f; viewportImg.color = c; // transparent so it doesn't cover UI
            viewportImg.raycastTarget = false;
        }
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        listParent = content.transform;

        // provide a simple default row prefab
        WireRowPrefabIfMissing();
    }

    private void EnsureListLayout()
    {
        if (listParent == null) return;
        var vlg = listParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = listParent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            Debug.Log("QuestListUI: Added VerticalLayoutGroup to listParent to stack rows");
        }
        var fitter = listParent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = listParent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        }
        // Optional: wire ScrollRect content if the parent has one
        var scroll = listParent.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll != null && scroll.content != (RectTransform)listParent)
        {
            scroll.content = (RectTransform)listParent;
        }
    }

    private void EnsureRowLayout(GameObject row)
    {
        if (row == null) return;
        var rt = row.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, rt.offsetMin.y);
            rt.offsetMax = new Vector2(0, rt.offsetMax.y);
        }
        var le = row.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = row.AddComponent<UnityEngine.UI.LayoutElement>();
        if (le.minHeight < 28f) le.minHeight = 36f;
        var hlg = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (hlg == null)
        {
            hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
        }
        // configure children layout for Name and Progress
        var nameTf = row.transform.Find("Name") ?? row.transform.Find("quest/Name");
        var progTf = row.transform.Find("Progress");
        var descTf = row.transform.Find("Description") ?? row.transform.Find("quest/Description");
        // if there is a 'quest' container, make it a vertical stack for name+description
        var questContainer = row.transform.Find("quest");
        if (questContainer != null)
        {
            var v = questContainer.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? questContainer.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperLeft;
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var qle = questContainer.GetComponent<UnityEngine.UI.LayoutElement>() ?? questContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            qle.flexibleWidth = 1;
            qle.preferredWidth = 0;
        }
        if (nameTf != null)
        {
            var nameLE = nameTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? nameTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 0;
        }
        if (descTf != null)
        {
            var descLE = descTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? descTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            descLE.flexibleWidth = 1;
            descLE.preferredWidth = 0;
        }
        if (progTf != null)
        {
            var progLE = progTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? progTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            if (progLE.preferredWidth < 80f) progLE.preferredWidth = 120f;
            progLE.flexibleWidth = 0;
        }
    }
}
