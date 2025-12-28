using UnityEngine;
using UnityEditor;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Editor window for creating new custom node types.
    /// Allows the user to specify a name and color for the new node type.
    /// </summary>
    public class CreateCustomNodeTypeWindow : EditorWindow
    {
        private string m_nodeName = "";
        private Color m_nodeColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private System.Action<string, Color> m_onCreateCallback;
        private bool m_nameAlreadyExists = false;

        public static void ShowWindow(System.Action<string, Color> onCreateCallback)
        {
            var window = GetWindow<CreateCustomNodeTypeWindow>(true, "Create New Node Type", true);
            window.m_onCreateCallback = onCreateCallback;
            window.minSize = new Vector2(350, 150);
            window.maxSize = new Vector2(350, 150);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Custom Node Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical("box");

            // Node name field
            EditorGUI.BeginChangeCheck();
            m_nodeName = EditorGUILayout.TextField("Node Type Name:", m_nodeName);
            if (EditorGUI.EndChangeCheck())
            {
                // Check if name already exists when user types
                var registry = CustomNodeTypeRegistry.GetOrCreateDefault();
                m_nameAlreadyExists = !string.IsNullOrWhiteSpace(m_nodeName) && registry.HasNodeType(m_nodeName);
            }

            // Show error if name already exists
            if (m_nameAlreadyExists)
            {
                EditorGUILayout.HelpBox($"A node type with the name '{m_nodeName}' already exists. Please choose a different name.", MessageType.Error);
            }

            EditorGUILayout.Space(5);

            // Color picker
            m_nodeColor = EditorGUILayout.ColorField("Node Color:", m_nodeColor);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                Close();
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(m_nodeName) || m_nameAlreadyExists);
            if (GUILayout.Button("Create", GUILayout.Width(80)))
            {
                m_onCreateCallback?.Invoke(m_nodeName, m_nodeColor);
                Close();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }
    }
}
