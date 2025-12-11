using UnityEditor;
using UnityEngine;

namespace DungeonGraph.Editor
{
    [CustomEditor(typeof(DungeonTilemapSystem))]
    public class DungeonTilemapSystemEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DungeonTilemapSystem tilemapSystem = (DungeonTilemapSystem)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Tilemap Operations", EditorStyles.boldLabel);

            // Button to merge rooms to master tilemap
            if (GUILayout.Button("Merge Rooms to Master Tilemap", GUILayout.Height(30)))
            {
                if (tilemapSystem.masterTilemap == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please assign a Master Tilemap first!", "OK");
                }
                else
                {
                    // Find all room instances in the scene
                    var roomInstances = new System.Collections.Generic.Dictionary<string, GameObject>();
                    var roomTemplates = FindObjectsOfType<RoomTemplate>();

                    foreach (var template in roomTemplates)
                    {
                        // Use GameObject's instance ID as key since we don't have node IDs in this context
                        roomInstances[template.gameObject.GetInstanceID().ToString()] = template.gameObject;
                    }

                    tilemapSystem.MergeRoomsToMasterTilemap(roomInstances);
                    EditorUtility.DisplayDialog("Success",
                        $"Merged {roomTemplates.Length} rooms to master tilemap!", "OK");
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Corridor Generation", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "To generate corridors, you need to:\n" +
                "1. Assign a Master Tilemap\n" +
                "2. Assign a Corridor Tile\n" +
                "3. Use the generation buttons above during dungeon generation\n\n" +
                "Corridors are automatically generated during dungeon generation, but you can also\n" +
                "manually call GenerateAllCorridors() from code with your DungeonGraphAsset.",
                MessageType.Info
            );

            EditorGUILayout.Space(5);

            // Display current settings
            EditorGUILayout.LabelField("Current Settings:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Grid Cell Size: {tilemapSystem.gridCellSize}");
            EditorGUILayout.LabelField($"Corridor Width: {tilemapSystem.corridorWidth} tiles");
            EditorGUILayout.LabelField($"Master Tilemap: {(tilemapSystem.masterTilemap != null ? tilemapSystem.masterTilemap.name : "Not Assigned")}");
            EditorGUILayout.LabelField($"Corridor Tile: {(tilemapSystem.corridorTile != null ? tilemapSystem.corridorTile.name : "Not Assigned")}");
        }
    }
}
