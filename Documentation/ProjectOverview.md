# Dungeon-Graph Project Overview

This document provides a comprehensive technical overview of the Dungeon-Graph Unity project, a procedural dungeon generation system that uses force-directed graph layouts to create organic, interconnected room layouts.

---

## Table of Contents

1. [Room Authoring Tool](#1-room-authoring-tool)
2. [DungeonGraphView Editor](#2-dungeongraphview-editor)
3. [Settings and Parameters](#3-settings-and-parameters)
4. [OrganicGeneration System](#4-organicgeneration-system)
5. [Tilemap Merging System](#5-tilemap-merging-system)
6. [File Reference](#6-file-reference)

---

## 1. Room Authoring Tool

The Room Authoring Tool provides menu-driven workflows for creating, baking, and saving room prefabs that are compatible with the dungeon generation system.

### Core File

**[RoomAuthoringTool.cs](../Assets/Editor/RoomAuthoringTool.cs)**
A static utility class that provides Unity Editor menu items for room creation. Handles tilemap compression, tile flag locking, exit point discovery, and prefab serialization.

### Workflow

1. **Create Room**: Design a room in the Scene view using Unity's Tilemap system. The room should have a Grid component with child Tilemap objects.

2. **Add Exits**: Create a child GameObject named "Exits" and add empty GameObjects as children to mark corridor connection points.

3. **Bake & Save**: Use `Dungeon > Rooms > Save` or `Save As...` to:
   - Compress tilemap bounds (remove empty margins)
   - Lock tile flags (`LockColor`, `LockTransform`) to prevent per-cell overrides
   - Add/update the `RoomTemplate` component
   - Auto-populate the exits array from the "Exits" container
   - Save as a prefab asset

### Key Features

- **Folder Memory**: Remembers the last save location using EditorPrefs keys (`DG_RoomsFolder_Current`, `DG_RoomsFolder_Previous`)
- **Prefab Updates**: Detects existing prefab instances and saves back to the original asset
- **Validation**: Ensures selected GameObject has Grid + Tilemap components before processing

### Related Runtime Component

**[RoomTemplate.cs](../Assets/Scripts/Runtime/RoomTemplate.cs)**
MonoBehaviour attached to room prefabs that stores computed metadata:
- `worldBounds` - World-space bounding box (center + size)
- `sizeInCells` - Width/height in tile units
- `tilemaps` - Array of all child Tilemap components
- `exits` - Transform array marking corridor connection points
- `Recompute()` - Recalculates bounds from all child tilemaps
- `RepopulateExitsIfNeeded()` - Auto-discovers exits from "Exits" GameObject at runtime

---

## 2. DungeonGraphView Editor

The DungeonGraphView is the main visual editor window for designing dungeon layouts using a node-based graph interface.

### Core Files

**[DungeonGraphView.cs](../Assets/Scripts/Editor/DungeonGraphView.cs)**
The main GraphView-based editor that provides the visual interface. Extends Unity's `GraphView` class for UIElements support. Handles node creation, connections, parameter UI, and generation controls.

**[DungeonGraphEditorWindow.cs](../Assets/Scripts/Editor/DungeonGraphEditorWindow.cs)**
The EditorWindow host that creates and manages the DungeonGraphView instance. Opened via `Window > Dungeon Graph` or by double-clicking a DungeonGraphAsset.

**[DungeonGraphEditorNode.cs](../Assets/Scripts/Editor/DungeonGraphEditorNode.cs)**
Visual representation of graph nodes. Extends Unity's `Node` class with custom port layout (center-positioned input/output), styling based on node type, and exposed property fields.

**[DungeonGraphWindowSearchProvider.cs](../Assets/Scripts/Editor/DungeonGraphWindowSearchProvider.cs)**
Provides the node creation search menu (right-click > Create Node). Discovers available node types via reflection.

### UI Structure

The editor consists of three main panels:

#### 1. Graph Canvas
- Interactive node graph with pan/zoom support (0.5x - 10x)
- Grid background for visual alignment
- Copy/paste functionality for nodes
- Drag-and-drop connections between node ports

#### 2. Dungeon Tools Panel (Blackboard)
A movable, collapsible panel containing:

**Generation Controls:**
- "Generate Dungeon" - Full generation (rooms + corridors)
- "Generate Rooms" - Physics simulation only
- "Generate Corridors" - Connection pathways only
- "Clear Dungeon" - Remove all generated objects

**Floor Selection:**
- Dropdown for selecting active dungeon floor
- "Create New Floor" button for new floor hierarchies

**Room Settings:**
- Ideal Distance, Repulsion Factor, Stiffness Factor
- Simulation Iterations, Real-Time toggle, Simulation Speed

**Corridor Settings:**
- Corridor Tile asset reference
- Corridor Width (tiles)
- Corridor Type (Direct, Angled, Both)

**Advanced Settings (Foldout):**
- Area Placement Factor, Force Mode, Chaos Factor
- Overlap prevention settings, regeneration limits

#### 3. Node Settings Panel
Displays properties for the selected node:
- Spawn Chance slider (0-100%)
- Warning indicator for conditional nodes with multiple connections

### Data Persistence

All editor settings are persisted using EditorPrefs with the prefix `DungeonGraph.*`:
- Parameters survive editor restarts
- Settings are workspace-global (not per-asset)

### Related Data Files

**[DungeonGraphAsset.cs](../Assets/Scripts/Runtime/DungeonGraphAsset.cs)**
ScriptableObject that stores the graph structure:
- `Nodes` - List of DungeonGraphNode instances
- `Connections` - List of DungeonGraphConnection links
- `Init()` - Builds lookup dictionaries for fast access
- `GetStartNode()` / `GetNode(id)` - Node retrieval methods

**[DungeonGraphNode.cs](../Assets/Scripts/Runtime/DungeonGraphNode.cs)**
Base class for all node types:
- `id` - Unique GUID
- `position` - Editor rect for visual placement
- `spawnChance` - Conditional spawning (0-100%)
- `typeName` - Assembly-qualified type name for serialization

**[DungeonGraphConnection.cs](../Assets/Scripts/Runtime/DungeonGraphConnection.cs)**
Data structure for node connections:
- `inputPort` / `outputPort` - Connection endpoints with nodeId and portIndex

---

## 3. Settings and Parameters

### Physics Simulation Parameters

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| **Ideal Distance** | 20 | 0+ | Target gap between connected rooms (in world units) |
| **Repulsion Factor** | 1.0 | 0.1-5.0 | Multiplier for repulsion force between all rooms |
| **Stiffness Factor** | 1.0 | 0.1-20.0 | Multiplier for spring attraction between connected rooms |
| **Simulation Iterations** | 100 | 1+ | Number of physics iterations (when not in Force Mode) |
| **Force Mode** | false | - | If true, iterate until convergence (up to 2096 iterations) |
| **Chaos Factor** | 0.0 | 0-1 | Random velocity perturbation strength |

### Simulation Mode Parameters

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| **Real-Time Simulation** | false | - | If true, animate the simulation; if false, compute instantly |
| **Simulation Speed** | 10 | 1-1000 | Iterations per second (only in real-time mode) |

### Overlap Prevention Parameters

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| **Allow Room Overlap** | false | - | If true, skip overlap detection |
| **Max Room Regenerations** | 3 | 0+ | Retry limit for room placement |
| **Max Corridor Regenerations** | 3 | 0+ | Retry limit for corridor routing |

### Corridor Parameters

| Parameter | Default | Options | Description |
|-----------|---------|---------|-------------|
| **Corridor Tile** | null | TileBase | Tile asset used for corridor floors |
| **Corridor Width** | 2 | 1+ | Width of corridors in tiles |
| **Corridor Type** | Direct | Direct, Angled, Both | Corridor shape preference |

### Internal Physics Constants

These are defined in `DungeonSimulationUtility.cs`:

```csharp
float springStiffness = 0.01f * stiffnessFactor;
float repulsionStrength = 50f * repulsionFactor;
float damping = 0.9f;
float energyThreshold = 0.01f; // For force mode convergence
```

---

## 4. OrganicGeneration System

The OrganicGeneration system implements a force-directed graph layout algorithm to position rooms organically while respecting graph topology.

### Core Files

**[OrganicGeneration.cs](../Assets/Scripts/Editor/OrganicGeneration.cs)**
Static class containing the main generation logic:
- `GenerateRooms()` - Entry point for room generation
- `GenerateCorridors()` - Entry point for corridor generation
- `ProcessSpawnChances()` - Handles conditional node spawning
- `InstantiateRoomForNode()` - Loads and instantiates room prefabs
- `PostSimulationSetup()` - Called after real-time simulation completes

**[DungeonSimulationUtility.cs](../Assets/Scripts/Runtime/DungeonSimulationUtility.cs)**
Shared physics simulation logic used by both instant and real-time generation:
- `SimulateForces()` - Full simulation loop (for instant mode)
- `CalculateForcesForIteration()` - Single iteration (for real-time mode)

**[DungeonSimulationController.cs](../Assets/Scripts/Runtime/DungeonSimulationController.cs)**
Runtime MonoBehaviour for real-time simulation:
- Runs one physics iteration per frame with configurable delay
- Smoothly lerps room positions toward simulation targets
- Calls PostSimulationSetup via reflection when complete

### Generation Flow

```
1. GenerateRooms() called from DungeonGraphView
   │
   ├─► ProcessSpawnChances()
   │   └─ Remove nodes that fail spawn roll, reconnect neighbors
   │
   ├─► CalculateGraphDistances()
   │   └─ Floyd-Warshall all-pairs shortest paths
   │
   ├─► Instantiate rooms at random circular positions
   │
   ├─► Run Simulation
   │   ├─ Instant: DungeonSimulationUtility.SimulateForces()
   │   └─ RealTime: DungeonSimulationController coroutine
   │
   ├─► Check for overlaps (if enabled)
   │   └─ Regenerate up to maxRoomRegenerations times
   │
   ├─► SnapRoomsToGrid()
   │
   ├─► MergeRoomsToMasterTilemap()
   │
   └─► Add DungeonConnectionVisualizer for debug rendering
```

### Physics Algorithm

The simulation uses a spring-force model with repulsion:

**Spring Forces** (connected rooms attract):
```
direction = neighbor.position - current.position
pairIdealDistance = radiusA + radiusB + idealDistance
force = springStiffness * (distance - pairIdealDistance)
```

**Repulsion Forces** (all pairs repel, scaled by graph distance):
```
graphDist = shortest path length between rooms in graph
repulsion = (repulsionStrength * graphDist) / (distance^2)
```

This causes rooms that are topologically far apart in the graph to repel more strongly, creating natural clustering of related rooms.

### Room Instantiation

Rooms are loaded from floor-specific folders:
```
Assets/Dungeon_Floors/{floorName}/{NodeType}/{Size}/
```

For example, a Medium-sized BasicNode on floor "Demo" would load from:
```
Assets/Dungeon_Floors/Demo/Basic/Medium/
```

The system tracks spawned prefabs per type to avoid duplicates until all available prefabs are used.

### Related Files

**[RoomNodeReference.cs](../Assets/Scripts/Runtime/RoomNodeReference.cs)**
Component added to each instantiated room to link it back to its graph node:
- `nodeId` - Graph node GUID
- `nodeTypeName` - Simple type name (e.g., "Basic", "Hub")

**[DungeonConnectionVisualizer.cs](../Assets/Scripts/Runtime/DungeonConnectionVisualizer.cs)**
Debug component that draws gizmos showing room bounds and connections.

**[DungeonFloorManager.cs](../Assets/Scripts/Editor/DungeonFloorManager.cs)**
Utility for managing floor folder structures and creating blank room templates.

---

## 5. Tilemap Merging System

The tilemap system handles grid alignment, tile composition, and corridor generation.

### Core File

**[DungeonTilemapSystem.cs](../Assets/Scripts/Runtime/DungeonTilemapSystem.cs)**
MonoBehaviour that manages all tilemap operations:

#### Grid Alignment

`SnapRoomsToGrid(roomInstances)`
- Snaps all room positions to a universal grid
- Ensures tile-perfect alignment between merged rooms
- Uses configurable `gridCellSize` (default: 1.0)

```csharp
snappedPos = Round(currentPos / gridCellSize) * gridCellSize
```

#### Tilemap Merging

`MergeRoomsToMasterTilemap(roomInstances)`
1. Clears the master tilemap
2. For each room:
   - Gets all child tilemaps from RoomTemplate
   - Calculates world offset between source and master
   - Copies tiles block-by-block with offset translation
3. Disables source tilemaps to prevent visual duplication

The master tilemap must be tagged "Dungeon" for auto-discovery.

#### Corridor Generation

Three corridor types are supported:

**Direct Corridors** (`GenerateDirectCorridor`)
- Straight line using Bresenham's algorithm
- Corridor width applied centered on path

**Angled Corridors** (`GenerateRightCorridor`)
- L-shaped path with corner point
- Can be horizontal-first or vertical-first

**Both** (random per connection)
- 50/50 mix of direct and angled

#### Exit Point Selection

`GetBestConnectionPoint(sourceRoom, targetRoom)`
- If room has exits defined, uses closest exit to target room
- Falls back to room center if no valid exits

#### Overlap Prevention

`GenerateAllCorridorsWithOverlapPrevention(graph, roomInstances, corridorType, maxRegenerations)`

Attempts multiple strategies to avoid corridor-room overlaps:
1. **Attempts 1-2**: Try different exit point combinations
2. **Attempts 3-5**: Try different corridor types (Direct, Angled-H, Angled-V)
3. **Fallback**: Proceed anyway after max attempts

### Coordinate Systems

The system handles two coordinate spaces:
- **World Space**: Room positions, exit points
- **Cell Space**: Tilemap grid coordinates

Conversion uses:
```csharp
cellPos = tilemap.WorldToCell(worldPos)
worldPos = tilemap.CellToWorld(cellPos)
```

---

## 6. File Reference

### Editor Scripts (`Assets/Scripts/Editor/`)

| File | Purpose |
|------|---------|
| [DungeonGraphView.cs](../Assets/Scripts/Editor/DungeonGraphView.cs) | Main graph editor UI with parameter controls |
| [DungeonGraphEditorWindow.cs](../Assets/Scripts/Editor/DungeonGraphEditorWindow.cs) | EditorWindow host for the graph view |
| [DungeonGraphEditorNode.cs](../Assets/Scripts/Editor/DungeonGraphEditorNode.cs) | Visual node representation with ports |
| [DungeonGraphWindowSearchProvider.cs](../Assets/Scripts/Editor/DungeonGraphWindowSearchProvider.cs) | Node creation search menu |
| [OrganicGeneration.cs](../Assets/Scripts/Editor/OrganicGeneration.cs) | Main generation algorithm and room instantiation |
| [DungeonFloorManager.cs](../Assets/Scripts/Editor/DungeonFloorManager.cs) | Floor folder management and blank room creation |
| [BlankRoomGenerator.cs](../Assets/Scripts/Editor/BlankRoomGenerator.cs) | Template room prefab creation |
| [CreateFloorWindow.cs](../Assets/Scripts/Editor/CreateFloorWindow.cs) | UI for creating new floor hierarchies |
| [CreateCustomNodeTypeWindow.cs](../Assets/Scripts/Editor/CreateCustomNodeTypeWindow.cs) | UI for registering custom node types |
| [PortTypes.cs](../Assets/Scripts/Editor/PortTypes.cs) | Port type definitions for node connections |

### Runtime Scripts (`Assets/Scripts/Runtime/`)

| File | Purpose |
|------|---------|
| [DungeonGraphAsset.cs](../Assets/Scripts/Runtime/DungeonGraphAsset.cs) | ScriptableObject storing graph structure |
| [DungeonGraphNode.cs](../Assets/Scripts/Runtime/DungeonGraphNode.cs) | Base class for all node types |
| [DungeonGraphConnection.cs](../Assets/Scripts/Runtime/DungeonGraphConnection.cs) | Connection data structure |
| [DungeonSimulationUtility.cs](../Assets/Scripts/Runtime/DungeonSimulationUtility.cs) | Shared physics simulation algorithm |
| [DungeonSimulationController.cs](../Assets/Scripts/Runtime/DungeonSimulationController.cs) | Real-time simulation runner |
| [DungeonTilemapSystem.cs](../Assets/Scripts/Runtime/DungeonTilemapSystem.cs) | Tilemap merging and corridor generation |
| [RoomTemplate.cs](../Assets/Scripts/Runtime/RoomTemplate.cs) | Room metadata component |
| [RoomNodeReference.cs](../Assets/Scripts/Runtime/RoomNodeReference.cs) | Runtime room-to-node linking |
| [DungeonConnectionVisualizer.cs](../Assets/Scripts/Runtime/DungeonConnectionVisualizer.cs) | Debug gizmo renderer |
| [DungeonFloorConfig.cs](../Assets/Scripts/Runtime/DungeonFloorConfig.cs) | Floor configuration data |
| [CustomNodeType.cs](../Assets/Scripts/Runtime/CustomNodeType.cs) | Custom node type definition |
| [CustomNodeTypeRegistry.cs](../Assets/Scripts/Runtime/CustomNodeTypeRegistry.cs) | Registry of custom node types |

### Node Types (`Assets/Scripts/Runtime/Types/`)

| File | Purpose |
|------|---------|
| [StartNode.cs](../Assets/Scripts/Runtime/Types/StartNode.cs) | Dungeon entry point |
| [BasicNode.cs](../Assets/Scripts/Runtime/Types/BasicNode.cs) | Generic rooms with size options (Small/Medium/Large) |
| [HubNode.cs](../Assets/Scripts/Runtime/Types/HubNode.cs) | Central gathering areas |
| [BossNode.cs](../Assets/Scripts/Runtime/Types/BossNode.cs) | Boss encounter rooms |
| [RewardNode.cs](../Assets/Scripts/Runtime/Types/RewardNode.cs) | Treasure/reward rooms |
| [EndNode.cs](../Assets/Scripts/Runtime/Types/EndNode.cs) | Dungeon exits |
| [DebugLogNode.cs](../Assets/Scripts/Runtime/Types/DebugLogNode.cs) | Debug/logging utility |
| [CustomNode.cs](../Assets/Scripts/Runtime/Types/CustomNode.cs) | User-created node types |

### Other Files

| File | Purpose |
|------|---------|
| [RoomAuthoringTool.cs](../Assets/Editor/RoomAuthoringTool.cs) | Room baking and saving utilities |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        EDITOR LAYER                                  │
├─────────────────────────────────────────────────────────────────────┤
│  DungeonGraphEditorWindow                                           │
│       │                                                             │
│       └──► DungeonGraphView                                         │
│                 │                                                   │
│                 ├──► DungeonGraphEditorNode (visual nodes)          │
│                 │                                                   │
│                 └──► OrganicGeneration                              │
│                           │                                         │
│                           ├──► Room Instantiation                   │
│                           │         │                               │
│                           │         └──► Prefab Loading             │
│                           │                                         │
│                           └──► Simulation                           │
│                                     │                               │
├─────────────────────────────────────┼───────────────────────────────┤
│                        RUNTIME LAYER│                               │
├─────────────────────────────────────┼───────────────────────────────┤
│                                     ▼                               │
│  ┌─────────────────────────────────────────────────────────┐       │
│  │           DungeonSimulationUtility                       │       │
│  │  (Shared physics: springs, repulsion, damping)           │       │
│  └─────────────────────────────────────────────────────────┘       │
│                     │                                               │
│        ┌────────────┴────────────┐                                  │
│        ▼                         ▼                                  │
│  Instant Mode              Real-Time Mode                           │
│  (batch loop)              (DungeonSimulationController)            │
│        │                         │                                  │
│        └────────────┬────────────┘                                  │
│                     ▼                                               │
│  ┌─────────────────────────────────────────────────────────┐       │
│  │              DungeonTilemapSystem                        │       │
│  │  - SnapRoomsToGrid                                       │       │
│  │  - MergeRoomsToMasterTilemap                             │       │
│  │  - GenerateCorridors                                     │       │
│  └─────────────────────────────────────────────────────────┘       │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────┐       │
│  │              Data Model                                  │       │
│  │  DungeonGraphAsset ◄──► DungeonGraphNode                 │       │
│  │                    ◄──► DungeonGraphConnection           │       │
│  └─────────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────────┘
```

---

*Document generated for Dungeon-Graph Unity project*
