## Phase 5, architecture

| # | Milestone | Detail | Done when |
|---|---|---|---|
| 5.1 | **Kit and materials.** | About 60–100 pieces plus trim sheets. Cover plinths, quoins, sills, lintels, shutters, arched doors, balconies, loggias, pergolas, external stairs, chimney caps, parapet bands, drainpipes, dormers and stepped gables. | Every piece is tagged by style and snaps to a grid. |
| 5.2 | **Massing generator.** | Stacked volumes on terraced pads, with upper volumes on lower roofs for terraces. Roof types are gabled terracotta, flat with parapet, and stepped gable. Cantilevers stay under 1 m with corbels. | Every upper wall sits on a wall below. |
| 5.3 | **Floor plans.** | Rooms come from the household type. Farmers get an entry or tool room, kitchen and hearth, living room, pantry, byre or storage on the ground floor, and sleeping rooms or lofts above. Fishers get a net room, and shopkeepers get a shopfront. | Every room is reachable from the entry without crossing a bedroom. |
| 5.4 | **Exterior detail pass.** | Openings are placed by room, with deep wall reveals. Add roof-tile colour variation, weathering gradients, flower pots, laundry lines, wood stacks, barrels, hanging herbs, lanterns, shop signs and dovecotes. | No blank wall over about 6 m, and every habitable room has a window. |
| 5.5 | **Walkable interiors.** | Wall thickness 0.4–0.6 m, door 0.9 × 2.0 m, stairs 0.9 m wide with rise 0.17–0.19 m and run about 0.27 m, headroom 2.0 m, and landings. | The player can walk through every house from door to roof. |
| 5.6 | **Furnishing and lighting.** | Furniture sets per room type: bed, table, hearth, shelves, tools, tiled kitchen floors and timber floors. Light comes from windows, the hearth glow and lanterns at dusk. | Every room has furniture, with no collisions or blocked doors. |
| 5.7 | **House yards.** | Each house gets a stoop, yard, fence, garden, well or cistern link, and a shade tree where there is space. Privies and dung heaps sit downslope of wells. | Every yard is reachable from both the door and the lane. |
| 5.8 | **LOD and streaming.** | Interiors load within about 25 m. Beyond that, windows use a fake interior. Exteriors use the LOD chain from the main plan. | 30+ fps near a dense town on this machine. |
| 5.9 | **Building validator.** | All the rules above in one report. | Every building passes, with a report listing any that don't. |

**What "liveable" means in the validator**
- The door has a route to the road graph, and any steps on it are flagged.
- Every room is reachable from the entry.
- Stairs meet the rise, run and headroom limits.
- Every habitable room has a window of at least about 1/10 of its floor area, and every hearth has a flue to a chimney.
- Roofs drain to a cistern or the ground.
- The house has water within 80 m.
- The upper walls are supported by walls below.