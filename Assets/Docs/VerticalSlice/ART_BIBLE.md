# First quest-prop production batch

The approved working direction uses weathered blue slate, turquoise linen/crystal, restrained warm brass and dark brown timber. Separate silhouette features remain readable at a 3–8 m gameplay distance: crossed parcel straps, pointed crystal, star-inlaid landing stones, concentric astronomical rings, individual pier planks and rope-bound posts.

Unity units are metres. Assets export from Blender in FBX with Y-up conversion and applied scale. Eight original 256×256 material textures include linen weave, timber grain and mineral/pigment variation. They are opaque surfaces, not sprites. Import uses mipmaps, repeat wrapping, URP Lit and shared material assets. Brass is the only metallic material.

Generator: Tools/VerticalSlice/build_quest_props.py, deterministic seed 411. All geometry and textures in this batch are original procedural work; no external source assets are redistributed by the generator. Unity importer: Assets/Editor/VerticalSlice/QuestPropImporter.cs. These are first-pass authored props, not a claim that the full world meets the master specification.

Visual review: Captures/VerticalSlice/quest-props-lit.png. Models are correctly oriented, textured and dimensioned. Texture contact sheet reviewed. The first scene-based review exposed unrelated magenta world surfaces; isolated lighting was used for prop assessment only and does not clear the world rendering gate.

Remaining quality work: reduce renderers by merging compatible material meshes, bake a shared atlas, produce actual reduced-geometry LODs, improve wear/paint variation, animate interaction details, inspect every asset in its gameplay shot. The current LODGroup only culls distant props; it is not a substitute for three geometry LODs.
