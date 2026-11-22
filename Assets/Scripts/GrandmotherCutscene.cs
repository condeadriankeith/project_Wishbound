using System.Collections;
using UnityEngine;

/// <summary>
/// A short interior cutscene: the grandmother sits by the hearth, warming her hands.
/// Plays once when the player first enters a house that has a grandmother.
/// Camera is placed to frame her clearly, never behind a wall - it sits in the
/// doorway looking in, with the hearth and grandmother both in view.
/// </summary>
public class GrandmotherCutscene : MonoBehaviour
{
    public static GrandmotherCutscene Instance { get; private set; }

    [Header("Actors (auto-assigned if empty)")]
    public Transform grandmother;    // the grandmother NPC
    public Transform hearth;         // the fire/hearth she sits by
    public InteriorScene interior;   // the interior scene this belongs to

    [Header("Camera framing")]
    [Tooltip("Where the camera sits. Defaults to just inside the door looking at the hearth.")]
    public Vector3 cameraPosition;
    [Tooltip("What the camera looks at. Defaults to the grandmother.")]
    public Vector3 cameraLookAt;
    public float cameraFov = 28f;
    public float duration = 6f;

    [Header("State")]
    public bool hasPlayed;

    void Awake() { Instance = this; }

    /// <summary>Call from InteriorScene.OnEntered() when entering a house with a grandmother.</summary>
    public void PlayIfNotSeen()
    {
        if (hasPlayed) return;
        if (!grandmother || !hearth) return;   // nothing to show
        hasPlayed = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        var dir = CutsceneDirector.Begin();
        if (!CutsceneDirector.Active) yield break;
        CutsceneSkip.Begin(Skip);

        // Default camera: stand in the doorway, look through at the grandmother by the hearth.
        // This keeps both the grandmother and the hearth in frame, never behind a wall.
        Vector3 camPos = cameraPosition != Vector3.zero ? cameraPosition
            : hearth.position - hearth.forward * 3.5f + Vector3.up * 1.2f;
        Vector3 lookAt = cameraLookAt != Vector3.zero ? cameraLookAt
            : grandmother ? grandmother.position : hearth.position;

        // Frame the hands and face together, from a validated point inside this room.
        lookAt = Vector3.Lerp(grandmother.position + Vector3.up, hearth.position + Vector3.up * .6f, .3f);
        if (!interior) interior = GetComponentInParent<InteriorScene>();
        var room = new Bounds(interior ? interior.transform.position + Vector3.up * 1.5f : lookAt, new Vector3(7f, 3f, 7f));
        camPos = CutsceneCam.Pick(lookAt, grandmother.forward, room, 2f, 3f, 1.5f);
        GrassExclusions.AddInterior(room);
        WarmHands();
        dir.Cut(camPos, lookAt, cameraFov);

        yield return dir.Glide(() => camPos, () => lookAt, cameraFov, 1f);
        yield return dir.Glide(() => camPos, () => lookAt, cameraFov, duration - 1f);

        CutsceneSkip.End();
        dir.End(1.2f);
    }

    void Skip()
    {
        StopAllCoroutines();
        if (CutsceneDirector.Instance) CutsceneDirector.Instance.End(.8f);
    }

    /// <summary>Grandmother warms her hands by the hearth - called by the cutscene animation.</summary>
    public void WarmHands()
    {
        if (!grandmother) return;
        var rig = grandmother.GetComponentInChildren<SettlerRig>();
        if (rig) { rig.pose = SettlerRig.Pose.WarmHands; rig.lookPoint = hearth.position; }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
