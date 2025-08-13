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

        string folder = EditorPrefs.GetString(KeyCurrent, "");
        if (!IsValidAssetsFolder(folder))
        {
            // First time or missing: prompt once
            if (!ChooseFolderAndRemember(out folder)) return;
        }

        // Bake, then save into the current folder with default name
        BakeRoom(go);
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
        var rt = go.GetComponent<RoomTemplate>() ?? go.AddComponent<RoomTemplate>();
        rt.Recompute();
    }

    private static void SavePrefab(GameObject go, string file)
    {
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, file, InteractionMode.UserAction);
        if (prefab != null)
        {
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            var rt = go.GetComponent<RoomTemplate>();
            Debug.Log($"Saved room prefab at: <b>{file}</b>\n" +
                      $"World bounds center={rt.worldBounds.center}, size={rt.worldBounds.size}, " +
                      $"tiles={rt.sizeInCells.x}x{rt.sizeInCells.y}");
        }
        else
        {
            Debug.LogError("Failed to save prefab.");
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