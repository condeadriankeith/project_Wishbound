# Wishbound baseline — 2026-11-23

- Repository: C:/Users/conde/Downloads/project_Wishbound. Started on main with existing untracked tools, backups, captures, and graph data; preserved these files.
- Working branch: feat/wishbound-vertical-slice.
- Live MCP verified project_Wishbound, Island scene, Unity 6000.3.11f1, StandaloneWindows64. First project-info request failed transiently; retry succeeded.
- Packages: URP/VFX Graph 17.3.0, Cinemachine 3.1.7, Input System 1.19.0, AI Navigation 2.0.11, Timeline 1.8.11, Test Framework 1.6.0.
- Python 3.15.1, Graphify CLI and existing 2299-node graph available. Blender found at C:/Program Files/Blender Foundation/Blender 5.2/blender.exe. Graph query used as an index; source rechecked directly.
- No existing VerticalSlice work state or ledger. Master spec copied byte-for-byte from supplied download.
- Baseline MCP console: zero error entries. FPS, load time, GC, material census, license redistribution and visual fidelity have NOT been validated.

## Confirmed code defects

1. FragmentBlessing wrote route flags/spectrum and called CottageChoiceScene.Finish through CompleteChoice, bypassing the cottage consequence animations.
2. FragmentBlessing.Finish unconditionally took the Child's hand, cancelling Tether rejection.
3. Roots consequence dereferenced StarRadiance outside its null guard.
4. MountainJourney.Instance was only assigned in Start, permitting duplicate Begin calls in one frame.
5. Editor reset deleted a subset of persistent save keys and left ending/quest flags behind.
6. GuideWisp had no mountain quest goals; mandatory Roots progression included optional pebble/stone/music activities.
7. MountainJourney contains temporary generated props, distant road-percentage anchors, a separate water errand, six-second finales and a visible post-ending teleport. No epilogue/replay exists at baseline.

## Existing systems retained

Island runtime construction, terrain, input, camera, interiors, music and asset GUIDs remain in place. CottageChoiceScene is the permitted single-authority alternative to introducing a duplicate story manager. WispCursor already uses GuideWisp/WispPreset and is persistent; title/pause/farewell claim semantics need replay validation. Licensed nature/medieval/character assets exist, but their build redistribution rights have not been audited.
