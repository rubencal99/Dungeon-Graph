using UnityEngine;
using UnityEditor; 
using System.Collections.Generic;

namespace DungeonGraph.Editor
{
    public static class DungeonGenerator
    {
        // Data structures for graph
        private class NodeInfo { public string id; /* other metadata like type, size */ }

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
            roomObj.name = $"{node.typeName}_Room";  // Name the instance for clarity
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


