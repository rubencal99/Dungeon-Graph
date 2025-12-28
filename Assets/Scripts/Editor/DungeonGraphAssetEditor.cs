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
            if (asset == null)
            {
                return false;
            }

            if (asset is DungeonGraphAsset dungeonGraph)
            {
                DungeonGraphEditorWindow.Open(dungeonGraph);
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
