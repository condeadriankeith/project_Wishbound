# Work ledger

## 2026-11-23 — Phase 0 implementation checkpoint
- Created feature branch, copied master spec, verified live project/scene and package versions.
- CottageChoiceScene now owns guarded route commitment and dispatches its existing Tether/Roots consequences. FragmentBlessing remains the presentation/input surface.
- Removed immediate Tether hand restore; reconciliation moved to summit ending. Fixed Roots null guard and journey creation race.
- NewDemoRun isolates all story flags in memory, preserving persistent free-roam saves; title PLAY starts a clean run.
- PASS: editor compilation, zero console errors, four EditMode regression cases (choice exactly once on both routes, invalid/early choices, reset/save isolation). Evidence: logs/phase0-editmode.json.
- Initial test request found zero cases before assembly import finished; that empty run is not counted as verification.
- NOT RUN: actual cottage cinematics, fresh-boot route gate, full route playthroughs, build, performance.
- Next: complete Phase 0 live gate; repair route guidance/companion interactions, then compact authored progression and epilogue.

## 2026-11-23 — Live checkpoint

- A clean Play session resumed from title to impact. A debug-assisted Tether choice produced one journey and left the Child's hands unheld. Initial live freeze traced to an editor script reload during Play.
- Imported seven UV-mapped authored quest prefabs. Added compact route spacing, objective guidance, and an epilogue with scene reload. Full route traversal and replay remain unverified.
- Rebuilt seven road bridges and the village footbridge with the ferry deck's materials, continuous collision, and bridge-aware Child following. A scripted follow target verified all 16 directional crossings. See BRIDGE_MIGRATION_NOTES.md.
- Ground paint now evaluates the existing grass, dirt, sand, chalk, moss and wet-bank palette in world space at fragment resolution. The terrain geometry and interpolation masks still control broad region placement. Reduced the tilt-shift blur radius from 26 to 6 px at its reference resolution and widened its focus band.
- Unity Play verified the procedural material on the 116281-vertex Island mesh, render scale 1, and zero console or shader errors in the final capture. Paired captures are in Captures/VerticalSlice/ground-final-*.png.
- Eight outlying community farm plots spawned in Play. Verified scene placement count only. Remote job reachability and complete route playthroughs remain unverified.

## Summit route polish

- Tether recovers the ferry and seedbed fragments before collecting the summit fragment and returning to the night sky. Roots learns the village's story at the well, trail marker, and observatory before resting with the Child at the summit.
- The guide follows the road with elevation-aware waypoints. The overlook is selected near the actual road peak with room for the actors and clear camera sightlines.
- Assisted Unity Play passed every quest gate on both routes, confirmed early summit interaction cannot bypass route goals, and reached both epilogues. The test supplied quest inputs directly and jumped between locations. Physical traversal, duration, and replay remain unverified.
- The Roots capture showed the Star could obscure the Child. Reduced the Star's size and light and moved it to the far seat. Unity MCP disconnected before this final visual tuning could be recaptured.
- Removed unused glow components from the pause help text. The assisted run opened pause with no help-text shadows and no console errors.

## Player test readiness

- Reloaded the saved Island scene and verified the 60-resident cap. The live run populated 21 mountain residents and formed 13 families, four partner groups, and eight friend circles.
- Added local navigation grids and assigned nearby homes to outlying farms. Verified reachable work spots for all eight remote plots. Remote workers use their own household and farm supplies when the hamlet lies outside their navigation area.
- Verified both summit quest sequences and recaptured the Roots bench finale with the dimmer Star. Experience the Other Wish successfully reloaded Island, cleared both ending flags, and left the next choice unset.
- Assisted verification entered each route directly and moved between quest stops. Continuous climbs and player pacing remain open for a manual playtest.
- Raw Unity Play results are in Captures/VerticalSlice/player-test-readiness.txt and player-test-farms-final.txt.
