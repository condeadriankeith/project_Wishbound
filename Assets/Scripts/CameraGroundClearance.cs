using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// Obstacle-safe orbit, followed by a continuous blend into the star's eye.
//
// Smoothing rules (2026-11-15 rework, was jittery):
//   * The ground floor under the camera is low-pass filtered (fast to rise, slow to fall), so terrain
//     cells, small props and collider seams no longer step the camera height every frame.
//   * Small colliders (debris, clods, fences thinner than smallObstacleSize) never push the camera in.
//   * Obstructions still pull the camera in quickly, but through a very short damp with a hysteresis band
//     instead of a hard snap, so the camera stops "pumping" when skimming past a collider edge. A sudden
//     large obstruction (more than snapFraction of the distance) still snaps for safety.
//   * First-person eye uses the stable avatar root + target height, not the bobbing visual.
[DisallowMultipleComponent]
public class CameraGroundClearance : CinemachineExtension
{
    public float clearance = 1.1f;
    public float collisionRadius = .22f;
    [Tooltip("Seconds to ease back out after an obstruction clears.")]
    public float recoveryTime = .35f;
    [Tooltip("Seconds to ease in when an obstruction appears (0 = snap).")]
    public float retractTime = .06f;
    [Tooltip("Ignore obstruction changes smaller than this (metres) to stop pumping.")]
    public float hysteresis = .12f;
    [Tooltip("If an obstruction removes more than this fraction of the distance at once, snap instead of easing.")]
    [Range(0, 1)] public float snapFraction = .45f;
    [Tooltip("Colliders whose largest bounds dimension is below this are ignored for camera collision.")]
    public float smallObstacleSize = 1.2f;
    [Tooltip("Seconds for the ground floor to rise / fall under the camera.")]
    public float floorRiseTime = .12f, floorFallTime = .4f;

    IsoCameraRig rig;
    AmberOrbBody orb;
    float safeDistance, distanceVelocity;
    float floorY = float.NegativeInfinity, floorVelocity;
    readonly RaycastHit[] hits = new RaycastHit[128];
    readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();

    internal static bool BlocksCamera(Collider c)
    {
        if (!c || c.isTrigger) return false;
        if (c is CharacterController || c.GetComponentInParent<SettlerRig>() || c.GetComponentInParent<ProceduralFollow>()) return false;
        if (TerrainProbe.IsTerrain(c) || c is TerrainCollider) return true;
        for (Transform t = c.transform; t; t = t.parent)
        {
            if (t.name == "TreeColliders" || t.name == "WeatheredCottage") return true;
            if (t.parent && t.parent.TryGetComponent<VillageLot>(out var lot) && t.name == lot.house) return true;
        }
        return false;
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (!rig) rig = GetComponent<IsoCameraRig>();
        if (!rig || !rig.focus) return;
        if (!orb && rig.avatarRoot) orb = rig.avatarRoot.GetComponentInChildren<AmberOrbBody>();
        Vector3 anchor = rig.focus.position;
        bool reset = deltaTime < 0;
        float dt = Mathf.Max(0, deltaTime);
        int mask = Physics.AllLayers;

        if (stage == CinemachineCore.Stage.Body)
        {
            Vector3 desiredPosition = state.RawPosition;
            float radius = Mathf.Max(collisionRadius, state.Lens.NearClipPlane *
                Mathf.Sqrt(1f + Mathf.Pow(Mathf.Tan(state.Lens.FieldOfView * .5f * Mathf.Deg2Rad), 2) * (1f + state.Lens.Aspect * state.Lens.Aspect)));
            if (TerrainProbe.TryGetGroundY(desiredPosition.x, desiredPosition.z, out float ground))
                desiredPosition.y = Mathf.Max(desiredPosition.y, ground + radius);

            // ---- filtered floor
            float top = Mathf.Max(desiredPosition.y, anchor.y) + 3f;
            int groundCount = Physics.RaycastNonAlloc(new Vector3(desiredPosition.x, top, desiredPosition.z), Vector3.down, hits, top - desiredPosition.y + 4f, mask, QueryTriggerInteraction.Ignore);
            float floor = float.NegativeInfinity;
            for (int i = 0; i < groundCount; i++)
            {
                var h = hits[i];
                if (!BlocksCamera(h.collider) || h.normal.y < .65f) continue;
                if (h.point.y > desiredPosition.y + 2.5f) continue;   // a roof / canopy far above us is not a floor
                floor = Mathf.Max(floor, h.point.y);
            }
            if (floor > float.NegativeInfinity)
            {
                if (reset || floorY == float.NegativeInfinity) { floorY = floor; floorVelocity = 0; }
                else floorY = Mathf.SmoothDamp(floorY, floor, ref floorVelocity, floor > floorY ? floorRiseTime : floorFallTime, Mathf.Infinity, dt);
                desiredPosition.y = Mathf.Max(desiredPosition.y, Mathf.Max(floor, floorY) + clearance * (1 - rig.FirstPersonBlend));
            }

            // ---- obstruction (damped, with hysteresis)
            Vector3 offset = desiredPosition - anchor;
            float desired = offset.magnitude;
            if (desired > .001f)
            {
                float allowed = desired;
                int count = Physics.SphereCastNonAlloc(anchor, radius, offset / desired, hits, desired, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var c = hits[i].collider;
                    if (!BlocksCamera(c)) continue;
                    allowed = Mathf.Min(allowed, Mathf.Max(.02f, hits[i].distance - .08f));
                }
                if (reset || safeDistance <= 0) { safeDistance = allowed; distanceVelocity = 0; }
                else if (allowed < safeDistance - hysteresis || allowed < safeDistance && allowed >= desired - .001f)
                {
                    if (safeDistance - allowed > snapFraction * desired || retractTime <= 0) { safeDistance = allowed; distanceVelocity = 0; }
                    else safeDistance = Mathf.SmoothDamp(safeDistance, allowed, ref distanceVelocity, retractTime, Mathf.Infinity, dt);
                }
                else if (allowed > safeDistance + hysteresis || allowed >= desired - .001f)
                    safeDistance = Mathf.SmoothDamp(safeDistance, allowed, ref distanceVelocity, recoveryTime, Mathf.Infinity, dt);
                safeDistance = Mathf.Min(safeDistance, allowed);
                state.RawPosition = anchor + offset / desired * safeDistance;
            }
        }
        if (stage != CinemachineCore.Stage.Finalize) return;
        float fp = rig.FirstPersonBlend;
        // Stable eye: the follow target, not the bobbing / spring-lagged visual.
        Vector3 eye = anchor;
        state.RawPosition = Vector3.Lerp(state.RawPosition, eye, fp);
        var finalPosition = state.RawPosition;
        if (TerrainProbe.TryGetGroundY(finalPosition.x, finalPosition.z, out float groundFloor))
        {
            finalPosition.y = Mathf.Max(finalPosition.y, groundFloor + Mathf.Max(collisionRadius, state.Lens.NearClipPlane * 2f));
            state.RawPosition = finalPosition;
        }
        state.RawOrientation = Quaternion.Slerp(state.RawOrientation, Quaternion.Euler(rig.CameraPitch, rig.CameraYaw, 0), fp);
        bool hide = fp > .65f || (orb && Vector3.Distance(state.RawPosition, orb.transform.position) < .65f);
        if (hide && orb)
        {
            foreach (var r in orb.GetComponentsInChildren<Renderer>())
            {
                if (!hidden.ContainsKey(r)) hidden[r] = r.forceRenderingOff;
                r.forceRenderingOff = true;
            }
        }
        else RestoreVisuals();
    }
    void RestoreVisuals() { foreach (var pair in hidden) if (pair.Key) pair.Key.forceRenderingOff = pair.Value; hidden.Clear(); }
    protected override void OnDestroy() { RestoreVisuals(); base.OnDestroy(); }
    void OnDisable() { RestoreVisuals(); }
}
