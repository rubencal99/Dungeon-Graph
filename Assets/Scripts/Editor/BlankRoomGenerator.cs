using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

namespace DungeonGraph.Editor
{
    public static class BlankRoomGenerator
    {
        [MenuItem("Assets/Create/Dungeon Graph/Blank Room", false, 0)]
        public static void CreateBlankRoom()
        {
            // Create root Room object
            GameObject room = new GameObject("Room");
            room.AddComponent<RoomTemplate>();

            // Create Grid child
            GameObject grid = new GameObject("Grid");
            grid.transform.SetParent(room.transform);

            Grid gridComponent = grid.AddComponent<Grid>();
            gridComponent.cellSize = new Vector3(1, 1, 0);

            Rigidbody2D rb = grid.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            CompositeCollider2D compositeCollider = grid.AddComponent<CompositeCollider2D>();
            compositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;

            // Create Exits child
            GameObject exits = new GameObject("Exits");
            exits.transform.SetParent(room.transform);

            // Create Tilemap_Floor child (child of Grid)
            GameObject tilemapFloor = new GameObject("Tilemap_Floor");
            tilemapFloor.transform.SetParent(grid.transform);

            Tilemap tilemap = tilemapFloor.AddComponent<Tilemap>();
            TilemapRenderer tilemapRenderer = tilemapFloor.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = 0;

            // Optionally add TilemapCollider2D for collision (common in dungeon rooms)
            TilemapCollider2D tilemapCollider = tilemapFloor.AddComponent<TilemapCollider2D>();
            tilemapCollider.usedByComposite = true;

            // Get the active folder path in the Project window
            string path = GetActiveFolderPath();

            // Create unique prefab name
            string prefabName = "Room.prefab";
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path + "/" + prefabName);

            // Save as prefab
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(room, uniquePath);

            // Clean up the scene instance
            GameObject.DestroyImmediate(room);

            // Select the newly created prefab
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[BlankRoomGenerator] Created blank room prefab at: {uniquePath}");
        }

        private static string GetActiveFolderPath()
        {
            // Try to get the selected folder in the Project window
            string path = "Assets";

            foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets))
            {
                path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    path = System.IO.Path.GetDirectoryName(path);
                }
                break;
            }

            return path;
        }
    }
}
