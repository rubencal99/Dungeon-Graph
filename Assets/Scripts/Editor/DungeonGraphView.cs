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
        private Blackboard m_nodeSettingsBoard;
        private DungeonGraphEditorNode m_selectedNode;

        // Organic generation parameters
        private float m_areaPlacementFactor = 2.0f;
        private float m_repulsionFactor = 1.0f;
        private int m_simulationIterations = 100;
        private bool m_forceMode = false;
        private float m_stiffnessFactor = 1.0f;
        private float m_chaosFactor = 0.0f;
        private bool m_realTimeSimulation = false;
        private float m_simulationSpeed = 10f;
        private float m_idealDistance = 20f;
        private bool m_allowRoomOverlap = false;
        private int m_maxRoomRegenerations = 3;
        private int m_maxCorridorRegenerations = 3;

        // UI toggle for advanced parameters
        private bool m_showAdvanced = false;

        // Corridor generation parameters
        private UnityEngine.Tilemaps.TileBase m_corridorTile = null;
        private int m_corridorWidth = 2;
        private CorridorType m_corridorType = CorridorType.Direct;

        // Floor selection
        private List<DungeonFloorConfig> m_availableFloors = new List<DungeonFloorConfig>();
        private int m_selectedFloorIndex = 0;
        private string m_currentFloorPath = "";

        // EditorPrefs keys for persistence
        private const string PREF_AREA_PLACEMENT = "DungeonGraph.AreaPlacement";
        private const string PREF_REPULSION = "DungeonGraph.Repulsion";
        private const string PREF_ITERATIONS = "DungeonGraph.Iterations";
        private const string PREF_FORCE_MODE = "DungeonGraph.ForceMode";
        private const string PREF_STIFFNESS = "DungeonGraph.Stiffness";
        private const string PREF_CHAOS = "DungeonGraph.Chaos";
        private const string PREF_REALTIME_SIMULATION = "DungeonGraph.RealTimeSimulation";
        private const string PREF_SIMULATION_SPEED = "DungeonGraph.SimulationSpeed";
        private const string PREF_IDEAL_DISTANCE = "DungeonGraph.IdealDistance";
        private const string PREF_ALLOW_ROOM_OVERLAP = "DungeonGraph.AllowRoomOverlap";
        private const string PREF_MAX_ROOM_REGENERATIONS = "DungeonGraph.MaxRoomRegenerations";
        private const string PREF_MAX_CORRIDOR_REGENERATIONS = "DungeonGraph.MaxCorridorRegenerations";
        private const string PREF_CORRIDOR_TILE = "DungeonGraph.CorridorTile";
        private const string PREF_CORRIDOR_WIDTH = "DungeonGraph.CorridorWidth";
        private const string PREF_CORRIDOR_TYPE = "DungeonGraph.CorridorType";
        private const string PREF_SHOW_ADVANCED = "DungeonGraph.ShowAdvanced";
        private const string PREF_SELECTED_FLOOR = "DungeonGraph.SelectedFloor";
        private const string PREF_TOOLS_PANEL_STICKY = "DungeonGraph.ToolsPanelSticky";

        private bool m_toolsPanelSticky = true; // Track if panel should stick to top-right

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

            // Enable copy/paste
            serializeGraphElements = SerializeGraphElementsCallback;
            unserializeAndPaste = UnserializeAndPasteCallback;
            canPasteSerializedData = CanPasteSerializedDataCallback;

            DrawNodes();
            DrawConnections();

            RegisterCallback<MouseDownEvent>(OnGlobalMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(OnGlobalMouseUp, TrickleDown.TrickleDown);

            // Set the default pickability once after building the view
            SetPickabilityDuringDrag(false, null, Direction.Output);

            // var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Scripts/Editor/DungeonGraphEditor.uss");
            // if (sheet != null) styleSheets.Add(sheet);

            graphViewChanged += OnGraphViewChangedEvent;

            // Track node selection changes
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

            BuildToolsPanel();
            BuildNodeSettingsPanel();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Use schedule to defer the check until after selection is updated
            schedule.Execute(() =>
            {
                // Check current selection
                var selectedNodes = selection.OfType<DungeonGraphEditorNode>().ToList();

                if (selectedNodes.Count == 1)
                {
                    // Single node selected
                    var node = selectedNodes[0];
                    UpdateNodeSettingsPanel(node);
                }
                else if (selectedNodes.Count == 0)
                {
                    // Nothing selected or non-node element selected
                    UpdateNodeSettingsPanel(null);
                }
                // For multiple nodes selected, keep the current panel state
            });
        }

        private void BuildToolsPanel()
        {
            // A movable ShaderGraph-style panel
            m_toolsBoard = new Blackboard(this)
            {
                title = "Dungeon Tools",
                subTitle = m_dungeonGraph != null ? m_dungeonGraph.name : string.Empty
            };

            // Make it draggable | collapsible | resizable
            m_toolsBoard.capabilities |= Capabilities.Movable | Capabilities.Collapsible | Capabilities.Resizable;

            // Position at top-right corner with reasonable default size
            // Will be repositioned on geometry change to stick to top-right
            float defaultWidth = 320;
            float defaultHeight = 600;
            // Use a safe default position if layout width is not yet initialized
            float xPos = layout.width > defaultWidth ? layout.width - defaultWidth - 16 : 16;
            m_toolsBoard.SetPosition(new Rect(xPos, 16, defaultWidth, defaultHeight));

            // Set minimum size to prevent content overlap
            m_toolsBoard.style.minWidth = 300;
            m_toolsBoard.style.minHeight = 400;

            var addButton = m_toolsBoard.Q<Button>("addButton");
            if (addButton != null)
                addButton.style.display = DisplayStyle.None;

            // Track when user manually moves the panel
            m_toolsBoard.RegisterCallback<MouseDownEvent>(evt =>
            {
                // If user clicks on the title bar to drag, mark panel as no longer sticky
                var target = evt.target as VisualElement;
                if (target != null && target.name == "titleLabel")
                {
                    m_toolsPanelSticky = false;
                    SavePreferences();
                }
            });

            // Register callback to keep panel at top-right when view resizes
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // Create a styled header for Actions
            var actionsHeader = new Label("Actions");
            actionsHeader.AddToClassList("section-header");
            m_toolsBoard.Add(actionsHeader);

            // Create a container for action buttons
            var actions = new VisualElement();
            actions.style.marginBottom = 10;

            // Generation buttons
            var generateDungeonBtn = new Button(GenerateDungeon)
            {
                text = "Generate Dungeon",
                tooltip = "Generate complete dungeon with rooms and corridors"
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
                text = "Generate Rooms",
                tooltip = "Generate only rooms using physics simulation"
            };
            generateRoomsBtn.style.height = 28;
            generateRoomsBtn.style.flexGrow = 1;
            generateRoomsBtn.style.marginRight = 4;
            buttonRow.Add(generateRoomsBtn);

            var generateCorridorsBtn = new Button(GenerateCorridors)
            {
                text = "Generate Corridors",
                tooltip = "Generate corridors connecting existing rooms"
            };
            generateCorridorsBtn.style.height = 28;
            generateCorridorsBtn.style.flexGrow = 1;
            buttonRow.Add(generateCorridorsBtn);

            actions.Add(buttonRow);

            // Clear button
            var clearBtn = new Button(ClearDungeon)
            {
                text = "Clear Dungeon",
                tooltip = "Remove all generated rooms and corridors"
            };
            clearBtn.style.height = 28;
            clearBtn.style.marginTop = 6;
            actions.Add(clearBtn);

            m_toolsBoard.Add(actions);

            // ===== FLOOR SELECTION =====
            var floorHeader = new Label("Dungeon Floor");
            floorHeader.AddToClassList("section-header");
            floorHeader.style.marginTop = 10;
            m_toolsBoard.Add(floorHeader);

            var floorContainer = new VisualElement();
            floorContainer.style.marginBottom = 10;

            // Floor dropdown
            var floorDropdown = new UnityEngine.UIElements.PopupField<DungeonFloorConfig>(
                "Selected Floor",
                m_availableFloors,
                m_selectedFloorIndex >= 0 && m_selectedFloorIndex < m_availableFloors.Count ? m_availableFloors[m_selectedFloorIndex] : null
            );
            floorDropdown.tooltip = "Select which dungeon floor to use for room generation";
            floorDropdown.RegisterValueChangedCallback(evt =>
            {
                m_selectedFloorIndex = m_availableFloors.IndexOf(evt.newValue);
                UpdateCurrentFloorPath();
                SavePreferences();
            });
            floorContainer.Add(floorDropdown);

            // Create New Floor button
            var createFloorBtn = new Button(() =>
            {
                CreateFloorWindow.ShowWindow((floorName) =>
                {
                    if (DungeonFloorManager.CreateNewFloor(floorName))
                    {
                        // Refresh floor list
                        m_availableFloors = DungeonFloorManager.GetAllFloors();
                        m_selectedFloorIndex = m_availableFloors.FindIndex(f => f.floorName == floorName);
                        UpdateCurrentFloorPath();
                        SavePreferences();

                        // Rebuild tools panel to update dropdown
                        m_toolsBoard.Clear();
                        BuildToolsPanel();

                        EditorUtility.DisplayDialog("Success",
                            $"Floor '{floorName}' created successfully with all standard and custom node folders!", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error",
                            $"Failed to create floor '{floorName}'. It may already exist.", "OK");
                    }
                });
            })
            {
                text = "Create New Floor",
                tooltip = "Create a new dungeon floor with all node type folders"
            };
            createFloorBtn.style.height = 24;
            createFloorBtn.style.marginTop = 4;
            floorContainer.Add(createFloorBtn);

            m_toolsBoard.Add(floorContainer);

            // ===== BASIC SETTINGS =====
            var basicSettingsHeader = new Label("Basic Settings");
            basicSettingsHeader.AddToClassList("section-header");
            basicSettingsHeader.style.marginTop = 10;
            m_toolsBoard.Add(basicSettingsHeader);

            // ===== ROOM SETTINGS =====
            var roomSettings = new BlackboardSection { title = "Room Settings" };
            roomSettings.name = "room-settings";

            var idealDistanceSlider = new Slider("Ideal Distance", 1f, 50f) { value = m_idealDistance };
            idealDistanceSlider.showInputField = true;
            idealDistanceSlider.tooltip = "Target distance between connected rooms";
            idealDistanceSlider.RegisterValueChangedCallback(evt =>
            {
                m_idealDistance = evt.newValue;
                SavePreferences();
            });
            roomSettings.Add(idealDistanceSlider);

            var repulsionField = new Slider("Repulsion Factor", 0.1f, 5.0f) { value = m_repulsionFactor };
            repulsionField.showInputField = true;
            repulsionField.tooltip = "Strength of repulsion between rooms to prevent overlap";
            repulsionField.RegisterValueChangedCallback(evt =>
            {
                m_repulsionFactor = evt.newValue;
                SavePreferences();
            });
            roomSettings.Add(repulsionField);

            var stiffnessSlider = new Slider("Stiffness Factor", 0.1f, 20.0f) { value = m_stiffnessFactor };
            stiffnessSlider.showInputField = true;
            stiffnessSlider.tooltip = "How strongly rooms are pulled toward their ideal distance";
            stiffnessSlider.RegisterValueChangedCallback(evt =>
            {
                m_stiffnessFactor = evt.newValue;
                SavePreferences();
            });
            roomSettings.Add(stiffnessSlider);

            var iterationsField = new IntegerField("Simulation Iterations") { value = m_simulationIterations };
            iterationsField.tooltip = "Number of physics iterations to run (higher = more stable layout)";
            iterationsField.RegisterValueChangedCallback(evt =>
            {
                m_simulationIterations = evt.newValue;
                SavePreferences();
            });
            roomSettings.Add(iterationsField);

            // Create speed slider first so it can be referenced by toggle
            var speedSlider = new Slider("Simulation Speed", 1f, 1000f) { value = m_simulationSpeed };
            speedSlider.showInputField = true;
            speedSlider.tooltip = "Iterations per second during real-time simulation";
            speedSlider.SetEnabled(m_realTimeSimulation); // Initially set based on toggle state
            speedSlider.RegisterValueChangedCallback(evt =>
            {
                m_simulationSpeed = evt.newValue;
                SavePreferences();
            });

            var realTimeToggle = new Toggle("Real-Time Simulation") { value = m_realTimeSimulation };
            realTimeToggle.tooltip = "Animate the physics simulation in real-time instead of instant generation";
            realTimeToggle.RegisterValueChangedCallback(evt =>
            {
                m_realTimeSimulation = evt.newValue;
                SavePreferences();
                // Update the enabled state of speed slider
                speedSlider.SetEnabled(evt.newValue);
            });
            roomSettings.Add(realTimeToggle);
            roomSettings.Add(speedSlider);

            m_toolsBoard.Add(roomSettings);

            // ===== CORRIDOR SETTINGS =====
            var corridorSettings = new BlackboardSection { title = "Corridor Settings" };
            corridorSettings.name = "corridor-settings";

            var corridorTileField = new ObjectField("Corridor Tile")
            {
                objectType = typeof(UnityEngine.Tilemaps.TileBase),
                value = m_corridorTile,
                tooltip = "Tile used to paint corridors connecting rooms"
            };
            corridorTileField.RegisterValueChangedCallback(evt =>
            {
                m_corridorTile = evt.newValue as UnityEngine.Tilemaps.TileBase;
                SavePreferences();
            });
            corridorSettings.Add(corridorTileField);

            var corridorWidthField = new IntegerField("Corridor Width") { value = m_corridorWidth };
            corridorWidthField.tooltip = "Width of corridors in tiles";
            corridorWidthField.RegisterValueChangedCallback(evt =>
            {
                m_corridorWidth = evt.newValue;
                SavePreferences();
            });
            corridorSettings.Add(corridorWidthField);

            var corridorTypeField = new EnumField("Corridor Type", m_corridorType);
            corridorTypeField.tooltip = "Pathfinding algorithm for corridor generation";
            corridorTypeField.RegisterValueChangedCallback(evt =>
            {
                m_corridorType = (CorridorType)evt.newValue;
                SavePreferences();
            });
            corridorSettings.Add(corridorTypeField);

            m_toolsBoard.Add(corridorSettings);

            // ===== ADVANCED SETTINGS (Foldout with ScrollView) =====
            var advancedFoldout = new Foldout { text = "Advanced Settings", value = m_showAdvanced };
            advancedFoldout.tooltip = "Advanced parameters for fine-tuning dungeon generation";

            // Create scroll view for advanced settings
            var advancedScrollView = new ScrollView(ScrollViewMode.Vertical);
            advancedScrollView.style.maxHeight = 250; // Limit height to enable scrolling

            // Create container for advanced content
            var advancedContent = new VisualElement();

            // Advanced Room Settings
            var areaField = new FloatField("Area Placement Factor") { value = m_areaPlacementFactor };
            areaField.tooltip = "Multiplier for initial room placement spread";
            areaField.RegisterValueChangedCallback(evt =>
            {
                m_areaPlacementFactor = evt.newValue;
                SavePreferences();
            });
            advancedContent.Add(areaField);

            var forceModeToggle = new Toggle("Force Mode") { value = m_forceMode };
            forceModeToggle.tooltip = "Force generation to iterate until we've reached a stable layout (max 2096)";
            forceModeToggle.RegisterValueChangedCallback(evt =>
            {
                m_forceMode = evt.newValue;
                SavePreferences();
                iterationsField.SetEnabled(!evt.newValue);
            });
            advancedContent.Add(forceModeToggle);

            var chaosSlider = new Slider("Chaos Factor", 0.0f, 1.0f) { value = m_chaosFactor };
            chaosSlider.showInputField = true;
            chaosSlider.tooltip = "Randomness added to room velocities during generation (0 = ordered, 1 = chaotic)";
            chaosSlider.RegisterValueChangedCallback(evt =>
            {
                m_chaosFactor = evt.newValue;
                SavePreferences();
            });
            advancedContent.Add(chaosSlider);

            // Create Max Room Regens field first so it can be referenced by overlap toggle
            var maxRoomRegenerationsField = new IntegerField("Max Room Regenerations") { value = m_maxRoomRegenerations };
            maxRoomRegenerationsField.tooltip = "Maximum attempts to regenerate room layout if overlaps are detected";
            maxRoomRegenerationsField.SetEnabled(!m_allowRoomOverlap);
            maxRoomRegenerationsField.RegisterValueChangedCallback(evt =>
            {
                m_maxRoomRegenerations = evt.newValue;
                SavePreferences();
            });

            var allowOverlapToggle = new Toggle("Allow Room Overlap") { value = m_allowRoomOverlap };
            allowOverlapToggle.tooltip = "Allow rooms to overlap during generation";
            allowOverlapToggle.RegisterValueChangedCallback(evt =>
            {
                m_allowRoomOverlap = evt.newValue;
                SavePreferences();
                maxRoomRegenerationsField.SetEnabled(!evt.newValue);
            });

            advancedContent.Add(allowOverlapToggle);
            advancedContent.Add(maxRoomRegenerationsField);

            // Advanced Corridor Settings
            var maxCorridorRegenerationsField = new IntegerField("Max Corridor Regenerations") { value = m_maxCorridorRegenerations };
            maxCorridorRegenerationsField.tooltip = "Maximum attempts to regenerate corridors if overlaps are detected";
            maxCorridorRegenerationsField.RegisterValueChangedCallback(evt =>
            {
                m_maxCorridorRegenerations = evt.newValue;
                SavePreferences();
            });
            advancedContent.Add(maxCorridorRegenerationsField);

            // Add content to scroll view and scroll view to foldout
            advancedScrollView.Add(advancedContent);
            advancedFoldout.Add(advancedScrollView);

            // Register foldout value changed callback
            advancedFoldout.RegisterValueChangedCallback(evt =>
            {
                m_showAdvanced = evt.newValue;
                SavePreferences();
            });

            m_toolsBoard.Add(advancedFoldout);

            Add(m_toolsBoard);
        }

        private void BuildNodeSettingsPanel()
        {
            // Create Node Settings panel
            m_nodeSettingsBoard = new Blackboard(this)
            {
                title = "Node Settings",
                subTitle = "No node selected"
            };

            // Make it draggable, collapsible, and resizable
            m_nodeSettingsBoard.capabilities |= Capabilities.Movable | Capabilities.Collapsible | Capabilities.Resizable;

            // Position to the left of the tools panel
            float defaultWidth = 320;
            float defaultHeight = 300;
            float toolsPanelWidth = 320;
            // Use a safe default position if layout width is not yet initialized
            float xPos = layout.width > (defaultWidth + toolsPanelWidth + 48)
                ? layout.width - defaultWidth - toolsPanelWidth - 32
                : 16;
            m_nodeSettingsBoard.SetPosition(new Rect(xPos, 16, defaultWidth, defaultHeight));

            // Set minimum size
            m_nodeSettingsBoard.style.minWidth = 250;
            m_nodeSettingsBoard.style.minHeight = 150;

            var addButton = m_nodeSettingsBoard.Q<Button>("addButton");
            if (addButton != null)
                addButton.style.display = DisplayStyle.None;

            // Always visible
            m_nodeSettingsBoard.style.display = DisplayStyle.Flex;

            Add(m_nodeSettingsBoard);
        }

        private void UpdateNodeSettingsPanel(DungeonGraphEditorNode node)
        {
            if (node == null)
            {
                // Don't hide the panel, just keep showing the last selected node
                return;
            }

            m_selectedNode = node;
            m_nodeSettingsBoard.subTitle = node.Node.GetType().Name;

            // Clear existing content
            m_nodeSettingsBoard.Clear();

            // Get connection count for this node
            int connectionCount = GetNodeConnectionCount(node.Node.id);

            // Create Spawn Chance section
            var spawnChanceSection = new VisualElement();
            spawnChanceSection.style.marginTop = 10;
            spawnChanceSection.style.marginBottom = 10;
            spawnChanceSection.style.marginLeft = 5;
            spawnChanceSection.style.marginRight = 5;

            var spawnChanceLabel = new Label("Spawn Chance");
            spawnChanceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            spawnChanceLabel.style.marginBottom = 5;
            spawnChanceSection.Add(spawnChanceLabel);

            var spawnChanceSlider = new Slider("Chance (%)", 0f, 100f)
            {
                value = node.Node.spawnChance
            };
            spawnChanceSlider.showInputField = true;

            // Always allow slider adjustment, but show warning if conditions aren't met
            spawnChanceSlider.RegisterValueChangedCallback(evt =>
            {
                node.Node.spawnChance = evt.newValue;
                EditorUtility.SetDirty(m_dungeonGraph);

                // Update warning if needed
                UpdateSpawnChanceWarning(spawnChanceSection, node, connectionCount);
            });

            spawnChanceSection.Add(spawnChanceSlider);

            // Add warning label
            UpdateSpawnChanceWarning(spawnChanceSection, node, connectionCount);

            m_nodeSettingsBoard.Add(spawnChanceSection);
        }

        private void UpdateSpawnChanceWarning(VisualElement container, DungeonGraphEditorNode node, int connectionCount)
        {
            // Remove existing warning if any
            var existingWarning = container.Q<Label>("spawn-chance-warning");
            if (existingWarning != null)
            {
                container.Remove(existingWarning);
            }

            // Add warning if spawn chance < 100 and connections > 2
            if (node.Node.spawnChance < 100f && connectionCount > 2)
            {
                var warningLabel = new Label("⚠ Conditional nodes can only be used with maximum 2 connections");
                warningLabel.name = "spawn-chance-warning";
                warningLabel.style.color = new Color(1f, 0.6f, 0f); // Orange
                warningLabel.style.marginTop = 5;
                warningLabel.style.whiteSpace = WhiteSpace.Normal;
                warningLabel.style.fontSize = 11;
                container.Add(warningLabel);
            }
        }

        private int GetNodeConnectionCount(string nodeId)
        {
            if (m_dungeonGraph.Connections == null) return 0;

            int count = 0;
            foreach (var connection in m_dungeonGraph.Connections)
            {
                if (connection.inputPort.nodeId == nodeId || connection.outputPort.nodeId == nodeId)
                {
                    count++;
                }
            }
            return count;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // Keep the tools panel positioned at top-right when the view resizes (if sticky)
            if (m_toolsBoard != null && m_toolsPanelSticky && layout.width > 0)
            {
                Rect currentPos = m_toolsBoard.GetPosition();
                float newX = layout.width - currentPos.width - 16;

                // Ensure panel stays within bounds
                if (newX < 0) newX = 0;

                m_toolsBoard.SetPosition(new Rect(newX, currentPos.y, currentPos.width, currentPos.height));
            }

            // Also reposition Node Settings panel if it's visible and at the initial position
            if (m_nodeSettingsBoard != null && m_nodeSettingsBoard.style.display == DisplayStyle.Flex && layout.width > 0)
            {
                Rect currentPos = m_nodeSettingsBoard.GetPosition();

                // Check if it's still at the default left position (meaning it hasn't been manually moved)
                if (currentPos.x < 100) // If positioned at left edge, recalculate position
                {
                    float toolsPanelWidth = 320;
                    float newX = layout.width - currentPos.width - toolsPanelWidth - 32;

                    if (newX < 0) newX = 0;

                    m_nodeSettingsBoard.SetPosition(new Rect(newX, currentPos.y, currentPos.width, currentPos.height));
                }
            }
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
            m_idealDistance = EditorPrefs.GetFloat(PREF_IDEAL_DISTANCE, 20f);
            m_allowRoomOverlap = EditorPrefs.GetBool(PREF_ALLOW_ROOM_OVERLAP, false);
            m_maxRoomRegenerations = EditorPrefs.GetInt(PREF_MAX_ROOM_REGENERATIONS, 3);

            // Load corridor tile by asset path
            string tilePath = EditorPrefs.GetString(PREF_CORRIDOR_TILE, "");
            if (!string.IsNullOrEmpty(tilePath))
            {
                m_corridorTile = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(tilePath);
            }
            m_corridorWidth = EditorPrefs.GetInt(PREF_CORRIDOR_WIDTH, 2);
            m_corridorType = (CorridorType)EditorPrefs.GetInt(PREF_CORRIDOR_TYPE, (int)CorridorType.Direct);
            m_maxCorridorRegenerations = EditorPrefs.GetInt(PREF_MAX_CORRIDOR_REGENERATIONS, 3);
            m_showAdvanced = EditorPrefs.GetBool(PREF_SHOW_ADVANCED, false);
            m_toolsPanelSticky = EditorPrefs.GetBool(PREF_TOOLS_PANEL_STICKY, true);

            // Load floor selection
            m_availableFloors = DungeonFloorManager.GetAllFloors();
            string savedFloor = EditorPrefs.GetString(PREF_SELECTED_FLOOR, "");
            if (!string.IsNullOrEmpty(savedFloor) && m_availableFloors.Count > 0)
            {
                m_selectedFloorIndex = m_availableFloors.FindIndex(f => f.folderPath == savedFloor);
                if (m_selectedFloorIndex < 0) m_selectedFloorIndex = 0;
            }
            UpdateCurrentFloorPath();
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
            EditorPrefs.SetFloat(PREF_IDEAL_DISTANCE, m_idealDistance);
            EditorPrefs.SetBool(PREF_ALLOW_ROOM_OVERLAP, m_allowRoomOverlap);
            EditorPrefs.SetInt(PREF_MAX_ROOM_REGENERATIONS, m_maxRoomRegenerations);

            // Save corridor tile as asset path
            string tilePath = m_corridorTile != null ? AssetDatabase.GetAssetPath(m_corridorTile) : "";
            EditorPrefs.SetString(PREF_CORRIDOR_TILE, tilePath);
            EditorPrefs.SetInt(PREF_CORRIDOR_WIDTH, m_corridorWidth);
            EditorPrefs.SetInt(PREF_CORRIDOR_TYPE, (int)m_corridorType);
            EditorPrefs.SetInt(PREF_MAX_CORRIDOR_REGENERATIONS, m_maxCorridorRegenerations);
            EditorPrefs.SetBool(PREF_SHOW_ADVANCED, m_showAdvanced);
            EditorPrefs.SetBool(PREF_TOOLS_PANEL_STICKY, m_toolsPanelSticky);

            // Save floor selection
            EditorPrefs.SetString(PREF_SELECTED_FLOOR, m_currentFloorPath);
        }

        private void UpdateCurrentFloorPath()
        {
            if (m_availableFloors.Count > 0 && m_selectedFloorIndex >= 0 && m_selectedFloorIndex < m_availableFloors.Count)
            {
                m_currentFloorPath = m_availableFloors[m_selectedFloorIndex].folderPath;
            }
            else
            {
                m_currentFloorPath = "";
            }
        }

        public string GetCurrentFloorPath()
        {
            return m_currentFloorPath;
        }

        /// <summary>
        /// Generate complete dungeon (rooms + corridors + tilemap merge)
        /// </summary>
        private void GenerateDungeon()
        {
            GenerateRooms();
            GenerateCorridors();
            //Debug.Log("[DungeonGraphView] Complete dungeon generation finished!");
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
                //Debug.Log("[DungeonGraphView] Destroyed previous dungeon.");
            }

            // Reset Master_Tilemap (clear all tiles)
            var masterTilemapObj = GameObject.FindGameObjectWithTag("Dungeon");
            if (masterTilemapObj != null)
            {
                var masterTilemap = masterTilemapObj.GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (masterTilemap != null)
                {
                    masterTilemap.ClearAllTiles();
                    //Debug.Log("[DungeonGraphView] Cleared Master_Tilemap for new generation.");
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
                //Debug.Log("[DungeonGraphView] Starting room generation using Organic method...");

                OrganicGeneration.GenerateRooms(instance, null, m_areaPlacementFactor, m_repulsionFactor,
                    m_simulationIterations, m_forceMode, m_stiffnessFactor, m_chaosFactor,
                    m_realTimeSimulation, m_simulationSpeed, m_idealDistance, m_allowRoomOverlap, m_maxRoomRegenerations, m_maxCorridorRegenerations,
                    m_currentFloorPath);

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
                        //Debug.Log($"[DungeonGraphView] Assigned corridor tile, width, and type ({m_corridorType}) to tilemap system");
                    }
                }

                //Debug.Log("[DungeonGraphView] Room generation complete!");
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
                    //Debug.Log("[DungeonGraphView] Destroyed graph instance after instant generation");
                }
                else if (instance != null && m_realTimeSimulation)
                {
                    //Debug.Log("[DungeonGraphView] Graph instance will be preserved for real-time simulation");
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

            // Clear existing corridors before generating new ones
            tilemapSystem.ClearCorridors();

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
                //Debug.Log("[DungeonGraphView] Generating corridors...");

                OrganicGeneration.GenerateCorridors(instance, existingDungeon, m_maxCorridorRegenerations);

                //Debug.Log("[DungeonGraphView] Corridor generation complete!");
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

        /// <summary>
        /// Clear the generated dungeon and all associated data
        /// </summary>
        private void ClearDungeon()
        {
            // Destroy the generated dungeon GameObject
            var existingDungeon = GameObject.Find("Generated_Dungeon");
            if (existingDungeon != null)
            {
                GameObject.DestroyImmediate(existingDungeon);
                Debug.Log("[DungeonGraphView] Cleared generated dungeon.");
            }

            // Clear the master tilemap
            var masterTilemapObj = GameObject.FindGameObjectWithTag("Dungeon");
            if (masterTilemapObj != null)
            {
                var masterTilemap = masterTilemapObj.GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (masterTilemap != null)
                {
                    masterTilemap.ClearAllTiles();
                    Debug.Log("[DungeonGraphView] Cleared Master_Tilemap.");
                }
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

                // Update Node Settings if a selected node's connections changed
                if (m_selectedNode != null)
                {
                    UpdateNodeSettingsPanel(m_selectedNode);
                }
            }

            // Update Node Settings when connections are removed
            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.OfType<Edge>().Any())
            {
                if (m_selectedNode != null)
                {
                    UpdateNodeSettingsPanel(m_selectedNode);
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
            string nodeId = editorNode.Node.id;

            // Remove all connections that involve this node
            var connectionsToRemove = m_dungeonGraph.Connections
                .Where(c => c.inputPort.nodeId == nodeId || c.outputPort.nodeId == nodeId)
                .ToList();

            // Find and remove the visual edges
            var edgesToRemove = new List<Edge>();
            foreach (var connection in connectionsToRemove)
            {
                foreach (var kvp in m_connectionDictionary)
                {
                    var conn = kvp.Value;
                    if (conn.inputPort.nodeId == connection.inputPort.nodeId &&
                        conn.inputPort.portIndex == connection.inputPort.portIndex &&
                        conn.outputPort.nodeId == connection.outputPort.nodeId &&
                        conn.outputPort.portIndex == connection.outputPort.portIndex)
                    {
                        edgesToRemove.Add(kvp.Key);
                        break;
                    }
                }
            }

            // Remove connections from data
            foreach (var connection in connectionsToRemove)
            {
                m_dungeonGraph.Connections.Remove(connection);
            }

            // Remove visual edges
            foreach (var edge in edgesToRemove)
            {
                m_connectionDictionary.Remove(edge);
                edge.RemoveFromHierarchy();
            }

            // Remove the node itself
            m_dungeonGraph.Nodes.Remove(editorNode.Node);
            m_nodeDictionary.Remove(nodeId);
            m_graphNodes.Remove(editorNode);

            // Force serialization update
            m_serializedObject.Update();
            EditorUtility.SetDirty(m_dungeonGraph);
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
            // Look up the two nodes the connection references
            DungeonGraphEditorNode inputNode = GetNode(connection.inputPort.nodeId);
            DungeonGraphEditorNode outputNode = GetNode(connection.outputPort.nodeId);
            if (inputNode == null || outputNode == null) return;
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
                // (nodes should always have exactly one visible Output + one Input)
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

            // Always connect Output -> Input (prevents "same direction" exception)
            var edge = outEnd.ConnectTo(inEnd);
            AddElement(edge);

            // Keep the view/data map in sync so deletes work later
            m_connectionDictionary[edge] = connection;

            // ApplyAimTangents(edge);
            // edge.RegisterCallback<GeometryChangedEvent>(_ => ApplyAimTangents(edge));

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

        // ===== COPY/PASTE FUNCTIONALITY =====

        private string SerializeGraphElementsCallback(IEnumerable<GraphElement> elements)
        {
            var nodes = elements.OfType<DungeonGraphEditorNode>().Select(n => n.Node).ToList();

            // Create a wrapper ScriptableObject to leverage Unity's serialization
            var wrapper = ScriptableObject.CreateInstance<CopyPasteWrapper>();
            wrapper.nodes = nodes;

            string json = EditorJsonUtility.ToJson(wrapper, true);
            ScriptableObject.DestroyImmediate(wrapper);

            return json;
        }

        private void UnserializeAndPasteCallback(string operationName, string data)
        {
            // Create a wrapper to deserialize into
            var wrapper = ScriptableObject.CreateInstance<CopyPasteWrapper>();
            EditorJsonUtility.FromJsonOverwrite(data, wrapper);

            if (wrapper?.nodes == null || wrapper.nodes.Count == 0)
            {
                ScriptableObject.DestroyImmediate(wrapper);
                return;
            }

            // Offset for pasted nodes
            Vector2 pasteOffset = new Vector2(50, 50);

            Undo.RecordObject(m_serializedObject.targetObject, "Paste Nodes");

            foreach (var originalNode in wrapper.nodes)
            {
                if (originalNode == null)
                    continue;

                // Create a new instance - this automatically generates a new GUID
                if (!(Activator.CreateInstance(originalNode.GetType()) is DungeonGraphNode newNode))
                    continue;

                // Copy field values using reflection to preserve node-specific data
                CopyNodeFields(originalNode, newNode);

                // Offset position
                var oldPos = originalNode.position;
                newNode.SetPosition(new Rect(oldPos.position + pasteOffset, oldPos.size));

                // Add to graph
                m_dungeonGraph.Nodes.Add(newNode);

                // Update serialized object BEFORE creating editor node
                // This ensures the serialized property can be found in DungeonGraphEditorNode constructor
                m_serializedObject.Update();

                AddNodeToGraph(newNode);
            }

            ScriptableObject.DestroyImmediate(wrapper);
            m_serializedObject.Update();

            // Bind the serialized object to ensure property fields are connected
            Bind();

            EditorUtility.SetDirty(m_dungeonGraph);
        }

        private void CopyNodeFields(DungeonGraphNode source, DungeonGraphNode destination)
        {
            // Get all fields from the source node's type hierarchy
            var type = source.GetType();
            while (type != null && type != typeof(object))
            {
                var fields = type.GetFields(System.Reflection.BindingFlags.Public |
                                           System.Reflection.BindingFlags.NonPublic |
                                           System.Reflection.BindingFlags.Instance |
                                           System.Reflection.BindingFlags.DeclaredOnly);

                foreach (var field in fields)
                {
                    // Skip guid and position fields - we handle those separately
                    if (field.Name == "m_guid" || field.Name == "m_position")
                        continue;

                    // Skip readonly/const fields
                    if (field.IsLiteral || field.IsInitOnly)
                        continue;

                    try
                    {
                        var value = field.GetValue(source);
                        field.SetValue(destination, value);
                    }
                    catch
                    {
                        // Skip fields that can't be copied
                    }
                }

                type = type.BaseType;
            }
        }

        private bool CanPasteSerializedDataCallback(string data)
        {
            try
            {
                var wrapper = ScriptableObject.CreateInstance<CopyPasteWrapper>();
                EditorJsonUtility.FromJsonOverwrite(data, wrapper);
                bool canPaste = wrapper?.nodes != null && wrapper.nodes.Count > 0;
                ScriptableObject.DestroyImmediate(wrapper);
                return canPaste;
            }
            catch
            {
                return false;
            }
        }

        private class CopyPasteWrapper : ScriptableObject
        {
            [SerializeReference]
            public List<DungeonGraphNode> nodes = new List<DungeonGraphNode>();
        }
    }
}
