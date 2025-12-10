using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Constraint-based generation method for deterministic room placement
    /// </summary>
    public static class ConstraintGeneration
    {
        // Main entry point for dungeon generation
        public static void GenerateDungeon(DungeonGraphAsset graph, Transform parent = null)
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

            Debug.Log($"[ConstraintGeneration] Starting generation for graph with {graph.Nodes.Count} nodes");

            // 1. Find all loops in the graph
            var loops = FindAllLoops(graph);
            Debug.Log($"[ConstraintGeneration] Found {loops.Count} loops");

            // 2. Create parent containers for each loop and instantiate loop rooms
            var loopParents = new Dictionary<int, Transform>();
            var loopRoomInstances = new Dictionary<int, Dictionary<string, GameObject>>();
            var allRoomInstances = new Dictionary<string, GameObject>();
            var nodesInLoops = new HashSet<string>();

            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                var loopContainer = new GameObject($"Loop {i + 1}");
                loopContainer.transform.SetParent(parent);
                loopParents[i] = loopContainer.transform;

                Debug.Log($"[ConstraintGeneration] Creating Loop {i + 1} with {loop.Count} rooms");

                // Instantiate rooms for this loop in circular layout
                var loopRooms = InstantiateLoopRooms(graph, loop, loopContainer.transform);
                loopRoomInstances[i] = loopRooms;

                // Track all rooms in loops and add to global instances
                foreach (var kvp in loopRooms)
                {
                    nodesInLoops.Add(kvp.Key);
                    allRoomInstances[kvp.Key] = kvp.Value;
                }
            }

            // 3. Connect loops that are connected in the graph
            ConnectLoops(graph, loops, loopParents, loopRoomInstances);

            // 4. Find all tree nodes (nodes not in any loop)
            var treeNodes = new List<string>();
            foreach (var node in graph.Nodes)
            {
                if (!nodesInLoops.Contains(node.id))
                    treeNodes.Add(node.id);
            }

            Debug.Log($"[ConstraintGeneration] Found {treeNodes.Count} tree nodes to place");

            // 5. Instantiate and connect tree nodes
            if (treeNodes.Count > 0)
            {
                InstantiateTreeNodes(graph, treeNodes, parent, allRoomInstances, nodesInLoops);
            }

            // 6. Add debug visualization component to show connections
            var visualizer = parent.gameObject.AddComponent<DungeonConnectionVisualizer>();
            visualizer.connections = new List<(GameObject, GameObject)>();
            visualizer.rooms = new List<DungeonConnectionVisualizer.RoomInfo>();

            // Populate room info with colors
            foreach (var node in graph.Nodes)
            {
                if (allRoomInstances.ContainsKey(node.id))
                {
                    string typeName = node.GetType().Name.Replace("Node", "");
                    Color nodeColor = DungeonConnectionVisualizer.GetColorForNodeType(typeName);

                    visualizer.rooms.Add(new DungeonConnectionVisualizer.RoomInfo
                    {
                        room = allRoomInstances[node.id],
                        typeName = typeName,
                        color = nodeColor
                    });
                }
            }

            // Populate connections
            foreach (var conn in graph.Connections)
            {
                if (allRoomInstances.ContainsKey(conn.inputPort.nodeId) &&
                    allRoomInstances.ContainsKey(conn.outputPort.nodeId))
                {
                    var roomA = allRoomInstances[conn.inputPort.nodeId];
                    var roomB = allRoomInstances[conn.outputPort.nodeId];
                    visualizer.connections.Add((roomA, roomB));
                }
            }

            Debug.Log("[ConstraintGeneration] Generation complete!");
        }

        // Find all loops (cycles) in the graph
        private static List<List<string>> FindAllLoops(DungeonGraphAsset graph)
        {
            var loops = new List<List<string>>();
            var adj = BuildAdjacency(graph);
            var globalVisited = new HashSet<string>();

            foreach (var node in graph.Nodes)
            {
                if (globalVisited.Contains(node.id)) continue;

                var visited = new HashSet<string>();
                var stack = new List<string>();

                if (FindCycleDFS(node.id, null, adj, visited, stack, out List<string> cycle))
                {
                    if (cycle != null && cycle.Count > 2)
                    {
                        loops.Add(cycle);
                        foreach (var n in cycle)
                            globalVisited.Add(n);
                    }
                }
            }

            return loops;
        }

        // Instantiate rooms for a loop in circular layout
        private static Dictionary<string, GameObject> InstantiateLoopRooms(DungeonGraphAsset graph, List<string> loopNodeIds, Transform parent)
        {
            var roomInstances = new Dictionary<string, GameObject>();
            int nodeCount = loopNodeIds.Count;

            // Calculate circular layout parameters
            float radius = 15f + (nodeCount * 4f);
            float angleStep = 360f / nodeCount;

            for (int i = 0; i < nodeCount; i++)
            {
                string nodeId = loopNodeIds[i];
                var node = graph.GetNode(nodeId);
                if (node == null) continue;

                float angle = (angleStep * i) * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f
                );

                var roomObj = InstantiateRoomForNode(node, parent);
                if (roomObj != null)
                {
                    // Center the room at the calculated position
                    var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();
                    if (roomTemplate != null)
                    {
                        Vector3 centerOffset = roomTemplate.worldBounds.center - roomObj.transform.position;
                        roomObj.transform.position = position - centerOffset;
                    }
                    else
                    {
                        roomObj.transform.position = position;
                    }

                    roomInstances[nodeId] = roomObj;
                    Debug.Log($"[ConstraintGeneration] Placed loop room {node.GetType().Name} at {position}");
                }
            }

            return roomInstances;
        }

        // Connect loops that share connections in the graph
        private static void ConnectLoops(DungeonGraphAsset graph, List<List<string>> loops,
            Dictionary<int, Transform> loopParents, Dictionary<int, Dictionary<string, GameObject>> loopRoomInstances)
        {
            // Build a map of which loop each node belongs to
            var nodeToLoopIndex = new Dictionary<string, int>();
            for (int i = 0; i < loops.Count; i++)
            {
                foreach (var nodeId in loops[i])
                {
                    nodeToLoopIndex[nodeId] = i;
                }
            }

            // Find connections between different loops
            var loopConnections = new HashSet<(int, int)>();
            foreach (var conn in graph.Connections)
            {
                var nodeA = conn.inputPort.nodeId;
                var nodeB = conn.outputPort.nodeId;

                if (nodeToLoopIndex.ContainsKey(nodeA) && nodeToLoopIndex.ContainsKey(nodeB))
                {
                    int loopA = nodeToLoopIndex[nodeA];
                    int loopB = nodeToLoopIndex[nodeB];

                    if (loopA != loopB)
                    {
                        // Connection between two different loops
                        var pair = loopA < loopB ? (loopA, loopB) : (loopB, loopA);
                        if (!loopConnections.Contains(pair))
                        {
                            loopConnections.Add(pair);
                            Debug.Log($"[ConstraintGeneration] Found connection between Loop {loopA + 1} and Loop {loopB + 1}");
                        }
                    }
                }
            }

            // Position loops relative to each other
            // For now, use a simple horizontal spacing strategy
            int loopIndex = 0;
            float spacing = 100f; // Space between loop centers

            foreach (var kvp in loopParents)
            {
                kvp.Value.position = new Vector3(loopIndex * spacing, 0f, 0f);
                loopIndex++;
            }
        }

        // Instantiate tree nodes (nodes not in loops) and connect them to loops or other trees
        private static void InstantiateTreeNodes(DungeonGraphAsset graph, List<string> treeNodeIds, Transform parent,
            Dictionary<string, GameObject> allRoomInstances, HashSet<string> nodesInLoops)
        {
            var adj = BuildAdjacency(graph);
            var placed = new HashSet<string>(nodesInLoops); // Start with all loop nodes as already placed
            var nodeBounds = new Dictionary<string, Bounds>();

            // Cache bounds for all tree nodes
            foreach (var nodeId in treeNodeIds)
            {
                var node = graph.GetNode(nodeId);
                if (node != null)
                    nodeBounds[nodeId] = GetNodeBounds(node);
            }

            // Build a queue starting from nodes connected to loops
            var queue = new Queue<(string nodeId, Vector3 anchorPos, Bounds anchorBounds)>();

            foreach (var nodeId in treeNodeIds)
            {
                if (!adj.ContainsKey(nodeId)) continue;

                foreach (var neighbor in adj[nodeId])
                {
                    if (placed.Contains(neighbor) && allRoomInstances.ContainsKey(neighbor))
                    {
                        // This tree node is connected to an already-placed node (loop or other tree)
                        var anchorRoom = allRoomInstances[neighbor];
                        var anchorTemplate = anchorRoom.GetComponent<DungeonGraph.RoomTemplate>();
                        Vector3 anchorPos = anchorRoom.transform.position;
                        Bounds anchorBounds = anchorTemplate != null ? anchorTemplate.worldBounds : new Bounds(anchorPos, Vector3.one * 10f);

                        if (anchorTemplate != null)
                            anchorPos += anchorTemplate.worldBounds.center - anchorRoom.transform.position;

                        queue.Enqueue((nodeId, anchorPos, anchorBounds));
                        break; // Only need one anchor point
                    }
                }
            }

            // Place tree nodes using BFS from anchor points
            while (queue.Count > 0)
            {
                var (currentId, anchorPos, anchorBounds) = queue.Dequeue();

                if (placed.Contains(currentId)) continue;

                var currentNode = graph.GetNode(currentId);
                if (currentNode == null) continue;

                var currentBounds = nodeBounds[currentId];

                // Try to place this node near the anchor
                Vector3 newPos = TryPlaceRoom(anchorPos, anchorBounds, currentBounds, allRoomInstances, nodeBounds);

                var roomObj = InstantiateRoomForNode(currentNode, parent);
                if (roomObj != null)
                {
                    var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();
                    if (roomTemplate != null)
                    {
                        Vector3 centerOffset = roomTemplate.worldBounds.center - roomObj.transform.position;
                        roomObj.transform.position = newPos - centerOffset;
                    }
                    else
                    {
                        roomObj.transform.position = newPos;
                    }

                    allRoomInstances[currentId] = roomObj;
                    placed.Add(currentId);

                    Debug.Log($"[ConstraintGeneration] Placed tree node {currentNode.GetType().Name} at {newPos}");

                    // Add neighbors to queue
                    if (adj.ContainsKey(currentId))
                    {
                        foreach (var neighbor in adj[currentId])
                        {
                            if (!placed.Contains(neighbor))
                            {
                                Vector3 currentCenter = newPos;
                                queue.Enqueue((neighbor, currentCenter, currentBounds));
                            }
                        }
                    }
                }
            }
        }

        // Get bounds for a node by loading its prefab's RoomTemplate
        private static Bounds GetNodeBounds(DungeonGraphNode node)
        {
            // Find the prefab for this node type
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

            // Try to load a prefab and get its RoomTemplate bounds
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

            // Fallback: default size
            return new Bounds(Vector3.zero, new Vector3(10f, 1f, 10f));
        }

        // Try to place a room without collisions (new version for GameObject instances)
        private static Vector3 TryPlaceRoom(Vector3 parentPos, Bounds parentBounds, Bounds childBounds,
            Dictionary<string, GameObject> allRoomInstances, Dictionary<string, Bounds> nodeBounds)
        {
            // Cardinal directions only for intuitive flow (no diagonals)
            var directions = new List<Vector3> {
                new Vector3(0, 1, 0),      // North (up in Y)
                new Vector3(1, 0, 0),      // East (right in X)
                new Vector3(0, -1, 0),     // South (down in Y)
                new Vector3(-1, 0, 0)      // West (left in X)
            };

            // Randomize direction order for varied layouts
            ShuffleList(directions);

            // Calculate edge-to-edge spacing (rooms should be close but not overlapping)
            // Distance = half of parent's extent in direction + half of child's extent + small gap
            float minGap = Random.Range(0.5f, 2f); // Small gap for natural feel

            foreach (var dir in directions)
            {
                // Calculate how far to move based on room sizes in this direction
                float parentExtent = (Mathf.Abs(dir.x) * parentBounds.size.x + Mathf.Abs(dir.y) * parentBounds.size.y) * 0.5f;
                float childExtent = (Mathf.Abs(dir.x) * childBounds.size.x + Mathf.Abs(dir.y) * childBounds.size.y) * 0.5f;
                float distance = parentExtent + childExtent + minGap;

                Vector3 candidatePos = parentPos + dir * distance;
                candidatePos.z = 0f; // Ensure Z is always 0

                // Check collision with all existing rooms using actual bounds
                if (!CheckCollisionWithInstances(candidatePos, childBounds, allRoomInstances))
                {
                    return candidatePos;
                }
            }

            // Fallback: try with increased spacing
            foreach (var dir in directions)
            {
                float parentExtent = (Mathf.Abs(dir.x) * parentBounds.size.x + Mathf.Abs(dir.y) * parentBounds.size.y) * 0.5f;
                float childExtent = (Mathf.Abs(dir.x) * childBounds.size.x + Mathf.Abs(dir.y) * childBounds.size.y) * 0.5f;
                float distance = (parentExtent + childExtent) * 2f + Random.Range(2f, 5f);

                Vector3 candidatePos = parentPos + dir * distance;
                candidatePos.z = 0f;

                if (!CheckCollisionWithInstances(candidatePos, childBounds, allRoomInstances))
                {
                    return candidatePos;
                }
            }

            // Last resort: place far away
            return new Vector3(parentPos.x + 50f, parentPos.y, 0f);
        }

        // Check if a position would cause collision with existing room instances
        private static bool CheckCollisionWithInstances(Vector3 position, Bounds bounds,
            Dictionary<string, GameObject> allRoomInstances)
        {
            Bounds testBounds = new Bounds(position, bounds.size);

            foreach (var kvp in allRoomInstances)
            {
                GameObject existingRoom = kvp.Value;
                var existingTemplate = existingRoom.GetComponent<DungeonGraph.RoomTemplate>();

                Vector3 existingPos = existingRoom.transform.position;
                Bounds existingBounds;

                if (existingTemplate != null)
                {
                    existingPos += existingTemplate.worldBounds.center - existingRoom.transform.position;
                    existingBounds = new Bounds(existingPos, existingTemplate.worldBounds.size);
                }
                else
                {
                    existingBounds = new Bounds(existingPos, Vector3.one * 10f);
                }

                if (testBounds.Intersects(existingBounds))
                {
                    return true; // Collision detected
                }
            }

            return false; // No collision
        }

        // Fisher-Yates shuffle for randomizing list order
        private static void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int randomIndex = Random.Range(0, i + 1);
                T temp = list[i];
                list[i] = list[randomIndex];
                list[randomIndex] = temp;
            }
        }

        // Build adjacency list from graph connections
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

        // DFS traversal to detect a cycle in an undirected graph
        private static bool FindCycleDFS(string nodeId, string parentId,
                                        Dictionary<string, List<string>> adj,
                                        HashSet<string> visited,
                                        List<string> stack,
                                        out List<string> cycleNodes)
        {
            visited.Add(nodeId);
            stack.Add(nodeId);
            cycleNodes = null;
            if (!adj.ContainsKey(nodeId))
                return false;
            foreach (string neighbor in adj[nodeId])
            {
                if (neighbor == parentId) continue; // skip the edge back to parent
                if (!visited.Contains(neighbor))
                {
                    // Recurse into neighbor
                    if (FindCycleDFS(neighbor, nodeId, adj, visited, stack, out cycleNodes))
                        return true;
                }
                else
                {
                    // Found a back-edge to a visited node -> cycle detected
                    int cycleStartIndex = stack.IndexOf(neighbor);
                    if (cycleStartIndex != -1)
                    {
                        cycleNodes = stack.GetRange(cycleStartIndex, stack.Count - cycleStartIndex);
                    }
                    return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            return false;
        }

        // Helper to pick and instantiate a room prefab for a given node
        private static GameObject InstantiateRoomForNode(DungeonGraphNode node, Transform parent = null)
        {
            string typeName = node.GetType().Name;  // e.g. "StartNode", "HubNode", etc.
            string folderPath;
            if (typeName.Contains("Basic"))
            {
                // Assume the node has a field "size" indicating "Small" or "Medium"
                string sizeCategory = "Small";
                var sizeField = node.GetType().GetField("size");
                if (sizeField != null)
                    sizeCategory = sizeField.GetValue(node).ToString();
                folderPath = $"Assets/Rooms/Basic/{sizeCategory}";
            }
            else
            {
                // e.g. "StartNode" -> "Start"
                string typeFolder = typeName.Replace("Node", "");
                folderPath = $"Assets/Rooms/{typeFolder}";
            }

            // Find prefabs in the folder
            string[] prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            if (prefabGUIDs.Length == 0)
            {
                Debug.LogError($"No prefabs found in {folderPath} for node type {typeName}");
                return null;
            }
            // Pick a random prefab from the results
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGUIDs[Random.Range(0, prefabGUIDs.Length)]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            // Instantiate the prefab in the scene (as an editor operation)
            GameObject roomObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) roomObj.transform.SetParent(parent);

            // Simplified naming: just [RoomType] Room
            string simpleType = typeName.Replace("Node", "");
            roomObj.name = $"{simpleType} Room";

            return roomObj;
        }
    }
}
