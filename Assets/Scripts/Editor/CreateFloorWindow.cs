using UnityEngine;
using UnityEditor;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Editor window for creating new dungeon floors.
    /// </summary>
    public class CreateFloorWindow : EditorWindow
    {
        private string m_floorName = "";
        private System.Action<string> m_onCreateCallback;

        public static void ShowWindow(System.Action<string> onCreateCallback)
        {
            var window = GetWindow<CreateFloorWindow>(true, "Create New Dungeon Floor", true);
            window.m_onCreateCallback = onCreateCallback;
            window.minSize = new Vector2(350, 180);
            window.maxSize = new Vector2(350, 180);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Create New Dungeon Floor", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical("box");

            // Floor name field
            m_floorName = EditorGUILayout.TextField("Floor Name:", m_floorName);

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("A new floor will be created with all standard and custom node type folders populated with blank rooms.", MessageType.Info);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                Close();
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(m_floorName));
            if (GUILayout.Button("Create", GUILayout.Width(80)))
            {
                m_onCreateCallback?.Invoke(m_floorName);
                Close();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }
    }
}
