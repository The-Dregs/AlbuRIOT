using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(EquipmentGripPreview))]
public class EquipmentGripPreviewEditor : Editor
{
    private EquipmentGripPreview preview;

    void OnEnable()
    {
        preview = (EquipmentGripPreview)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(preview == null || preview.equipmentManager == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(preview.previewActive ? "Refresh Preview" : "Spawn Preview"))
            {
                preview.previewActive = true;
                preview.RefreshPreview();
            }
            if (GUILayout.Button("Clear Preview"))
            {
                preview.previewActive = false;
                preview.ClearPreview();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Bake Current Pose To ItemData"))
            {
                preview.BakeToItemData();
            }
        }
    }

    // Draw simple handles when preview is active to adjust position and rotation in the Scene view
    void OnSceneGUI()
    {
        if (preview == null || !preview.previewActive || preview.previewInstance == null) return;

        Transform t = preview.previewInstance.transform;
        EditorGUI.BeginChangeCheck();
        Vector3 pos = Handles.PositionHandle(t.position, t.rotation);
        Quaternion rot = Handles.RotationHandle(t.rotation, pos);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(t, "Move Preview Model");
            t.position = pos;
            t.rotation = rot;
            // keep scale as set in ItemData
        }
    }
}
