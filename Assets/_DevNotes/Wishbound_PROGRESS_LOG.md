# Wishbound - Opening / Title / Pause progress log
(Keep this updated after every step. Another Claude should read this first, then continue at 'NEXT'.)

## Tooling
Unity MCP (project_Wishbound, Island scene, Unity 6.3). Key files in Assets/Scripts:
WishboundIntro.cs (whole sequence + comet hold + impact + hyperlapse Finish), WishboundTitleMenu.cs (title buttons),
WishboundSettings.cs, PauseMenu.cs (ESC), WishCometVisual.cs, BeachCometCrater.cs (crater), GuideWisp.cs, IslandCameraFX.cs, CameraDirector.cs

## Requirements checklist (from user)
- [ ] R1 Comet: slow-mo then smooth stop before title, trail/anim slow+pause, comet does not retract, smooth resume on PLAY
- [ ] R2 Impact dramatic BUT no camera shake on title screen (user: too dramatic / shaking on title)
- [ ] R3 Crater must appear after impact (user: it did not)
- [ ] R4 ESC in-game = pause using existing settings overlay + RESUME and QUIT
- [ ] R5 Fog hides water edge on horizon in opening wide shot
- [ ] R6 Scene1 day->sunset shift clearly visible
- [ ] R7 Comet scene: hyperlapse zoom-out, just above water level looking up at island front, trees+grass still render
- [ ] R8 Water matches river/waterfall design
- [ ] R9 Title: logo alone center, ~2s later PLAY/SETTINGS/QUIT domino fade-in, Cutout Chaos font white, wisp cursor snaps + text takes wisp styling
- [ ] R10 PLAY -> cinematic hyperlapse into THIRD-person of star
- [ ] R11 SETTINGS placeholder page, changes work in game, smooth morph transition
- [ ] R12 QUIT: wisp leaves button, fade to black until only wisp glows, wisp flies off, exit
- [ ] R13 Keys 1/2/3 = first person / third person / high tilt-shift, smooth transitions; scroll wheel removed; camera never bumps into child

## Audit findings (session 1)
- WishboundIntro.cs: hold logic (phases 0-4 via Time.timeScale), fog (WideView/FogOverride), impact FX (dust/debris/shockwave prefabs, bloom, chroma) already exist.
- Shake during title hold: Shot3 sets shake = Noise3(...) every frame, not zeroed during hold -> fix by scaling by (1-breathK)
- ImpactTimeAAA/ShakePulse/CoreImpact shake very strong -> tone down
- ESC handled in WishboundIntro.Update -> PauseMenu.Toggle()

## Done (session 1)
- R4 ESC pause: ALREADY implemented (PauseMenu.cs has RESUME/QUIT, ESC handled in WishboundIntro.Update). Needs play test only.
- R1 comet slow-mo/stop/resume: ALREADY implemented in WishboundIntro.Shot3 (phases 0-4 via timeScale). Needs play test only.
- R5 fog: WideView() sets FogOverride=.0011 (exists). Needs visual check of horizon edge.
- R9 title buttons: WishboundTitleMenu.cs exists, loads Resources/Fonts/CutoutChaos. Needs review.
- R13 keys 1/2/3 exist in IslandThirdPersonController (SetCameraMode). Mode 3 = overview distance; verify it has tilt-shift.
- EDIT: scroll-wheel zoom removed in IslandThirdPersonController.cs (CameraDirector.cs lines ~83/102 still read scroll: check whether CameraDirector is active)
- EDIT: WishboundIntro.Shot3: shake multiplied by (1 - breathK) so title hold is dead still (R2)
- EDIT: BeachCometCrater.Impact(): crater formation + flash now use unscaledDeltaTime so slow-mo cannot stall the crater (R3, suspected cause)
- EDIT: Impact toned down: CoreImpact shake .9/.3 (was 2.5/.8), FOV punch 6 (was 12), rot shake 1.0 (was 2.5), bloom 8 (was 15), chroma .6, vignette .6, hit-stop .04 for .12s then .06-.25 (was .01 / .02-.15)
- Console clean after compile.

## NEXT
1. Play-test the intro in editor: confirm crater visible after impact, no shake on title, comet stops/resumes. If crater still missing, check BeachCometCrater.Revealed / IslandBuilder.HGrid and that the camera frames the beach at impact.
2. Review WishboundTitleMenu.cs (domino fade, wisp snap, QUIT sequence) and WishboundSettings (placeholder + morph).
3. R6 scene1 day->sunset visibility, R7 wide shot framing (above water, looking up), R8 water look vs river/waterfalls.
4. R13 mode 3 tilt-shift + smooth transitions; camera must not bump the child.


## Session 3 update (continuation)
### Done this session
- Scroll wheel: removed from CameraDirector.cs (orbit zoom + play-mode zoom). Project-wide search: no remaining mouseScrollDelta / Mouse ScrollWheel reads. (IslandThirdPersonController keeps a dead `scroll` read of ms.scroll, unused.)
- Title menu (WishboundTitleMenu.cs) reviewed against the spec: 2 s title-alone hold, domino fade of PLAY/SETTINGS/QUIT, logo re-centering, Cutout Chaos font, wisp snap + glow/sparkle motes, settings morph (volume / look speed / fullscreen), QUIT farewell (wisp leaves button, black fade with only wisp visible, wisp exits, then quit). No code change needed.
- Intro verified in play mode with screenshots: Shot 1 sunset grade clearly visible; Shot 2 flyover OK; comet pauses in sky under the title; wide shot is just above water looking up (islandViewPosition 30,16,640); trees render in wide shot; horizon fades into haze; PLAY resumes comet, impact happens, crater.Revealed == true, intro hands over to gameplay.
- Camera modes: SetCameraMode now eases pitch for ALL modes (1 = 0 deg eye level, 2 = 22 deg, 3 = 52 deg high overview), 1.4 s window, SmoothDamp 0.45 s. Before, only mode 2 set a pitch, so mode 3 never tilted up. Compiles clean.

### Still to verify (needs eyes)
- Mode 3 visuals after the pitch fix (a magenta ground was seen at pitch 0 / distance ~55 before the fix; check it is gone).
- Mode 1 <-> 2 <-> 3 transitions feel smooth, no clipping into the child.
- Tilt-shift is a global always-on effect (IslandCameraFX, Hidden/Island/TiltShift); not blended per mode.
- Water vs river look: ocean uses Assets/Shaders/GhibliOcean.shader, river uses Assets/Resources/IslandGhibliRiver.shader; not compared visually yet.


## Session 4 update
### Done
- Ocean water restyled to match the river: Assets/Shaders/GhibliOcean.shader now uses the Island/GhibliRiver palette (_Deep/_Shallow/_Streak/_Foam), 3 toon depth bands, ripple streaks, sun glints, stepped shore foam. Detail fades with distance (calm flat sea at the horizon, no shimmer). Keeps the same globals (_GhibliHeightMap, _GhibliE, _GhibliMirrorZ, fog). Old version saved as Assets/Shaders/GhibliOcean.shader.bak.txt. Verified in Shot 1: toon streaks visible, compiles with 0 shader messages.
- Tilt-shift per camera mode: TiltShift.shader multiplies blur by global _IslandTiltStrength; IslandCameraFX.TiltGoal (static) is eased each frame; IslandThirdPersonController.SetCameraMode sets 0.5 / 1.0 / 1.8 for modes 1 / 2 / 3 (PrepareThirdPerson sets 1.0).
- Compiles clean (0 errors).

### Still to eyeball
- Wide island shot with the new ocean (horizon haze / fog blend), mode 3 visuals (magenta ground seen earlier at pitch 0 should be gone), 1<->2<->3 transition smoothness, camera vs child clearance in 2 -> 1 transition.
