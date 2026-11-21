# Wishbound audio

Replaced the synthetic placeholder library on 2026-11-18. Edited files are in Assets/Audio/Wishbound. Source recordings were trimmed, filtered, level matched, and given fades; looping beds have an overlapping seam. Music is streamed. Effects are preloaded. Original placeholders are backed up outside Assets in the Codex working folder.

## Public domain / CC0

- Roots_Piano: “Slow Memory Bpm”, Lots of Sounds sample fs-873537. https://api.lotsofsounds.com/api/v1/sounds/sample
- Awaken_Resonance and Choice_Resonance: “Steel Tongue Drum”, Lots of Sounds sample fs-868787. Same sample API.
- Birds_Distant: “Birds Singing Spring”, Lots of Sounds sample fs-387978. Same sample API. Used as a short distant birds cue, not a wing-flap recording.
- Explore_Woodland: “Forest Ambience”, TinyWorlds. https://opengameart.org/content/forest-ambience
- Tether_Dream: “A Dreamy Forest”, Sen. https://opengameart.org/content/a-dreamy-forest
- Wind_Leaves and Night_Air: “Wind”, IgnasD. https://opengameart.org/content/wind
- Celestial_Bloom and Warm_Light: “Magic Spell SFX”, JaggedStone. https://opengameart.org/content/magic-spell-sfx
- Shard_Break: “Glass Break”, Till Behrend, uploaded by TinyWorlds. https://opengameart.org/content/glass-break
- Cottage_Cry: “Crying newborn baby”, the_yura. https://commons.wikimedia.org/wiki/File:Crying_newborn_baby.ogg

Lots of Sounds license reference: https://www.lotsofsounds.com/developers
CC0: https://creativecommons.org/publicdomain/zero/1.0/

## Attribution required: CC BY 3.0

Sky_Rumble and Comet_Impact are edited excerpts of “Thunder” by Jerimee.
https://opengameart.org/content/thunder
Licensed under Creative Commons Attribution 3.0: https://creativecommons.org/licenses/by/3.0/
Changes: trimming, filtering, loudness adjustment; Sky_Rumble additionally looped.
Include this attribution in the shipped game credits.

## Playback

WishboundMix routes Music, SFX, and Ambience separately. The intro owns its audio until handover; exploration music then fades in. Story choices crossfade to Tether or Roots. One-shot story effects temporarily duck music. The old repeating footsteps layer has been removed from the score. Runtime checks verify routing and transitions; final subjective listening on speakers and headphones remains recommended.
