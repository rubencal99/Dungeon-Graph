using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEditor.UI;

namespace DungeonGraph.Editor
{
    public class DungeonGraphView : GraphView
    {
        private DungeonGraphAsset m_dungeonGraph;
        private SerializedObject m_serializedObject;
        private DungeonGraphEditorWindow m_window;
        public DungeonGraphEditorWindow window => m_window;

        public List<DungeonGraphEditorNode> m_graphNodes;
        public Dictionary<string, DungeonGraphEditorNode> m_nodeDictionary;
        public Dictionary<Edge, DungeonGraphConnection> m_connectionDictionary;

        private DungeonGraphWindowSearchProvider m_searchProvider;

        public DungeonGraphView(SerializedObject serializedObject, DungeonGraphEditorWindow window)
        {
            m_serializedObject = serializedObject;
            m_dungeonGraph = (DungeonGraphAsset)serializedObject.targetObject;
            m_window = window;

            m_graphNodes = new List<DungeonGraphEditorNode>();
            m_nodeDictionary = new Dictionary<string, DungeonGraphEditorNode>();
            m_connectionDictionary = new Dictionary<Edge, DungeonGraphConnection>();

            m_searchProvider = ScriptableObject.CreateInstance<DungeonGraphWindowSearchProvider>();
            m_searchProvider.graph = this;
            this.nodeCreationRequest = ShowSearchWindow;

            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Scripts/Editor/USS/DungeonGraphEditor.uss");
            styleSheets.Add(style);

            GridBackground background = new GridBackground();
            background.name = "Grid";
            Add(background);
            background.SendToBack();

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());
            this.SetupZoom(0.5f, 10f);

            DrawNodes();
            DrawConnections();

            graphViewChanged += OnGraphViewChangedEvent;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> allPorts = new List<Port>();
            List<Port> ports = new List<Port>();

            foreach (var node in m_graphNodes)
            {
                allPorts.AddRange(node.Ports);
            }

            foreach (Port p in allPorts)
            {
                if (p == startPort) { continue; }
                if (p.node == startPort.node) { continue; }
                if (p.direction == startPort.direction) { continue; }
                if (p.portType == startPort.portType)
                    ports.Add(p);

            }

            return ports;
        }

        private GraphViewChange OnGraphViewChangedEvent(GraphViewChange graphViewChange)
        {
            if (graphViewChange.movedElements != null)
            {
                Undo.RecordObject(m_serializedObject.targetObject, "Moved Elements");
                foreach (DungeonGraphEditorNode editorNode in graphViewChange.movedElements.OfType<DungeonGraphEditorNode>())
                {
                    editorNode.SavePosition();
                }
            }
            // If a node got deleted
            if (graphViewChange.elementsToRemove != null)
            {
                List<DungeonGraphEditorNode> nodes = graphViewChange.elementsToRemove.OfType<DungeonGraphEditorNode>().ToList();
                if (nodes.Count > 0)
                {
                    Undo.RecordObject(m_serializedObject.targetObject, "Removed Node");
                    for (int i = nodes.Count - 1; i >= 0; i--)
                    {
                        RemoveNode(nodes[i]);
                    }
                }

                foreach (Edge e in graphViewChange.elementsToRemove.OfType<Edge>())
                {
                    RemoveConnection(e);
                }
            }

            // new connection
            if (graphViewChange.edgesToCreate != null)
            {
                Undo.RecordObject(m_serializedObject.targetObject, "Added Connections");
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    CreateEdge(edge);
                }
            }

            return graphViewChange;
        }

        private void CreateEdge(Edge edge)
        {
            DungeonGraphEditorNode inputNode = (DungeonGraphEditorNode)edge.input.node;
            int inputIndex = inputNode.Ports.IndexOf(edge.input);

            DungeonGraphEditorNode outputNode = (DungeonGraphEditorNode)edge.output.node;
            int outputIndex = outputNode.Ports.IndexOf(edge.output);

            DungeonGraphConnection connection = new DungeonGraphConnection(inputNode.Node.id, inputIndex, outputNode.Node.id, outputIndex);
            m_dungeonGraph.Connections.Add(connection);
        }

        private void RemoveConnection(Edge e)
        {
            if (m_connectionDictionary.TryGetValue(e, out DungeonGraphConnection connection))
            {
                m_dungeonGraph.Connections.Remove(connection);
                m_connectionDictionary.Remove(e);
            }
        }

        private void RemoveNode(DungeonGraphEditorNode editorNode)
        {
            //Undo.RecordObject(m_serializedObject.targetObject, "Removed Node");
            m_dungeonGraph.Nodes.Remove(editorNode.Node);
            m_nodeDictionary.Remove(editorNode.Node.id);
            m_graphNodes.Remove(editorNode);
            m_serializedObject.Update();

        }

        private void DrawNodes()
        {
            foreach (DungeonGraphNode node in m_dungeonGraph.Nodes)
            {
                AddNodeToGraph(node);
            }
        }

        private void DrawConnections()
        {
            if (m_dungeonGraph.Connections == null) { return; }
            foreach (DungeonGraphConnection connection in m_dungeonGraph.Connections)
            {
                DrawConnection(connection);
            }
        }

        private void DrawConnection(DungeonGraphConnection connection)
        {
            DungeonGraphEditorNode inputNode = GetNode(connection.inputPort.nodeId);
            DungeonGraphEditorNode outputNode = GetNode(connection.outputPort.nodeId);
            if (inputNode == null) { return; }
            if (outputNode == null) { return; }

            Port inPort = inputNode.Ports[connection.inputPort.portIndex];
            Port outPort = outputNode.Ports[connection.outputPort.portIndex];
            Edge edge = inPort.ConnectTo(outPort);
            AddElement(edge);

            m_connectionDictionary.Add(edge, connection);
        }

        private DungeonGraphEditorNode GetNode(string nodeId)
        {
            DungeonGraphEditorNode node = null;
            m_nodeDictionary.TryGetValue(nodeId, out node);
            return node;
        }

        private void ShowSearchWindow(NodeCreationContext context)
        {
            m_searchProvider.target = (VisualElement)focusController.focusedElement;
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), m_searchProvider);
        }

        public void Add(DungeonGraphNode node)
        {
            Undo.RecordObject(m_serializedObject.targetObject, "Added Node");

            m_dungeonGraph.Nodes.Add(node);

            m_serializedObject.Update();

            AddNodeToGraph(node);
        }

        private void AddNodeToGraph(DungeonGraphNode node)
        {
            node.typeName = node.GetType().AssemblyQualifiedName;

            DungeonGraphEditorNode editorNode = new DungeonGraphEditorNode(node);
            editorNode.SetPosition(node.position);
            m_graphNodes.Add(editorNode);
            m_nodeDictionary.Add(node.id, editorNode);
            AddElement(editorNode);
        }
    }
}
