using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DungeonGraph.Editor
{
    public static class DungeonGenerator
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

            Debug.Log($"[DungeonGenerator] Starting generation for graph with {graph.Nodes.Count} nodes");

            // 1. Decompose graph into composites (loops and trees)
            var composites = BuildComposites(graph);
            Debug.Log($"[DungeonGenerator] Found {composites.Count} composites");

            // 2. Calculate spatial layout for all nodes
            var placements = new Dictionary<string, Vector3>();
            foreach (var composite in composites)
            {
                if (composite.isLoop)
                {
                    Debug.Log($"[DungeonGenerator] Laying out loop composite with {composite.nodeIds.Count} nodes");
                    LayoutLoopComposite(graph, composite, placements);
                }
                else
                {
                    Debug.Log($"[DungeonGenerator] Laying out tree composite with {composite.nodeIds.Count} nodes");
                    LayoutTreeComposite(graph, composite, placements);
                }
            }

            // 3. Instantiate rooms at calculated positions
            var roomInstances = new Dictionary<string, GameObject>();
            foreach (var node in graph.Nodes)
            {
                var roomObj = InstantiateRoomForNode(node, parent);
                if (roomObj != null && placements.ContainsKey(node.id))
                {
                    // Get the room's template to find its center offset
                    var roomTemplate = roomObj.GetComponent<DungeonGraph.RoomTemplate>();
                    if (roomTemplate != null)
                    {
                        // The placement position is where we want the center to be
                        // So we need to offset by the negative of the local center
                        Vector3 centerOffset = roomTemplate.worldBounds.center - roomObj.transform.position;
                        roomObj.transform.position = placements[node.id] - centerOffset;
                    }
                    else
                    {
                        // Fallback if no template
                        roomObj.transform.position = placements[node.id];
                    }

                    roomInstances[node.id] = roomObj;
                    Debug.Log($"[DungeonGenerator] Placed {node.GetType().Name} at {placements[node.id]}");
                }
            }

            // 4. Generate corridors between connected rooms
            Debug.Log($"[DungeonGenerator] Generating {graph.Connections.Count} corridors");
            foreach (var conn in graph.Connections)
            {
                if (roomInstances.ContainsKey(conn.inputPort.nodeId) &&
                    roomInstances.ContainsKey(conn.outputPort.nodeId))
                {
                    var roomA = roomInstances[conn.inputPort.nodeId];
                    var roomB = roomInstances[conn.outputPort.nodeId];
                    GenerateCorridor(roomA, roomB, parent);
                }
            }

            // 5. Add debug visualization component to show connections
            var visualizer = parent.gameObject.AddComponent<DungeonConnectionVisualizer>();
            visualizer.connections = new System.Collections.Generic.List<(GameObject, GameObject)>();
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

            Debug.Log("[DungeonGenerator] Generation complete!");
        }

        // Layout a tree composite using depth-first placement with collision avoidance
        private static void LayoutTreeComposite(DungeonGraphAsset graph, Composite composite, Dictionary<string, Vector3> placements)
        {
            // Find starting node (prefer StartNode, or use first node in composite)
            string startNodeId = FindTreeRoot(graph, composite);
            placements[startNodeId] = Vector3.zero;

            var adj = BuildAdjacency(graph);
            var placed = new HashSet<string> { startNodeId };
            var queue = new Queue<string>();
            queue.Enqueue(startNodeId);

            // Cache bounds for all nodes to avoid repeated lookups during collision detection
            var nodeBounds = new Dictionary<string, Bounds>();
            foreach (var nodeId in composite.nodeIds)
            {
                var node = graph.GetNode(nodeId);
                nodeBounds[nodeId] = GetNodeBounds(node);
            }

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var currentPos = placements[currentId];
                var currentBounds = nodeBounds[currentId];

                if (!adj.ContainsKey(currentId)) continue;

                foreach (var neighborId in adj[currentId])
                {
                    if (!composite.nodeIds.Contains(neighborId)) continue; // Only process nodes in this composite
                    if (placed.Contains(neighborId)) continue;

                    var neighborBounds = nodeBounds[neighborId];

                    // Try to place neighbor with collision avoidance
                    Vector3 newPos = TryPlaceRoom(currentPos, currentBounds, neighborBounds, placements, nodeBounds);

                    placements[neighborId] = newPos;
                    placed.Add(neighborId);
                    queue.Enqueue(neighborId);
                }
            }
        }

        // Layout a loop composite in a circular/polygonal arrangement
        private static void LayoutLoopComposite(DungeonGraphAsset graph, Composite composite, Dictionary<string, Vector3> placements)
        {
            if (composite.nodeIds.Count < 3)
            {
                // Too small for a loop, treat as tree
                LayoutTreeComposite(graph, composite, placements);
                return;
            }

            // Simple circular layout in 2D (XY plane) with randomization
            int nodeCount = composite.nodeIds.Count;

            // Randomize radius for variety
            float radius = Random.Range(15f, 25f) + (nodeCount * Random.Range(4f, 6f));
            float angleStep = 360f / nodeCount;

            // Random starting angle to rotate the entire loop
            float startAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;

            for (int i = 0; i < nodeCount; i++)
            {
                string nodeId = composite.nodeIds[i];

                // Add per-room angle variation for organic feel
                float angleVariation = Random.Range(-10f, 10f) * Mathf.Deg2Rad;
                float angle = startAngle + (angleStep * i * Mathf.Deg2Rad) + angleVariation;

                // Add slight radius variation per room
                float radiusVariation = Random.Range(0.9f, 1.1f);
                float variedRadius = radius * radiusVariation;

                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * variedRadius,  // X axis (horizontal)
                    Mathf.Sin(angle) * variedRadius,  // Y axis (vertical)
                    0f                                // Z = 0 (2D)
                );
                placements[nodeId] = position;
            }
        }

        // Find the best starting node for a tree composite
        private static string FindTreeRoot(DungeonGraphAsset graph, Composite composite)
        {
            // Prefer StartNode if it's in this composite
            foreach (var nodeId in composite.nodeIds)
            {
                var node = graph.GetNode(nodeId);
                if (node is StartNode)
                    return nodeId;
            }

            // Otherwise, just use the first node
            return composite.nodeIds[0];
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

        // Try to place a room without collisions
        private static Vector3 TryPlaceRoom(Vector3 parentPos, Bounds parentBounds, Bounds childBounds,
            Dictionary<string, Vector3> existingPlacements, Dictionary<string, Bounds> nodeBounds)
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
                if (!CheckCollision(candidatePos, childBounds, existingPlacements, nodeBounds))
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

                if (!CheckCollision(candidatePos, childBounds, existingPlacements, nodeBounds))
                {
                    return candidatePos;
                }
            }

            // Last resort: place far away
            return new Vector3(parentPos.x + 50f, parentPos.y, 0f);
        }

        // Check if a position would cause collision with existing rooms
        private static bool CheckCollision(Vector3 position, Bounds bounds,
            Dictionary<string, Vector3> existingPlacements, Dictionary<string, Bounds> nodeBounds)
        {
            Bounds testBounds = new Bounds(position, bounds.size);

            foreach (var kvp in existingPlacements)
            {
                string existingNodeId = kvp.Key;
                Vector3 existingPos = kvp.Value;

                Bounds existingBounds;
                if (nodeBounds.ContainsKey(existingNodeId))
                {
                    existingBounds = new Bounds(existingPos, nodeBounds[existingNodeId].size);
                }
                else
                {
                    existingBounds = new Bounds(existingPos, bounds.size);
                }

                if (testBounds.Intersects(existingBounds))
                {
                    return true; // Collision detected
                }
            }

            return false; // No collision
        }

        // Generate a simple corridor between two rooms
        private static void GenerateCorridor(GameObject roomA, GameObject roomB, Transform parent)
        {
            Vector3 startPos = roomA.transform.position;
            Vector3 endPos = roomB.transform.position;

            // For now, just log the corridor (actual tilemap generation can be added later)
            Debug.Log($"[DungeonGenerator] Corridor from {roomA.name} to {roomB.name}");

            // TODO: Generate actual corridor tilemap geometry
            // This would involve creating an L-shaped or straight corridor using Tilemap
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

        // Build composites from the graph by extracting loops
        private static List<Composite> BuildComposites(DungeonGraphAsset graph)
        {
            var composites = new List<Composite>();
            var adj = BuildAdjacency(graph);
            var visited = new HashSet<string>();
            // Track nodes already assigned to a composite
            var assigned = new HashSet<string>();

            // 1. Identify loop composites
            foreach (var node in graph.Nodes)
            {
                string nodeId = node.id;
                if (assigned.Contains(nodeId)) continue;
                // Find a cycle starting from this unassigned node, if any
                var stack = new List<string>();
                if (FindCycleDFS(nodeId, null, adj, visited, stack, out List<string> cycle))
                {
                    if (cycle != null && cycle.Count > 1)
                    {
                        // Create a loop composite
                        composites.Add(new Composite(cycle, loop: true));
                        // Mark these nodes as assigned
                        foreach (var n in cycle) assigned.Add(n);
                    }
                }
            }

            // 2. Remaining nodes -> tree composites
            foreach (var node in graph.Nodes)
            {
                string nodeId = node.id;
                if (assigned.Contains(nodeId)) continue; // skip nodes already in a loop composite
                // BFS/DFS to gather all connected nodes of this component
                if (!assigned.Contains(nodeId))
                {
                    var compNodes = new List<string>();
                    var queue = new Queue<string>();
                    queue.Enqueue(nodeId);
                    assigned.Add(nodeId);
                    while (queue.Count > 0)
                    {
                        var curr = queue.Dequeue();
                        compNodes.Add(curr);
                        if (!adj.ContainsKey(curr)) continue;
                        foreach (var nbr in adj[curr])
                        {
                            if (!assigned.Contains(nbr))
                            {
                                assigned.Add(nbr);
                                queue.Enqueue(nbr);
                            }
                        }
                    }
                    composites.Add(new Composite(compNodes, loop: false));
                }
            }

            return composites;
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

    // Composite representation
    public class Composite
    {
        public List<string> nodeIds;
        public bool isLoop;
        // Can include other info like list of internal edges or a GameObject group transform, etc.
        public Composite(List<string> nodes, bool loop)
        {
            nodeIds = nodes;
            isLoop = loop;
        }
    };
}


