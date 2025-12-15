using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class RoomAuthoringTools
{
    // EditorPrefs keys
    private const string KeyCurrent = "DG_RoomsFolder_Current";
    private const string KeyPrev    = "DG_RoomsFolder_Previous";

    // ---------- PUBLIC MENUS ----------

    [MenuItem("Dungeon/Rooms/Bake & Save/Save %#b")] // Ctrl/Cmd+Shift+B
    public static void BakeSaveQuick()
    {
        var go = GetSelectedRoomRootOrWarn();
        if (!go) return;

        BakeRoom(go);

        // Check if this GameObject is already a prefab instance
        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            // Get the prefab asset path and save to it
            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot);

            if (!string.IsNullOrEmpty(assetPath))
            {
                SavePrefab(go, assetPath);
                return;
            }
        }

        // Not a prefab instance - try to use remembered folder or prompt
        string folder = EditorPrefs.GetString(KeyCurrent, "");
        if (!IsValidAssetsFolder(folder))
        {
            // First time or missing: prompt once
            if (!ChooseFolderAndRemember(out folder)) return;
        }

        // Save into the current folder with default name
        var file = Path.Combine(folder, go.name + ".prefab").Replace("\\", "/");
        SavePrefab(go, file);
    }

    [MenuItem("Dungeon/Rooms/Bake & Save/Save As... _F12")] // F12 opens dialog
    public static void BakeSaveAs()
    {
        var go = GetSelectedRoomRootOrWarn();
        if (!go) return;

        BakeRoom(go);

        // Let user choose file+folder; remember new folder
        string lastDir = EditorPrefs.GetString(KeyCurrent, "Assets");
        string file = EditorUtility.SaveFilePanelInProject(
            "Save Room Prefab",
            go.name + ".prefab",
            "prefab",
            "Choose where to save this room prefab.",
            lastDir
        );
        if (string.IsNullOrEmpty(file)) return;

        RememberFolder(Path.GetDirectoryName(file)!.Replace("\\", "/"));
        SavePrefab(go, file);
    }

    // ---------- CORE HELPERS ----------

    private static GameObject GetSelectedRoomRootOrWarn()
    {
        var go = Selection.activeGameObject;
        if (!go)
        {
            Debug.LogWarning("Select your Room root GameObject in the Hierarchy.");
            return null;
        }

        if (go.GetComponentInChildren<Grid>() == null ||
            go.GetComponentsInChildren<Tilemap>(true).Length == 0)
        {
            Debug.LogError("Selected object doesn't look like a room (need a Grid and at least one Tilemap).");
            return null;
        }

        return go;
    }

    private static void BakeRoom(GameObject go)
    {
        // Trim empties + compress bounds + lock flags
        var tms = go.GetComponentsInChildren<Tilemap>(true);
        foreach (var tm in tms)
        {
            tm.CompressBounds();

            var cb = tm.cellBounds;
            if (cb.size.x == 0 || cb.size.y == 0)
            {
                Object.DestroyImmediate(tm.gameObject);
                continue;
            }

            // Avoid storing per-cell overrides
            for (int x = cb.xMin; x < cb.xMax; x++)
            for (int y = cb.yMin; y < cb.yMax; y++)
            {
                var p = new Vector3Int(x, y, 0);
                if (tm.GetTile(p) == null) continue;
                tm.SetTileFlags(p, TileFlags.LockColor | TileFlags.LockTransform);
            }
        }

        // Recompute bounds/doors metadata
        var rt = go.GetComponent<DungeonGraph.RoomTemplate>() ?? go.AddComponent<DungeonGraph.RoomTemplate>();
        rt.Recompute();

        // Auto-populate exits from "Exits" GameObject if it exists
        PopulateExits(go, rt);
    }

    private static void PopulateExits(GameObject go, DungeonGraph.RoomTemplate rt)
    {
        // Look for a child GameObject named "Exits"
        Transform exitsContainer = go.transform.Find("Exits");

        if (exitsContainer == null)
        {
            // No "Exits" container found, leave exits array as-is
            return;
        }

        // Get all direct children of the Exits container
        int childCount = exitsContainer.childCount;

        if (childCount == 0)
        {
            Debug.LogWarning($"[RoomAuthoringTool] Found 'Exits' container but it has no children. Exits array will be empty.");
            rt.exits = new Transform[0];
            return;
        }

        // Populate the exits array with all children transforms
        rt.exits = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
        {
            rt.exits[i] = exitsContainer.GetChild(i);
        }

        Debug.Log($"[RoomAuthoringTool] Auto-populated {childCount} exit(s) from 'Exits' container");
    }

    private static void SavePrefab(GameObject go, string file)
    {
        // Unpack completely to remove any prefab connections
        // This ensures designers can duplicate existing prefabs for faster iteration
        // and that all data (extents, center, etc.) is properly baked
        if (PrefabUtility.IsPartOfAnyPrefab(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        // Create a new, unconnected prefab from the scene GameObject
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, file);

        if (prefab != null)
        {
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            var rt = go.GetComponent<DungeonGraph.RoomTemplate>();
            Debug.Log($"<color=#4CAF50><b>✓ Saved</b></color> room prefab: <b>{file}</b>\n" +
                      $"Bounds: center={rt.worldBounds.center}, size={rt.worldBounds.size}, " +
                      $"tiles={rt.sizeInCells.x}×{rt.sizeInCells.y}");
        }
        else
        {
            Debug.LogError($"<color=#F44336><b>✗ Failed</b></color> to save prefab at: {file}");
        }
    }

    // ---------- FOLDER MEMORY ----------

    private static void RememberFolder(string newFolder)
    {
        if (!IsValidAssetsFolder(newFolder))
        {
            Debug.LogError("Folder must be inside 'Assets/'.");
            return;
        }
        var cur = EditorPrefs.GetString(KeyCurrent, "");
        if (!string.IsNullOrEmpty(cur) && cur != newFolder)
            EditorPrefs.SetString(KeyPrev, cur);     // keep quick toggle target

        EditorPrefs.SetString(KeyCurrent, newFolder);
    }

    private static bool ChooseFolderAndRemember(out string assetsFolder)
    {
        assetsFolder = "";

        // Use a SaveFilePanel trick to pick an in-project folder
        string lastDir = EditorPrefs.GetString(KeyCurrent, "Assets");
        string probe = EditorUtility.SaveFilePanelInProject(
            "Choose Folder (select any name; we'll use just the directory)",
            "RoomPlaceholder.prefab",
            "prefab",
            "Pick a folder to save future rooms.",
            lastDir
        );
        if (string.IsNullOrEmpty(probe)) return false;

        assetsFolder = Path.GetDirectoryName(probe)!.Replace("\\", "/");
        RememberFolder(assetsFolder);
        return true;
    }

    private static bool IsValidAssetsFolder(string path)
        => !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
}