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
// Line 500: OrganicGeneration.cs
float force = springStiffness * (distance - idealDistance);
forces[nodeId] += direction * force;
```

**Visual Example**:

```
Current State:              After Spring Forces:

[A]─────────────────[B]     [A]────────→[B]
 ↓                               ↓        ←
 │    [C]                   [D]←────────[C]
 ↓     │
[D]────┘

Forces Applied:
• A→B: distance=40, ideal=20 → PULL with force=0.01*(40-20)=0.2
• A→D: distance=30, ideal=20 → PULL with force=0.01*(30-20)=0.1
• D→C: distance=25, ideal=20 → PULL with force=0.01*(25-20)=0.05
```

**Key Parameters**:
- `idealDistance` (now controllable via slider, default=20): The target distance springs try to achieve
- `springStiffness = 0.01f * stiffnessFactor`: How strong the spring force is
  - Higher stiffness = rooms snap to ideal distance faster
  - Lower stiffness = rooms drift more gradually

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
**Effect**: Target spacing between connected rooms

```
idealDistance = 10         idealDistance = 30
(Tight clustering)         (Wide spacing)

[A]─[B]                   [A]────────[B]
 │   │                     │
 │  [C]                    │          [C]
 │   │                     │           │
[D]─┘                     [D]─────────┘
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

## Tips for Best Results

### Tight, clustered dungeons:
- ↓ Ideal Distance (10-15)
- ↓ Repulsion Factor (0.5-0.8)
- ↑ Stiffness (1.5-2.0)

### Wide, sprawling dungeons:
- ↑ Ideal Distance (30-40)
- ↑ Repulsion Factor (1.5-2.5)
- ↓ Stiffness (0.5-0.8)

### Organic, natural feeling:
- Chaos Factor (0.2-0.4)
- Medium Ideal Distance (20-25)
- Balanced Repulsion (1.0)

### Fast convergence:
- Force Mode enabled
- High Stiffness (2.0+)
- No Chaos

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

- Main simulation: [OrganicGeneration.cs:448-583](Assets/Scripts/Editor/OrganicGeneration.cs#L448-L583)
- Real-time controller: [DungeonSimulationController.cs:77-295](Assets/Scripts/Runtime/DungeonSimulationController.cs#L77-L295)
- Graph view UI: [DungeonGraphView.cs:118-279](Assets/Scripts/Editor/DungeonGraphView.cs#L118-L279)
- Ideal distance parameter: [OrganicGeneration.cs:499-501](Assets/Scripts/Editor/OrganicGeneration.cs#L499-L501)
