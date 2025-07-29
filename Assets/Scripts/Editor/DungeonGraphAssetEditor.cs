using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DungeonGraph.Editor
{
    [CustomEditor(typeof(DungeonGraphAsset))]
    public class DungeonGraphAssetEditor : UnityEditor.Editor
    {
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int index)
        {
            Object asset = EditorUtility.InstanceIDToObject(instanceId);
            if (asset.GetType() == typeof(DungeonGraphAsset))
            {
                DungeonGraphEditorWindow.Open((DungeonGraphAsset)asset);
                return true;
            }
            return false;
        }
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open"))
            {
                DungeonGraphEditorWindow.Open((DungeonGraphAsset)target);
            }
        }
    }
}
