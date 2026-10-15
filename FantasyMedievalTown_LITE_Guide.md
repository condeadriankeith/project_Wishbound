# Fantasy Medieval Town LITE — Asset Package Guide

Package: **Low Poly Fantasy Medieval Village Vol.2 Lite** (Unity Asset Store)
Imported folder: `Assets/FantasyMedievalTown_LITE/`
Demo scene: `Assets/FantasyMedievalTown_LITE/Scenes/MedievalTownLite_Overview.unity`

This is the free/LITE sample of a modular low-poly medieval building kit. It ships
14 prefabs total: a modular house-building kit (walls, corners, bases, floors,
roof, stairs) plus a small set of standalone props, and one fully-assembled
example house (`House_01_LITE`) showing how the modular pieces fit together.

## Folder Structure

- `Prefabs/` — 14 ready-to-use prefabs (see tables below). Use these, not the raw
  meshes in `Models/`, unless you need to build custom variants.
- `Models/` — 14 raw FBX/mesh source files backing the prefabs.
- `Materials/` — 5 materials shared across all meshes (this is a low-poly kit, so
  texturing is intentionally minimal/atlased).
- `Textures/` — 1 texture (the shared atlas/palette used by the materials).
- `Scenes/` — `MedievalTownLite_Overview.unity`, a demo scene showing the asset
  set laid out for preview.

## Modular Building Kit

All modular pieces snap to a **2.5m × 3m grid cell** footprint for walls, with
foundation/floor tiles on roughly the same horizontal grid. Measured bounds:

| Prefab | Size (W×H×D, meters) | Role |
|---|---|---|
| `HouseBase_03_LITE` | 2.66 × 0.70 × 2.67 | Foundation/plinth module, sits under floor level |
| `Corner_01_LITE` | 0.66 × 3.70 × 0.65 | Vertical corner post, placed at building corners, spans full wall height |
| `HouseWall_02_LITE` | 2.50 × 3.00 × 0.30 | Plain solid wall panel |
| `HouseWall_08_LITE` | 2.50 × 3.00 × 0.43 | Wall panel with a door opening (door mesh nested inside) |
| `HouseWall_11_LITE` | 2.50 × 3.00 × 0.37 | Wall panel with a window opening (glass mesh nested inside) |
| `Floor_01_LITE` | 2.50 × 0.20 × 2.50 | Interior floor tile |
| `HouseRoof_02_LITE` | 5.70 × 3.60 × 6.30 | Pitched roof cap, sized to cover a 2×2 wall-cell footprint |
| `Stairs_01_LITE` | 2.09 × 0.70 × 1.18 | Exterior staircase/entry steps |

**Note:** There are no separate `HouseDoor` / `Glass` prefabs — doors and
windows only exist nested as child meshes inside `HouseWall_08_LITE` and
`HouseWall_11_LITE` respectively. If you need a door or window elsewhere,
duplicate one of those wall prefabs rather than looking for a standalone piece.

### How a building is assembled (from the `House_01_LITE` example)

The included example house (5.70 × 7.20 × 6.95m overall) is built from:

- `HouseBase_03_LITE` × 4 — forms the foundation footprint
- `Corner_01_LITE` × 4 — one at each corner, holding the wall tops/bottoms together
- `HouseWall_02_LITE` × 6 — plain walls filling most of the perimeter
- `HouseWall_08_LITE` × 1 — the wall segment with the entrance door
- `HouseWall_11_LITE` × 1 — the wall segment with a window
- `Floor_01_LITE` × 8 — tiled across the interior footprint
- `HouseRoof_02_LITE` × 1 — single roof cap over the whole footprint
- `Stairs_01_LITE` × 1 — leading up to the door

**Assembly pattern to follow for your own buildings:**
1. Lay `HouseBase_03_LITE` tiles edge-to-edge to define your footprint (2.66m grid).
2. Place `Floor_01_LITE` tiles on top, aligned to the same grid.
3. Place a `Corner_01_LITE` at each outer corner of the footprint.
4. Fill the perimeter between corners with `HouseWall_02_LITE` (plain),
   swapping in one `HouseWall_08_LITE` for your entrance and one or more
   `HouseWall_11_LITE` for windows. Walls are 2.5m wide, so corner spacing
   should be in multiples of 2.5m.
5. Cap the structure with `HouseRoof_02_LITE` (sized for a 2×2 cell building;
   for larger/irregular footprints you'll need multiple roof pieces or to rely
   on the asset's non-LITE/full version for more roof variants).
6. Add `Stairs_01_LITE` at the door wall if the floor sits above ground level.
7. Treat `House_01_LITE` itself as a drop-in prefab if you just want one
   complete building without assembling manually — it's already fully composed.

All modular prefabs use a 2–3 tier LOD setup (`_LOD0`, `_LOD1`, ...) via
`LODGroup` components, so they're automatically optimized for distance — no
extra setup needed when placing them in a scene.

**Colliders:** Most pieces use simple `BoxCollider`s for performance.
Wall prefabs (`HouseWall_*`) use a `MeshCollider` on their LOD0 mesh instead,
since box colliders can't represent the door/window cutouts accurately.

## Standalone Props

These are decorative, not part of the modular wall/floor grid — scatter them
freely:

| Prefab | Approx. Size (W×H×D, meters) | Notes |
|---|---|---|
| `Barrel_01_LITE` | 0.75 × 0.77 × 0.75 | 3 sub-meshes |
| `Fence_01_LITE` | 1.49 × 0.91 × 0.17 | 3 sub-meshes; fence segments, chain along a line for a full fence run |
| `FlowerPot_03_LITE` | 0.23 × 0.40 × 0.22 | 2 sub-meshes; small decorative accent |
| `Lantern_01_LITE` | 0.47 × 2.53 × 1.09 | 3 sub-meshes; tall — works well beside doors/paths |
| `Rock_01_LITE` | 0.58 × 0.55 × 0.54 | 2 sub-meshes; generic scatter prop for terrain dressing |

## Materials & Textures

This kit uses a shared low-poly material setup:

- 5 materials total, covering all 14 prefabs between them (expect color-blocked
  / flat-shaded look typical of low-poly kits — don't expect per-object unique
  textures).
- 1 shared texture/atlas backs the materials.
- Because materials are shared across prefabs, recoloring a material will
  affect every prefab instance using it — duplicate the material first if you
  want to recolor just one building.

## Quick Reference: All 14 Prefabs

```
Assets/FantasyMedievalTown_LITE/Prefabs/
├── Barrel_01_LITE.prefab          (prop)
├── Corner_01_LITE.prefab          (modular: wall corner)
├── Fence_01_LITE.prefab           (prop)
├── Floor_01_LITE.prefab           (modular: interior floor)
├── FlowerPot_03_LITE.prefab       (prop)
├── House_01_LITE.prefab           (complete pre-built house)
├── HouseBase_03_LITE.prefab       (modular: foundation)
├── HouseRoof_02_LITE.prefab       (modular: roof cap)
├── HouseWall_02_LITE.prefab       (modular: plain wall)
├── HouseWall_08_LITE.prefab       (modular: wall + door)
├── HouseWall_11_LITE.prefab       (modular: wall + window)
├── Lantern_01_LITE.prefab         (prop)
├── Rock_01_LITE.prefab            (prop)
└── Stairs_01_LITE.prefab          (modular: entry stairs)
```

## Practical Tips

- **Snapping:** Since walls are 2.5m and bases/floors are ~2.66m, use Unity's
  grid/vertex snapping (or set your scene grid size to match) when hand-placing
  pieces — eyeballing positions will leave visible gaps or overlaps.
- **Scaling the village:** For a full village, duplicate `House_01_LITE` as a
  quick starting point, then vary roof/wall/door orientation (rotate in 90°
  increments) and swap in `HouseWall_11_LITE` vs `HouseWall_02_LITE` to vary
  each house's window layout.
- **This is the LITE edition** — it has a reduced prop/building variety
  compared to the full ''Vol.2'' asset. If you need more wall/roof/prop variants
  (additional house styles, more corner types, extra props like crates/carts/
  wells), you'd need the full paid version of the package.
- **Performance:** LOD groups and mostly-box colliders mean this kit scales
  reasonably well for scattering many houses/props across a large scene.
