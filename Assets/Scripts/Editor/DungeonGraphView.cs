using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEditor.UI;
using CorridorType = DungeonGraph.CorridorType;

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

        // Organic generation parameters
        private float m_areaPlacementFactor = 2.0f;
        private float m_repulsionFactor = 1.0f;
        private int m_simulationIterations = 100;
        private bool m_forceMode = false;
        private float m_stiffnessFactor = 1.0f;
        private float m_chaosFactor = 0.0f;
        private bool m_realTimeSimulation = false;
        private float m_simulationSpeed = 10f;

        // Corridor generation parameters
        private UnityEngine.Tilemaps.TileBase m_corridorTile = null;
        private int m_corridorWidth = 2;
        private CorridorType m_corridorType = CorridorType.Direct;

        // EditorPrefs keys for persistence
        private const string PREF_AREA_PLACEMENT = "DungeonGraph.AreaPlacement";
        private const string PREF_REPULSION = "DungeonGraph.Repulsion";
        private const string PREF_ITERATIONS = "DungeonGraph.Iterations";
        private const string PREF_FORCE_MODE = "DungeonGraph.ForceMode";
        private const string PREF_STIFFNESS = "DungeonGraph.Stiffness";
        private const string PREF_CHAOS = "DungeonGraph.Chaos";
        private const string PREF_REALTIME_SIMULATION = "DungeonGraph.RealTimeSimulation";
        private const string PREF_SIMULATION_SPEED = "DungeonGraph.SimulationSpeed";
        private const string PREF_CORRIDOR_TILE = "DungeonGraph.CorridorTile";
        private const string PREF_CORRIDOR_WIDTH = "DungeonGraph.CorridorWidth";
        private const string PREF_CORRIDOR_TYPE = "DungeonGraph.CorridorType";

        public DungeonGraphView(SerializedObject serializedObject, DungeonGraphEditorWindow window)
        {
            m_serializedObject = serializedObject;
            m_dungeonGraph = (DungeonGraphAsset)serializedObject.targetObject;
            m_window = window;

            m_graphNodes = new List<DungeonGraphEditorNode>();
            m_nodeDictionary = new Dictionary<string, DungeonGraphEditorNode>();
            m_connectionDictionary = new Dictionary<Edge, DungeonGraphConnection>();

            // Load persisted parameters
            LoadPreferences();

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

            // Position and a reasonable default size that fits all parameters and buttons
            m_toolsBoard.SetPosition(new Rect(16, 16, 300, 600));

            var addButton = m_toolsBoard.Q<Button>("addButton");
            if (addButton != null)
                addButton.style.display = DisplayStyle.None;

            // Create a section to hold actions
            var actions = new BlackboardSection { title = "Actions" };
            m_toolsBoard.Add(actions);

            // Organic generation parameters
            var organicParams = new BlackboardSection { title = "Organic Parameters" };
            organicParams.name = "organic-params";

            var areaField = new FloatField("Area Placement Factor") { value = m_areaPlacementFactor };
            areaField.RegisterValueChangedCallback(evt =>
            {
                m_areaPlacementFactor = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(areaField);

            var repulsionField = new FloatField("Repulsion Factor") { value = m_repulsionFactor };
            repulsionField.RegisterValueChangedCallback(evt =>
            {
                m_repulsionFactor = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(repulsionField);

            var iterationsField = new IntegerField("Simulation Iterations") { value = m_simulationIterations };
            iterationsField.RegisterValueChangedCallback(evt =>
            {
                m_simulationIterations = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(iterationsField);

            var forceModeToggle = new Toggle("Force Mode") { value = m_forceMode };
            forceModeToggle.RegisterValueChangedCallback(evt =>
            {
                m_forceMode = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(forceModeToggle);

            var stiffnessSlider = new Slider("Stiffness Factor", 0.1f, 5.0f) { value = m_stiffnessFactor };
            stiffnessSlider.RegisterValueChangedCallback(evt =>
            {
                m_stiffnessFactor = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(stiffnessSlider);

            var chaosSlider = new Slider("Chaos Factor", 0.0f, 1.0f) { value = m_chaosFactor };
            chaosSlider.RegisterValueChangedCallback(evt =>
            {
                m_chaosFactor = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(chaosSlider);

            var realTimeToggle = new Toggle("Real-Time Simulation") { value = m_realTimeSimulation };
            realTimeToggle.RegisterValueChangedCallback(evt =>
            {
                m_realTimeSimulation = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(realTimeToggle);

            var speedSlider = new Slider("Simulation Speed (iter/s)", 1f, 1000f) { value = m_simulationSpeed };
            speedSlider.showInputField = true; // Show numeric input field
            speedSlider.RegisterValueChangedCallback(evt =>
            {
                m_simulationSpeed = evt.newValue;
                SavePreferences();
            });
            organicParams.Add(speedSlider);

            m_toolsBoard.Add(organicParams);

            // Corridor parameters section
            var corridorParams = new BlackboardSection { title = "Corridor Settings" };

            var corridorTileField = new ObjectField("Corridor Tile")
            {
                objectType = typeof(UnityEngine.Tilemaps.TileBase),
                value = m_corridorTile
            };
            corridorTileField.RegisterValueChangedCallback(evt =>
            {
                m_corridorTile = evt.newValue as UnityEngine.Tilemaps.TileBase;
                SavePreferences();
            });
            corridorParams.Add(corridorTileField);

            var corridorWidthField = new IntegerField("Corridor Width") { value = m_corridorWidth };
            corridorWidthField.RegisterValueChangedCallback(evt =>
            {
                m_corridorWidth = evt.newValue;
                SavePreferences();
            });
            corridorParams.Add(corridorWidthField);

            var corridorTypeField = new EnumField("Corridor Type", m_corridorType);
            corridorTypeField.RegisterValueChangedCallback(evt =>
            {
                m_corridorType = (CorridorType)evt.newValue;
                SavePreferences();
            });
            corridorParams.Add(corridorTypeField);

            m_toolsBoard.Add(corridorParams);

            // Generation buttons
            var generateDungeonBtn = new Button(GenerateDungeon)
            {
                text = "Generate Dungeon"
            };
            generateDungeonBtn.style.height = 35;
            generateDungeonBtn.style.marginBottom = 6;
            actions.Add(generateDungeonBtn);

            // Side-by-side button container
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.SpaceBetween;

            var generateRoomsBtn = new Button(GenerateRooms)
            {
                text = "Generate Rooms"
            };
            generateRoomsBtn.style.height = 28;
            generateRoomsBtn.style.flexGrow = 1;
            generateRoomsBtn.style.marginRight = 4;
            buttonRow.Add(generateRoomsBtn);

            var generateCorridorsBtn = new Button(GenerateCorridors)
            {
                text = "Generate Corridors"
            };
            generateCorridorsBtn.style.height = 28;
            generateCorridorsBtn.style.flexGrow = 1;
            buttonRow.Add(generateCorridorsBtn);

            actions.Add(buttonRow);

            Add(m_toolsBoard);
        }

        private void LoadPreferences()
        {
            m_areaPlacementFactor = EditorPrefs.GetFloat(PREF_AREA_PLACEMENT, 2.0f);
            m_repulsionFactor = EditorPrefs.GetFloat(PREF_REPULSION, 1.0f);
            m_simulationIterations = EditorPrefs.GetInt(PREF_ITERATIONS, 100);
            m_forceMode = EditorPrefs.GetBool(PREF_FORCE_MODE, false);
            m_stiffnessFactor = EditorPrefs.GetFloat(PREF_STIFFNESS, 1.0f);
            m_chaosFactor = EditorPrefs.GetFloat(PREF_CHAOS, 0.0f);
            m_realTimeSimulation = EditorPrefs.GetBool(PREF_REALTIME_SIMULATION, false);
            m_simulationSpeed = EditorPrefs.GetFloat(PREF_SIMULATION_SPEED, 10f);

            // Load corridor tile by asset path
            string tilePath = EditorPrefs.GetString(PREF_CORRIDOR_TILE, "");
            if (!string.IsNullOrEmpty(tilePath))
            {
                m_corridorTile = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(tilePath);
            }
            m_corridorWidth = EditorPrefs.GetInt(PREF_CORRIDOR_WIDTH, 2);
            m_corridorType = (CorridorType)EditorPrefs.GetInt(PREF_CORRIDOR_TYPE, (int)CorridorType.Direct);
        }

        private void SavePreferences()
        {
            EditorPrefs.SetFloat(PREF_AREA_PLACEMENT, m_areaPlacementFactor);
            EditorPrefs.SetFloat(PREF_REPULSION, m_repulsionFactor);
            EditorPrefs.SetInt(PREF_ITERATIONS, m_simulationIterations);
            EditorPrefs.SetBool(PREF_FORCE_MODE, m_forceMode);
            EditorPrefs.SetFloat(PREF_STIFFNESS, m_stiffnessFactor);
            EditorPrefs.SetFloat(PREF_CHAOS, m_chaosFactor);
            EditorPrefs.SetBool(PREF_REALTIME_SIMULATION, m_realTimeSimulation);
            EditorPrefs.SetFloat(PREF_SIMULATION_SPEED, m_simulationSpeed);

            // Save corridor tile as asset path
            string tilePath = m_corridorTile != null ? AssetDatabase.GetAssetPath(m_corridorTile) : "";
            EditorPrefs.SetString(PREF_CORRIDOR_TILE, tilePath);
            EditorPrefs.SetInt(PREF_CORRIDOR_WIDTH, m_corridorWidth);
            EditorPrefs.SetInt(PREF_CORRIDOR_TYPE, (int)m_corridorType);
        }

        /// <summary>
        /// Generate complete dungeon (rooms + corridors + tilemap merge)
        /// </summary>
        private void GenerateDungeon()
        {
            GenerateRooms();
            GenerateCorridors();
            Debug.Log("[DungeonGraphView] Complete dungeon generation finished!");
        }

        /// <summary>
        /// Generate only the rooms with visual connections (no corridors)
        /// </summary>
        private void GenerateRooms()
        {
            if (m_dungeonGraph == null)
            {
                Debug.LogWarning("[DungeonGraphView] No graph is loaded.");
                return;
            }

            // Destroy previous dungeon if it exists
            var existingDungeon = GameObject.Find("Generated_Dungeon");
            if (existingDungeon != null)
            {
                GameObject.DestroyImmediate(existingDungeon);
                Debug.Log("[DungeonGraphView] Destroyed previous dungeon.");
            }

            // Reset Master_Tilemap (clear all tiles)
            var masterTilemapObj = GameObject.FindGameObjectWithTag("Dungeon");
            if (masterTilemapObj != null)
            {
                var masterTilemap = masterTilemapObj.GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (masterTilemap != null)
                {
                    masterTilemap.ClearAllTiles();
                    Debug.Log("[DungeonGraphView] Cleared Master_Tilemap for new generation.");
                }
            }

            // Work on a copy so the editor asset isn't mutated by runtime logic
            var instance = ScriptableObject.Instantiate(m_dungeonGraph);
            try
            {
                instance.Init();
                var start = instance.GetStartNode();
                if (start == null)
                {
                    Debug.LogError("[DungeonGraphView] No StartNode found in this graph.");
                    return;
                }

                // Generate rooms using organic generation
                Debug.Log("[DungeonGraphView] Starting room generation using Organic method...");

                OrganicGeneration.GenerateRooms(instance, null, m_areaPlacementFactor, m_repulsionFactor,
                    m_simulationIterations, m_forceMode, m_stiffnessFactor, m_chaosFactor,
                    m_realTimeSimulation, m_simulationSpeed);

                // Assign corridor parameters to the tilemap system
                var generatedDungeon = GameObject.Find("Generated_Dungeon");
                if (generatedDungeon != null)
                {
                    var tilemapSystem = generatedDungeon.GetComponent<DungeonGraph.DungeonTilemapSystem>();
                    if (tilemapSystem != null)
                    {
                        tilemapSystem.corridorTile = m_corridorTile;
                        tilemapSystem.corridorWidth = m_corridorWidth;
                        tilemapSystem.corridorType = m_corridorType;
                        Debug.Log($"[DungeonGraphView] Assigned corridor tile, width, and type ({m_corridorType}) to tilemap system");
                    }
                }

                Debug.Log("[DungeonGraphView] Room generation complete!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonGraphView] Room generation failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Clean up the temporary instance ONLY if NOT using real-time simulation
                // Real-time simulation needs the graph to persist until simulation completes
                if (instance != null && !m_realTimeSimulation)
                {
                    ScriptableObject.DestroyImmediate(instance);
                    Debug.Log("[DungeonGraphView] Destroyed graph instance after instant generation");
                }
                else if (instance != null && m_realTimeSimulation)
                {
                    Debug.Log("[DungeonGraphView] Graph instance will be preserved for real-time simulation");
                }
            }
        }

        /// <summary>
        /// Generate or regenerate corridors for the existing dungeon
        /// </summary>
        private void GenerateCorridors()
        {
            var existingDungeon = GameObject.Find("Generated_Dungeon");
            if (existingDungeon == null)
            {
                Debug.LogWarning("[DungeonGraphView] No Generated_Dungeon found! Generate rooms first.");
                return;
            }

            // Check if real-time simulation is still running
            var simulationController = existingDungeon.GetComponent<DungeonGraph.DungeonSimulationController>();
            if (simulationController != null)
            {
                Debug.LogWarning("[DungeonGraphView] Real-time simulation is still running! Wait for simulation to complete before generating corridors.");
                return;
            }

            var tilemapSystem = existingDungeon.GetComponent<DungeonTilemapSystem>();
            if (tilemapSystem == null)
            {
                Debug.LogError("[DungeonGraphView] No DungeonTilemapSystem found on Generated_Dungeon!");
                return;
            }

            // Assign corridor parameters from UI
            if (m_corridorTile != null)
            {
                tilemapSystem.corridorTile = m_corridorTile;
                tilemapSystem.corridorWidth = m_corridorWidth;
            }

            // Try to find master tilemap by tag if not assigned
            if (tilemapSystem.masterTilemap == null)
            {
                if (!tilemapSystem.FindMasterTilemap())
                {
                    Debug.LogError("[DungeonGraphView] Could not find master tilemap! Tag a tilemap with 'Dungeon' tag.");
                    return;
                }
            }

            if (tilemapSystem.corridorTile == null)
            {
                Debug.LogError("[DungeonGraphView] Corridor tile not assigned! Please assign a corridor tile in Dungeon Tools.");
                return;
            }

            // Get the graph instance
            var instance = ScriptableObject.Instantiate(m_dungeonGraph);
            try
            {
                instance.Init();

                // Generate corridors using organic generation
                Debug.Log("[DungeonGraphView] Generating corridors...");

                OrganicGeneration.GenerateCorridors(instance, existingDungeon);

                Debug.Log("[DungeonGraphView] Corridor generation complete!");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonGraphView] Corridor generation failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
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
