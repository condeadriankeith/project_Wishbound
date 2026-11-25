using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Method 1 (dithered transparency, camera-to-player raycast): fades out trees that sit between the
/// camera and the player's avatar by punching a dithered alpha-clip hole in them, so the camera never
/// needs to move/zoom/deocclude (replaces CinemachineDeoccluder). Obstacles are fully opaque trees on
/// the dedicated "TreeOcclusion" layer; a sphere-cast from camera to player against that layer finds
/// which tree(s) sit in the way, and their dither fade is driven up/down accordingly.
///
/// Uses the occlusion data IslandStylizedTrees already builds: TreeColliders (on "TreeOcclusion") and
/// ColliderToInstance (collider index -> which GhibliBatch/chunk/slot that collider's tree is drawn as).
/// An occluding instance is pulled out of the normal opaque instanced draw (GhibliBatch.Draw's
/// excludeSlots, via IslandStylizedTrees.SetExcludedSlots) and drawn once on its own with
/// GhibliBatch.DrawSingleInstance using a real dithered stand-in material (GhibliDitherMaterials) whose
/// _DitherFade sweeps the Dither-node alpha-clip threshold from 0 (solid) to 1 (fully clipped/invisible).
///
/// Fade is "commit, don't chase": the raw hit test flickers frame to frame (the collider is a capsule
/// approximating a much bushier canopy, and the sphere-cast itself brushes in and out near edges), so
/// instead of re-aiming at hit?1:0 every frame and re-easing from wherever it happens to be (which stalls
/// at whatever fade value the flicker rate works out to - this is why it was stuck around ~70%), each
/// instance latches "occluding" as soon as it's hit at all and stays latched, ignoring brief gaps, until
/// it's been clear for releaseDelay seconds - then it sweeps the rest of the way to fully solid once and
/// stays until hit again. So fade always drives all the way to 0 or all the way to 1, never settles mid-way.
/// </summary>
[DefaultExecutionOrder(100)]   // after IslandStylizedTrees.Update has rebuilt pkgBatches for this frame
public class TreeOcclusionFader : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;                         // defaults to Camera.main
    public Transform player;                   // the avatar's chest point; falls back to IslandThirdPersonController's cameraTarget
    public IslandStylizedTrees trees;           // defaults to FindFirstObjectByType

    [Header("Behaviour")]
    [Tooltip("Extra radius around the camera-to-player ray so trees near the sightline dither too, not just a pixel-thin line.")]
    public float rayRadius = 0.6f;
    [Tooltip("How fast an instance's dither fade sweeps 0->1 / 1->0, in units per second (1 = full sweep in 1s).")]
    public float fadeSpeed = 6f;
    [Tooltip("Seconds an instance must go unhit before it's allowed to start fading back to solid - absorbs frame-to-frame hit flicker so fade always commits to 0 or 1 instead of stalling mid-way.")]
    public float releaseDelay = 0.15f;
    [Tooltip("Radius (metres) of the see-through hole around the camera-to-avatar sightline.")]
    public float holeRadius = 3.5f;
    [Tooltip("Fraction of the hole radius used as the dithered soft edge (0 = hard edge, 1 = fades from the centre out).")]
    [Range(0f, 1f)] public float holeSoftness = 0.6f;
    [Tooltip("Only geometry at least this many metres in front of the avatar is cut, so the ground/avatar surroundings never get holes.")]
    public float holeMargin = 1.0f;

    class State { public float fade; public float unhitTimer; public bool occluding; }
    readonly Dictionary<(GhibliBatch batch, int chunk, int slot), State> states = new Dictionary<(GhibliBatch, int, int), State>();
    readonly Dictionary<GhibliBatch, HashSet<(int chunk, int slot)>> liveExcludes = new Dictionary<GhibliBatch, HashSet<(int chunk, int slot)>>();
    readonly HashSet<(GhibliBatch batch, int chunk, int slot)> hitThisFrame = new HashSet<(GhibliBatch, int, int)>();
    readonly RaycastHit[] hitBuf = new RaycastHit[32];
    static readonly List<(GhibliBatch, int, int)> scratchRemove = new List<(GhibliBatch, int, int)>();

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!trees) trees = FindFirstObjectByType<IslandStylizedTrees>();
        if (!player)
        {
            var ctrl = FindFirstObjectByType<IslandThirdPersonController>();
            if (ctrl) player = ctrl.cameraTarget ? ctrl.cameraTarget : ctrl.transform;
        }
    }

void LateUpdate()
    {
        if (cam == null || player == null) return;
        // the opening sequence frames the world freely - no see-through holes cut into trees there
        if (WishboundIntro.IsPlaying || CutsceneDirector.Active) { Shader.SetGlobalVector("_OccParams", Vector4.zero); Shader.SetGlobalVector("_OccDir", Vector4.zero); return; }
        if (trees != null) trees.ClearAllExcludedSlots();   // legacy ghost-pass exclusion is retired; trees always draw with their real materials
        Vector3 from = cam.transform.position;
        Vector3 dir = player.position - from;
        float dist = dir.magnitude;
        Vector3 unitDir = dist > 0.01f ? dir / dist : cam.transform.forward;
        Shader.SetGlobalVector("_OccCamPos", from);
        Shader.SetGlobalVector("_OccDir", new Vector4(unitDir.x, unitDir.y, unitDir.z, dist));
        Shader.SetGlobalVector("_OccParams", new Vector4(holeRadius, holeSoftness, holeMargin, 1f));
    }

    // TreeColliders' CapsuleCollider components were added in Scatter() in the same order ColliderToInstance's
    // keys were recorded in - GetComponents preserves addition order, so index-in-array == original collider index.
    CapsuleCollider[] cachedColliders;
    GameObject cachedGO;
    int IndexOfCollider(CapsuleCollider c)
    {
        if (cachedColliders == null || cachedGO != trees.TreeCollidersGO)
        {
            cachedGO = trees.TreeCollidersGO;
            cachedColliders = cachedGO.GetComponents<CapsuleCollider>();
        }
        for (int i = 0; i < cachedColliders.Length; i++) if (cachedColliders[i] == c) return i;
        return -1;
    }
}
