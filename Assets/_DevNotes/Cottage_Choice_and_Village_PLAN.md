# Wishbound — Cottage Choice scene + Village build plan

Status (2026-11-15): ALL PHASES BUILT and play-tested (both choices, house enter/exit). Remaining gaps are listed under "Open items" at the end.

Decisions (2026-11-15): no humanoid Star — the Star stays an orb ("ribbons" = its trail/sparkles). Build order: village kit (Phase D) first, painterly Kuwahara + ink outlines (Phase E) now, then the cottage scene. Choice controls still to confirm.

## Phase A — Foundations the scene depends on
1. **Camera profiles** (`IsoCameraRig`): add named profiles that blend smoothly.
   - `Explore` (current high iso), `IntimateCompanion` (35° pitch, 28° FOV, closer radius), `WindowHold` (locked framing on the window, input still drives the Star).
   - `CameraProfileZone` trigger volumes (extend `CameraZoneTrigger`) that request a profile with a blend time.
2. **Input actions**: `StarlightPulse` (hold; keyboard F / gamepad RT) and `Reach` (press; E / gamepad West). `H` hold-hands stays.
3. **Star state** (`StarRadiance` component): one 0..1 value plus an alignment (Tether / Roots) that drives the orb shader intensity, rim flicker, light range/colour temperature, trail length and particle density, and a gravity / jump multiplier on the controller.
   - The Star is an orb for good: "ribbons" = the orb trail + sparkle shell.
4. **Child dialogue**: `WhisperLine` — a handwriting label anchored above the child's head (world-to-screen), the same wobbly white text, fading in and out. No boxes.
5. **Adaptive music**: `MusicDirector` with layered stems and crossfades (Tether: choir; Roots: piano + footsteps). Placeholder synthesized stems until real ones arrive.

## Phase B — The Sunken Village courtyard (location)
- Pick the cottage at the edge of the hamlet nearest the pine line (around 0, 6, 316), or build a dedicated one.
- Flooded courtyard: a shallow water plane with a toon water material, flagstones, puddles and wet dark walls (low-roughness material variant).
- Cottage: a window you can actually see through, a real interior (cradle, cold hearth, cracked clay lantern holding the fragment, a sleeping-infant silhouette under a blanket) and a heartbeat amber light.
- Lighting: cold 3000K twilight ambient around the courtyard; warm 2800K inside.

## Phase C — Scene flow (`CottageChoiceScene` state machine)
1. **Approach**: entering the courtyard zone eases the camera into IntimateCompanion. The child walks ahead, tugs (a short animation or nudge) and points at the window.
2. **Discovery**: the player steps near the sill. WindowHold locks the camera on, with tilt-shift DoF on the lantern. The Star's rim flickers and its trail shortens (the missing piece).
3. **Interaction**: the child moves to the sill → whisper "It's keeping her warm..." → the fragment pulses faster and golden motes drift through the window cracks to the Star's chest. Glass harmonica + cello sting → the child steps back → "If you take it back... can you go home?"
4. **Choice** (no HUD):
   - **Tether**: hold StarlightPulse ~1.5 s → the Star flares, the window shatters (glass shards + cyan/magenta sparks), the fragment zips into the core. Radiance goes up, the trail lengthens, gravity lightens. The room goes indigo, a baby-cry cue plays, the child shields their eyes and won't hold hands again (hold-hands disabled, the child trails behind).
   - **Roots**: release / dim, then press Reach at the sill → a glowing hand-print on the glass, a warmth trickle into the lantern. The lantern settles at 2800K and warms the room. The Star dims to matte ivory, its trail shortens and gets heavier. The child exhales a visible breath and takes the Star's hand automatically.
5. **Exit**: the camera returns to Explore. The path to Mount Aethel opens (a gate or trigger); the music crossfades by alignment.
6. Save the choice to a `StoryFlags` asset or PlayerPrefs for later scenes.

## Phase D — Village dressing kit (reference images)
Modular prefabs built from simple, bold, chamfered low-poly shapes with Ghibli/ToonLit materials plus wind sway:
- **Terraced farm plots**: stone or log retaining walls, furrowed dark soil rows, cabbage / pumpkin / turnip props, A-frame bean trellises with leaf cards, scarecrow, baskets, wheelbarrow, clay pots. Blighted patches that revive near the Star when Roots-aligned.
- **Well hub**: a cobble ring well with a lily-pad water surface (replace the intro's placeholder well), windlass, roof, flagstone apron, overflowing trough, puddles.
- **Fishponds**: stepped stone basins, toon water depth gradient, foam rings, koi on spline paths that school toward the Star, footbridge, dock, net racks.
- **Paths and borders**: recessed packed-dirt and cobble paths painted into the terrain colour/path map, glossy puddle decals, split-rail fences, mossy stone walls, wildflower clusters.
- **Life props**: lantern posts (warm 2800–4500K lights), rain barrels, woodpiles, clotheslines, small shrines.
- A placement tool (editor script) to stamp these along the existing road and plot data (`FarmPlots`, `RoadNet`).

## Phase E — Look development (optional, larger)
- Kuwahara painterly filter + Sobel ink outlines as a URP renderer feature (full-screen pass). Heavy, so it ships with a quality toggle.
- Tilt-shift as a reusable DoF profile for interaction framing.

## Blocked on / needs from you
- A child "tug / point / shield eyes / hold hand" animation set (or approval for procedural placeholder motion).
- Voice / music assets (otherwise synthesized placeholders).

## Suggested order
E (painterly look) + D (village kit: well hub → farms → paths → ponds) → A → B → C.

## Added 2026-11-15 (requests after the village kit pass)
- **Prefab colliders**: audit the imported packs (LowPolyMedievalStarterPack, GHIBLI nature set, VillagePlaceholders) and give each prefab proper colliders (box/capsule hulls for props, simplified mesh colliders for buildings, trunk capsules for trees) by editing the prefab assets.
- **Water upgrade**: extend the existing ocean/river/waterfall shaders (GhibliOcean, GhibliWaterfall, RiverWater) instead of new ones. Targets from the references: cel-shaded caustic patches, sparkle glints, a shallow turquoise to deep teal gradient, foam edges, and point-light response so the Star and lanterns reflect on the surface. Reuse the same material on the village pond and well.
- **House interiors**: 68 placed houses come from the starter pack (e.g. TwoStoryMedievalHouse_2: LOD0-3 + one collider, no door object). Find each model's front/door side once per prefab (mesh analysis + a visual check, Blender if needed), store it as a `HouseEntrance` marker (door point + facing). A prompt near the door ("enter" in handwriting) loads a dedicated detailed interior scene additively, moving the star and child to its door; the interior door returns them to the exact outside spot. Interior scenes are built per house type.
- **Blender**: available for custom models (well, trellis, lanterns, interior furniture) if the procedural kit pieces aren't enough.

## Open items after the build (2026-11-15)
- Waterfall shader not yet given the shared water FX (ocean, river, ponds, well, trough and flood water have it).
- Interiors are procedural (InteriorBuilder) - one room layout per house type, not hand-dressed scenes yet. Blender was not used.
- Child "tug / point / shield eyes" are movement beats only (no animation clips).
- Farm plots / props are placed at runtime around the hamlet; grass can still poke through some kit pieces outside GrassExclusions.
- Audio is synthesized placeholder.

## Where things live
- Camera profiles: IsoCameraRig.Profile / SetProfile, CameraProfileZone
- Star state: StarRadiance (StoryFoundations.cs), StarOrbFX state fields; StoryFlags; StarInput (F hold / E press)
- Whispers: WhisperLine; music: MusicDirector (moods explore / tether / roots)
- Cottage scene: CottageChoiceScene (location + flow); grass kept clear with GrassExclusions
- Village kit: VillageKit, VillageKitExtras, VillageDressing (incl. lanterns along RoadNet roads)
- Painterly look: Wishbound/Painterly full-screen pass + PainterlyLook (F8)
- Water: WishboundWaterFX.hlsl (Ocean, River), Wishbound/ToonWater (Resources/WishboundToonWater)
- Colliders: added to all 20 LowPolyMedievalStarterPack prefabs
- Houses: HouseEntrances (doors on local +Z), InteriorScene + InteriorBuilder, Assets/Scenes/Interiors/*
