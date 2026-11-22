using UnityEngine;

/// <summary>Root of an interior scene: where the party spawns, where the way out is, and the camera framing inside.</summary>
public class InteriorScene : MonoBehaviour
{
    public static InteriorScene Active { get; private set; }
    public Transform spawn, exit;
    public float exitRadius = 1.6f;
    /// <summary>Isometric interior profile (Image 5 style): high angle looking down into the room,
    /// close enough to read the furniture and characters but wide enough to see the whole space.</summary>
    public IsoCameraRig.Profile cameraProfile = new IsoCameraRig.Profile
    {
        name = "Interior Iso",
        pitch = 52f,
        distance = 12f,
        fov = 34f,
        yaw = 45f,
        blend = 1.0f
    };
    void OnEnable() { Active = this; }
    void OnDisable() { if (Active == this) Active = null; }
    public bool NearExit(Vector3 p) => exit && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(exit.position.x, 0, exit.position.z)) < exitRadius;
    IslandThirdPersonController pc; int prevMode = 2; bool previousOrbit; float previousDistance;
    Transform roomFocus, previousFocus, previousFollow, previousLookAt;
    /// <summary>Inside a house the view switches to the isometric interior profile - a high, wide shot
    /// that shows the room layout, furniture and characters at once (Image 5).</summary>
    public void OnEntered()
    {
        var pl = GameObject.FindGameObjectWithTag("Player"); pc = pl ? pl.GetComponent<IslandThirdPersonController>() : null;
        if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(null);
        if (pc)
        {
            prevMode = pc.CameraMode;
            previousDistance = IsoCameraRig.Active ? IsoCameraRig.Active.ZoomDistance : 12f;
            // keep the iso camera but apply the interior framing instead of first-person
            pc.PrepareThirdPerson();
            pc.SetCameraMode(2); // third-person iso, distance is driven by the profile below
        }
        var rig = IsoCameraRig.Active;
        if (rig)
        {
            previousOrbit = rig.allowMouseOrbit; rig.allowMouseOrbit = false;
            previousFocus = rig.focus; previousFollow = rig.vcam.Follow; previousLookAt = rig.vcam.LookAt;
            if (!roomFocus) { roomFocus = new GameObject("Room camera focus").transform; roomFocus.SetParent(transform, false); }
            var floor = transform.Find("Floor"); var floorRenderer = floor ? floor.GetComponent<Renderer>() : null;
            var bounds = floorRenderer ? floorRenderer.bounds : new Bounds(transform.position, new Vector3(10, .1f, 11));
            roomFocus.position = bounds.center + Vector3.up * .8f;
            rig.focus = rig.vcam.Follow = rig.vcam.LookAt = roomFocus;
            float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            cameraProfile.distance = Mathf.Max(19f, radius * 1.15f / Mathf.Tan(21f * Mathf.Deg2Rad));
        }
        cameraProfile.pitch = 52f; cameraProfile.yaw = transform.eulerAngles.y + 45f; cameraProfile.fov = 42f;
        IsoCameraRig.Active?.SetProfile(cameraProfile);
        // point lights light toon surfaces inside (GhibliToonLit reads _ToonAddLights per fragment)
        Shader.SetGlobalFloat("_ToonAddLights", 1f);
        var roomFloor = transform.Find("Floor");
        var floorMesh = roomFloor ? roomFloor.GetComponent<Renderer>() : null;
        GrassExclusions.AddInterior(floorMesh ? floorMesh.bounds : new Bounds(transform.position, new Vector3(12, 4, 12)));   // keep scattered grass out of the house interior
        // grandmother cutscene: play once when entering a house that has one
        var gm = GetComponentInChildren<GrandmotherCutscene>(true);
        if (!gm) gm = FindFirstObjectByType<GrandmotherCutscene>();
        if (gm) gm.PlayIfNotSeen();
    }
    void Update()
    {
        if (pc && HouseEntrances.Instance && HouseEntrances.Instance.Inside)
        {
            if (pc.CameraMode != 2) pc.SetCameraMode(2);
            pc.SetCameraDistance(cameraProfile.distance);
            IslandCameraFX.TiltGoal = .05f; /* walls are inches from the lens: the tilt-shift blur smears the whole room */
        }
        // safety net: if the party ever ends up outside the room's floor, put them back at the door
        if (!spawn) return;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p && p.transform.position.y < transform.position.y - 3f)
        {
            var c = p.GetComponent<IslandThirdPersonController>();
            if (c) c.Teleport(spawn.position + Vector3.up * .3f, spawn.eulerAngles.y); else p.transform.position = spawn.position;
        }
    }
    public void OnLeaving()
    {
        if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(null);
        if (IsoCameraRig.Active)
        {
            var rig = IsoCameraRig.Active; rig.allowMouseOrbit = previousOrbit;
            rig.focus = previousFocus; rig.vcam.Follow = previousFollow; rig.vcam.LookAt = previousLookAt;
        }
        if (pc) { pc.PrepareThirdPerson(); pc.SetCameraMode(prevMode); pc.SetCameraDistance(previousDistance); }
        Shader.SetGlobalFloat("_ToonAddLights", 0f);
    }
}
