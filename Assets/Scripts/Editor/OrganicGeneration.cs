using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Spatial hash grid for efficient O(n) collision detection and proximity queries
    /// </summary>
    public class SpatialHashGrid
    {
        private readonly Dictionary<Vector2Int, List<string>> grid;
        private readonly float cellSize;
        private Dictionary<string, Vector3> positions;
        private Dictionary<string, float> radii;

        public SpatialHashGrid(float cellSize)
        {
            this.cellSize = cellSize;
            this.grid = new Dictionary<Vector2Int, List<string>>();
        }

        /// <summary>
        /// Rebuild the grid with current room positions and radii
        /// </summary>
        public void UpdateGrid(Dictionary<string, Vector3> roomPositions, Dictionary<string, float> roomRadii)
        {
            grid.Clear();
            positions = roomPositions;
            radii = roomRadii;

            foreach (var kvp in roomPositions)
            {
                string nodeId = kvp.Key;
                Vector3 position = kvp.Value;
                float radius = roomRadii.ContainsKey(nodeId) ? roomRadii[nodeId] : 5f;

                // Add to all cells this room occupies
                foreach (var cell in GetOccupiedCells(position, radius))
                {
                    if (!grid.ContainsKey(cell))
                        grid[cell] = new List<string>();

                    grid[cell].Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Get all cells that a room at given position with given radius occupies
        /// </summary>
        private List<Vector2Int> GetOccupiedCells(Vector3 position, float radius)
        {
            var cells = new List<Vector2Int>();

            // Calculate bounding box of the room
            int minX = Mathf.FloorToInt((position.x - radius) / cellSize);
            int maxX = Mathf.FloorToInt((position.x + radius) / cellSize);
            int minY = Mathf.FloorToInt((position.y - radius) / cellSize);
            int maxY = Mathf.FloorToInt((position.y + radius) / cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }

            return cells;
        }

        /// <summary>
        /// Get all nearby rooms to check for repulsion (includes rooms in neighboring cells)
        /// </summary>
        public List<string> GetNearbyRooms(string nodeId)
        {
            if (!positions.ContainsKey(nodeId))
                return new List<string>();

            Vector3 position = positions[nodeId];
            float radius = radii.ContainsKey(nodeId) ? radii[nodeId] : 5f;

            var nearbyRooms = new HashSet<string>(); // Use HashSet to avoid duplicates

            // Check all cells this room occupies plus their neighbors
            foreach (var cell in GetOccupiedCells(position, radius))
            {
                // Check this cell and all 8 neighboring cells
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Vector2Int neighborCell = new Vector2Int(cell.x + dx, cell.y + dy);

                        if (grid.ContainsKey(neighborCell))
                        {
                            foreach (string roomId in grid[neighborCell])
                            {
                                // Don't include self
                                if (roomId != nodeId)
                                {
                                    nearbyRooms.Add(roomId);
                                }
                            }
                        }
                    }
                }
            }

            return nearbyRooms.ToList();
        }
    }

    /// <summary>
    /// Organic generation method using force-directed layout with spring physics
    /// </summary>
    public static class OrganicGeneration
    {
        // Track spawned rooms per type to prevent duplicates until all are used
        private static Dictionary<string, List<string>> spawnedRoomsByType = new Dictionary<string, List<string>>();

        public static void GenerateRooms(DungeonGraphAsset graph, Transform parent = null,
            float areaPlacementFactor = 2.0f, float repulsionFactor = 1.0f, int simulationIterations = 100,
            bool forceMode = false, float stiffnessFactor = 1.0f, float chaosFactor = 0.0f,
            bool realTimeSimulation = false, float simulationSpeed = 30f, float idealDistance = 20f,
            bool allowRoomOverlap = false, int maxRoomRegenerations = 3, int maxCorridorRegenerations = 3,
            string floorPath = "")
        {
            if (graph == null)
            {
                Debug.LogError("Cannot generate dungeon: graph is null");
                return;
            }

            // Reset room tracking for new generation
            spawnedRoomsByType.Clear();

            // Process spawn chances and modify graph accordingly
            ProcessSpawnChances(graph);

            // Create parent container
            if (parent == null)
            {
                var container = new GameObject("Generated_Dungeon");
                parent = container.transform;
            }

            /*
            Debug.Log($"[OrganicGeneration] Starting generation for graph with {graph.Nodes.Count} nodes");
            Debug.Log($"[OrganicGeneration] Parameters: AreaFactor={areaPlacementFactor}, RepulsionFactor={repulsionFactor}, " +
                $"Iterations={simulationIterations}, ForceMode={forceMode}, StiffnessFactor={stiffnessFactor}, ChaosFactor={chaosFactor}");
            */

            // 1. Calculate total area and bounds
            var nodeBounds = new Dictionary<string, Bounds>();
            float totalArea = 0f;
            foreach (var node in graph.Nodes)
            {
                var bounds = GetNodeBounds(node, floorPath);
                nodeBounds[node.id] = bounds;
                totalArea += bounds.size.x * bounds.size.y;
            }

            // Calculate placement area based on total room area and placement factor
            float placementRadius = Mathf.Sqrt(totalArea * areaPlacementFactor) / 2f;
            //Debug.Log($"[OrganicGeneration] Total room area: {totalArea:F2}, Placement radius: {placementRadius:F2}");

            // 2. Calculate graph distances (for repulsion based on separation)
            var graphDistances = CalculateGraphDistances(graph);

            // 3. Instantiate all rooms at random positions within the placement area
            // Wrap in regeneration loop if overlap prevention is enabled
            var roomInstances = new Dictionary<string, GameObject>();
            var roomPositions = new Dictionary<string, Vector3>();
            bool hasOverlap = false;
            int regenerationAttempt = 0;

            do
            {
                // Clear previous attempt if regenerating
                if (regenerationAttempt > 0)
                {
                    //Debug.Log($"[OrganicGeneration] Regeneration attempt {regenerationAttempt}/{maxRoomRegenerations}");

                    // Destroy previous room instances
                    foreach (var roomObj in roomInstances.Values)
                    {
                        if (roomObj != null)
                            GameObject.DestroyImmediate(roomObj);
                    }
                    roomInstances.Clear();
                    roomPositions.Clear();

                    // Reset room tracking so we can use fresh rooms on regeneration
                    spawnedRoomsByType.Clear();
                }

                // Instantiate rooms at random positions
                foreach (var node in graph.Nodes)
                {
                    // Random position within circular area
                    float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float distance = Random.Range(0f, placementRadius);
                    Vector3 initialPos = new Vector3(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance,
                        0f
                    );

                    var roomObj = InstantiateRoomForNode(node, parent, floorPath);
                    if (roomObj != null)
                    {
                        roomInstances[node.id] = roomObj;
                        roomPositions[node.id] = initialPos;
                    }
                }

                // Run simulation (only in instant mode for overlap check)
                if (!realTimeSimulation || !Application.isPlaying)
                {
                    SimulateForces(graph, roomInstances, roomPositions, graphDistances,
                        repulsionFactor, simulationIterations, forceMode, stiffnessFactor, chaosFactor, idealDistance);

                    // Check for overlap if enabled
                    if (!allowRoomOverlap && regenerationAttempt < maxRoomRegenerations)
                    {
                        hasOverlap = CheckRoomOverlap(roomInstances, roomPositions);
                        if (hasOverlap)
                        {
                            regenerationAttempt++;
                        }
                    }
                    else
                    {
                        hasOverlap = false; // Accept the result
                    }
                }
                else
                {
                    // Real-time mode: skip overlap check (interactive)
                    hasOverlap = false;
                }

            } while (hasOverlap && regenerationAttempt <= maxRoomRegenerations);

            // Warn if we hit max regenerations with overlap
            if (hasOverlap && regenerationAttempt >= maxRoomRegenerations)
            {
                Debug.LogWarning($"[OrganicGeneration] Maximum regenerations ({maxRoomRegenerations}) reached with room overlap still present. Proceeding with current layout.");
            }
            else if (regenerationAttempt > 0)
            {
                Debug.Log($"[OrganicGeneration] Successfully generated dungeon without overlap after {regenerationAttempt} regenerations.");
            }

            // 4. Apply final positions and setup visualization
            if (realTimeSimulation && Application.isPlaying) // only run realTime during runtime, otherwise breaks generation
            {
                // Realtime: Apply initial positions first, then setup controller
                foreach (var kvp in roomPositions)
                {
                    if (roomInstances.ContainsKey(kvp.Key))
                    {
                        var roomObj = roomInstances[kvp.Key];
                        var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();

                        if (roomTemplate != null)
                        {
                            Vector3 centerOffset = roomTemplate.worldBounds.center - roomObj.transform.position;
                            roomObj.transform.position = kvp.Value - centerOffset;
                        }
                        else
                        {
                            roomObj.transform.position = kvp.Value;
                        }
                    }
                }

                // Setup tilemap system before simulation (but don't merge yet)
                var realtimeTilemapSystem = parent.gameObject.GetComponent<DungeonTilemapSystem>();
                if (realtimeTilemapSystem == null)
                {
                    realtimeTilemapSystem = parent.gameObject.AddComponent<DungeonTilemapSystem>();
                    Debug.Log("[OrganicGeneration] Added DungeonTilemapSystem component");
                }

                // Try to find and assign master tilemap by tag
                if (realtimeTilemapSystem.masterTilemap == null)
                {
                    realtimeTilemapSystem.FindMasterTilemap();
                }

                // Add debug visualization before simulation so connections are visible during simulation
                var realtimeVisualizer = parent.gameObject.AddComponent<DungeonConnectionVisualizer>();
                realtimeVisualizer.connections = new List<(GameObject, GameObject)>();
                realtimeVisualizer.rooms = new List<DungeonConnectionVisualizer.RoomInfo>();

                // Populate room info with colors
                foreach (var node in graph.Nodes)
                {
                    if (roomInstances.ContainsKey(node.id))
                    {
                        string typeName = node.GetType().Name.Replace("Node", "");
                        Color nodeColor = DungeonConnectionVisualizer.GetColorForNodeType(typeName);

                        realtimeVisualizer.rooms.Add(new DungeonConnectionVisualizer.RoomInfo
                        {
                            room = roomInstances[node.id],
                            typeName = typeName,
                            color = nodeColor
                        });
                    }
                }

                // Populate connections
                foreach (var conn in graph.Connections)
                {
                    if (roomInstances.ContainsKey(conn.inputPort.nodeId) &&
                        roomInstances.ContainsKey(conn.outputPort.nodeId))
                    {
                        var roomA = roomInstances[conn.inputPort.nodeId];
                        var roomB = roomInstances[conn.outputPort.nodeId];
                        realtimeVisualizer.connections.Add((roomA, roomB));
                    }
                }

                // Now start the simulation controller
                var controller = parent.gameObject.AddComponent<DungeonSimulationController>();
                var simParams = new DungeonSimulationController.SimulationParameters
                {
                    areaPlacementFactor = areaPlacementFactor,
                    repulsionFactor = repulsionFactor,
                    simulationIterations = simulationIterations,
                    forceMode = forceMode,
                    stiffnessFactor = stiffnessFactor,
                    chaosFactor = chaosFactor,
                    simulationSpeed = simulationSpeed,
                    idealDistance = idealDistance,
                    allowRoomOverlap = allowRoomOverlap,
                    maxRoomRegenerations = maxRoomRegenerations,
                    maxCorridorRegenerations = maxCorridorRegenerations
                };

                //Debug.Log("[OrganicGeneration] Real-time simulation started! Tilemap merge will occur after simulation completes.");
                controller.StartSimulation(graph, roomInstances, roomPositions, nodeBounds, simParams);
                
                // Controller will handle position updates and post-simulation setup
                // Early return - rest of the setup happens after simulation
                return;
            }
            else
            {
                // Instant mode: apply final positions (simulation already ran in the loop above)
                foreach (var kvp in roomPositions)
                {
                    if (roomInstances.ContainsKey(kvp.Key))
                    {
                        var roomObj = roomInstances[kvp.Key];
                        var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();

                        if (roomTemplate != null)
                        {
                            Vector3 centerOffset = roomTemplate.worldBounds.center - roomObj.transform.position;
                            roomObj.transform.position = kvp.Value - centerOffset;
                        }
                        else
                        {
                            roomObj.transform.position = kvp.Value;
                        }
                    }
                }
            }

            // 5. Add debug visualization (for instant mode only)
            var visualizer = parent.gameObject.AddComponent<DungeonConnectionVisualizer>();
            visualizer.connections = new List<(GameObject, GameObject)>();
            visualizer.rooms = new List<DungeonConnectionVisualizer.RoomInfo>();

            // Populate room info with colors
            foreach (var node in graph.Nodes)
            {
                if (roomInstances.ContainsKey(node.id))
                {
                    string typeName = node.GetType().Name.Replace("Node", "");
                    Color nodeColor = DungeonConnectionVisualizer.GetColorForNodeType(typeName);

                    visualizer.rooms.Add(new DungeonConnectionVisualizer.RoomInfo
                    {
                        room = roomInstances[node.id],
                        typeName = typeName,
                        color = nodeColor
                    });
                }
            }

            // Populate connections
            foreach (var conn in graph.Connections)
            {
                if (roomInstances.ContainsKey(conn.inputPort.nodeId) &&
                    roomInstances.ContainsKey(conn.outputPort.nodeId))
                {
                    var roomA = roomInstances[conn.inputPort.nodeId];
                    var roomB = roomInstances[conn.outputPort.nodeId];
                    visualizer.connections.Add((roomA, roomB));
                }
            }

            // 6. Setup tilemap system for grid alignment
            var tilemapSystem = parent.gameObject.GetComponent<DungeonTilemapSystem>();
            if (tilemapSystem == null)
            {
                tilemapSystem = parent.gameObject.AddComponent<DungeonTilemapSystem>();
                //Debug.Log("[OrganicGeneration] Added DungeonTilemapSystem component");
            }

            // Snap rooms to grid (important for proper tilemap alignment)
            tilemapSystem.SnapRoomsToGrid(roomInstances);

            // Try to find and assign master tilemap by tag
            if (tilemapSystem.masterTilemap == null)
            {
                tilemapSystem.FindMasterTilemap();
            }

            // 8. Merge tilemaps if master tilemap is available
            if (tilemapSystem.masterTilemap != null)
            {
                tilemapSystem.MergeRoomsToMasterTilemap(roomInstances);
                //Debug.Log("[OrganicGeneration] Tilemap merge complete!");
            }
            else
            {
                Debug.LogWarning("[OrganicGeneration] Master tilemap not found. Tag a tilemap with 'Dungeon' to enable tilemap merging.");
            }

            //Debug.Log("[OrganicGeneration] Room generation complete!");
        }

        /// <summary>
        /// Called after real-time simulation completes to finalize room setup
        /// </summary>
        public static void PostSimulationSetup(DungeonGraphAsset graph, GameObject dungeonParent)
        {
            if (graph == null || dungeonParent == null)
            {
                Debug.LogError("[OrganicGeneration] Cannot perform post-simulation setup: graph or parent is null");
                return;
            }

            //Debug.Log("[OrganicGeneration] Starting post-simulation setup...");

            // Find all room instances using RoomNodeReference components
            var roomInstances = new Dictionary<string, GameObject>();
            var roomReferences = dungeonParent.GetComponentsInChildren<RoomNodeReference>();

            foreach (var roomRef in roomReferences)
            {
                if (!string.IsNullOrEmpty(roomRef.nodeId))
                {
                    roomInstances[roomRef.nodeId] = roomRef.gameObject;
                }
            }

            // 1. Visualizer should already exist from pre-simulation setup
            // Just verify it's there
            var visualizer = dungeonParent.GetComponent<DungeonConnectionVisualizer>();
            if (visualizer == null)
            {
                Debug.LogWarning("[OrganicGeneration] Visualizer not found during post-simulation setup. This is unexpected.");
            }

            // 2. Get or setup tilemap system
            var tilemapSystem = dungeonParent.GetComponent<DungeonTilemapSystem>();
            if (tilemapSystem == null)
            {
                Debug.LogError("[OrganicGeneration] No DungeonTilemapSystem found! This should have been setup before simulation.");
                return;
            }

            // 3. Snap rooms to grid (important for proper tilemap alignment)
            //Debug.Log("[OrganicGeneration] Snapping rooms to grid...");
            tilemapSystem.SnapRoomsToGrid(roomInstances);

            // 4. Merge tilemaps if master tilemap is available
            if (tilemapSystem.masterTilemap != null)
            {
                //Debug.Log("[OrganicGeneration] Merging tilemaps...");
                tilemapSystem.MergeRoomsToMasterTilemap(roomInstances);
                //Debug.Log("[OrganicGeneration] Tilemap merge complete!");
            }
            else
            {
                Debug.LogWarning("[OrganicGeneration] Master tilemap not found. Tag a tilemap with 'Dungeon' to enable tilemap merging.");
            }

            // 5. Generate corridors automatically after real-time simulation
            if (tilemapSystem.corridorTile != null && tilemapSystem.masterTilemap != null)
            {
                //Debug.Log($"[OrganicGeneration] Auto-generating corridors after simulation (type: {tilemapSystem.corridorType})...");

                // Get maxCorridorRegenerations from simulation controller
                var simController = dungeonParent.GetComponent<DungeonSimulationController>();
                int maxCorridorRegenerations = simController != null && simController.Parameters != null
                    ? simController.Parameters.maxCorridorRegenerations
                    : 3;

                tilemapSystem.GenerateAllCorridorsWithOverlapPrevention(graph, roomInstances, tilemapSystem.corridorType, maxCorridorRegenerations);
                //Debug.Log("[OrganicGeneration] Corridor generation complete!");
            }
            else
            {
                if (tilemapSystem.corridorTile == null)
                {
                    Debug.LogWarning("[OrganicGeneration] Corridor tile not assigned. Skipping auto-corridor generation.");
                }
            }

            //Debug.Log("[OrganicGeneration] Post-simulation setup complete!");
        }

        /// <summary>
        /// Generate corridors for an existing dungeon
        /// </summary>
        public static void GenerateCorridors(DungeonGraphAsset graph, GameObject dungeonParent, int maxCorridorRegenerations = 3)
        {
            if (graph == null)
            {
                Debug.LogError("[OrganicGeneration] Cannot generate corridors: graph is null");
                return;
            }

            if (dungeonParent == null)
            {
                Debug.LogError("[OrganicGeneration] Cannot generate corridors: dungeon parent is null");
                return;
            }

            // Get tilemap system
            var tilemapSystem = dungeonParent.GetComponent<DungeonTilemapSystem>();
            if (tilemapSystem == null)
            {
                Debug.LogError("[OrganicGeneration] No DungeonTilemapSystem found on dungeon parent!");
                return;
            }

            // Find all room instances using RoomNodeReference components
            var roomInstances = new Dictionary<string, GameObject>();
            var roomReferences = dungeonParent.GetComponentsInChildren<RoomNodeReference>();

            //Debug.Log($"[OrganicGeneration] Found {roomReferences.Length} RoomNodeReference components");

            foreach (var roomRef in roomReferences)
            {
                if (!string.IsNullOrEmpty(roomRef.nodeId))
                {
                    roomInstances[roomRef.nodeId] = roomRef.gameObject;
                    //Debug.Log($"[OrganicGeneration] Mapped room: {roomRef.gameObject.name} -> nodeId: {roomRef.nodeId}");
                }
                else
                {
                    Debug.LogWarning($"[OrganicGeneration] RoomNodeReference on {roomRef.gameObject.name} has empty nodeId!");
                }
            }

            if (roomInstances.Count == 0)
            {
                Debug.LogError("[OrganicGeneration] No room instances found! Make sure rooms are children of the dungeon parent.");
                return;
            }

            //Debug.Log($"[OrganicGeneration] Successfully mapped {roomInstances.Count} rooms for corridor generation");

            // Generate corridors using the corridor type from tilemap system with overlap prevention
            //Debug.Log($"[OrganicGeneration] Generating corridors (type: {tilemapSystem.corridorType})...");
            tilemapSystem.GenerateAllCorridorsWithOverlapPrevention(graph, roomInstances, tilemapSystem.corridorType, maxCorridorRegenerations);

            //Debug.Log($"[OrganicGeneration] Generated corridors for {roomInstances.Count} rooms!");
        }

        // Calculate shortest path distances between all node pairs
        private static Dictionary<(string, string), int> CalculateGraphDistances(DungeonGraphAsset graph)
        {
            var distances = new Dictionary<(string, string), int>();
            var adj = BuildAdjacency(graph);

            // All-pairs shortest paths
            foreach (var nodeA in graph.Nodes)
            {
                foreach (var nodeB in graph.Nodes)
                {
                    if (nodeA.id == nodeB.id)
                    {
                        distances[(nodeA.id, nodeB.id)] = 0;
                    }
                    else
                    {
                        distances[(nodeA.id, nodeB.id)] = 999999; // Infinity
                    }
                }
            }

            // Set distances for direct connections
            foreach (var conn in graph.Connections)
            {
                var a = conn.inputPort.nodeId;
                var b = conn.outputPort.nodeId;
                distances[(a, b)] = 1;
                distances[(b, a)] = 1;
            }

            // Floyd-Warshall
            foreach (var k in graph.Nodes)
            {
                foreach (var i in graph.Nodes)
                {
                    foreach (var j in graph.Nodes)
                    {
                        var distIK = distances[(i.id, k.id)];
                        var distKJ = distances[(k.id, j.id)];
                        var distIJ = distances[(i.id, j.id)];

                        if (distIK + distKJ < distIJ)
                        {
                            distances[(i.id, j.id)] = distIK + distKJ;
                        }
                    }
                }
            }

            return distances;
        }

        // Simulate spring forces between connected rooms and repulsion between all rooms
        private static void SimulateForces(DungeonGraphAsset graph, Dictionary<string, GameObject> roomInstances,
            Dictionary<string, Vector3> roomPositions, Dictionary<(string, string), int> graphDistances,
            float repulsionFactor, int iterations, bool forceMode, float stiffnessFactor, float chaosFactor, float idealDistance)
        {
            var adj = BuildAdjacency(graph);

            // Calculate room radii (approximate as half of max dimension)
            var roomRadii = new Dictionary<string, float>();
            foreach (var kvp in roomInstances)
            {
                var roomObj = kvp.Value;
                var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();
                if (roomTemplate != null)
                {
                    var bounds = roomTemplate.worldBounds;
                    // Use the maximum of width/height as the diameter, then halve for radius
                    float radius = Mathf.Max(bounds.size.x, bounds.size.y) / 2f;
                    roomRadii[kvp.Key] = radius;
                }
                else
                {
                    roomRadii[kvp.Key] = 5f; // Default radius
                }
            }

            // IMPORTANT: Some magic numbers here, needs more iteration/fine-tuning/editor customization
            float springStiffness = 0.01f * stiffnessFactor;  // Attraction force for connected rooms (affected by stiffness factor)
            float repulsionStrength = 50f * repulsionFactor;  // Base repulsion between rooms
            float damping = 0.9f;  // Velocity damping
            var velocities = new Dictionary<string, Vector3>();

            // Initialize velocities
            foreach (var nodeId in roomPositions.Keys)
            {
                velocities[nodeId] = Vector3.zero;
            }

            int maxIterations = forceMode ? 2096 : iterations;
            float energyThreshold = 0.01f; // Energy considered "zero" for force mode

            Debug.Log($"[OrganicGeneration] Starting simulation with {(forceMode ? "Force Mode (max " + maxIterations + " iterations)" : iterations + " iterations")}");

            // IMPORTANT: Spring/Repulsion forces being updated
            for (int iter = 0; iter < maxIterations; iter++)
            {
                var forces = new Dictionary<string, Vector3>();

                // Initialize forces
                foreach (var nodeId in roomPositions.Keys)
                {
                    forces[nodeId] = Vector3.zero;
                }

                // Spring forces (attraction between connected rooms)
                foreach (var nodeId in roomPositions.Keys)
                {
                    if (!adj.ContainsKey(nodeId)) continue;

                    foreach (var neighborId in adj[nodeId])
                    {
                        if (!roomPositions.ContainsKey(neighborId)) continue;

                        Vector3 direction = roomPositions[neighborId] - roomPositions[nodeId];
                        float distance = direction.magnitude;

                        if (distance > 0.01f)
                        {
                            direction /= distance;

                            // Calculate ideal distance for this pair: radiusA + radiusB + gap
                            float radiusA = roomRadii.ContainsKey(nodeId) ? roomRadii[nodeId] : 5f;
                            float radiusB = roomRadii.ContainsKey(neighborId) ? roomRadii[neighborId] : 5f;
                            float pairIdealDistance = radiusA + radiusB + idealDistance;

                            // Spring force proportional to distance from ideal
                            float force = springStiffness * (distance - pairIdealDistance);
                            forces[nodeId] += direction * force;
                        }
                    }
                }

                // Repulsion forces (all pairs, scaled by graph distance)
                var nodes = roomPositions.Keys.ToList();
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        var nodeA = nodes[i];
                        var nodeB = nodes[j];

                        Vector3 direction = roomPositions[nodeB] - roomPositions[nodeA];
                        float distance = direction.magnitude;

                        if (distance > 0.01f)
                        {
                            direction /= distance;

                            // Get graph distance for repulsion scaling
                            int graphDist = graphDistances.ContainsKey((nodeA, nodeB))
                                ? graphDistances[(nodeA, nodeB)]
                                : 1;

                            // Cap graph distance to prevent infinity issues with disjointed graphs
                            // If nodes are unreachable (graphDist >= 999999), treat them as maximally distant (10)
                            if (graphDist >= 999999)
                            {
                                graphDist = 10; // Cap at a reasonable maximum
                            }

                            // Stronger repulsion for nodes that are farther apart in the graph
                            float repulsion = (repulsionStrength * graphDist) / (distance * distance);

                            // Safety check: prevent infinite or NaN values
                            if (float.IsNaN(repulsion) || float.IsInfinity(repulsion))
                            {
                                Debug.LogError($"[OrganicGeneration] Invalid repulsion calculated between {nodeA} and {nodeB}. GraphDist={graphDist}, Distance={distance}");
                                continue;
                            }

                            forces[nodeA] -= direction * repulsion;
                            forces[nodeB] += direction * repulsion;
                        }
                    }
                }

                // Update velocities and positions (store updates first to avoid collection modification)
                var positionUpdates = new Dictionary<string, Vector3>();
                foreach (var nodeId in roomPositions.Keys.ToList())
                {
                    velocities[nodeId] += forces[nodeId];

                    // Apply chaos factor: add random perturbations to velocities
                    if (chaosFactor > 0f)
                    {
                        Vector3 chaosVelocity = new Vector3(
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f),
                            0f
                        ) * chaosFactor * 5f;

                        velocities[nodeId] += chaosVelocity;
                    }

                    velocities[nodeId] *= damping;
                    positionUpdates[nodeId] = roomPositions[nodeId] + velocities[nodeId];
                    positionUpdates[nodeId] = new Vector3(positionUpdates[nodeId].x, positionUpdates[nodeId].y, 0f);
                }

                // Apply all position updates
                foreach (var kvp in positionUpdates)
                {
                    roomPositions[kvp.Key] = kvp.Value;
                }

                // Calculate total energy
                float totalEnergy = velocities.Values.Sum(v => v.sqrMagnitude);

                // Log progress every 20 iterations
                // if (iter % 20 == 0)
                // {
                //     Debug.Log($"[OrganicGeneration] Iteration {iter}/{maxIterations}, Total energy: {totalEnergy:F2}");
                // }

                // In force mode, stop when energy is near zero
                if (forceMode && totalEnergy < energyThreshold)
                {
                    Debug.Log($"[OrganicGeneration] Force mode converged at iteration {iter} with energy {totalEnergy:F4}");
                    break;
                }
            }

            Debug.Log($"[OrganicGeneration] Simulation complete.");
        }

        // Helper methods from ConstraintGeneration
        private static Bounds GetNodeBounds(DungeonGraphNode node, string floorPath = "")
        {
            string typeName = node.GetType().Name;
            string folderPath;

            // Use floor path if provided, otherwise fall back to legacy path
            string basePath = string.IsNullOrEmpty(floorPath) ? "Assets/Rooms" : floorPath;

            if (typeName.Contains("Basic"))
            {
                string sizeCategory = "Small";
                var sizeField = node.GetType().GetField("size");
                if (sizeField != null)
                    sizeCategory = sizeField.GetValue(node).ToString();
                folderPath = $"{basePath}/Basic/{sizeCategory}";
            }
            else if (node is CustomNode customNode)
            {
                folderPath = $"{basePath}/{customNode.customTypeName}";
            }
            else
            {
                string typeFolder = typeName.Replace("Node", "");
                folderPath = $"{basePath}/{typeFolder}";
            }

            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            if (prefabGUIDs.Length > 0)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGUIDs[0]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    var roomTemplate = prefab.GetComponent<DungeonGraph.RoomTemplate>();
                    if (roomTemplate != null && roomTemplate.worldBounds.size.magnitude > 0.1f)
                    {
                        return roomTemplate.worldBounds;
                    }
                }
            }

            return new Bounds(Vector3.zero, new Vector3(10f, 10f, 1f));
        }

        private static GameObject InstantiateRoomForNode(DungeonGraphNode node, Transform parent = null, string floorPath = "")
        {
            string typeName = node.GetType().Name;
            string folderPath;

            // Use floor path if provided, otherwise fall back to legacy path
            string basePath = string.IsNullOrEmpty(floorPath) ? "Assets/Rooms" : floorPath;

            if (typeName == "BasicNode")
            {
                string sizeCategory = "Small";
                var sizeField = node.GetType().GetField("size");
                if (sizeField != null)
                    sizeCategory = sizeField.GetValue(node).ToString();
                folderPath = $"{basePath}/Basic/{sizeCategory}";
            }
            else if (node is CustomNode customNode)
            {
                folderPath = $"{basePath}/{customNode.customTypeName}";
            }
            else
            {
                string typeFolder = typeName.Replace("Node", "");
                folderPath = $"{basePath}/{typeFolder}";
            }

            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            if (prefabGUIDs.Length == 0)
            {
                Debug.LogError($"No prefabs found in {folderPath} for node type {typeName}");
                return null;
            }

            // Convert GUIDs to paths
            string[] allPrefabPaths = prefabGUIDs.Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToArray();

            // Initialize tracking for this folder type if not already done
            string typeKey = folderPath; // Use folder path as unique key for this room type
            if (!spawnedRoomsByType.ContainsKey(typeKey))
            {
                spawnedRoomsByType[typeKey] = new List<string>();
            }

            // Get list of available (not yet spawned) rooms
            var availablePrefabs = allPrefabPaths.Where(path => !spawnedRoomsByType[typeKey].Contains(path)).ToList();

            // If all rooms have been spawned, reset the tracking to allow duplicates
            if (availablePrefabs.Count == 0)
            {
                //Debug.Log($"[OrganicGeneration] All {allPrefabPaths.Length} rooms of type '{typeKey}' have been spawned. Resetting to allow duplicates.");
                spawnedRoomsByType[typeKey].Clear();
                availablePrefabs = allPrefabPaths.ToList();
            }

            // Randomly select from available prefabs
            string prefabPath = availablePrefabs[Random.Range(0, availablePrefabs.Count)];

            //Debug.Log($"[OrganicGeneration] Selected room from {typeKey}: {System.IO.Path.GetFileNameWithoutExtension(prefabPath)} ({availablePrefabs.Count}/{allPrefabPaths.Length} available)");

            // Track this prefab as spawned
            spawnedRoomsByType[typeKey].Add(prefabPath);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            // DEBUG: Check prefab's exits BEFORE instantiation
            var prefabTemplate = prefab.GetComponent<DungeonGraph.RoomTemplate>();
            if (prefabTemplate != null)
            {
                int prefabExitCount = prefabTemplate.exits != null ? prefabTemplate.exits.Length : 0;
                //Debug.Log($"[OrganicGeneration] PREFAB {prefab.name} has {prefabExitCount} exits in array");
            }

            GameObject roomObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) roomObj.transform.SetParent(parent);

            string simpleType = typeName.Replace("Node", "");
            roomObj.name = $"{simpleType} Room";

            // Ensure exits are populated (fixes prefab instantiation issue where exits array can be empty)
            var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();
            if (roomTemplate != null)
            {
                // DEBUG: Check exits BEFORE repopulation
                int beforeCount = roomTemplate.exits != null ? roomTemplate.exits.Length : 0;
                //Debug.Log($"[OrganicGeneration] INSTANCE {roomObj.name} has {beforeCount} exits BEFORE repopulation");

                roomTemplate.RepopulateExitsIfNeeded();

                // Log the status for debugging
                if (roomTemplate.exits != null && roomTemplate.exits.Length > 0)
                {
                    int validExits = 0;
                    foreach (var exit in roomTemplate.exits)
                    {
                        if (exit != null) validExits++;
                    }
                    //Debug.Log($"[OrganicGeneration] INSTANCE {roomObj.name} has {validExits} valid exits out of {roomTemplate.exits.Length} AFTER repopulation");
                }
                else
                {
                    //Debug.LogWarning($"[OrganicGeneration] {roomObj.name} has no exits defined after repopulation attempt");
                }
            }

            // Add node reference component for corridor generation
            var nodeRef = roomObj.AddComponent<RoomNodeReference>();
            nodeRef.nodeId = node.id;
            nodeRef.nodeTypeName = simpleType;

            return roomObj;
        }

        private static Dictionary<string, List<string>> BuildAdjacency(DungeonGraphAsset graph)
        {
            var adjacency = new Dictionary<string, List<string>>();
            foreach (var conn in graph.Connections)
            {
                string a = conn.inputPort.nodeId;
                string b = conn.outputPort.nodeId;
                if (!adjacency.ContainsKey(a)) adjacency[a] = new List<string>();
                if (!adjacency.ContainsKey(b)) adjacency[b] = new List<string>();
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
            return adjacency;
        }

        /// <summary>
        /// Check if any rooms are overlapping based on their bounds
        /// </summary>
        private static bool CheckRoomOverlap(Dictionary<string, GameObject> roomInstances, Dictionary<string, Vector3> roomPositions)
        {
            var roomKeys = roomInstances.Keys.ToList();

            for (int i = 0; i < roomKeys.Count; i++)
            {
                for (int j = i + 1; j < roomKeys.Count; j++)
                {
                    var keyA = roomKeys[i];
                    var keyB = roomKeys[j];

                    var roomA = roomInstances[keyA];
                    var roomB = roomInstances[keyB];

                    var templateA = roomA.GetComponent<DungeonGraph.RoomTemplate>();
                    var templateB = roomB.GetComponent<DungeonGraph.RoomTemplate>();

                    if (templateA == null || templateB == null) continue;

                    // Get bounds at current positions
                    var boundsA = templateA.worldBounds;
                    var boundsB = templateB.worldBounds;

                    // Translate bounds to simulation positions
                    var posA = roomPositions[keyA];
                    var posB = roomPositions[keyB];

                    // Create bounds centered at simulation positions
                    var adjustedBoundsA = new Bounds(posA, boundsA.size);
                    var adjustedBoundsB = new Bounds(posB, boundsB.size);

                    // Check for intersection
                    if (adjustedBoundsA.Intersects(adjustedBoundsB))
                    {
                        //Debug.LogWarning($"[OrganicGeneration] Overlap detected between {roomA.name} and {roomB.name}");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Process spawn chances for nodes with <= 2 connections
        /// If a node doesn't spawn, connect its neighbors directly
        /// </summary>
        private static void ProcessSpawnChances(DungeonGraphAsset graph)
        {
            if (graph == null || graph.Nodes == null || graph.Connections == null)
                return;

            var nodesToRemove = new List<DungeonGraphNode>();
            var connectionsToAdd = new List<DungeonGraphConnection>();
            var connectionsToRemove = new List<DungeonGraphConnection>();

            foreach (var node in graph.Nodes)
            {
                // Skip nodes with 100% spawn chance
                if (node.spawnChance >= 100f)
                    continue;

                // Get all connections for this node
                var nodeConnections = graph.Connections
                    .Where(c => c.inputPort.nodeId == node.id || c.outputPort.nodeId == node.id)
                    .ToList();

                // Only process nodes with <= 2 connections
                if (nodeConnections.Count > 2)
                {
                    Debug.Log($"[OrganicGeneration] Node {node.GetType().Name} has more than 2 connections.");
                    continue;
                    
                }

                // Roll for spawn
                float roll = Random.Range(0f, 100f);
                if (roll >= node.spawnChance)
                {
                    // Node doesn't spawn - remove it and connect its neighbors
                    Debug.Log($"[OrganicGeneration] Node {node.GetType().Name} failed spawn chance ({roll:F1}% >= {node.spawnChance:F1}%)");

                    nodesToRemove.Add(node);
                    connectionsToRemove.AddRange(nodeConnections);

                    // If the node has exactly 2 connections, connect them to each other
                    if (nodeConnections.Count == 2)
                    {
                        var conn1 = nodeConnections[0];
                        var conn2 = nodeConnections[1];

                        // Determine the two neighbors
                        string neighbor1 = conn1.inputPort.nodeId == node.id ? conn1.outputPort.nodeId : conn1.inputPort.nodeId;
                        string neighbor2 = conn2.inputPort.nodeId == node.id ? conn2.outputPort.nodeId : conn2.inputPort.nodeId;

                        // Create new connection between neighbors (if they're not already connected)
                        bool alreadyConnected = graph.Connections.Any(c =>
                            (c.inputPort.nodeId == neighbor1 && c.outputPort.nodeId == neighbor2) ||
                            (c.inputPort.nodeId == neighbor2 && c.outputPort.nodeId == neighbor1));

                        if (!alreadyConnected && neighbor1 != neighbor2)
                        {
                            var newConnection = new DungeonGraphConnection(neighbor1, 0, neighbor2, 0);
                            connectionsToAdd.Add(newConnection);
                            Debug.Log($"[OrganicGeneration] Connecting neighbors: {neighbor1} <-> {neighbor2}");
                        }
                    }
                }
            }

            // Apply changes to graph
            foreach (var node in nodesToRemove)
            {
                graph.Nodes.Remove(node);
            }

            foreach (var connection in connectionsToRemove)
            {
                graph.Connections.Remove(connection);
            }

            foreach (var connection in connectionsToAdd)
            {
                graph.Connections.Add(connection);
            }

            if (nodesToRemove.Count > 0)
            {
                Debug.Log($"[OrganicGeneration] Removed {nodesToRemove.Count} nodes due to spawn chance");
            }
        }
    }
}
