/*
====================================================================
 ISOMETRIC CAMERA RIG - PROGRESS NOTES (not a real script)
 Last updated: 2026-10-23 (procedural occlusion adjust REMOVED; see CURRENT STATE)
====================================================================

CURRENT STATE (2026-10-23)  <-- READ THIS FIRST
-------------------------------------------------
The procedural camera-angle adjustment (the occlusion solver) was REMOVED at the
user's request because it was too unstable. Everything below that describes the
solver (design, tuning fields, steps 6-7, the occlusion test plan, the TreeColliders
canopy probe) is HISTORICAL and no longer matches the code.

What IsoCameraRig.cs does now:
  * Fixed isometric base view: baseYaw / basePitch, Q / E (or gamepad shoulders)
    turn the base heading in rotateStep steps, smoothed by rotateSmoothTime.
  * CameraZoneTrigger zones still blend the camera into a cinematic pose.
  * ControlForward() still latches the WASD heading (controlDriftLimit) so a camera
    turn or zone blend does not bend the avatar's path mid-stride.
  * Zoom still comes from IslandThirdPersonController.SetZoomDistance().
  * NO automatic yaw/pitch offsets, no raycasts/spherecasts, no anticipation.
  * Removed public members: avoidOcclusion and all occlusion tuning fields,
    IsAdjusting, CurrentOffset, avatar-sample/velocity tracking, candidate search.
    Nothing else in the project used them (checked before removal).
  * Kept for API compatibility: OnTargetWarped() (now an empty method).

Only thing reacting to geometry between camera and avatar now: the
CinemachineDeoccluder on PlayerCamera. Its MinimumOcclusionTime was raised 0 -> 0.4
in step 5 only so it would not fight the solver; it is still 0.4. Set it back to 0
(the original value) if the camera should pull in instantly when blocked.

Restore the old solver: the full previous IsoCameraRig.cs is saved in the project
root as IsoCameraRig.with-occlusion-solver.backup.txt (outside Assets, so it does
not compile). Copy it over Assets/Scripts/IsoCameraRig.cs to bring it back.

Other things noticed (not changed):
  * basePitch is 35 in both the script default and the Island scene right now;
    the 2026-10-22 addendum below says it was raised to 55 - that is not what is
    saved. Re-apply if 55 is still wanted.
  * Assets/Docs/ISO_CAMERA_GUIDE.md was never written (old step 7) and is no
    longer needed for the solver; this file is the camera doc.

GOAL (original, partly historical)
----
Give the player camera a fixed, isometric-style viewing angle (diorama /
miniature look) and add a procedural "occlusion-aware" layer: when trees,
buildings or terrain would hide the avatar, the camera quietly slides to a
nearby angle that has a clear line of sight, then eases back to the base
angle once the view is free again.
Cinemachine is used for the actual camera body; our code only drives its
orbit angles.

STATUS CHECKLIST  (edit this list as work proceeds)
---------------------------------------------------
[x] 1. Investigated the existing camera setup (findings below).
[x] 2. Wrote Assets/Scripts/IsoCameraRig.cs (base angle + occlusion solver). Compiles.
[x] 3. Hooked into IslandThirdPersonController (7 small patches, all marked by 'isoRig').
[x] 4. Added IsoCameraRig component to the PlayerCamera GameObject in the
       Island scene, assign it to Player > IslandThirdPersonController.isoRig.
[x] 5. Deoccluder: AvoidObstacles.MinimumOcclusionTime 0 -> 0.4 (only change; revert to 0 to undo).
[-] 6. (dropped) Play-mode occlusion test - solver removed 2026-10-23.
[-] 7. (dropped) ISO_CAMERA_GUIDE.md - not needed; see CURRENT STATE above.

WHAT EXISTED BEFORE THIS WORK (so you can revert / compare)
-----------------------------------------------------------
Scene: Assets/Scenes/Island.unity
  Main Camera   : Camera + UniversalAdditionalCameraData + CinemachineBrain
  PlayerCamera  : CinemachineCamera (FOV 30, near 1, far 4000)
                  + CinemachineOrbitalFollow (Sphere orbit, Radius 20,
                    World Space binding, damping 0.03 / 0.2 / 0.03)
                  + CinemachineRotationComposer (damping 0.05)
                  + CinemachineDeoccluder (CollideAgainst = Default layer,
                    Pull Camera Forward, CameraRadius 0.5,
                    MinimumDistanceFromTarget 3, MinimumOcclusionTime 0,
                    IgnoreTag "Player")
                  Follow + LookAt target = Player/CameraTarget
  Player        : CharacterController (h 1.8, r 0.3) + IslandThirdPersonController
                  (avatar renderers are OFF: showAvatar = false)
Camera control before: IslandThirdPersonController.ApplyCamera() wrote
  orbital.HorizontalAxis (mouse yaw), VerticalAxis (locked 44 deg) and Radius
  (mouse-wheel zoom, 10..38, default 20) every Update. WASD was relative to
  Camera.main.forward.
Other camera-ish scripts (left untouched): CameraDirector (disables itself
  when a CinemachineBrain exists), IslandCameraFX (fade + tilt-shift only),
  TerrainColorCameraSetup, LODSystem / GrassTrampleSystem (read Camera.main).
Nothing builds the player rig from code; it is all scene-authored.
Versions: Unity 6000.3.11f1, Cinemachine 3.1.7 (namespace Unity.Cinemachine).

KEY FACTS ABOUT WHAT CAN OCCLUDE THE AVATAR
-------------------------------------------
* Trees are drawn with Graphics.DrawMeshInstanced (IslandStylizedTrees) -
  there are NO per-tree GameObjects. The only physics representation is the
  "TreeColliders" object: ~469 CapsuleColliders (radius ~0.54-1.02, height
  ~6.3-11.9), layer Default. These are trunk-sized, they do NOT cover the leaf
  canopy, so the occlusion probe uses a wider spherecast corridor
  (clearanceRadius) to approximate the canopy.
* There are TWO identical "TreeColliders" objects in the Island scene
  (instance ids 92098 and 58540). IslandStylizedTrees destroys/recreates
  one at runtime. Not touched - probably a stale duplicate worth cleaning.
* Terrain = "Island" object, 2 MeshColliders, layer Default (layer 8 "Terrain"
  exists but is unused). Player is also on Default, so the probe filters the
  avatar out by transform hierarchy, not by layer.

DESIGN (what IsoCameraRig does)
-------------------------------
* Base view: baseYaw (default 45), basePitch (default 40), distance comes from
  the controller's mouse-wheel zoom. Camera is NOT mouse-orbited in this mode;
  Q / E (or gamepad shoulders) rotate the base view in 90 degree steps.
* Occlusion solver (every ~0.1 s): test the base pose; if blocked, test
  candidate (yawOffset, pitchOffset) poses ordered by how far they deviate
  from the base; pick the cheapest clear one (with stickiness so it does not
  flip-flop); SmoothDamp the camera to it. When the base pose is clear again
  for releaseDelay seconds, ease back. Also tests where the avatar WILL be in
  ~0.35 s (velocity anticipation) so the camera moves before the avatar is
  covered.
* Controls: WASD uses a LATCHED yaw (frozen while a key is held, clamped to
  controlDriftLimit degrees of the real camera yaw) so the camera's automatic
  swings do not make the avatar veer off the player's intended heading.
* Cinemachine Deoccluder stays as a safety net (camera inside walls), with a
  MinimumOcclusionTime so it does not yank the camera forward before the rig
  has had time to swing around.

KNOWN TOOLING GOTCHAS (this project, via MCP)
---------------------------------------------
- manage_camera screenshots have returned stale/cached images in earlier
  sessions. Verify camera behaviour numerically (execute_code reading
  Camera.main transform, running Physics casts) instead of by screenshot.

ADDENDUM 2026-10-22 (resumed session)
--------------------------------------
* Step 6 (Play-mode test) IN PROGRESS. Entered Play, confirmed IsoCameraRig
  present+enabled on PlayerCamera at runtime. First occlusion-trigger attempt
  produced a bad sample: Application.isPlaying had flipped back to false and
  Time.time had reset, right after an execute_code call that toggled
  CharacterController.enabled off/on on the player (used to teleport it
  safely). Treat any reading taken immediately after that toggle as suspect -
  always check Application.isPlaying + Time.time before trusting a sample.
* Gotcha found: CapsuleCollider.transform.position returned local (0,0,0) for
  nested tree capsules (parent is "TreeColliders"); use c.bounds.center for
  world position instead. Confirmed 938 tree capsules total in-scene (matches
  the ~469 x 2 duplicate note above), nearest one to the player was ~15 units
  away.
* basePitch raised 40 -> 55 at the user's explicit request (camera sits
  noticeably higher now). Changed in two places: the field default in
  Assets/Scripts/IsoCameraRig.cs (so new/reset components pick it up) AND the
  live PlayerCamera component in the Island scene (via SerializedObject, then
  File/Save). Range attribute is still (10,80) so it's tunable in the
  Inspector. NOT yet re-verified: whether pitchLimitMin/pitchLimitMax (the
  runtime clamp used by the occlusion solver, separate from the Range
  attribute) still leave enough headroom above 55 for upward occlusion
  candidates - check those two fields next.
* STILL TO DO for step 6: re-run the occlusion test cleanly (enter Play, wait
  for island generation ~2-3s, re-locate nearest tree capsule, move player
  via CharacterController disable/move/enable, then poll camera yaw/pitch
  offsets - exposed as IsoCameraRig.CameraYaw and the private pitch offset
  field read via SerializedObject - on several ticks ~0.1s apart) to confirm
  the swing-away and ease-back actually happen, THEN check pitchLimitMin/Max
  against the new basePitch=55, THEN move to step 7 (ISO_CAMERA_GUIDE.md).
- The island world is generated at runtime after pressing Play; wait a few
  seconds before sampling anything.
- Do not dump Cinemachine components with a generic SerializedObject iterator;
  the output is enormous (script-name char arrays). Read specific fields.

RESUME HERE
-----------
Camera work is at a stable stopping point (see CURRENT STATE). Nothing is pending unless the user asks for more.
Old instructions follow for history: find the first unchecked box above and continue. Each step's result is
verifiable: file exists / component present on PlayerCamera /
Player's IslandThirdPersonController.isoRig assigned / Console has no errors.
====================================================================
*/
