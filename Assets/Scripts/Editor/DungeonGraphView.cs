using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
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

        private Blackboard m_toolsBoard;

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
            //background.SendToBack();

            // Ensure the grid fills the view and cannot eat mouse events
            background.StretchToParentSize();
            background.pickingMode = PickingMode.Ignore;

            // Make sure it really sits behind everything else
            background.SendToBack();
            //background.style.zIndex = 0;

            // Keep the graph content above the grid (and pickable)
            this.style.flexGrow = 1;
            //contentViewContainer.style.zIndex = 1;
            contentViewContainer.pickingMode = PickingMode.Position;

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());
            this.SetupZoom(0.5f, 10f);

            DrawNodes();
            DrawConnections();

            RegisterCallback<MouseDownEvent>(OnGlobalMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(OnGlobalMouseUp, TrickleDown.TrickleDown);

            // Set the default pickability once after building the view
            SetPickabilityDuringDrag(false, null, Direction.Output);

            // var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Scripts/Editor/DungeonGraphEditor.uss");
            // if (sheet != null) styleSheets.Add(sheet);

            graphViewChanged += OnGraphViewChangedEvent;

            BuildToolsPanel();
        }

        private void BuildToolsPanel()
        {
            // A movable ShaderGraph-style panel
            m_toolsBoard = new Blackboard(this)
            {
                title = "Dungeon Tools",
                subTitle = m_dungeonGraph != null ? m_dungeonGraph.name : string.Empty
            };

            // Make it draggable / collapsible / resizable (nice QoL)
            m_toolsBoard.capabilities |= Capabilities.Movable | Capabilities.Collapsible | Capabilities.Resizable;

            // Position and a reasonable default size
            m_toolsBoard.SetPosition(new Rect(16, 16, 260, 150));

            var addButton = m_toolsBoard.Q<Button>("addButton");
            if (addButton != null)
                addButton.style.display = DisplayStyle.None;

            // Create a section to hold actions
            var actions = new BlackboardSection { title = "Actions" };
            m_toolsBoard.Add(actions);

            // The actual button
            var generateBtn = new Button(GenerateDungeon)
            {
                text = "Generate Dungeon"
            };
            actions.Add(generateBtn);

            Add(m_toolsBoard);
        }

        /// <summary>
        /// Editor-side traversal: instantiate the graph, Init(), then follow the flow
        /// using node.OnProcess(...) until it ends or we detect a loop.
        /// </summary>
        private void GenerateDungeon()
        {
            if (m_dungeonGraph == null)
            {
                EditorUtility.DisplayDialog("Dungeon Graph", "No graph is loaded.", "OK");
                return;
            }

            // Work on a copy so the editor asset isn't mutated by runtime logic.
            var instance = ScriptableObject.Instantiate(m_dungeonGraph);
            try
            {
                instance.Init();
                var start = instance.GetStartNode();
                if (start == null)
                {
                    EditorUtility.DisplayDialog("Dungeon Graph", "No StartNode found in this graph.", "OK");
                    return;
                }

                var visited = new HashSet<string>();
                var order   = new List<DungeonGraphNode>();

                var node   = start;
                int safety = 0; // protect against accidental infinite loops

                while (node != null && visited.Add(node.id) && safety++ < 4096)
                {
                    order.Add(node);

                    // Your current flow chooses output index 0 by default (see DungeonGraphNode.OnProcess)
                    string nextId = node.OnProcess(instance);
                    if (string.IsNullOrEmpty(nextId))
                        break;

                    node = instance.GetNode(nextId);
                }

                // For now, just log the traversal. Later, pass `order` to your generator.
                Debug.Log($"[DungeonGraph] Traversal order ({order.Count}): " +
                          string.Join(" -> ", order.Select(n => n.GetType().Name)));

                // TODO: Hook the actual dungeon generation here, using `order`
            }
            finally
            {
                // Clean up the temporary instance
                if (instance != null)
                    ScriptableObject.DestroyImmediate(instance);
            }
        }

        // old function for directed ports
        // public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        // {
        //     List<Port> allPorts = new List<Port>();
        //     List<Port> ports = new List<Port>();

        //     foreach (var node in m_graphNodes)
        //     {
        //         allPorts.AddRange(node.Ports);
        //     }

        //     foreach (Port p in allPorts)
        //     {
        //         if (p == startPort) { continue; }
        //         if (p.node == startPort.node) { continue; }
        //         if (p.direction == startPort.direction) { continue; }
        //         if (p.portType == startPort.portType)
        //             ports.Add(p);

        //     }

        //     return ports;
        // }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var result = new List<Port>();
            foreach (var p in ports)
            {
                if (p == startPort) continue;
                if (p.node == startPort.node) continue;                    // no self-loops
                if (p.portType != startPort.portType) continue;            // matching types only
                if (p.direction == startPort.direction) continue;          // require Output↔Input

                // Optional: prevent parallel edges between the same two nodes
                var a = ((DungeonGraphEditorNode)startPort.node).Node.id;
                var b = ((DungeonGraphEditorNode)p.node).Node.id;
                if (HasConnectionBetween(a, b)) continue;

                result.Add(p);
            }
            return result;
        }

        private GraphViewChange OnGraphViewChangedEvent(GraphViewChange graphViewChange)
        {
            if (graphViewChange.movedElements != null)
            {
                Undo.RecordObject(m_serializedObject.targetObject, "Moved Elements");
                foreach (var n in graphViewChange.movedElements.OfType<DungeonGraphEditorNode>())
                {
                    n.SavePosition();
                    // re-aim all edges touching this node
                    // foreach (var p in n.Ports)
                    //     foreach (var e in p.connections)
                    //         ApplyAimTangents(e);
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
                foreach (var edge in graphViewChange.edgesToCreate)
                {
                    CreateEdge(edge);                // your persistence
                    // ApplyAimTangents(edge);          // aim now
                    // edge.RegisterCallback<GeometryChangedEvent>(_ => ApplyAimTangents(edge)); // keep aimed on layout changes
                }
            }

            return graphViewChange;
        }

        // old directional port code
        // private void CreateEdge(Edge edge)
        // {
        //     DungeonGraphEditorNode inputNode = (DungeonGraphEditorNode)edge.input.node;
        //     int inputIndex = inputNode.Ports.IndexOf(edge.input);

        //     DungeonGraphEditorNode outputNode = (DungeonGraphEditorNode)edge.output.node;
        //     int outputIndex = outputNode.Ports.IndexOf(edge.output);

        //     DungeonGraphConnection connection = new DungeonGraphConnection(inputNode.Node.id, inputIndex, outputNode.Node.id, outputIndex);
        //     m_dungeonGraph.Connections.Add(connection);
        // }

        private void CreateEdge(Edge edge)
        {
            var nodeA = (DungeonGraphEditorNode)edge.output.node; // “output” just means drag start
            var nodeB = (DungeonGraphEditorNode)edge.input.node;

            var aId = nodeA.Node.id;
            var bId = nodeB.Node.id;

            // Canonicalize by GUID so storage is undirected and unique
            DungeonGraphEditorNode leftNode, rightNode;
            Port leftPort, rightPort;

            if (string.CompareOrdinal(aId, bId) <= 0)
            {
                leftNode = nodeA; leftPort = edge.output;
                rightNode = nodeB; rightPort = edge.input;
            }
            else
            {
                leftNode = nodeB; leftPort = edge.input;
                rightNode = nodeA; rightPort = edge.output;
            }

            int leftIndex = Math.Max(0, leftNode.Ports.IndexOf(leftPort));
            int rightIndex = Math.Max(0, rightNode.Ports.IndexOf(rightPort));

            var connection = new DungeonGraphConnection(
                leftNode.Node.id, leftIndex,
                rightNode.Node.id, rightIndex
            );

            m_dungeonGraph.Connections.Add(connection);
            m_connectionDictionary[edge] = connection; // <-- ensure deletions remove from asset
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

            Bind();
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
            // Look up the two nodes the connection references (unchanged)
            DungeonGraphEditorNode inputNode = GetNode(connection.inputPort.nodeId);
            DungeonGraphEditorNode outputNode = GetNode(connection.outputPort.nodeId);
            if (inputNode == null || outputNode == null) return;

            // Defensive: no ports, nothing to draw (unchanged)
            if (inputNode.Ports == null || inputNode.Ports.Count == 0) return;
            if (outputNode.Ports == null || outputNode.Ports.Count == 0) return;

            // Clamp indices in case older assets still point past end (you already do this)
            int inIdx = Mathf.Clamp(connection.inputPort.portIndex, 0, inputNode.Ports.Count - 1);
            int outIdx = Mathf.Clamp(connection.outputPort.portIndex, 0, outputNode.Ports.Count - 1);

            // Grab the saved ports
            Port a = inputNode.Ports[inIdx];
            Port b = outputNode.Ports[outIdx];

            // Figure out which end is Output and which is Input
            Port outEnd = null, inEnd = null;
            DungeonGraphEditorNode outNode = null, inNode = null;

            if (a.direction == Direction.Output && b.direction == Direction.Input)
            {
                outEnd = a; inEnd = b; outNode = inputNode; inNode = outputNode;
            }
            else if (a.direction == Direction.Input && b.direction == Direction.Output)
            {
                outEnd = b; inEnd = a; outNode = outputNode; inNode = inputNode;
            }
            else
            {
                // Fallback: pick the correct jacks from each node regardless of saved indices
                // (nodes in this project always have exactly one visible Output + one Input)
                var aOut = inputNode.Ports.FirstOrDefault(p => p.direction == Direction.Output);
                var aIn = inputNode.Ports.FirstOrDefault(p => p.direction == Direction.Input);
                var bOut = outputNode.Ports.FirstOrDefault(p => p.direction == Direction.Output);
                var bIn = outputNode.Ports.FirstOrDefault(p => p.direction == Direction.Input);

                // Prefer InputNode as Input and OutputNode as Output
                if (aOut != null && bIn != null)
                {
                    outEnd = aOut; inEnd = bIn; outNode = inputNode; inNode = outputNode;
                }
                else if (bOut != null && aIn != null)
                {
                    outEnd = bOut; inEnd = aIn; outNode = outputNode; inNode = inputNode;
                }
                else
                {
                    // Nothing to do if we still can’t resolve ends
                    return;
                }
            }

            // Always connect Output → Input (prevents "same direction" exception)
            var edge = outEnd.ConnectTo(inEnd);
            AddElement(edge);

            // Keep the view↔data map in sync so deletes work later
            m_connectionDictionary[edge] = connection;

            // ApplyAimTangents(edge);
            // edge.RegisterCallback<GeometryChangedEvent>(_ => ApplyAimTangents(edge));

            // Optional: migrate saved indices to match the actual ends we used
            int newInIdx = (inNode == inputNode) ? inputNode.Ports.IndexOf(inEnd) : outputNode.Ports.IndexOf(inEnd);
            int newOutIdx = (outNode == outputNode) ? outputNode.Ports.IndexOf(outEnd) : inputNode.Ports.IndexOf(outEnd);

            bool changed = false;
            if (newInIdx != connection.inputPort.portIndex) { connection.inputPort.portIndex = newInIdx; changed = true; }
            if (newOutIdx != connection.outputPort.portIndex) { connection.outputPort.portIndex = newOutIdx; changed = true; }
            if (changed) EditorUtility.SetDirty(m_dungeonGraph);
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
            Bind();
        }

        private void AddNodeToGraph(DungeonGraphNode node)
        {
            node.typeName = node.GetType().AssemblyQualifiedName;

            DungeonGraphEditorNode editorNode = new DungeonGraphEditorNode(node, m_serializedObject);
            editorNode.SetPosition(node.position);
            m_graphNodes.Add(editorNode);
            m_nodeDictionary.Add(node.id, editorNode);
            AddElement(editorNode);
        }

        private void Bind()
        {
            Debug.Log("Bind");
            m_serializedObject.Update();
            this.Bind(m_serializedObject);
        }

        private bool HasConnectionBetween(string aId, string bId)
        {
            if (m_dungeonGraph.Connections == null) return false;
            foreach (var c in m_dungeonGraph.Connections)
            {
                var x = c.inputPort.nodeId;
                var y = c.outputPort.nodeId;
                if ((x == aId && y == bId) || (x == bId && y == aId))
                    return true;
            }
            return false;
        }

        private DungeonGraphEditorNode _dragStartNode;
        private Direction _dragStartDirection;

        private void OnGlobalMouseDown(MouseDownEvent evt)
        {
            var port = (evt.target as VisualElement)?.GetFirstAncestorOfType<Port>();
            if (port == null) return;

            // We only allow drags to start from the visible Output jack
            if (port.direction != Direction.Output) return;

            _dragStartNode = (DungeonGraphEditorNode)port.node;
            _dragStartDirection = Direction.Output;

            // While dragging from an Output, only Inputs on other nodes are droppable
            SetPickabilityDuringDrag(true, _dragStartNode, _dragStartDirection);
        }

        private void OnGlobalMouseUp(MouseUpEvent evt)
        {
            // Drag ended or canceled: restore defaults (Output grabbable, Input ignored)
            SetPickabilityDuringDrag(false, null, Direction.Output);
            _dragStartNode = null;
        }

        private void SetPickabilityDuringDrag(bool dragging, DungeonGraphEditorNode startNode, Direction startDir)
        {
            foreach (var n in m_graphNodes)
            {
                foreach (var p in n.Ports)
                {
                    if (!dragging)
                    {
                        // Default: you can only grab Outputs; Inputs are not pickable until a drag starts
                        p.pickingMode = (p.direction == Direction.Output) ? PickingMode.Position : PickingMode.Ignore;
                        continue;
                    }

                    // Dragging from an Output on startNode?
                    if (startDir == Direction.Output)
                    {
                        if (n == startNode)
                        {
                            // Keep this node's Output pickable so the edge connector keeps the drag alive
                            p.pickingMode = (p.direction == Direction.Output) ? PickingMode.Position : PickingMode.Ignore;
                        }
                        else
                        {
                            // Other nodes: only Inputs are droppable
                            p.pickingMode = (p.direction == Direction.Input) ? PickingMode.Position : PickingMode.Ignore;
                        }
                    }
                }
            }
        }
        
        // Tune this to taste or expose it in your window; pixels at 100% zoom.
        private const float TangentPixels = 100f;

        private void ApplyAimTangents(Edge edge)
        {
            if (edge?.output == null || edge.input == null) return;

            // EdgeControl works in contentViewContainer space
            Vector2 from = contentViewContainer.WorldToLocal(edge.output.worldBound.center);
            Vector2 to   = contentViewContainer.WorldToLocal(edge.input.worldBound.center);

            Vector2 dir = (to - from);
            float dist = dir.magnitude;
            if (dist < 0.001f) dist = 0.001f;
            dir /= dist;

            //float strength = Mathf.Min(TangentPixels, dist * 0.5f);
            float strength = TangentPixels;

            var ec = edge.edgeControl;
            ec.from = from;
            ec.to   = to;

            // ⬇️ Mutate the existing control points array instead of assigning a new one
            var cps = ec.controlPoints;
            if (cps == null || cps.Length < 2)
            {
                // EdgeControl may not have allocated yet; try again on next layout tick
                edge.schedule.Execute(() => ApplyAimTangents(edge));
                return;
            }

            cps[0] = from + dir * strength;  // start tangent
            cps[1] = to   - dir * strength;  // end tangent

            ec.MarkDirtyRepaint();
        }

        // Re-aim all edges touching a node (call on move/geometry change)
        private void ReaimEdgesForNode(DungeonGraphEditorNode node)
        {
            if (node == null) return;
            foreach (var port in node.Ports)
            {
                // Port.connections is IEnumerable<Edge>
                foreach (var e in port.connections)
                    ApplyAimTangents(e);
            }
        }
    }
}
