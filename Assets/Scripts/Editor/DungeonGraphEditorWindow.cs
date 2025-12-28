using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DungeonGraph.Editor
{
    public class DungeonGraphEditorWindow : EditorWindow
    {
        public static void Open(DungeonGraphAsset target)
        {
            DungeonGraphEditorWindow[] windows = Resources.FindObjectsOfTypeAll<DungeonGraphEditorWindow>();
            foreach (var w in windows)
            {
                if (w.currentGraph == target)
                {
                    w.Focus();
                    return;
                }
            }

            DungeonGraphEditorWindow window = CreateWindow<DungeonGraphEditorWindow>(typeof(DungeonGraphEditorWindow), typeof(SceneView));
            window.autoRepaintOnSceneChange = true;
            window.Load(target);
        }

        [SerializeField]
        private DungeonGraphAsset m_currentGraph;

        [SerializeField]
        private SerializedObject m_serializedObject;

        [SerializeField]
        private DungeonGraphView m_currentView;

        private bool m_isGraphDrawn = false;

        public DungeonGraphAsset currentGraph => m_currentGraph;

        private void OnEnable()
        {
            // Redraw graph if we have a graph but no view (happens after script recompilation)
            if (m_currentGraph != null && m_currentView == null)
            {
                m_isGraphDrawn = false;
                DrawGraph();
            }
            // Only draw graph if it hasn't been drawn yet
            else if (m_currentGraph != null && !m_isGraphDrawn)
            {
                DrawGraph();
            }

            // Update title in case asset was renamed
            UpdateTitle();
        }

        private void OnGUI()
        {
            if (m_currentGraph != null)
            {
                // Update title every frame in case asset was renamed externally
                UpdateTitle();

                if (EditorUtility.IsDirty(m_currentGraph))
                {
                    this.hasUnsavedChanges = true;
                }
                else
                {
                    this.hasUnsavedChanges = false;
                }
            }
        }

        private void OnFocus()
        {
            // Save any pending changes when we gain focus
            if (m_currentGraph != null && m_serializedObject != null)
            {
                m_serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        private void OnLostFocus()
        {
            // CRITICAL: Save changes when losing focus to prevent data loss
            if (m_currentGraph != null && m_serializedObject != null)
            {
                m_serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        private void OnDestroy()
        {
            // Clean up view and save changes before window is destroyed
            if (m_currentView != null)
            {
                m_currentView.graphViewChanged -= OnChange;
                m_currentView = null;
            }

            if (m_currentGraph != null && m_serializedObject != null)
            {
                m_serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        private void UpdateTitle()
        {
            if (m_currentGraph != null)
            {
                titleContent = new GUIContent(m_currentGraph.name, EditorGUIUtility.ObjectContent(null, typeof(DungeonGraphAsset)).image);
            }
        }

        public void Load(DungeonGraphAsset target)
        {
            m_currentGraph = target;
            m_isGraphDrawn = false;
            DrawGraph();
            UpdateTitle();
        }

        private void DrawGraph()
        {
            // Clear existing view if any
            if (m_currentView != null)
            {
                m_currentView.graphViewChanged -= OnChange;
                rootVisualElement.Remove(m_currentView);
                m_currentView = null;
            }

            m_serializedObject = new SerializedObject(m_currentGraph);
            m_currentView = new DungeonGraphView(m_serializedObject, this);
            m_currentView.graphViewChanged += OnChange;
            rootVisualElement.Add(m_currentView);
            m_isGraphDrawn = true;
        }

        private GraphViewChange OnChange(GraphViewChange graphViewChange)
        {
            // Mark the asset as dirty
            EditorUtility.SetDirty(m_currentGraph);

            // Apply changes immediately to serialized object
            if (m_serializedObject != null)
            {
                m_serializedObject.ApplyModifiedProperties();
                m_serializedObject.Update();
            }

            return graphViewChange;
        }
    }
}
