# Tilemap & Corridor Generation Guide

This guide explains how to use the new tilemap alignment and corridor generation system in the Dungeon Graph tool.

## Overview

The tilemap system provides two main features:
1. **Universal Grid Snapping**: Aligns all rooms to a consistent grid for clean tilemap integration
2. **Corridor Generation**: Creates connecting corridors between rooms using two methods (Direct & Right-Angle)

## Setup

### 1. Create a Master Tilemap

Before generating your dungeon, you need to set up a master tilemap:

1. In your scene hierarchy, create a new **Grid** GameObject (if you don't have one)
2. Under the Grid, create a new **Tilemap** GameObject
3. Name it something like "Master_Tilemap" or "Dungeon_Floor"
4. This tilemap will hold all room tiles and corridors

### 2. Assign a Corridor Tile

1. In your project, create or locate a **Tile** asset to use for corridors
2. This should be a floor tile that matches your dungeon aesthetic
3. Keep this tile handy - you'll assign it in the next step

## Usage

### Automatic Setup (Recommended)

When you generate a dungeon, the system will automatically:

1. Add a `DungeonTilemapSystem` component to the Generated_Dungeon object
2. Snap all rooms to the grid (default: 1 unit cell size)
3. If configured, merge all rooms and generate corridors

### Configuring the System

After generating your first dungeon:

1. Select the **Generated_Dungeon** object in the hierarchy
2. Find the **DungeonTilemapSystem** component in the inspector
3. Configure the settings:
   - **Grid Cell Size**: Size of grid cells (default: 1.0)
   - **Corridor Tile**: Assign your corridor floor tile here
   - **Corridor Width**: Width of corridors in tiles (default: 2)
   - **Master Tilemap**: Drag your Master_Tilemap here

### Generating Corridors

Once configured, corridors will automatically generate when you create a new dungeon:

- **Organic Generation**: Uses **Direct Corridors** (straight lines, more natural)
- **Constraint Generation**: Uses **Right-Angle Corridors** (L-shaped, more structured)

### Manual Control

You can also manually trigger operations using the custom inspector:

1. Select the Generated_Dungeon object
2. In the DungeonTilemapSystem inspector, click:
   - **"Merge Rooms to Master Tilemap"**: Combines all room tilemaps into one

## Technical Details

### Grid Snapping

All room positions are snapped to the nearest grid cell:
```
snappedPos.x = Round(pos.x / cellSize) * cellSize
snappedPos.y = Round(pos.y / cellSize) * cellSize
```

This ensures perfect tile alignment for composite colliders and seamless corridors.

### Tilemap Merging

The system copies all tiles from individual room tilemaps to the master tilemap:
- Accounts for different tilemap origins and transforms
- Preserves tile types and sprites
- Disables source tilemaps to avoid duplication

### Corridor Algorithms

**Direct Corridors** (Organic):
- Uses Bresenham's line algorithm
- Creates straight path from room A center to room B center
- Fills corridor with specified width around the path

**Right-Angle Corridors** (Constraint):
- Creates L-shaped path with two straight segments
- Alternates between horizontal-first and vertical-first for variety
- First segment: start → corner point
- Second segment: corner point → end

### Collision Handling

Corridors won't overwrite existing room tiles:
```csharp
if (!masterTilemap.HasTile(cell))
{
    masterTilemap.SetTile(cell, corridorTile);
}
```

This prevents corridors from damaging room layouts.

## Tips & Best Practices

1. **Cell Size**: Match your grid cell size to your tile size (usually 1.0)
2. **Corridor Width**: 2-3 tiles wide works well for most dungeons
3. **Tile Assets**: Use a dedicated corridor tile for visual consistency
4. **Composite Collider**: After generation, add a Composite Collider 2D to your master tilemap for efficient collision
5. **Re-generation**: The system clears the master tilemap on each generation, so you can iterate freely

## Troubleshooting

**Corridors not appearing?**
- Check that Master Tilemap and Corridor Tile are assigned
- Look for warnings in the Console log
- Verify your corridor tile is not null

**Rooms not aligned properly?**
- Adjust the Grid Cell Size to match your tile size
- Check that room prefabs have RoomTemplate components

**Tiles look duplicated?**
- The system automatically disables source tilemaps after merging
- If you see duplicates, check for multiple active tilemaps

## Code Reference

Key classes:
- `DungeonTilemapSystem.cs` - Main tilemap operations
- `DungeonTilemapSystemEditor.cs` - Custom inspector UI
- `OrganicGeneration.cs` - Integration with organic generation
- `ConstraintGeneration.cs` - Integration with constraint generation

Key methods:
- `SnapRoomsToGrid()` - Aligns rooms to grid
- `MergeRoomsToMasterTilemap()` - Combines all tilemaps
- `GenerateDirectCorridor()` - Creates straight corridors
- `GenerateRightCorridor()` - Creates L-shaped corridors
- `GenerateAllCorridors()` - Processes all connections
