# Ghibli Nature Pack Integration - Progress Log

> **Purpose:** single source of truth so work can resume after a rate-limit/suspension.
> **Rule:** update the checklist + 'Change Log' after EVERY step, before starting the next.
> Last updated: 2026-10-16 (Phase 0 - recon complete, no scene/code changes made yet)

## Goal
Use the package `Assets/3D set of stylized nature - GHIBLI style` (by Baturinets) to:
1. Replace current procedural trees + shrubs/bushes (keep size + positions).
2. Add package grass across the terrain.
3. Add package flowers.
4. Switch foliage to the package's shaders.
Props (rocks, fences, wagon, etc.) are OUT OF SCOPE for now.

## Environment
- Unity 6000.3.11f1, **URP** (UniversalRenderPipelineAsset).
- Island scene: `Assets/Scenes/Island.unity`. Terrain is a custom mesh (IslandBuilder), 920 m domain, NOT a Unity Terrain.

## Package inventory (Assets/3D set of stylized nature - GHIBLI style/Art)
| Type | Prefabs | Shader (Shader Graph) | Approx size (m) |
|---|---|---|---|
| Trees | Tree_01..05 | bark: `Shader Graphs/Layered`, leaves: `Shader Graphs/Leaves` (2 materials each) | T01 8.1h, T02 8.1h, T03 4.3h, T04 10.8h (11 wide), T05 5.9h |
| Big tree | Big Tree | Layered + Leaves | 37h x 34w |
| Shrubs | Shrubs_01..03 | `Shader Graphs/Leaves` | 0.7-1.0h, ~1-1.6 wide |
| Grass | Gras_01..03 (sic) | `Shader Graphs/Grass` | 0.28-0.33h, 0.2-0.5 wide, 128-400 verts |
| Flowers | Flower_01_1 .. Flower_13 (30 prefabs) | `Shader Graphs/Flower` (one shared `Flowers.mat`) | 0.01-1.05h (01_x are flat ground blooms) |
| Shaders | Grass, Leaves, Flower, Layered, Props + subgraphs (Wind, Wind Leaves, Slope, Vertex Paint, Mask, RGB layers) | | |
| Props (ignored) | Altar, Arch, Box, Fence, Rock_01-11, Sign, Stone_Slab, Street_Lamp, Wagon | Props | |

Demo scene: `.../Art/Demo/Overiew.unity` (reference for how materials/wind are set up).

## Current project systems (what we are replacing)
| System | File | Notes |
|---|---|---|
| Trees + bushes | `Assets/Scripts/IslandStylizedTrees.cs` | Procedural meshes (BuildRound x2, BuildPine, BuildBush), GPU-instanced via `Graphics.DrawMeshInstanced` in `Update()`. 4 variants: `vars[0..2]` trees, `vars[3]` bushes. Placement is deterministic (seed 4242) in `Scatter()`; matrices = TRS(pos, yaw, scale). Tree scale 0.9-1.6; bush scale 0.6-1.05 (x1.15 on XZ). Procedural trees ~12-18 m tall BEFORE scale. Trunk uses `IslandToonFoliage` shader, leaves `IslandGhibliFoliage` (loaded from `Assets/Resources`). Adds CapsuleColliders in `TreeColliders`. Has baked path `LoadBakedData(TreeScatterData)` (currently bypassed). |
| Grass | `Assets/Scripts/IslandStylizedGrass.cs` | **Currently DISABLED** via `public const bool GRASS_ENABLED = false` (for fast load times). 400k blades, `DrawMeshInstancedProcedural`, custom `Assets/Shaders/StylizedGrass.shader`, terrain-color texture bake. Rules: slope >= 0.85, cliff margin 3 m, `Surface.Classify`/`GrassDensity`. |
| Flowers | none | New system required. |
| Baking | `IslandRuntimeLoader.cs` (`FORCE_RUNTIME_GENERATION = true`), `BakedScatterData.cs`, `Editor/Baking/IslandBaker.cs`, `Editor/IslandLeafBaker.cs` | Runtime generation forced ON; baked data bypassed. Keep containers compatible or knowingly retire them. |
| Placement helpers | `TerrainMath`, `Surface`, `WorldField` | `Hm()` height, `NV()` normal, `U()` to Unity space, `Surface.Classify` ground type. |

## Key design findings / decisions
1. **Size matching:** package trees are much smaller than ours (4-11 m vs ~12-30 m after scale). Multiply instance scale by a per-variant normalization factor so final height matches the old tree it replaces. Factor = old variant height / package prefab height. Record below.
2. **Position matching:** reuse the exact same `Scatter()` RNG sequence and matrices so positions/rotation/scale-variation are unchanged; only mesh + materials + size multiplier change.
3. **Mapping (proposed):** vars[0] round1 -> Tree_01, vars[1] round2 -> Tree_02, vars[2] pine -> Tree_05 (tall/narrow), vars[3] bush -> Shrubs_01..03 (split deterministically by hash). Tree_03/04/Big Tree optional extra variety (Big Tree is 37 m: rare hero trees only).
4. **Shader Graph + GPU instancing:** package materials use Shader Graph shaders. `Graphics.DrawMeshInstanced` needs `enableInstancing` on the material and the graph must support instancing. Wind must be verified per-instance. Fallback: `RenderMeshInstanced` or real GameObjects.
5. **Grass is a large perf risk:** it was disabled for load time. Package grass is a mesh clump (128-400 verts), not a single blade, so instance count must be far lower than 400k. Plan: clump instancing with density tuning + reuse existing distance cull/fade.
6. **Flowers:** 30 prefabs sharing one material (atlas `Flowers_Maps.png` + opacity). Scatter in noise-driven patches, not uniformly.
7. **Safety:** back up scripts to `.bak_pre_ghibli.txt` before editing; do not delete old procedural code until the new path is verified.

## Phase checklist (update as you go)
- [x] Phase 0 - Recon: package inventory, current systems, pipeline, sizes
- [x] Phase 1 - Verify package shaders/materials compile and render in URP (DONE: all 4 shader graphs compile; demo renders fine when viewed from lit side; materials ship with GPU instancing OFF so we use instanced copies)
- [x] Phase 2 - Back up Island scene + scripts (DONE: `*.bak_pre_ghibli.txt` next to IslandStylizedTrees.cs, IslandStylizedGrass.cs, RiverRocks.cs, Island.unity)
- [ ] Phase 3 - Trees/shrubs: measure old procedural heights, compute size factors, swap meshes/materials, keep matrices
- [ ] Phase 4 - Grass: instancing system using Gras_01..03 + Grass shader, enable across terrain
- [ ] Phase 5 - Flowers: scatter system using Flower_* prefabs + Flower shader
- [ ] Phase 6 - Shaders: wire package wind/lighting params, match island lighting, tune
- [ ] Phase 7 - Perf pass (FPS, draw calls), screenshots, final doc update

## Size normalization table (fill in during Phase 3)
| Old variant | Old height (pre-scale) | New prefab | New height | Factor |
|---|---|---|---|---|
| round1 | ~14 | TBD | TBD | TBD |
| round2 | ~17 | TBD | TBD | TBD |
| pine | ~19 | TBD | TBD | TBD |
| bush | ~1.7 | TBD | TBD | TBD |

## Additional findings (session 2)
- **STONES are in scope now.** Current stones = `Assets/Scripts/RiverRocks.cs` (procedural low-poly, merged into one mesh, called from RiverWater.cs x4 sites + WaterBuilder.cs lake ring; `RiverRocks.Build(parent)` at WaterBuilder.cs:94). Village yard rocks come from the LowPolyMedievalStarterPack (VKind.Rock) and are NOT touched.
- `IslandWorld.BuildVegetation` (oak/cypress/pine/ivy/grass via TreeFactory) is DEAD CODE: `bareIsland = true` returns before it. Only `IslandStylizedTrees` is live.
- Package trees are all round-canopy deciduous (mint foliage); `Big Tree` is a pink blossom tree. No conifer in package: pine slot uses narrow Tree_05/Tree_02.
- Scene contains **3 duplicate `TreeColliders` objects** (stale, ~500 capsule colliders each, saved from earlier play sessions). Script recreates one at runtime (`GameObject.Find` + Destroy). Candidate for cleanup in final phase.
- Measured old procedural sizes (top of mesh above origin, m): round1 13.31 (radius 5.49), round2 16.32 (r 4.54), pine 18.60 (r 4.59), bush 1.65 (r 2.01). Tree origin is placed at terrain h-0.3; bush at h-0.15.
- Editor baker `IslandBaker.cs` reflects on `IslandStylizedGrass.Init/chunks/counts/bnds` and `IslandStylizedTrees.StartRoutine/vars`; baked path is bypassed (`FORCE_RUNTIME_GENERATION=true`) and is NOT supported by the new package renderers.

## Architecture (new)
- `Assets/Scripts/GhibliNatureSet.cs` - ScriptableObject of pieces (mesh + instancing-enabled materials + size). Asset: `Assets/Resources/GhibliNatureSet.asset` (loaded with Resources.Load).
- `Assets/Scripts/GhibliInstancing.cs` - `GhibliBatch`: chunked GPU instancing (Graphics.DrawMeshInstanced), distance cull, optional far LOD.
- `Assets/Editor/GhibliNatureSetBuilder.cs` - menu `Island/Ghibli/Build Nature Set`: builds the set, clones package materials into `Assets/Materials/GhibliNature/` with GPU instancing ON, combines multi-renderer prefabs into single meshes in `Assets/Models/GhibliNature/`.

## Change Log
- 2026-10-16 (s2): Phase 1+2 done. Backups made. Architecture decided (see above).
- 2026-10-16: Phase 0 recon complete. Created this document. No project code or scene modified.

## Resume instructions
1. Read this file top to bottom, then the Change Log.
2. Find the first unchecked phase and continue.
3. Backups live next to originals as `*.bak_pre_ghibli.txt`.
