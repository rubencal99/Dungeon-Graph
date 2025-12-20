using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonGraph
{
    public class RoomTemplate : MonoBehaviour
    {
        [Header("Computed on Bake")]
        public BoundsInt unionCellBounds;       // in cell coords (per-tilemap space is handled)
        public Vector3 worldMin;                // world-space min corner
        public Vector3 worldMax;                // world-space max corner
        public Bounds worldBounds;              // center + size in world units
        public Vector2Int sizeInCells;          // width/height in tiles (from unionCellBounds)
        public Tilemap[] tilemaps;              // all tilemaps used by this room

        [Header("Corridor Exits")]
        [Tooltip("Designated exit points for corridor connections. If empty, corridors will use room center.")]
        public Transform[] exits;               // exit transforms for corridor generation

        /// <summary>
        /// On instantiation, repopulate exits array if empty by finding "Exits" GameObject
        /// Only runs in Play mode - in Edit mode, OrganicGeneration calls this explicitly
        /// </summary>
        private void Awake()
        {
            // Only auto-repopulate in Play mode
            // In Edit mode, the generation system calls RepopulateExitsIfNeeded explicitly
            if (Application.isPlaying)
            {
                RepopulateExitsIfNeeded();
            }
        }

        /// <summary>
        /// Repopulate the exits array from the "Exits" GameObject if the array is empty
        /// </summary>
        public void RepopulateExitsIfNeeded()
        {
            // If exits array is already populated, nothing to do
            if (exits != null && exits.Length > 0)
            {
                // Double-check that the exits aren't all null
                bool hasValidExit = false;
                foreach (var exit in exits)
                {
                    if (exit != null)
                    {
                        hasValidExit = true;
                        break;
                    }
                }

                if (hasValidExit)
                {
                    return; // Exits are properly populated
                }
            }

            // Try to find the "Exits" GameObject
            Transform exitsContainer = transform.Find("Exits");
            if (exitsContainer == null)
            {
                // Try case-insensitive search as fallback
                foreach (Transform child in transform)
                {
                    if (child.name.Equals("Exits", System.StringComparison.OrdinalIgnoreCase))
                    {
                        exitsContainer = child;
                        break;
                    }
                }
            }

            if (exitsContainer != null)
            {
                // Populate exits array with all child transforms of the Exits container
                int childCount = exitsContainer.childCount;
                if (childCount > 0)
                {
                    exits = new Transform[childCount];
                    for (int i = 0; i < childCount; i++)
                    {
                        exits[i] = exitsContainer.GetChild(i);
                    }

                    //Debug.Log($"[RoomTemplate] Repopulated exits array for {gameObject.name} with {childCount} exits from 'Exits' GameObject");
                }
                else
                {
                    //Debug.LogWarning($"[RoomTemplate] Found 'Exits' GameObject in {gameObject.name}, but it has no children");
                }
            }
            else
            {
                //Debug.LogWarning($"[RoomTemplate] No 'Exits' GameObject found in {gameObject.name} to repopulate exits array");
            }
        }

        /// <summary>Recompute everything. Call this after painting or before saving.</summary>
        public void Recompute()
        {
            tilemaps = GetComponentsInChildren<Tilemap>(true);

            // First compress each tilemap to drop empty margins
            foreach (var tm in tilemaps)
                tm.CompressBounds();

            // Compute union in world space so different tilemap origins are handled correctly
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);

            // Also track a rough union in cell space (best-effort)
            bool anyCells = false;
            var minCell = new Vector3Int(int.MaxValue, int.MaxValue, 0);
            var maxCell = new Vector3Int(int.MinValue, int.MinValue, 0);

            foreach (var tm in tilemaps)
            {
                var cb = tm.cellBounds;             // local to this tilemap (includes origin)
                if (cb.size.x == 0 || cb.size.y == 0) continue;

                // World-space corners via THIS tilemap's layout (accounts for origin & transform)
                var worldA = tm.CellToWorld(cb.min);
                var worldB = tm.CellToWorld(new Vector3Int(cb.xMax, cb.yMax, 0));

                min = Vector3.Min(min, worldA);
                max = Vector3.Max(max, worldB);

                // Best-effort union in a shared integer grid space:
                // We'll use the smallest min and largest max among tilemaps.
                anyCells = true;
                minCell = Vector3Int.Min(minCell, cb.min);
                maxCell = Vector3Int.Max(maxCell, cb.max);
            }

            if (!anyCells)
            {
                unionCellBounds = new BoundsInt(Vector3Int.zero, Vector3Int.zero);
                worldMin = worldMax = Vector3.zero;
                worldBounds = new Bounds(Vector3.zero, Vector3.zero);
                sizeInCells = Vector2Int.zero;
                return;
            }

            // Save results
            unionCellBounds = new BoundsInt(minCell, maxCell - minCell);
            sizeInCells = new Vector2Int(unionCellBounds.size.x, unionCellBounds.size.y);

            worldMin = min;
            worldMax = max;
            worldBounds = new Bounds((worldMin + worldMax) * 0.5f, worldMax - worldMin);
        }

        private void OnDrawGizmosSelected()
        {
            // Visualize world bounds in editor
            // Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            // Gizmos.DrawCube(transform.position + worldBounds.center, worldBounds.size);
            // Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            // Gizmos.DrawWireCube(transform.position + worldBounds.center, worldBounds.size);
        }
    }
}
