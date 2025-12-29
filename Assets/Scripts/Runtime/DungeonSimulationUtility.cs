using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// Shared simulation utility for dungeon room layout physics.
    /// This is the single source of truth for the simulation algorithm used by both
    /// realtime and non-realtime generation paths.
    /// </summary>
    public static class DungeonSimulationUtility
    {
        /// <summary>
        /// Simulate spring forces between connected rooms and repulsion between all rooms.
        /// This method is shared between realtime and non-realtime generation to ensure consistency.
        /// </summary>
        /// <param name="roomPositions">Dictionary of room positions (will be modified)</param>
        /// <param name="adjacency">Adjacency list of connected rooms</param>
        /// <param name="graphDistances">Graph distances between all pairs of rooms</param>
        /// <param name="roomRadii">Radius of each room for collision calculations</param>
        /// <param name="velocities">Dictionary of room velocities (will be modified)</param>
        /// <param name="repulsionFactor">Multiplier for repulsion strength</param>
        /// <param name="iterations">Number of physics iterations to run</param>
        /// <param name="forceMode">If true, run until convergence (up to max iterations)</param>
        /// <param name="stiffnessFactor">Multiplier for spring stiffness</param>
        /// <param name="chaosFactor">Random perturbation strength</param>
        /// <param name="idealDistance">Target distance between connected rooms</param>
        public static void SimulateForces(
            Dictionary<string, Vector3> roomPositions,
            Dictionary<string, List<string>> adjacency,
            Dictionary<(string, string), int> graphDistances,
            Dictionary<string, float> roomRadii,
            Dictionary<string, Vector3> velocities,
            float repulsionFactor,
            int iterations,
            bool forceMode,
            float stiffnessFactor,
            float chaosFactor,
            float idealDistance)
        {
            // IMPORTANT: Some magic numbers here, needs more iteration/fine-tuning/editor customization
            float springStiffness = 0.01f * stiffnessFactor;  // Attraction force for connected rooms (affected by stiffness factor)
            float repulsionStrength = 50f * repulsionFactor;  // Base repulsion between rooms
            float damping = 0.9f;  // Velocity damping

            int maxIterations = forceMode ? 2096 : iterations;
            float energyThreshold = 0.01f; // Energy considered "zero" for force mode

            Debug.Log($"[DungeonSimulationUtility] Starting simulation with {(forceMode ? "Force Mode (max " + maxIterations + " iterations)" : iterations + " iterations")}");

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
                    if (!adjacency.ContainsKey(nodeId)) continue;

                    foreach (var neighborId in adjacency[nodeId])
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
                                Debug.LogError($"[DungeonSimulationUtility] Invalid repulsion calculated between {nodeA} and {nodeB}. GraphDist={graphDist}, Distance={distance}");
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

                // In force mode, stop when energy is near zero
                if (forceMode && totalEnergy < energyThreshold)
                {
                    Debug.Log($"[DungeonSimulationUtility] Force mode converged at iteration {iter} with energy {totalEnergy:F4}");
                    break;
                }
            }

            Debug.Log($"[DungeonSimulationUtility] Simulation complete.");
        }

        /// <summary>
        /// Calculate forces for a single iteration (used by realtime simulation).
        /// </summary>
        public static void CalculateForcesForIteration(
            Dictionary<string, Vector3> roomPositions,
            Dictionary<string, List<string>> adjacency,
            Dictionary<(string, string), int> graphDistances,
            Dictionary<string, float> roomRadii,
            Dictionary<string, Vector3> forces,
            float repulsionFactor,
            float stiffnessFactor,
            float idealDistance)
        {
            float springStiffness = 0.01f * stiffnessFactor;
            float repulsionStrength = 50f * repulsionFactor;

            // Initialize forces
            foreach (var nodeId in roomPositions.Keys)
            {
                forces[nodeId] = Vector3.zero;
            }

            // Spring forces (attraction between connected rooms)
            foreach (var nodeId in roomPositions.Keys)
            {
                if (!adjacency.ContainsKey(nodeId)) continue;

                foreach (var neighborId in adjacency[nodeId])
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
                        if (graphDist >= 999999)
                        {
                            graphDist = 10; // Cap at a reasonable maximum
                        }

                        // Stronger repulsion for nodes that are farther apart in the graph
                        float repulsion = (repulsionStrength * graphDist) / (distance * distance);

                        // Safety check: prevent infinite or NaN values
                        if (float.IsNaN(repulsion) || float.IsInfinity(repulsion))
                        {
                            Debug.LogError($"[DungeonSimulationUtility] Invalid repulsion calculated between {nodeA} and {nodeB}. GraphDist={graphDist}, Distance={distance}");
                            continue;
                        }

                        forces[nodeA] -= direction * repulsion;
                        forces[nodeB] += direction * repulsion;
                    }
                }
            }
        }
    }
}
