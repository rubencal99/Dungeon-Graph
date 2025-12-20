using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DungeonGraph.Editor
{
    /// <summary>
    /// Manages dungeon floors and their folder structure.
    /// Scans the Dungeon_Floors directory and provides utilities for floor creation.
    /// </summary>
    public static class DungeonFloorManager
    {
        private const string FLOORS_ROOT_PATH = "Assets/Dungeon_Floors";

        // Standard node type folders that should exist in each floor
        private static readonly string[] STANDARD_NODE_FOLDERS = new string[]
        {
            "Start",
            "Basic/Small",
            "Basic/Medium",
            "Basic/Large",
            "Hub",
            "End",
            "Boss",
            "Reward",
            "Debug"
        };

        /// <summary>
        /// Gets all available dungeon floors by scanning the Dungeon_Floors directory.
        /// </summary>
        public static List<DungeonFloorConfig> GetAllFloors()
        {
            List<DungeonFloorConfig> floors = new List<DungeonFloorConfig>();

            // Ensure root directory exists
            if (!AssetDatabase.IsValidFolder(FLOORS_ROOT_PATH))
            {
                Debug.LogWarning($"[DungeonFloorManager] Floors root directory does not exist: {FLOORS_ROOT_PATH}");
                return floors;
            }

            // Get all subdirectories in the Dungeon_Floors folder
            string fullPath = Path.Combine(Application.dataPath, FLOORS_ROOT_PATH.Replace("Assets/", ""));
            if (!Directory.Exists(fullPath))
            {
                Debug.LogWarning($"[DungeonFloorManager] Floors directory does not exist: {fullPath}");
                return floors;
            }

            string[] floorDirectories = Directory.GetDirectories(fullPath);
            foreach (string floorDir in floorDirectories)
            {
                string floorName = Path.GetFileName(floorDir);
                string relativePath = $"{FLOORS_ROOT_PATH}/{floorName}";
                floors.Add(new DungeonFloorConfig(floorName, relativePath));
            }

            return floors;
        }

        /// <summary>
        /// Creates a new dungeon floor with the specified name.
        /// Automatically creates all standard node type folders and populates them with blank rooms.
        /// </summary>
        public static bool CreateNewFloor(string floorName)
        {
            if (string.IsNullOrWhiteSpace(floorName))
            {
                Debug.LogError("[DungeonFloorManager] Floor name cannot be empty");
                return false;
            }

            // Ensure root directory exists
            EnsureRootDirectoryExists();

            // Create floor directory
            string floorPath = $"{FLOORS_ROOT_PATH}/{floorName}";
            if (AssetDatabase.IsValidFolder(floorPath))
            {
                Debug.LogError($"[DungeonFloorManager] Floor already exists: {floorName}");
                return false;
            }

            AssetDatabase.CreateFolder(FLOORS_ROOT_PATH, floorName);
            Debug.Log($"[DungeonFloorManager] Created floor: {floorName} at {floorPath}");

            // Create all standard node type folders
            CreateStandardNodeFolders(floorPath);

            // Get custom node types and create folders for them
            var customNodeRegistry = CustomNodeTypeRegistry.GetOrCreateDefault();
            foreach (var customType in customNodeRegistry.customNodeTypes)
            {
                CreateNodeFolderInFloor(floorPath, customType.typeName);
            }

            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Creates a new node type folder in all existing floors.
        /// Called when a new custom node type is created.
        /// </summary>
        public static void CreateNodeFolderInAllFloors(string nodeTypeName)
        {
            var floors = GetAllFloors();
            foreach (var floor in floors)
            {
                CreateNodeFolderInFloor(floor.folderPath, nodeTypeName);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Creates a node type folder in a specific floor and generates a blank room.
        /// </summary>
        private static void CreateNodeFolderInFloor(string floorPath, string nodeTypeName)
        {
            string nodeFolderPath = $"{floorPath}/{nodeTypeName}";

            // Create folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(nodeFolderPath))
            {
                string parentFolder = Path.GetDirectoryName(nodeFolderPath).Replace("\\", "/");
                string folderName = Path.GetFileName(nodeFolderPath);

                // Ensure parent exists
                EnsureFolderExists(parentFolder);

                AssetDatabase.CreateFolder(parentFolder, folderName);
                Debug.Log($"[DungeonFloorManager] Created node folder: {nodeFolderPath}");
            }

            // Generate a blank room in this folder
            GenerateBlankRoomInFolder(nodeFolderPath);
        }

        /// <summary>
        /// Creates all standard node type folders in a floor.
        /// </summary>
        private static void CreateStandardNodeFolders(string floorPath)
        {
            foreach (string nodeFolder in STANDARD_NODE_FOLDERS)
            {
                string fullNodePath = $"{floorPath}/{nodeFolder}";
                string[] pathParts = nodeFolder.Split('/');

                string currentPath = floorPath;
                foreach (string part in pathParts)
                {
                    string nextPath = $"{currentPath}/{part}";
                    if (!AssetDatabase.IsValidFolder(nextPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, part);
                    }
                    currentPath = nextPath;
                }

                // Generate a blank room in this folder
                GenerateBlankRoomInFolder(fullNodePath);
            }
        }

        /// <summary>
        /// Generates a blank room prefab in the specified folder.
        /// First tries to use the Blank_Room template from Assets/Prefabs.
        /// Falls back to procedural generation if template is not found.
        /// </summary>
        private static void GenerateBlankRoomInFolder(string folderPath)
        {
            const string TEMPLATE_PATH = "Assets/Prefabs/Blank_Room.prefab";
            GameObject room = null;

            // Try to load the template prefab
            GameObject templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TEMPLATE_PATH);

            if (templatePrefab != null)
            {
                // Instantiate from template
                room = (GameObject)PrefabUtility.InstantiatePrefab(templatePrefab);
                //Debug.Log($"[DungeonFloorManager] Using Blank_Room template from {TEMPLATE_PATH}");
            }
            else
            {
                // Fall back to procedural generation
                Debug.LogWarning($"[DungeonFloorManager] Blank_Room template not found at {TEMPLATE_PATH}, using procedural generation");

                room = new GameObject("Room");
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

                UnityEngine.Tilemaps.Tilemap tilemap = tilemapFloor.AddComponent<UnityEngine.Tilemaps.Tilemap>();
                UnityEngine.Tilemaps.TilemapRenderer tilemapRenderer = tilemapFloor.AddComponent<UnityEngine.Tilemaps.TilemapRenderer>();
                tilemapRenderer.sortingOrder = 0;

                UnityEngine.Tilemaps.TilemapCollider2D tilemapCollider = tilemapFloor.AddComponent<UnityEngine.Tilemaps.TilemapCollider2D>();
                tilemapCollider.usedByComposite = true;
            }

            // Create unique prefab name
            string prefabName = "Room.prefab";
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{prefabName}");

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(room, uniquePath);

            // Clean up the scene instance
            GameObject.DestroyImmediate(room);

            //Debug.Log($"[DungeonFloorManager] Generated blank room at: {uniquePath}");
        }

        /// <summary>
        /// Ensures the Dungeon_Floors root directory exists.
        /// </summary>
        private static void EnsureRootDirectoryExists()
        {
            if (!AssetDatabase.IsValidFolder(FLOORS_ROOT_PATH))
            {
                AssetDatabase.CreateFolder("Assets", "Dungeon_Floors");
                Debug.Log($"[DungeonFloorManager] Created root directory: {FLOORS_ROOT_PATH}");
            }
        }

        /// <summary>
        /// Recursively ensures a folder path exists, creating parent folders as needed.
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parentFolder = Path.GetDirectoryName(folderPath).Replace("\\", "/");
            string folderName = Path.GetFileName(folderPath);

            if (!AssetDatabase.IsValidFolder(parentFolder))
            {
                EnsureFolderExists(parentFolder);
            }

            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        /// <summary>
        /// Gets the room folder path for a specific node type on a specific floor.
        /// </summary>
        public static string GetRoomFolderPath(string floorPath, string nodeTypeName, string sizeCategory = null)
        {
            if (nodeTypeName == "BasicNode" && !string.IsNullOrEmpty(sizeCategory))
            {
                return $"{floorPath}/Basic/{sizeCategory}";
            }
            else
            {
                string typeFolder = nodeTypeName.Replace("Node", "");
                return $"{floorPath}/{typeFolder}";
            }
        }
    }
}
