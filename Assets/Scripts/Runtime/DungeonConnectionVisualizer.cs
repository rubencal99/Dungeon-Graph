using System.Collections.Generic;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// Debug component that visualizes connections between rooms in the dungeon
    /// </summary>
    public class DungeonConnectionVisualizer : MonoBehaviour
    {
        [System.Serializable]
        public class RoomInfo
        {
            public GameObject room;
            public string typeName;
            public Color color;
        }

        public List<(GameObject roomA, GameObject roomB)> connections = new List<(GameObject, GameObject)>();
        public List<RoomInfo> rooms = new List<RoomInfo>();
        public Color connectionColor = Color.cyan;
        public float lineWidth = 0.5f;

        // Node type colors matching the GraphView USS
        private static readonly Dictionary<string, Color> s_nodeColors = new Dictionary<string, Color>
        {
            { "Start", new Color(0.000f, 0.392f, 0.000f) },     // #006400 dark green
            { "Basic", new Color(0.000f, 0.000f, 0.545f) },     // #00008B deep blue
            { "Hub", new Color(0.255f, 0.106f, 0.545f) },       // #411B8B royal purple
            { "End", new Color(1.000f, 0.549f, 0.000f) },       // #FF8C00 bright orange
            { "Debug", new Color(0.984f, 0.749f, 0.141f) },     // #fbbf24 yellow
            { "Boss", new Color(0.984f, 0.141f, 0.141f) },      // #fb2424 red
            { "Reward", new Color(0.980f, 0.843f, 0.000f) }     // #fad700 gold
        };

        private void OnDrawGizmos()
        {
            // Draw room boxes with node type colors
            if (rooms != null && rooms.Count > 0)
            {
                foreach (var roomInfo in rooms)
                {
                    if (roomInfo.room == null) continue;

                    var template = roomInfo.room.GetComponent<RoomTemplate>();
                    if (template != null && template.worldBounds.size.magnitude > 0.1f)
                    {
                        Vector3 center = roomInfo.room.transform.position + template.worldBounds.center;
                        Vector3 size = template.worldBounds.size;

                        // Draw filled box with node color (semi-transparent)
                        Color boxColor = roomInfo.color;
                        boxColor.a = 0.2f;
                        Gizmos.color = boxColor;
                        Gizmos.DrawCube(center, size);

                        // Draw wireframe with full opacity
                        boxColor.a = 1f;
                        Gizmos.color = boxColor;
                        Gizmos.DrawWireCube(center, size);
                    }
                }
            }

            // Draw connections between rooms
            if (connections != null && connections.Count > 0)
            {
                Gizmos.color = connectionColor;

                foreach (var (roomA, roomB) in connections)
                {
                    if (roomA == null || roomB == null)
                        continue;

                    // Get actual room centers from RoomTemplate
                    Vector3 startPos = GetRoomCenter(roomA);
                    Vector3 endPos = GetRoomCenter(roomB);

                    // Draw line between room centers
                    Gizmos.DrawLine(startPos, endPos);

                    // Draw small spheres at connection points for visibility
                    Gizmos.DrawSphere(startPos, lineWidth);
                    Gizmos.DrawSphere(endPos, lineWidth);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw highlighted room boxes when selected
            if (rooms != null && rooms.Count > 0)
            {
                foreach (var roomInfo in rooms)
                {
                    if (roomInfo.room == null) continue;

                    var template = roomInfo.room.GetComponent<RoomTemplate>();
                    if (template != null && template.worldBounds.size.magnitude > 0.1f)
                    {
                        Vector3 center = roomInfo.room.transform.position + template.worldBounds.center;
                        Vector3 size = template.worldBounds.size;

                        // Brighter wireframe when selected
                        Gizmos.color = Color.Lerp(roomInfo.color, Color.white, 0.5f);
                        Gizmos.DrawWireCube(center, size);
                    }
                }
            }

            // Draw thicker/brighter connection lines when selected
            if (connections != null && connections.Count > 0)
            {
                Gizmos.color = Color.yellow;

                foreach (var (roomA, roomB) in connections)
                {
                    if (roomA == null || roomB == null)
                        continue;

                    Vector3 startPos = GetRoomCenter(roomA);
                    Vector3 endPos = GetRoomCenter(roomB);

                    // Draw thicker line
                    Gizmos.DrawLine(startPos, endPos);

                    // Draw direction indicator (arrow-like)
                    Vector3 direction = (endPos - startPos).normalized;
                    Vector3 midPoint = (startPos + endPos) * 0.5f;
                    Gizmos.DrawSphere(midPoint, lineWidth * 1.5f);
                }
            }
        }

        private Vector3 GetRoomCenter(GameObject room)
        {
            var template = room.GetComponent<RoomTemplate>();
            if (template != null)
            {
                // Use the room's actual center from bounds
                return room.transform.position + template.worldBounds.center;
            }
            // Fallback to transform position
            return room.transform.position;
        }

        public static Color GetColorForNodeType(string nodeTypeName)
        {
            if (s_nodeColors.TryGetValue(nodeTypeName, out Color color))
            {
                return color;
            }
            return Color.gray; // Default color for unknown types
        }
    }
}
