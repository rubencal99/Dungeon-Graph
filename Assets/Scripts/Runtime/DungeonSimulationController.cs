using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// Runtime controller for real-time dungeon generation simulation
    /// </summary>
    public class DungeonSimulationController : MonoBehaviour
    {
        public class SimulationParameters
        {
            public float areaPlacementFactor = 2.0f;
            public float repulsionFactor = 1.0f;
            public int simulationIterations = 100;
            public bool forceMode = false;
            public float stiffnessFactor = 1.0f;
            public float chaosFactor = 0.0f;
            public float simulationSpeed = 30f; // iterations per second
            public float idealDistance = 20f; // ideal spring length between connected rooms
            public bool allowRoomOverlap = false;
            public int maxRoomRegenerations = 3;
            public int maxCorridorRegenerations = 3;
        }

        // Simulation state
        private Dictionary<string, GameObject> m_roomInstances;
        private Dictionary<string, Vector3> m_targetPositions;
        private Dictionary<string, Vector3> m_currentPositions;
        private Dictionary<string, Vector3> m_centerOffsets; // Cache of room center offsets
        private Dictionary<string, Bounds> m_nodeBounds;
        private DungeonGraphAsset m_graph;
        private SimulationParameters m_params;
        private bool m_isSimulating = false;
        private float m_lerpSpeed = 8f; // Smoothness of position interpolation
        private int m_regenerationAttempt = 0;
        private Dictionary<string, Vector3> m_initialPositions; // Store initial positions for regeneration
        private float m_placementRadius; // Store placement radius for regeneration

        // Public property to access simulation parameters
        public SimulationParameters Parameters => m_params;

        /// <summary>
        /// Start the real-time simulation with the given parameters
        /// </summary>
        public void StartSimulation(DungeonGraphAsset graph, Dictionary<string, GameObject> roomInstances,
            Dictionary<string, Vector3> initialPositions, Dictionary<string, Bounds> nodeBounds,
            SimulationParameters parameters)
        {
            if (m_isSimulating)
            {
                Debug.LogWarning("[DungeonSimulationController] Simulation already running, stopping previous simulation.");
                StopAllCoroutines();
            }

            m_graph = graph;
            m_roomInstances = roomInstances;
            m_currentPositions = new Dictionary<string, Vector3>(initialPositions);
            m_targetPositions = new Dictionary<string, Vector3>(initialPositions);
            m_initialPositions = new Dictionary<string, Vector3>(initialPositions); // Store for regeneration
            m_nodeBounds = nodeBounds;
            m_params = parameters;
            m_isSimulating = true;
            m_regenerationAttempt = 0; // Reset regeneration counter

            // Calculate placement radius for regeneration
            float totalArea = 0f;
            foreach (var bounds in nodeBounds.Values)
            {
                totalArea += bounds.size.x * bounds.size.y;
            }
            m_placementRadius = Mathf.Sqrt(totalArea * parameters.areaPlacementFactor) / 2f;

            // Cache center offsets for each room to avoid recalculating every frame
            m_centerOffsets = new Dictionary<string, Vector3>();
            foreach (var kvp in roomInstances)
            {
                var roomTemplate = kvp.Value.GetComponent<RoomTemplate>();
                if (roomTemplate != null)
                {
                    // Calculate offset from GameObject position to its visual center
                    Vector3 centerOffset = roomTemplate.worldBounds.center;
                    m_centerOffsets[kvp.Key] = centerOffset;
                }
                else
                {
                    m_centerOffsets[kvp.Key] = Vector3.zero;
                }
            }

            // Start the simulation coroutine
            StartCoroutine(SimulationCoroutine());
        }

        private IEnumerator SimulationCoroutine()
        {
            // Wait a brief moment to show initial positions
            //yield return new WaitForSeconds(0.5f);

            // Validate graph has connections
            if (m_graph.Connections == null || m_graph.Connections.Count == 0)
            {
                Debug.LogWarning("[DungeonSimulationController] Graph has no connections. Skipping physics simulation.");
                m_isSimulating = false;

                // Still need to call post-simulation setup
                try
                {
                    var postSimType = System.Type.GetType("DungeonGraph.Editor.OrganicGeneration, Assembly-CSharp-Editor");
                    if (postSimType != null)
                    {
                        var method = postSimType.GetMethod("PostSimulationSetup",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        method?.Invoke(null, new object[] { m_graph, gameObject });
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[DungeonSimulationController] Error in post-simulation: {ex.Message}");
                }

                yield return null;
                Destroy(this);
                yield break;
            }

            // Calculate graph distances for repulsion scaling
            var graphDistances = CalculateGraphDistances(m_graph);
            var adjacency = BuildAdjacency(m_graph);

            // Calculate room radii (approximate as half of max dimension)
            var roomRadii = new Dictionary<string, float>();
            foreach (var kvp in m_roomInstances)
            {
                var roomObj = kvp.Value;
                var roomTemplate = roomObj.GetComponent<RoomTemplate>();
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

            // Physics parameters
            float damping = 0.9f;
            var velocities = new Dictionary<string, Vector3>();

            // Initialize velocities
            foreach (var nodeId in m_targetPositions.Keys)
            {
                velocities[nodeId] = Vector3.zero;
            }

            int maxIterations = m_params.forceMode ? 2096 : m_params.simulationIterations;
            float energyThreshold = 0.01f;
            float iterationDelay = 1f / m_params.simulationSpeed;

            Debug.Log($"[DungeonSimulationController] Starting real-time simulation with {(m_params.forceMode ? "Force Mode (max " + maxIterations + " iterations)" : m_params.simulationIterations + " iterations")}");
            Debug.Log($"[DungeonSimulationController] Simulation speed: {m_params.simulationSpeed} iterations/sec");

            for (int iter = 0; iter < maxIterations; iter++)
            {
                var forces = new Dictionary<string, Vector3>();

                // Use shared simulation utility for force calculation
                DungeonSimulationUtility.CalculateForcesForIteration(
                    m_targetPositions,
                    adjacency,
                    graphDistances,
                    roomRadii,
                    forces,
                    m_params.repulsionFactor,
                    m_params.stiffnessFactor,
                    m_params.idealDistance);

                // Update velocities and target positions
                var positionUpdates = new Dictionary<string, Vector3>();
                foreach (var nodeId in m_targetPositions.Keys.ToList())
                {
                    velocities[nodeId] += forces[nodeId];

                    // Apply chaos factor: add random perturbations to velocities
                    if (m_params.chaosFactor > 0f)
                    {
                        Vector3 chaosVelocity = new Vector3(
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f),
                            0f
                        ) * m_params.chaosFactor * 5f;

                        velocities[nodeId] += chaosVelocity;
                    }

                    velocities[nodeId] *= damping;
                    positionUpdates[nodeId] = m_targetPositions[nodeId] + velocities[nodeId];
                    positionUpdates[nodeId] = new Vector3(positionUpdates[nodeId].x, positionUpdates[nodeId].y, 0f);
                }

                // Apply all position updates
                foreach (var kvp in positionUpdates)
                {
                    m_targetPositions[kvp.Key] = kvp.Value;
                }

                // Calculate total energy
                float totalEnergy = velocities.Values.Sum(v => v.sqrMagnitude);

                // Log progress every 20 iterations
                // if (iter % 20 == 0)
                // {
                //     Debug.Log($"[DungeonSimulationController] Iteration {iter}/{maxIterations}, Total energy: {totalEnergy:F2}");
                // }

                // In force mode, stop when energy is near zero
                if (m_params.forceMode && totalEnergy < energyThreshold)
                {
                    //Debug.Log($"[DungeonSimulationController] Force mode converged at iteration {iter} with energy {totalEnergy:F4}");
                    break;
                }

                // Wait for next iteration
                yield return new WaitForSeconds(iterationDelay);
            }

            m_isSimulating = false;
            //Debug.Log("[DungeonSimulationController] Simulation complete!");

            // Check for overlap if enabled
            if (!m_params.allowRoomOverlap && m_regenerationAttempt < m_params.maxRoomRegenerations)
            {
                bool hasOverlap = CheckRoomOverlap();
                if (hasOverlap)
                {
                    m_regenerationAttempt++;
                    Debug.Log($"[DungeonSimulationController] Overlap detected! Restarting simulation (attempt {m_regenerationAttempt}/{m_params.maxRoomRegenerations})");

                    // Restart simulation with new random positions
                    RestartSimulation();
                    yield break; // Exit this coroutine, new one will start
                }
            }

            // Warn if we hit max regenerations with overlap
            if (!m_params.allowRoomOverlap && m_regenerationAttempt >= m_params.maxRoomRegenerations)
            {
                bool finalOverlap = CheckRoomOverlap();
                if (finalOverlap)
                {
                    Debug.LogWarning($"[DungeonSimulationController] Maximum regenerations ({m_params.maxRoomRegenerations}) reached with room overlap still present. Proceeding with current layout.");
                }
            }
            else if (m_regenerationAttempt > 0)
            {
                //Debug.Log($"[DungeonSimulationController] Successfully completed simulation without overlap after {m_regenerationAttempt} regenerations.");
            }

            // Call post-simulation setup using reflection (works in Editor play mode)
            //Debug.Log("[DungeonSimulationController] Attempting to call PostSimulationSetup via reflection...");
            try
            {
                // Try multiple assembly names
                string[] assemblyNames = new string[]
                {
                    "Assembly-CSharp-Editor",
                    "Assembly-CSharp-Editor-firstpass",
                    "DungeonGraph.Editor"
                };

                System.Type postSimType = null;
                foreach (var asmName in assemblyNames)
                {
                    string typeName = $"DungeonGraph.Editor.OrganicGeneration, {asmName}";
                    postSimType = System.Type.GetType(typeName);
                    if (postSimType != null)
                    {
                        Debug.Log($"[DungeonSimulationController] Found type using assembly: {asmName}");
                        break;
                    }
                }

                if (postSimType != null)
                {
                    var method = postSimType.GetMethod("PostSimulationSetup",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        Debug.Log($"[DungeonSimulationController] About to invoke PostSimulationSetup with graph={(m_graph != null ? "valid" : "NULL")} and gameObject={(gameObject != null ? gameObject.name : "NULL")}");

                        if (m_graph == null)
                        {
                            Debug.LogError("[DungeonSimulationController] m_graph is NULL!");
                        }
                        if (gameObject == null)
                        {
                            Debug.LogError("[DungeonSimulationController] gameObject is NULL!");
                        }

                        Debug.Log("[DungeonSimulationController] Invoking PostSimulationSetup...");
                        method.Invoke(null, new object[] { m_graph, gameObject });
                        Debug.Log("[DungeonSimulationController] PostSimulationSetup completed!");

                        // Clean up the graph instance after post-simulation setup
                        if (m_graph != null)
                        {
                            Debug.Log("[DungeonSimulationController] Destroying graph instance after post-simulation setup");
                            UnityEngine.Object.DestroyImmediate(m_graph);
                            m_graph = null;
                        }
                    }
                    else
                    {
                        Debug.LogError("[DungeonSimulationController] PostSimulationSetup method not found!");
                    }
                }
                else
                {
                    Debug.LogError("[DungeonSimulationController] OrganicGeneration type not found in any assembly!");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonSimulationController] Error calling PostSimulationSetup: {ex.Message}\n{ex.StackTrace}");
            }

            // Wait one frame to ensure post-simulation setup completes
            yield return null;

            // Destroy this controller component after completion so corridor generation knows simulation is done
            Debug.Log("[DungeonSimulationController] Destroying controller component...");
            Destroy(this);
        }

        private void Update()
        {
            if (!m_isSimulating) return;

            // Smoothly lerp room positions toward their targets
            foreach (var kvp in m_currentPositions.ToList())
            {
                string nodeId = kvp.Key;
                if (!m_targetPositions.ContainsKey(nodeId)) continue;

                Vector3 currentPos = m_currentPositions[nodeId];
                Vector3 targetPos = m_targetPositions[nodeId];

                // Check for NaN or Infinity values which indicate a problem
                if (float.IsNaN(targetPos.x) || float.IsNaN(targetPos.y) ||
                    float.IsInfinity(targetPos.x) || float.IsInfinity(targetPos.y))
                {
                    Debug.LogError($"[DungeonSimulationController] Invalid target position detected for node {nodeId}. Stopping simulation.");
                    StopSimulation();
                    return;
                }

                // Lerp towards target position
                Vector3 newPos = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * m_lerpSpeed);
                m_currentPositions[nodeId] = newPos;

                // Apply position to room GameObject
                if (m_roomInstances.ContainsKey(nodeId) && m_centerOffsets.ContainsKey(nodeId))
                {
                    var roomObj = m_roomInstances[nodeId];
                    if (roomObj == null)
                    {
                        Debug.LogError($"[DungeonSimulationController] Room object for node {nodeId} is null. Stopping simulation.");
                        StopSimulation();
                        return;
                    }

                    Vector3 centerOffset = m_centerOffsets[nodeId];
                    // newPos is the simulation position (center), subtract offset to get GameObject position
                    roomObj.transform.position = newPos - centerOffset;
                }
            }
        }

        /// <summary>
        /// Safely stop the simulation and clean up
        /// </summary>
        private void StopSimulation()
        {
            m_isSimulating = false;
            StopAllCoroutines();
            Debug.LogWarning("[DungeonSimulationController] Simulation stopped due to error.");
        }

        private Dictionary<(string, string), int> CalculateGraphDistances(DungeonGraphAsset graph)
        {
            var distances = new Dictionary<(string, string), int>();

            // Floyd-Warshall algorithm for all-pairs shortest paths
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

        private Dictionary<string, List<string>> BuildAdjacency(DungeonGraphAsset graph)
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
        private bool CheckRoomOverlap()
        {
            var roomKeys = m_roomInstances.Keys.ToList();

            for (int i = 0; i < roomKeys.Count; i++)
            {
                for (int j = i + 1; j < roomKeys.Count; j++)
                {
                    var keyA = roomKeys[i];
                    var keyB = roomKeys[j];

                    var roomA = m_roomInstances[keyA];
                    var roomB = m_roomInstances[keyB];

                    var templateA = roomA.GetComponent<RoomTemplate>();
                    var templateB = roomB.GetComponent<RoomTemplate>();

                    if (templateA == null || templateB == null) continue;

                    // Get bounds at current positions
                    var boundsA = templateA.worldBounds;
                    var boundsB = templateB.worldBounds;

                    // Translate bounds to simulation positions
                    var posA = m_targetPositions[keyA];
                    var posB = m_targetPositions[keyB];

                    // Create bounds centered at simulation positions
                    var adjustedBoundsA = new Bounds(posA, boundsA.size);
                    var adjustedBoundsB = new Bounds(posB, boundsB.size);

                    // Check for intersection
                    if (adjustedBoundsA.Intersects(adjustedBoundsB))
                    {
                        Debug.LogWarning($"[DungeonSimulationController] Overlap detected between {roomA.name} and {roomB.name}");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Restart simulation with new random positions
        /// </summary>
        private void RestartSimulation()
        {
            // Safety check: prevent infinite restart loops
            if (m_isSimulating)
            {
                Debug.LogError("[DungeonSimulationController] Cannot restart simulation while already simulating!");
                return;
            }

            // Generate new random positions for all rooms
            var newPositions = new Dictionary<string, Vector3>();
            foreach (var nodeId in m_roomInstances.Keys)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(0f, m_placementRadius);
                Vector3 newPos = new Vector3(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance,
                    0f
                );
                newPositions[nodeId] = newPos;
            }

            // Reset positions
            m_currentPositions = new Dictionary<string, Vector3>(newPositions);
            m_targetPositions = new Dictionary<string, Vector3>(newPositions);

            // Apply initial positions to room objects
            foreach (var kvp in newPositions)
            {
                if (m_roomInstances.ContainsKey(kvp.Key) && m_centerOffsets.ContainsKey(kvp.Key))
                {
                    var roomObj = m_roomInstances[kvp.Key];
                    Vector3 centerOffset = m_centerOffsets[kvp.Key];
                    roomObj.transform.position = kvp.Value - centerOffset;
                }
            }

            // Restart the simulation coroutine
            m_isSimulating = true;
            StartCoroutine(SimulationCoroutine());
        }
    }
}
