using UnityEngine;

/// <summary>Root of an interior scene: where the party spawns, where the way out is, and the camera framing inside.</summary>
public class InteriorScene : MonoBehaviour
{
    public static InteriorScene Active { get; private set; }
    public Transform spawn, exit;
    public float exitRadius = 1.6f;
    public IsoCameraRig.Profile cameraProfile = new IsoCameraRig.Profile { name = "Interior", pitch = 58, distance = 13, fov = 36, blend = .01f };
    void OnEnable() { Active = this; }
    void OnDisable() { if (Active == this) Active = null; }
    public bool NearExit(Vector3 p) => exit && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(exit.position.x, 0, exit.position.z)) < exitRadius;
    public void OnEntered() { if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(cameraProfile); }
    void Update()
    {
        // safety net: if the party ever ends up outside the room's floor, put them back at the door
        if (!spawn) return;
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p && p.transform.position.y < transform.position.y - 3f)
        {
            var c = p.GetComponent<IslandThirdPersonController>();
            if (c) c.Teleport(spawn.position + Vector3.up * .3f, spawn.eulerAngles.y); else p.transform.position = spawn.position;
        }
    }
    public void OnLeaving() { if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(null); }
}
