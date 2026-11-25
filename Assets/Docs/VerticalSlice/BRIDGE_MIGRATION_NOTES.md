# Bridge migration

The old bridge visual builder has been replaced by the shared authored deck system. Its design comments are retained below for reference; these are historical, not current implementation claims.

The live crossing harness used the Child's current CharacterController and follower logic, with a temporary moving target leading each crossing. The recorded run passed all 14 road-bridge traversals (seven bridges, both directions), with zero deck penetrations and zero recovery warps. The fishpond bridge stalled in both directions without falling through. The fishpond layout now aligns with its bridge, keeps the solid rim stones outside the walking corridor, and moves the dock and barrel to the perpendicular bank. In the updated live scene, 665 capsule checks spanning both banks and the deck found zero foreign colliders.

The fishpond rerun used an isolated follower with the same radius, height, center, skin width, slope limit, and step offset as the Child's CharacterController. It followed a moving target over both banks in both directions with zero deck penetration, zero recovery warps, and a maximum frame-to-frame vertical movement of 0.055 m. The first road and fishpond run is recorded in `Captures/VerticalSlice/bridge-traversal.csv`. The corrected fishpond run is recorded in `Captures/VerticalSlice/footbridge-traversal.csv`.

```text
// ---- Chunky irregular planks: each its own slab, slightly yawed/tilted/uneven, some mossy ----
// ---- Heavy side beams + inner beams under the deck ----
// ---- Posts, caps, rails, sagging ropes ----
// ---- Splayed A-frame trestle bents with cap beam, cross + X bracing, mossy/wet lower legs ----
// ---- Log-cabin crib abutments sunk into each bank ----
```
