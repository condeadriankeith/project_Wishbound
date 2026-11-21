using UnityEngine;

/// <summary>
/// Drop this on any trigger volume to make IsoCameraRig switch to a cinematic camera pose while the
/// Player is inside it. Auto-detected by tag - no manual wiring needed beyond placing the volume and
/// tagging it (e.g. "Bridge", "HouseInterior"). Multiple overlapping zones resolve by priority
/// (higher wins); leaving a zone falls back to whichever other zone the player is still inside, or to
/// IsoCameraRig's plain isometric base view if none.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CameraZoneTrigger : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>Low, near-level pitch; yaw snaps to face the player's current forward (a 'looking at them
        /// head-on' cinematic shot). Good for house fronts / interiors / tight spaces.</summary>
        LevelFront,
        /// <summary>Yaw continuously tracks 90 degrees off the player's current forward, re-evaluated every
        /// frame so it keeps re-orienting as the player turns; a fixed, more horizontal pitch. Good for bridges,
        /// corridors, anything you want a cinematic side-profile walk shot of.</summary>
        DynamicSide,
    }

    [Tooltip("How this zone poses the camera while the player is inside it.")]
    public Mode mode = Mode.LevelFront;

    [Header("Pose")]
    [Tooltip("Camera elevation in degrees while this zone is active.")]
    [Range(-10f, 85f)] public float pitch = 12f;
    [Tooltip("Camera distance while this zone is active. <= 0 keeps whatever distance is currently in use.")]
    public float distance = 10f;
    [Tooltip("Which side to orbit to for DynamicSide ('left' = player's left, as they face forward).")]
    public bool sideIsPlayerLeft = true;

    [Header("Blend")]
    [Tooltip("Seconds to blend into this zone's pose on entry and back out on exit.")]
    public float blendTime = 0.6f;

    [Header("Priority")]
    [Tooltip("If the player is standing inside multiple zones at once, the highest priority one wins.")]
    public int priority = 0;

    [Tooltip("Only objects with this tag trigger the zone (defaults to Player). Set in Awake from the GameObject's own tag if left empty - i.e. just tag THIS trigger object, e.g. 'Bridge', 'HouseInterior', and it auto-registers as that zone type without further setup.")]
    public string triggeringTag = "Player";

    Collider col;

    void Awake()
    {
        col = GetComponent<Collider>();
        if (!col.isTrigger) col.isTrigger = true; // zones must be triggers; fix it rather than silently doing nothing
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(triggeringTag)) return;
        var rig = IsoCameraRig.Active;
        if (rig) rig.RegisterZone(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(triggeringTag)) return;
        var rig = IsoCameraRig.Active;
        if (rig) rig.UnregisterZone(this);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = mode == Mode.LevelFront ? new Color(0.3f, 0.7f, 1f, 0.25f) : new Color(1f, 0.6f, 0.2f, 0.25f);
        var c = GetComponent<Collider>();
        if (c is BoxCollider bc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(bc.center, bc.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else if (c is SphereCollider sc)
        {
            Gizmos.DrawSphere(transform.TransformPoint(sc.center), sc.radius * transform.lossyScale.x);
        }
    }
}
