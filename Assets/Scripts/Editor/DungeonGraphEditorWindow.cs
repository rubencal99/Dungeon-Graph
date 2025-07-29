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
            window.titleContent = new GUIContent($"{target.name}", EditorGUIUtility.ObjectContent(null, typeof(DungeonGraphAsset)).image);
            window.Load(target);
        }

        [SerializeField]
        private DungeonGraphAsset m_currentGraph;

        [SerializeField]
        private SerializedObject m_serializedObject;

        [SerializeField]
        private DungeonGraphView m_currentView;

        public DungeonGraphAsset currentGraph => m_currentGraph;

        private void OnEnable()
        {
            if (m_currentGraph != null)
            {
                DrawGraph();
            }
        }

        private void OnGUI()
        {
            if (m_currentGraph != null)
            {
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

        public void Load(DungeonGraphAsset target)
        {
            m_currentGraph = target;
            DrawGraph();
        }

        private void DrawGraph()
        {
            m_serializedObject = new SerializedObject(m_currentGraph);
            m_currentView = new DungeonGraphView(m_serializedObject, this);
            m_currentView.graphViewChanged += OnChange;
            rootVisualElement.Add(m_currentView);
        }

        private GraphViewChange OnChange(GraphViewChange graphViewChange)
        {
            //this.hasUnsavedChanges = true;
            EditorUtility.SetDirty(m_currentGraph);
            return graphViewChange;
        }
    }
}
