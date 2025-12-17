using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Organic generation method using force-directed layout with spring physics
    /// </summary>
    public static class OrganicGeneration
    {
        public static void GenerateRooms(DungeonGraphAsset graph, Transform parent = null,
            float areaPlacementFactor = 2.0f, float repulsionFactor = 1.0f, int simulationIterations = 100,
            bool forceMode = false, float stiffnessFactor = 1.0f, float chaosFactor = 0.0f,
            bool realTimeSimulation = false, float simulationSpeed = 30f, float idealDistance = 20f)
        {
            if (graph == null)
            {
                Debug.LogError("Cannot generate dungeon: graph is null");
                return;
            }

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
                var bounds = GetNodeBounds(node);
                nodeBounds[node.id] = bounds;
                totalArea += bounds.size.x * bounds.size.y;
            }

            // Calculate placement area based on total room area and placement factor
            float placementRadius = Mathf.Sqrt(totalArea * areaPlacementFactor) / 2f;
            //Debug.Log($"[OrganicGeneration] Total room area: {totalArea:F2}, Placement radius: {placementRadius:F2}");

            // 2. Calculate graph distances (for repulsion based on separation)
            var graphDistances = CalculateGraphDistances(graph);

            // 3. Instantiate all rooms at random positions within the placement area
            var roomInstances = new Dictionary<string, GameObject>();
            var roomPositions = new Dictionary<string, Vector3>();

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

                var roomObj = InstantiateRoomForNode(node, parent);
                if (roomObj != null)
                {
                    roomInstances[node.id] = roomObj;
                    roomPositions[node.id] = initialPos;
                }
            }

            // 4. Run force-directed simulation (instant or real-time)
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
                    idealDistance = idealDistance
                };

                //Debug.Log("[OrganicGeneration] Real-time simulation started! Tilemap merge will occur after simulation completes.");
                controller.StartSimulation(graph, roomInstances, roomPositions, nodeBounds, simParams);
                
                // Controller will handle position updates and post-simulation setup
                // Early return - rest of the setup happens after simulation
                return;
            }
            else
            {
                // Instant mode: run simulation immediately
                SimulateForces(graph, roomInstances, roomPositions, graphDistances,
                    repulsionFactor, simulationIterations, forceMode, stiffnessFactor, chaosFactor, idealDistance);

                // Apply final positions immediately
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
                tilemapSystem.GenerateAllCorridors(graph, roomInstances, tilemapSystem.corridorType);
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
        public static void GenerateCorridors(DungeonGraphAsset graph, GameObject dungeonParent)
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

            // Generate corridors using the corridor type from tilemap system
            //Debug.Log($"[OrganicGeneration] Generating corridors (type: {tilemapSystem.corridorType})...");
            tilemapSystem.GenerateAllCorridors(graph, roomInstances, tilemapSystem.corridorType);

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

            //Debug.Log($"[OrganicGeneration] Starting simulation with {(forceMode ? "Force Mode (max " + maxIterations + " iterations)" : iterations + " iterations")}");

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

                            // Spring force proportional to distance from ideal
                            float force = springStiffness * (distance - idealDistance);
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

                            // Stronger repulsion for nodes that are farther apart in the graph
                            float repulsion = repulsionStrength * graphDist / (distance * distance);

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
                        ) * chaosFactor; // Scale chaos effect

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

            //Debug.Log("[OrganicGeneration] Simulation complete");
        }

        // Helper methods from ConstraintGeneration
        private static Bounds GetNodeBounds(DungeonGraphNode node)
        {
            string typeName = node.GetType().Name;
            string folderPath;

            if (typeName.Contains("Basic"))
            {
                string sizeCategory = "Small";
                var sizeField = node.GetType().GetField("size");
                if (sizeField != null)
                    sizeCategory = sizeField.GetValue(node).ToString();
                folderPath = $"Assets/Rooms/Basic/{sizeCategory}";
            }
            else
            {
                string typeFolder = typeName.Replace("Node", "");
                folderPath = $"Assets/Rooms/{typeFolder}";
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

        private static GameObject InstantiateRoomForNode(DungeonGraphNode node, Transform parent = null)
        {
            string typeName = node.GetType().Name;
            string folderPath;

            if (typeName.Contains("Basic"))
            {
                string sizeCategory = "Small";
                var sizeField = node.GetType().GetField("size");
                if (sizeField != null)
                    sizeCategory = sizeField.GetValue(node).ToString();
                folderPath = $"Assets/Rooms/Basic/{sizeCategory}";
            }
            else
            {
                string typeFolder = typeName.Replace("Node", "");
                folderPath = $"Assets/Rooms/{typeFolder}";
            }

            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            if (prefabGUIDs.Length == 0)
            {
                Debug.LogError($"No prefabs found in {folderPath} for node type {typeName}");
                return null;
            }

            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGUIDs[Random.Range(0, prefabGUIDs.Length)]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject roomObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) roomObj.transform.SetParent(parent);

            string simpleType = typeName.Replace("Node", "");
            roomObj.name = $"{simpleType} Room";

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
    }
}
