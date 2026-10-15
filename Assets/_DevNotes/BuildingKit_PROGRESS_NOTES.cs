/*
====================================================================
 MEDITERRANEAN BUILDING KIT - PROGRESS NOTES (not a real script)
 Last updated: 2026-10-14
====================================================================

WHERE THIS FITS IN THE BIGGER PICTURE
--------------------------------------
Original problem: IslandWorld.BuildPhase5Village() uses a crude
BuildingSystem/ExteriorDetailSystem that renders buildings as floating
scaled cubes. The nicer MediterraneanArchitecturalKit/
MediterraneanBuildingGenerator exists but is NOT wired up to anything.
VillageGenerator (organic cluster algorithm) exists but has never been
run, and its prefabs are deliberately placeholder cubes.

Decision made: build ONE real test building by hand in ProBuilder first
(via MCP), validate it looks right, THEN extract reusable modules, THEN
wire VillageGenerator in to replace BuildPhase5Village. Do NOT run both
systems at once - they'll compete.

CURRENT STATE: ONE TEST BUILDING IN PROGRESS
----------------------------------------------
GameObject: TestBuilding_01 (root, scene: Assets/Scenes/Island.unity, at
world origin 0,0,0 -- NOTE: Island terrain is ALSO at origin, so camera
shots flown toward world origin clip into terrain geometry. Frame the
building by selecting it directly in the Hierarchy and pressing F, don't
fly a camera toward world origin.)

Footprint: 4m (x) x 3m (z), wall height 2.8m, wall thickness 0.2m.
Door: 1.0m wide x 2.1m tall, centered on front (south, z=-1.5) wall.
Window: 0.8m wide x 0.9m tall, sill at y=1.0, centered on left (x=-2) wall.
Roof: pitched gable, ridge running along X at y=3.6, eaves at y=2.8,
  eave z = +/-1.6 (wall edge 1.5 + 0.1 overhang). Pitch angle = 26.565 deg
  (atan(0.8/1.6)), panel slope length = 1.7889m.
  THIS WAS WRONG ON FIRST PASS (used half-span 1.5 instead of eave
  distance 1.6, angle 28.1 instead of 26.565) which left a visible gap
  between roof and wall tops -- fixed by recalculating from actual
  ridge/eave points, not guessed values. If the roof looks disconnected
  from the walls again, redo this calc, don't guess.

All pieces are separate ProBuilder cubes/poly-shapes (NOT yet combined
into one mesh, NOT yet made into a prefab). Box-frame technique used for
both cutouts (jambs + header/sill/lintel as separate boxes around the
opening) -- this is the standard modular-kit technique, no booleans
needed, ProBuilder's MCP surface doesn't expose boolean ops anyway.

Piece list (all children of TestBuilding_01):
  - Wall_Front_LeftJamb, Wall_Front_RightJamb, Wall_Front_Header  (door cutout)
  - Wall_Back  (solid)
  - Wall_Left_Sill, Wall_Left_Lintel, Wall_Left_WinJamb_A, Wall_Left_WinJamb_B  (window cutout)
  - Wall_Right  (solid)
  - Floor_Slab
  - Roof_Panel_A, Roof_Panel_B  (pitched, SEE FIXED MATH ABOVE)
  - Gable_Left, Gable_Right  (triangular end caps, poly-shapes, closing roof peak)

Materials (created this session, Assets/Materials/):
  - TestBuilding_Wall.mat (tan, URP/Lit) - all wall + gable pieces
  - TestBuilding_Roof.mat (terracotta, URP/Lit) - both roof panels
  - TestBuilding_Floor.mat (grey, URP/Lit) - floor slab
  These are placeholder flat colors, not final textures.

NOT YET VERIFIED VISUALLY
----------------------------
The in-session screenshot tool (manage_camera action=screenshot) was
returning a STALE/CACHED image repeatedly -- identical pixels even after
moving the camera and editing roof panel transforms. Could not confirm
by screenshot whether the roof-gap fix actually worked. A temp camera
_ShotCam was left in the scene pointed at the building from (8,6,8)
looking at Floor_Slab -- DELETE THIS before shipping, it's a debug
leftover.
NEXT SESSION: open the Scene view manually (select TestBuilding_01,
press F) and visually confirm the roof sits flush on the walls before
doing anything else.

NEXT STEPS (in order)
------------------------
1. Visually confirm roof-to-wall seam is closed (see above).
2. If good: extract this into reusable modules (plain wall segment, door
   wall segment, window wall segment, corner piece, roof panel pair)
   sized to a consistent grid unit so VillageGenerator can tile them.
3. Wire VillageGenerator in to replace IslandWorld.BuildPhase5Village() --
   do this LAST, only once module art is validated, per the original plan.
4. Leave BuildPhase5Village/BuildingSystem alone otherwise - don't delete
   it until VillageGenerator is confirmed as the replacement in-game.

KNOWN TOOLING GOTCHAS HIT THIS SESSION (for whoever continues)
------------------------------------------------------------------
- ProBuilder create_shape's 'size' property is bugged for vector params
  -- create default cube, then set scale via manage_gameobject instead.
- manage_camera screenshot view_target on a GameObject does NOT frame to
  its bounds reliably -- it can snap uncomfortably close/inside geometry.
  orbit_distance only applies to batch='orbit', not single view_target
  shots. Coordinate arrays are not accepted for view_target in
  scene_view mode, only GameObject names.
- Screenshot caching bug: identical image returned across edits/camera
  moves in one session -- if this happens again, don't trust
  screenshots, ask the user to eyeball the Editor directly instead of
  burning calls on it.
====================================================================
*/
