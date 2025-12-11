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
            m_nodeBounds = nodeBounds;
            m_params = parameters;
            m_isSimulating = true;

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

            // Calculate graph distances for repulsion scaling
            var graphDistances = CalculateGraphDistances(m_graph);
            var adjacency = BuildAdjacency(m_graph);

            // Physics parameters
            float springStiffness = 0.01f * m_params.stiffnessFactor;
            float repulsionStrength = 50f * m_params.repulsionFactor;
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

                // Initialize forces
                foreach (var nodeId in m_targetPositions.Keys)
                {
                    forces[nodeId] = Vector3.zero;
                }

                // Spring forces (attraction between connected rooms)
                foreach (var nodeId in m_targetPositions.Keys)
                {
                    if (!adjacency.ContainsKey(nodeId)) continue;

                    foreach (var neighborId in adjacency[nodeId])
                    {
                        if (!m_targetPositions.ContainsKey(neighborId)) continue;

                        Vector3 direction = m_targetPositions[neighborId] - m_targetPositions[nodeId];
                        float distance = direction.magnitude;

                        if (distance > 0.01f)
                        {
                            direction /= distance;

                            // Spring force proportional to distance
                            float idealDistance = 20f; // Ideal spring length
                            float force = springStiffness * (distance - idealDistance);
                            forces[nodeId] += direction * force;
                        }
                    }
                }

                // Repulsion forces (all pairs, scaled by graph distance)
                var nodes = m_targetPositions.Keys.ToList();
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        var nodeA = nodes[i];
                        var nodeB = nodes[j];

                        Vector3 direction = m_targetPositions[nodeB] - m_targetPositions[nodeA];
                        float distance = direction.magnitude;

                        if (distance > 0.01f)
                        {
                            direction /= distance;

                            // Get graph distance for repulsion scaling
                            int graphDist = graphDistances.ContainsKey((nodeA, nodeB))
                                ? graphDistances[(nodeA, nodeB)]
                                : 1;

                            // Stronger repulsion for nodes that are farther apart in the graph
                            float repulsion = (repulsionStrength * graphDist) / (distance * distance);

                            forces[nodeA] -= direction * repulsion;
                            forces[nodeB] += direction * repulsion;
                        }
                    }
                }

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
                if (iter % 20 == 0)
                {
                    Debug.Log($"[DungeonSimulationController] Iteration {iter}/{maxIterations}, Total energy: {totalEnergy:F2}");
                }

                // In force mode, stop when energy is near zero
                if (m_params.forceMode && totalEnergy < energyThreshold)
                {
                    Debug.Log($"[DungeonSimulationController] Force mode converged at iteration {iter} with energy {totalEnergy:F4}");
                    break;
                }

                // Wait for next iteration
                yield return new WaitForSeconds(iterationDelay);
            }

            m_isSimulating = false;
            Debug.Log("[DungeonSimulationController] Simulation complete!");

            // Call post-simulation setup to handle visualization, grid snapping, and tilemap merging
            #if UNITY_EDITOR
            var postSimMethod = System.Type.GetType("DungeonGraph.Editor.OrganicGeneration, Assembly-CSharp-Editor");
            if (postSimMethod != null)
            {
                var method = postSimMethod.GetMethod("PostSimulationSetup",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, new object[] { m_graph, gameObject });
                }
            }
            #endif

            // Destroy this controller component after completion so corridor generation knows simulation is done
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

                // Lerp towards target position
                Vector3 newPos = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * m_lerpSpeed);
                m_currentPositions[nodeId] = newPos;

                // Apply position to room GameObject
                if (m_roomInstances.ContainsKey(nodeId) && m_centerOffsets.ContainsKey(nodeId))
                {
                    var roomObj = m_roomInstances[nodeId];
                    Vector3 centerOffset = m_centerOffsets[nodeId];
                    // newPos is the simulation position (center), subtract offset to get GameObject position
                    roomObj.transform.position = newPos - centerOffset;
                }
            }
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
    }
}
