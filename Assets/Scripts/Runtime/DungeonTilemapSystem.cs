using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonGraph
{
    /// <summary>
    /// Handles tilemap alignment, merging, and corridor generation for dungeons
    /// </summary>
    public class DungeonTilemapSystem : MonoBehaviour
    {
        [Header("Grid Settings")]
        public float gridCellSize = 1f; // Size of each grid cell in world units

        [Header("Corridor Settings")]
        public TileBase corridorTile; // Tile to use for corridor floors
        public int corridorWidth = 2; // Width of corridors in tiles

        [Header("References")]
        public Tilemap masterTilemap; // The unified tilemap for all rooms

        /// <summary>
        /// Snap all room positions to a universal grid
        /// </summary>
        public void SnapRoomsToGrid(Dictionary<string, GameObject> roomInstances)
        {
            foreach (var kvp in roomInstances)
            {
                GameObject roomObj = kvp.Value;
                Vector3 currentPos = roomObj.transform.position;

                // Snap to grid
                Vector3 snappedPos = new Vector3(
                    Mathf.Round(currentPos.x / gridCellSize) * gridCellSize,
                    Mathf.Round(currentPos.y / gridCellSize) * gridCellSize,
                    0f
                );

                roomObj.transform.position = snappedPos;
            }

            Debug.Log($"[DungeonTilemapSystem] Snapped {roomInstances.Count} rooms to grid (cell size: {gridCellSize})");
        }

        /// <summary>
        /// Transfer all room tiles to a single master tilemap
        /// </summary>
        public void MergeRoomsToMasterTilemap(Dictionary<string, GameObject> roomInstances)
        {
            if (masterTilemap == null)
            {
                Debug.LogError("[DungeonTilemapSystem] Master tilemap is not assigned!");
                return;
            }

            // Clear the master tilemap
            masterTilemap.ClearAllTiles();

            int totalTilesCopied = 0;

            foreach (var kvp in roomInstances)
            {
                GameObject roomObj = kvp.Value;
                RoomTemplate roomTemplate = roomObj.GetComponent<RoomTemplate>();

                if (roomTemplate == null || roomTemplate.tilemaps == null)
                {
                    Debug.LogWarning($"[DungeonTilemapSystem] Room {kvp.Key} has no RoomTemplate or tilemaps");
                    continue;
                }

                // Copy tiles from each tilemap in the room to the master tilemap
                foreach (Tilemap sourceTilemap in roomTemplate.tilemaps)
                {
                    if (sourceTilemap == null) continue;

                    BoundsInt bounds = sourceTilemap.cellBounds;
                    TileBase[] tiles = sourceTilemap.GetTilesBlock(bounds);

                    // Calculate world offset between source tilemap and master tilemap
                    Vector3 worldOffset = sourceTilemap.transform.position - masterTilemap.transform.position;
                    Vector3Int cellOffset = masterTilemap.WorldToCell(worldOffset);

                    // Copy each tile
                    for (int x = 0; x < bounds.size.x; x++)
                    {
                        for (int y = 0; y < bounds.size.y; y++)
                        {
                            int index = x + y * bounds.size.x;
                            TileBase tile = tiles[index];

                            if (tile != null)
                            {
                                Vector3Int sourceCell = new Vector3Int(bounds.x + x, bounds.y + y, 0);
                                Vector3Int targetCell = sourceCell + cellOffset;

                                masterTilemap.SetTile(targetCell, tile);
                                totalTilesCopied++;
                            }
                        }
                    }

                    // Optionally disable the source tilemap to avoid visual duplication
                    sourceTilemap.gameObject.SetActive(false);
                }
            }

            Debug.Log($"[DungeonTilemapSystem] Merged {totalTilesCopied} tiles from {roomInstances.Count} rooms to master tilemap");
        }

        /// <summary>
        /// Generate a direct corridor from one room center to another
        /// </summary>
        public void GenerateDirectCorridor(Vector3 roomACenter, Vector3 roomBCenter)
        {
            if (masterTilemap == null || corridorTile == null)
            {
                Debug.LogError("[DungeonTilemapSystem] Master tilemap or corridor tile not assigned!");
                return;
            }

            // Convert world positions to cell positions
            Vector3Int startCell = masterTilemap.WorldToCell(roomACenter);
            Vector3Int endCell = masterTilemap.WorldToCell(roomBCenter);

            // Use Bresenham's line algorithm to create a direct path
            List<Vector3Int> pathCells = GetLinePoints(startCell, endCell);

            // Draw corridor with specified width
            foreach (Vector3Int cell in pathCells)
            {
                DrawCorridorAtCell(cell, corridorWidth);
            }

            Debug.Log($"[DungeonTilemapSystem] Generated direct corridor from {startCell} to {endCell} ({pathCells.Count} cells)");
        }

        /// <summary>
        /// Generate a right-angle corridor (L-shaped) from one room center to another
        /// </summary>
        public void GenerateRightCorridor(Vector3 roomACenter, Vector3 roomBCenter, bool horizontalFirst = true)
        {
            if (masterTilemap == null || corridorTile == null)
            {
                Debug.LogError("[DungeonTilemapSystem] Master tilemap or corridor tile not assigned!");
                return;
            }

            Vector3Int startCell = masterTilemap.WorldToCell(roomACenter);
            Vector3Int endCell = masterTilemap.WorldToCell(roomBCenter);

            // Calculate the corner point
            Vector3Int cornerCell;
            if (horizontalFirst)
            {
                // Go horizontal first, then vertical
                cornerCell = new Vector3Int(endCell.x, startCell.y, 0);
            }
            else
            {
                // Go vertical first, then horizontal
                cornerCell = new Vector3Int(startCell.x, endCell.y, 0);
            }

            // Generate two straight corridors
            List<Vector3Int> segment1 = GetLinePoints(startCell, cornerCell);
            List<Vector3Int> segment2 = GetLinePoints(cornerCell, endCell);

            // Draw both segments
            foreach (Vector3Int cell in segment1)
            {
                DrawCorridorAtCell(cell, corridorWidth);
            }

            foreach (Vector3Int cell in segment2)
            {
                DrawCorridorAtCell(cell, corridorWidth);
            }

            Debug.Log($"[DungeonTilemapSystem] Generated right-angle corridor from {startCell} to {endCell} via {cornerCell}");
        }

        /// <summary>
        /// Draw a corridor tile at the specified cell with the given width
        /// </summary>
        private void DrawCorridorAtCell(Vector3Int centerCell, int width)
        {
            int halfWidth = width / 2;

            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                for (int dy = -halfWidth; dy <= halfWidth; dy++)
                {
                    Vector3Int cell = centerCell + new Vector3Int(dx, dy, 0);

                    // Only place corridor tile if there's no tile already (don't overwrite room tiles)
                    if (!masterTilemap.HasTile(cell))
                    {
                        masterTilemap.SetTile(cell, corridorTile);
                    }
                }
            }
        }

        /// <summary>
        /// Get line points using Bresenham's line algorithm
        /// </summary>
        private List<Vector3Int> GetLinePoints(Vector3Int start, Vector3Int end)
        {
            List<Vector3Int> points = new List<Vector3Int>();

            int x0 = start.x, y0 = start.y;
            int x1 = end.x, y1 = end.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                points.Add(new Vector3Int(x0, y0, 0));

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            return points;
        }

        /// <summary>
        /// Generate corridors for all connections in the graph
        /// </summary>
        public void GenerateAllCorridors(DungeonGraphAsset graph, Dictionary<string, GameObject> roomInstances,
            bool useRightAngleCorridors = false)
        {
            if (graph == null || graph.Connections == null)
            {
                Debug.LogError("[DungeonTilemapSystem] Graph or connections are null!");
                return;
            }

            int corridorCount = 0;

            foreach (var connection in graph.Connections)
            {
                string nodeAId = connection.inputPort.nodeId;
                string nodeBId = connection.outputPort.nodeId;

                if (!roomInstances.ContainsKey(nodeAId) || !roomInstances.ContainsKey(nodeBId))
                {
                    Debug.LogWarning($"[DungeonTilemapSystem] Connection references missing room: {nodeAId} <-> {nodeBId}");
                    continue;
                }

                GameObject roomA = roomInstances[nodeAId];
                GameObject roomB = roomInstances[nodeBId];

                RoomTemplate templateA = roomA.GetComponent<RoomTemplate>();
                RoomTemplate templateB = roomB.GetComponent<RoomTemplate>();

                if (templateA == null || templateB == null)
                {
                    Debug.LogWarning($"[DungeonTilemapSystem] Rooms missing RoomTemplate component");
                    continue;
                }

                // Get world centers of the rooms
                Vector3 centerA = roomA.transform.position + templateA.worldBounds.center;
                Vector3 centerB = roomB.transform.position + templateB.worldBounds.center;

                // Generate corridor
                if (useRightAngleCorridors)
                {
                    // Alternate between horizontal-first and vertical-first for variety
                    bool horizontalFirst = (corridorCount % 2) == 0;
                    GenerateRightCorridor(centerA, centerB, horizontalFirst);
                }
                else
                {
                    GenerateDirectCorridor(centerA, centerB);
                }

                corridorCount++;
            }

            Debug.Log($"[DungeonTilemapSystem] Generated {corridorCount} corridors");
        }
    }
}
