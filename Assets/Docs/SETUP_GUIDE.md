# Procedural Village Generator — Setup Guide

## What this system does
Generates organic, fused building clusters (like the Mansford/Altonford/Alderville
reference), each one a real walkable, enterable structure with furnished
interiors, plus surrounding farmland — all from a modular prefab kit and a
seed number. Re-running with a new seed gives you a different village layout
instantly, without touching any prefabs.

## File overview
| Script | Role |
|---|---|
| `VillageModuleSet.cs` | ScriptableObject holding every prefab piece (walls, doors, roofs, furniture, crops). You fill this in once. |
| `BuildingFootprint.cs` | Data structure: a sparse grid of occupied cells + floor counts. |
| `ClusterMassingGenerator.cs` | Algorithm: random-walk growth + height smoothing → produces the organic fused silhouette. |
| `BuildingInstancer.cs` | Turns a footprint into real geometry: walls, doors, windows, roofs, stairs. |
| `InteriorFurnisher.cs` | Assigns room types per cell and places furniture + lighting. |
| `FarmlandScatter.cs` | Surrounds each cluster with crop-row plots, trees, and a courtyard landmark. |
| `VillageGenerator.cs` | Top-level MonoBehaviour — the component you actually put in your scene. |
| `Editor/VillageGeneratorEditor.cs` | Adds Generate/Clear buttons to the inspector. |

## Step-by-step setup

### 1. Import scripts
Copy the `Scripts` folder into your Unity project's `Assets` folder (keep the
`Editor` subfolder as-is — Unity auto-excludes it from builds).

### 2. Build your modular prefab kit
This is the part that determines whether it looks like the reference image.
Model in Blender or ProBuilder, **all modules sized to the same grid cell**
(e.g. 2m x 2m footprint, 2.6m floor height — set these in `VillageModuleSet`).
Required pieces, minimum viable set:
- 1 straight wall, 1 door wall, 1-2 window walls
- 1 flat roof cap, 1 pitched roof cap
- 1 floor slab
- 1 interior stair module
- A few furniture props (table, bed, chest)
- A crop-row prefab (striped plane or low mesh, 2m x 2m)

You do **not** need corner pieces if your wall modules are simple flat planes
that just butt together at cell edges (recommended for a first pass — add
proper mitred corners later for polish).

### 3. Create the Module Set asset
`Assets > Create > Village > Module Set` → drag all your prefabs into the
matching fields.

### 4. Add the generator to your scene
- Create an empty GameObject, name it `VillageGenerator`.
- Add the `VillageGenerator` component.
- Assign your `Module Set` and your `Terrain` (optional — leave blank for flat ground).
- Set `Cluster Count`, `Area Size`, and a `Master Seed`.

### 5. Generate
Click **Generate Village** in the inspector (or right-click the component →
Generate Village). Click **Clear** to wipe and retry with a different seed.

## How the algorithm produces the organic look
1. **Massing**: starts from one cell and grows outward via a weighted random
   walk — most growth extends from the last-placed cell (keeps clusters
   compact), with a `branchiness` chance of jumping to a random earlier cell
   (creates the non-rectangular, fused-arm silhouettes in the reference).
2. **Height assignment**: each cell gets a floor count smoothed against its
   already-assigned neighbors (±1–2 floors) so the skyline steps gradually,
   with a small chance per cell to spike into a tall accent tower — exactly
   the mixed low-house/tower profile in the source art.
3. **Wall logic**: for every cell edge, the system checks whether the
   neighboring cell is occupied *and* tall enough to cover that face. If not,
   it's an exterior wall (gets a door/window/plain wall); if the neighbor
   covers it, no wall is spawned at all — this is what fuses adjacent cells
   into one continuous walkable interior, matching the reference's merged
   building masses.
4. **Entrances & stairs**: the instancer guarantees at least one ground-floor
   exterior door per cluster, and scatters interior stair modules so upper
   floors are reachable without leaving the building.
5. **Interiors**: each floor-cell is tagged Common/Bedroom/Kitchen/Storage by
   a cheap hash (deterministic per seed) and furnished accordingly, with a
   light prop in most rooms.
6. **Farmland**: the cluster's bounding box is expanded by `ringDepth` cells;
   every unoccupied cell outside a 1-cell buffer path gets a crop row, tree,
   or shrub, matching the striped-plot ring around each village in the
   reference. A courtyard cell (unoccupied but surrounded on 3+ sides) gets
   the statue/well landmark.

## Known limitations / next upgrades
- **Connectivity isn't guaranteed perfect**: the stair-placement heuristic is
  a cheap hash, not a BFS reachability check. For a production build, add a
  pass that floods from the ground-floor entrance and guarantees every room
  can reach it (placing extra stairs/doors where it can't).
- **Wall corners**: the current instancer spawns independent straight wall
  modules per edge; if you want properly mitred corner geometry, extend
  `BuildingInstancer` to detect adjacent exterior edges on the same cell and
  swap in `wallCornerOuter`/`wallCornerInner`.
- **NavMesh**: `InteriorFurnisher` adds a `NavMeshSurface` component via
  reflection if you have the `com.unity.ai.navigation` package installed —
  install it from Package Manager, then call `NavMeshSurface.BuildNavMesh()`
  on each floor (or write one more pass that collects them all and bakes in
  one batch) after `Generate()` completes.
- **LOD / culling**: not included here — once your kit is in and clusters
  look right, add `LODGroup` components to the prefabs or enable GPU
  occlusion culling in URP settings, since dense fused clusters have a lot
  of overdraw between buildings.

## Suggested first test
Set `clusterCount = 1`, `minCellsPerCluster = maxCellsPerCluster = 16`,
leave `targetTerrain` empty, and generate with a placeholder kit (simple
cubes/planes with the right dimensions) before investing in final art — this
lets you validate the massing/door/stair logic cheaply before modeling.
