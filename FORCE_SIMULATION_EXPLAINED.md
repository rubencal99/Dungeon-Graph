# Force Simulation Deep Dive: OrganicGeneration.cs

## Overview

The `SimulateForces` function in [OrganicGeneration.cs:448-583](Assets/Scripts/Editor/OrganicGeneration.cs#L448-L583) uses a **force-directed graph layout algorithm** to position dungeon rooms in 2D space. It simulates physical forces between connected and unconnected nodes to create visually pleasing, organic layouts.

---

## The Three Core Loops

The simulation runs iteratively, and each iteration consists of three main phases:

### 1. **Spring Force Loop** (Lines 482-504)
### 2. **Repulsion Force Loop** (Lines 506-534)
### 3. **Update Velocities & Positions Loop** (Lines 536-563)

---

## Visual Explanation

Let's visualize how these forces work with a simple 4-room dungeon:

```
Initial Random Placement:

    [A]────────[B]
     │
     │          [C]
     │           │
    [D]─────────┘

A-B: Connected (edge in graph)
A-D: Connected (edge in graph)
C-D: Connected (edge in graph)
B-C: NOT connected (but share graph distance = 2)
```

---

## 1. Spring Force Loop (Attraction)

**Purpose**: Pull connected rooms toward each other to reach an ideal distance.

**How it works**:
```
For each room:
    For each neighbor (connected by an edge):
        Calculate distance between them
        If distance > idealDistance:
            Apply PULL force (attract them closer)
        If distance < idealDistance:
            Apply PUSH force (push them apart)
```

**The Math**:
```csharp
// Lines 518-525: OrganicGeneration.cs
// Calculate ideal distance for this pair: radiusA + radiusB + gap
float radiusA = roomRadii.ContainsKey(nodeId) ? roomRadii[nodeId] : 5f;
float radiusB = roomRadii.ContainsKey(neighborId) ? roomRadii[neighborId] : 5f;
float pairIdealDistance = radiusA + radiusB + idealDistance;

// Spring force proportional to distance from ideal
float force = springStiffness * (distance - pairIdealDistance);
forces[nodeId] += direction * force;
```

**Key Insight**: The `idealDistance` slider controls the **edge-to-edge gap** between rooms, not the center-to-center distance! The actual ideal distance for each pair is calculated as:

```
pairIdealDistance = radiusA + radiusB + idealDistance_slider
```

This ensures that:
- Two large rooms have the same edge gap as two small rooms
- Room size is properly accounted for
- The slider value has consistent meaning across all room pairs

**Visual Example**:

```
Room Sizes:
[A] radius=5, [B] radius=8, [C] radius=3, [D] radius=5
idealDistance_slider = 10

Current State (centers):              After Spring Forces:

[  A  ]──────────────[   B   ]        [  A  ]────────→[   B   ]
   ↓                                         ↓          ←
   │         [C]                        [  D  ]←──────[C]
   ↓          │
[  D  ]──────┘

Forces Applied (with room radii considered):
• A→B: radiusA=5, radiusB=8, gap=10
       pairIdeal = 5+8+10 = 23
       distance=40 → PULL with force=0.01*(40-23)=0.17

• A→D: radiusA=5, radiusD=5, gap=10
       pairIdeal = 5+5+10 = 20
       distance=30 → PULL with force=0.01*(30-20)=0.10

• D→C: radiusD=5, radiusC=3, gap=10
       pairIdeal = 5+3+10 = 18
       distance=25 → PULL with force=0.01*(25-18)=0.07

Notice: Even though rooms have different sizes, the slider value (10)
represents the consistent edge-to-edge gap for all pairs!
```

**Key Parameters**:
- `idealDistance` (now controllable via slider, default=20): The **edge-to-edge gap** between connected rooms
  - **NOT** the center-to-center distance!
  - Actual pairIdealDistance = radiusA + radiusB + idealDistance
  - This ensures consistent spacing regardless of room size
- `springStiffness = 0.01f * stiffnessFactor`: How strong the spring force is
  - Higher stiffness = rooms snap to ideal distance faster
  - Lower stiffness = rooms drift more gradually
- `roomRadii`: Calculated as `max(width, height) / 2` from room bounds

---

## 2. Repulsion Force Loop (Separation)

**Purpose**: Push ALL rooms away from each other to prevent overlap and maintain spacing.

**How it works**:
```
For each pair of rooms (i, j):
    Calculate distance between them
    Calculate their graph distance (shortest path in the connection graph)
    Apply PUSH force inversely proportional to distance²
    Scale force by graph distance
```

**The Math**:
```csharp
// Lines 523-531: OrganicGeneration.cs
int graphDist = graphDistances[(nodeA, nodeB)];
float repulsion = repulsionStrength * graphDist / (distance * distance);

forces[nodeA] -= direction * repulsion;  // Push A away
forces[nodeB] += direction * repulsion;  // Push B away
```

**Why inverse square?**
Mimics physical repulsion forces (like electrostatic repulsion). Closer rooms repel MUCH more strongly:
- distance=5 → force ∝ 1/25 = 0.04
- distance=10 → force ∝ 1/100 = 0.01
- distance=20 → force ∝ 1/400 = 0.0025

**Graph Distance Scaling**:
This is clever! Rooms farther apart in the graph topology should be pushed farther apart spatially.

```
Example:
    [A]───[B]───[C]

A-B graph distance = 1 (direct connection)
A-C graph distance = 2 (through B)

Even if A and C are physically close:
• Repulsion(A,C) = 50 * 2 / distance²
• Repulsion(A,B) = 50 * 1 / distance²

C gets pushed harder away from A because it's "farther" in the graph!
```

**Visual Example**:

```
Current State:              After Repulsion Forces:

[A]───→  [B]               [A]←──────────→[B]
 ↓         ↑                 ↓             ↑
 │        ↑                  ↓             ↑
 ↓   ← [C]                  [D]←─────────→[C]
[D]←───┘

Repulsion applied to ALL pairs:
• A↔B: distance=15, graphDist=1 → repulsion=50*1/(15²)=0.22
• A↔C: distance=18, graphDist=2 → repulsion=50*2/(18²)=0.31 (STRONGER!)
• A↔D: distance=12, graphDist=1 → repulsion=50*1/(12²)=0.35
• B↔C: distance=10, graphDist=2 → repulsion=50*2/(10²)=1.00 (VERY STRONG!)
• B↔D: distance=20, graphDist=2 → repulsion=50*2/(20²)=0.25
• C↔D: distance=8, graphDist=1 → repulsion=50*1/(8²)=0.78
```

**Key Parameters**:
- `repulsionStrength = 50f * repulsionFactor`: Base strength of repulsion
  - Higher = rooms spread out more
  - Lower = rooms cluster tighter
- `graphDist`: Multiplier based on topological distance
  - Ensures logically distant rooms are physically distant

---

## 3. Update Velocities & Positions Loop

**Purpose**: Apply accumulated forces to room velocities and update positions.

**How it works**:
```
For each room:
    1. Add forces to velocity
    2. Add optional chaos (random jitter)
    3. Apply damping to velocity
    4. Update position based on velocity
```

**The Physics**:
```csharp
// Lines 540-556: OrganicGeneration.cs
velocities[nodeId] += forces[nodeId];           // F = ma (acceleration)

// Optional chaos injection
if (chaosFactor > 0f) {
    velocities[nodeId] += randomVector * chaosFactor;
}

velocities[nodeId] *= damping;                  // Energy dissipation
roomPositions[nodeId] += velocities[nodeId];    // Update position
```

**Damping**:
```
damping = 0.9f (90% velocity retained each frame)

Example:
Iteration 1: velocity = 10 units/sec
Iteration 2: velocity = 10 * 0.9 = 9 units/sec
Iteration 3: velocity = 9 * 0.9 = 8.1 units/sec
...
Converges to 0 as forces balance
```

**Visual Example**:

```
Forces → Velocities → Positions

Iteration 1:
[A] forces: (+2, -3)
    velocity: (0,0) + (2,-3) = (2,-3)
    velocity after damping: (2,-3) * 0.9 = (1.8, -2.7)
    new position: (10,10) + (1.8,-2.7) = (11.8, 7.3)

Iteration 2:
[A] forces: (+1, -1)
    velocity: (1.8,-2.7) + (1,-1) = (2.8, -3.7)
    velocity after damping: (2.8,-3.7) * 0.9 = (2.52, -3.33)
    new position: (11.8,7.3) + (2.52,-3.33) = (14.32, 3.97)

... continues until forces balance and velocity → 0
```

**Key Parameters**:
- `damping = 0.9f`: Energy loss per iteration (prevents oscillation)
  - 1.0 = no damping (system never settles)
  - 0.5 = heavy damping (converges very fast)
  - 0.9 = balanced (smooth convergence)
- `chaosFactor`: Random perturbation strength (0.0 to 1.0)
  - Adds variety to layouts
  - Can help escape local minima

---

## How They Work Together

### Complete Iteration Cycle:

```
┌─────────────────────────────────────────────────────────────┐
│  Start Iteration N                                          │
└────────────┬────────────────────────────────────────────────┘
             │
             ▼
     ┌───────────────────┐
     │ Spring Force Loop │  ← Connected rooms attract/repel to ideal distance
     └─────────┬─────────┘
               │
               ▼
     ┌───────────────────┐
     │ Repulsion Loop    │  ← All rooms push each other away
     └─────────┬─────────┘
               │
               ▼
     ┌───────────────────┐
     │ Update Loop       │  ← Apply forces → velocities → positions
     └─────────┬─────────┘
               │
               ▼
     ┌───────────────────┐
     │ Calculate Energy  │  ← Check if system has settled
     └─────────┬─────────┘
               │
               ▼
        Energy < Threshold? ──Yes──→ DONE
               │
               No
               │
               ▼
        Iteration N+1
```

### Force Balance Example:

```
Iteration 0 (Random Start):

    [B]
           [A]

         [D]    [C]

Total Energy: 1500 (high velocities, forces unbalanced)


Iteration 20:

    [A]────[B]
     │
     │     [C]
     │      │
    [D]────┘

Total Energy: 250 (forces starting to balance)


Iteration 100:

    [A]──────[B]
     │
     │
     │       [C]
     │        │
    [D]──────┘

Total Energy: 0.008 (equilibrium reached!)

Springs at ideal length ✓
Repulsion balanced with springs ✓
Velocities → 0 ✓
```

---

## Parameter Guide

### **Ideal Distance** (NEW! Controllable via slider)
**Default**: 20
**Range**: 5-50
**Effect**: **Edge-to-edge gap** between connected rooms

**IMPORTANT**: This is NOT center-to-center distance! The actual ideal distance is:
```
pairIdealDistance = radiusA + radiusB + idealDistance_slider
```

```
idealDistance = 5          idealDistance = 20
(Tight gap)                (Wide gap)

Small rooms (radius=3 each):
pairIdeal = 3+3+5 = 11     pairIdeal = 3+3+20 = 26
[A]─[B]                    [A]──────────[B]

Large rooms (radius=10 each):
pairIdeal = 10+10+5 = 25   pairIdeal = 10+10+20 = 40
[ A ]─[ B ]                [  A  ]──────────[  B  ]

Notice: The EDGE gap is 5 units in both cases on the left,
and 20 units in both cases on the right, regardless of room size!
```

### **Stiffness Factor**
**Default**: 1.0
**Effect**: Spring force strength

```
Low Stiffness (0.2)        High Stiffness (3.0)
(Slow, fluid)              (Fast, rigid)

Converges in 200+ iters    Converges in 50 iters
Smooth organic layout      More geometric layout
```

### **Repulsion Factor**
**Default**: 1.0
**Effect**: How strongly rooms push each other away

```
Low Repulsion (0.3)        High Repulsion (2.0)
(Tight packing)            (Spread out)

Rooms may overlap          Wide separation
Dense dungeons             Sparse dungeons
```

### **Chaos Factor**
**Default**: 0.0
**Range**: 0.0-1.0
**Effect**: Random jitter added to velocities

```
Chaos = 0.0                Chaos = 0.5
(Deterministic)            (Organic variation)

Same graph always          Different each time
looks identical            More natural feeling
```

### **Force Mode**
**Effect**: Run until energy < threshold (instead of fixed iterations)

```
Fixed Iterations (100)     Force Mode (auto-stop)

May not fully converge     Runs until stable
OR may waste iterations    Always converges
                          (max 2096 iters)
```

---

## Real-Time Simulation

When `realTimeSimulation = true`, the same physics run via `DungeonSimulationController.cs`, but:

1. **Smooth interpolation**: Rooms lerp to target positions for visual smoothness
2. **Configurable speed**: `simulationSpeed` controls iterations/second
3. **Live visualization**: Watch forces work in real-time!

```
Target positions update: 30 iterations/sec (configurable)
Visual positions lerp: Every frame (smooth)

Result: Buttery smooth force-directed animation!
```

---

## Overlap Prevention (NEW!)

To ensure high-quality dungeons, an automatic overlap detection and regeneration system is built in for **both instant and real-time simulations**:

### **Allow Room Overlap** (Default: OFF)
When disabled, the generator automatically checks if rooms are overlapping after simulation and regenerates if needed.

### **Max Regenerations** (Default: 3)
Maximum number of times to retry generation if overlap is detected.

### How it works (Instant Mode):

```
┌─────────────────────────────────────────────────────────────┐
│  Attempt 1: Random Placement → Simulation → Check Overlap  │
└────────────────────────┬────────────────────────────────────┘
                         │
                    Overlap? ──No──→ Success! ✓
                         │
                        Yes
                         ↓
┌─────────────────────────────────────────────────────────────┐
│  Attempt 2: New Random Placement → Simulation → Check      │
└────────────────────────┬────────────────────────────────────┘
                         │
                    Overlap? ──No──→ Success! ✓
                         │
                        Yes
                         ↓
        (Continues up to maxRegenerations)
                         │
                         ↓
        Max reached? ──→ Warning logged, proceed anyway
```

### How it works (Real-Time Mode):

```
Start Simulation:
├─ Rooms placed at random positions
├─ Force simulation begins (visible animation)
└─ Simulation completes
    │
    ├─ Check Overlap?
    │   ├─ No → Success! Proceed to corridors ✓
    │   └─ Yes → Regenerate new positions, restart simulation
    │
    └─ Continue until success or max regenerations reached
```

**Visual Feedback**: In real-time mode, you'll see the simulation restart with new positions if overlap is detected. The rooms will "snap" to new random positions and the force simulation will run again.

**Benefits**:
- Works in **both instant and real-time modes**
- Designers get clean, non-overlapping layouts by default
- Automatic retry prevents manual regeneration
- Adjustable max attempts prevents infinite loops
- Can be disabled for experimental/chaotic layouts
- **Real-time**: Visual feedback shows restarts happening

**Note**: Overlap check uses room bounds (bounding boxes), so rooms with irregular shapes may still have visual gaps even when "not overlapping."

---

## Tips for Best Results

### Tight, clustered dungeons:
- ↓ Ideal Distance (10-15)
- ↓ Repulsion Factor (0.5-0.8)
- ↑ Stiffness (1.5-2.0)
- Allow Room Overlap: OFF (recommended)

### Wide, sprawling dungeons:
- ↑ Ideal Distance (30-40)
- ↑ Repulsion Factor (1.5-2.5)
- ↓ Stiffness (0.5-0.8)
- Allow Room Overlap: OFF (recommended)

### Organic, natural feeling:
- Chaos Factor (0.2-0.4)
- Medium Ideal Distance (20-25)
- Balanced Repulsion (1.0)
- Allow Room Overlap: OFF

### Fast convergence:
- Force Mode enabled
- High Stiffness (2.0+)
- No Chaos
- Allow Room Overlap: ON (skip checks for speed)

---

## Algorithm Complexity

```
Per iteration:
• Spring Loop: O(E) where E = number of edges
• Repulsion Loop: O(N²) where N = number of rooms
• Update Loop: O(N)

Total: O(N²) per iteration

For typical dungeons (10-30 rooms):
• 10 rooms: 45 repulsion calculations
• 20 rooms: 190 repulsion calculations
• 30 rooms: 435 repulsion calculations

This is why large dungeons need more iterations or Force Mode!
```

---

## Summary

The force simulation creates beautiful dungeon layouts by:

1. **Springs** pull connected rooms to ideal spacing
2. **Repulsion** prevents overlap and maintains separation
3. **Velocity updates** apply forces smoothly with damping
4. **Iteration** continues until forces balance (equilibrium)

The new **Ideal Distance** slider gives you direct control over the most important parameter: how far apart connected rooms should be!

Experiment with the parameters in the Dungeon Tools panel to find your perfect layout style!

---

## Code References

### Main Generation
- Main generation method: [OrganicGeneration.cs:13-323](Assets/Scripts/Editor/OrganicGeneration.cs#L13-L323)
- Regeneration loop: [OrganicGeneration.cs:62-135](Assets/Scripts/Editor/OrganicGeneration.cs#L62-L135)
- Overlap detection: [OrganicGeneration.cs:708-749](Assets/Scripts/Editor/OrganicGeneration.cs#L708-L749)

### Force Simulation
- Main simulation: [OrganicGeneration.cs:513-622](Assets/Scripts/Editor/OrganicGeneration.cs#L513-L622)
- Room radii calculation (instant): [OrganicGeneration.cs:520-537](Assets/Scripts/Editor/OrganicGeneration.cs#L520-L537)
- Spring force with radii (instant): [OrganicGeneration.cs:583-590](Assets/Scripts/Editor/OrganicGeneration.cs#L583-L590)

### Real-Time Simulation
- Real-time controller: [DungeonSimulationController.cs:77-315](Assets/Scripts/Runtime/DungeonSimulationController.cs#L77-L315)
- Room radii calculation (realtime): [DungeonSimulationController.cs:87-104](Assets/Scripts/Runtime/DungeonSimulationController.cs#L87-L104)
- Spring force with radii (realtime): [DungeonSimulationController.cs:151-158](Assets/Scripts/Runtime/DungeonSimulationController.cs#L151-L158)

### UI Controls
- Graph view UI: [DungeonGraphView.cs:118-309](Assets/Scripts/Editor/DungeonGraphView.cs#L118-L309)
- Ideal distance slider: [DungeonGraphView.cs:193-200](Assets/Scripts/Editor/DungeonGraphView.cs#L193-L200)
- Overlap prevention toggles: [DungeonGraphView.cs:210-224](Assets/Scripts/Editor/DungeonGraphView.cs#L210-L224)

### Overlap Prevention (Real-Time)
- Overlap check in controller: [DungeonSimulationController.cs:260-287](Assets/Scripts/Runtime/DungeonSimulationController.cs#L260-L287)
- CheckRoomOverlap method: [DungeonSimulationController.cs:462-503](Assets/Scripts/Runtime/DungeonSimulationController.cs#L462-L503)
- RestartSimulation method: [DungeonSimulationController.cs:508-542](Assets/Scripts/Runtime/DungeonSimulationController.cs#L508-L542)

---

## Corridor Overlap Prevention

### Overview

After the force simulation completes and rooms are positioned, corridors are generated to connect rooms based on the graph's edge connections. However, corridors can sometimes "bulldoze through" intermediate rooms, creating unrealistic and visually jarring layouts.

The **Corridor Overlap Prevention** system detects when corridors intersect with rooms (other than the two rooms being connected) and automatically tries alternative corridor configurations to avoid these overlaps.

### How It Works

The corridor generation process now includes an intelligent regeneration loop:

```
For each corridor connection:
    Attempt 0-maxCorridorRegenerations:
        Strategy 1 (Attempts 0-1): Try different exit point combinations
        Strategy 2 (Attempts 2+): Try different corridor types (Direct, Angled H-first, Angled V-first)

        Generate corridor path → Check for room overlaps

        If NO overlap OR max attempts reached:
            Draw the corridor ✓
            Break loop
        Else:
            Log warning and try next variation
```

### Regeneration Strategies

#### **Strategy 1: Exit Point Variations** (Attempts 0-1)

When rooms have multiple exit points defined, the system tries different exit combinations:

- **Attempt 0**: Use closest exit points (default behavior)
- **Attempt 1**: Use second-closest exit points
- Higher attempts cycle through all available exits

This strategy maintains the desired corridor type while finding exit points that avoid intermediate rooms.

#### **Strategy 2: Corridor Type Variations** (Attempts 2+)

If exit variations don't work, the system tries different corridor types:

- **Attempt 2**: Direct corridor (straight line)
- **Attempt 3**: Angled corridor, horizontal-first (L-shaped)
- **Attempt 4**: Angled corridor, vertical-first (L-shaped, opposite direction)

Visual representation:

```
Direct Corridor:
  [Room A] ──────────→ [Room B]

Angled Horizontal-First:
  [Room A] ──────┐
                 │
                 └──→ [Room B]

Angled Vertical-First:
  [Room A]
     │
     └──────────→ [Room B]
```

### Overlap Detection Algorithm

The system uses **cell-based collision detection**:

1. **Generate corridor path** without drawing (virtual)
2. **Expand path to corridor width** (e.g., 2-tile width becomes 5x5 cells per path cell)
3. **Convert room bounds to cell coordinates**
4. **Check each corridor cell** against each room's cell bounds
5. **Exclude the two connected rooms** from overlap checks

```csharp
// Pseudocode
corridorCells = GetCorridorCells(startPoint, endPoint, useAngled, horizontalFirst)

foreach room in allRooms:
    if room == roomA OR room == roomB:
        continue  // Skip the two rooms being connected

    roomCellBounds = ConvertToCells(room.worldBounds)

    foreach corridorCell in corridorCells:
        if corridorCell intersects roomCellBounds:
            return OVERLAP_DETECTED

return NO_OVERLAP
```

### Configuration Parameters

| Parameter | Default | Description | Location |
|-----------|---------|-------------|----------|
| **Max Room Regenerations** | 3 | Maximum attempts to regenerate room layout if rooms overlap after force simulation | Organic Settings |
| **Max Corridor Regenerations** | 3 | Maximum attempts to regenerate each corridor with different exit/type combinations | Corridor Settings |

### Code References

#### Corridor Overlap Detection
- Main generation with overlap prevention: [DungeonTilemapSystem.cs:400-529](Assets/Scripts/Runtime/DungeonTilemapSystem.cs#L400-L529)
- Get corridor cells (virtual): [DungeonTilemapSystem.cs:579-615](Assets/Scripts/Runtime/DungeonTilemapSystem.cs#L579-L615)
- Exit point variations: [DungeonTilemapSystem.cs:534-574](Assets/Scripts/Runtime/DungeonTilemapSystem.cs#L534-L574)
- Overlap check algorithm: [DungeonTilemapSystem.cs:621-656](Assets/Scripts/Runtime/DungeonTilemapSystem.cs#L621-L656)

#### UI Integration
- Max corridor regenerations field: [DungeonGraphView.cs:276-282](Assets/Scripts/Editor/DungeonGraphView.cs#L276-L282)
- Parameter passing (instant mode): [OrganicGeneration.cs:449](Assets/Scripts/Editor/OrganicGeneration.cs#L449)
- Parameter passing (realtime mode): [OrganicGeneration.cs:382-388](Assets/Scripts/Editor/OrganicGeneration.cs#L382-L388)

### Logging and Debugging

The system provides detailed logging for corridor generation issues:

- **Info**: `Corridor overlap detected between RoomA and RoomB. Regeneration attempt X/3`
- **Success**: `Successfully generated corridor between RoomA and RoomB after X regeneration attempts.`
- **Warning**: `Corridor between RoomA and RoomB still overlaps after 3 regeneration attempts. Proceeding anyway.`
- **Summary**: `Generated 8 corridors with 12 total regenerations`

### Best Practices

1. **Set appropriate corridor width**: Wider corridors (3-4 tiles) are more likely to intersect rooms. Use narrower corridors (1-2 tiles) for complex layouts.

2. **Define multiple exit points per room**: Rooms with 3-4 exits give the system more options to avoid overlaps.

3. **Balance regeneration limits**: Higher `maxCorridorRegenerations` (5-10) gives better results but takes longer. Lower values (1-2) are faster but may result in more overlaps.

4. **Use mixed corridor types**: Setting corridor type to "Both" provides more variation options during regeneration.

5. **Check logs for persistent overlaps**: If corridors consistently fail regeneration, consider adjusting room spacing via the `Ideal Distance` slider or enabling `Allow Room Overlap` temporarily during testing.

### Performance Considerations

- Each corridor regeneration attempt involves:
  - Virtual path generation (lightweight)
  - Cell-based overlap checks (O(rooms × corridorCells))
  - No actual drawing until a valid configuration is found

- For a typical dungeon with 10 rooms and 15 connections:
  - Best case: 0 regenerations (~instant)
  - Average case: 5-10 total regenerations (~0.1s overhead)
  - Worst case: 15 × maxCorridorRegenerations attempts (~0.5s overhead)

The overhead is negligible compared to force simulation time.
