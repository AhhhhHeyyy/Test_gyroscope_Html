using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RotateY90))]
public class RotateY90Editor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Rotate Y +90°", GUILayout.Height(30)))
        {
            var t = ((RotateY90)target).transform;
            Undo.RecordObject(t, "Rotate Y +90°");
            t.Rotate(0f, 90f, 0f, Space.Self);
        }
    }
}
