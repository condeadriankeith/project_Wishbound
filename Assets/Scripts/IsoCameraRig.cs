using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Isometric-style camera driver.
/// Lives on the PlayerCamera object (next to CinemachineCamera + CinemachineOrbitalFollow) and is the ONLY
/// writer of that orbital camera's yaw / pitch / radius while it is active.
///
///   BASE VIEW      A fixed elevation (basePitch, locked). The heading (baseYaw) continuously follows mouse-X
///                  (and gamepad right-stick X) while the cursor is captured - horizontal orbit only, no Q/E steps.
///   ZONES          CameraZoneTrigger volumes can blend the camera into a cinematic pose while the player is inside.
///   CONTROLS       ControlForward() hands the controller a movement heading that is latched while a stick / key is
///                  held (and never drifts more than controlDriftLimit degrees from the real camera heading), so
///                  a camera turn (Q / E or a zone blend) does not bend the avatar's path mid-stride.
///
/// There is NO camera auto-pull-in / collision-avoidance any more - the CinemachineDeoccluder was removed
/// (2026-10-23). Occlusion is now handled by fading the blocking object translucent instead of moving the
/// camera; see TreeOcclusionFader. The previous occlusion-solver experiment is kept in the project root as
/// IsoCameraRig.with-occlusion-solver.backup.txt.
///
/// Zoom distance is supplied by IslandThirdPersonController through SetZoomDistance().
///
/// Progress / resume notes: Assets/_DevNotes/IsoCamera_PROGRESS_NOTES.cs
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]   // after IslandThirdPersonController.Update has moved the avatar this frame
[RequireComponent(typeof(CinemachineCamera))]
public class IsoCameraRig : MonoBehaviour
{
    // ------------------------------------------------------------------ references
    [Header("References (auto-filled when left empty)")]
    public CinemachineCamera vcam;
    [Tooltip("Point the camera looks at / follows (the avatar's CameraTarget).")]
    public Transform focus;
    [Tooltip("The avatar's root transform (used by camera zones to read the avatar's facing).")]
    public Transform avatarRoot;

    // ------------------------------------------------------------------ base view
    [Header("Isometric base view")]
    [Tooltip("Compass heading the camera looks along, in degrees (45 = classic isometric corner view).")]
    [Range(0f, 360f)] public float baseYaw = 45f;
    [Tooltip("Camera elevation above the avatar in degrees (about 35 = true isometric, higher = more top-down).")]
[Range(-65f, 80f)] public float basePitch = 20f; public bool verticalOrbit = true; public bool shoulderView; public float shoulderDistance = 4.2f; public float shoulderSide = .65f; float pitchSmooth, pitchVelocity, shoulderBlend, lookUpBlend; [Header("Look-up shoulder view (automatic)")] [Tooltip("When you pull the camera pitch down to look at the sky, ease into an over-the-shoulder framing that aims above the avatar.")] public bool autoShoulder = true; [Tooltip("Camera pitch (deg) where the look-up blend starts.")] public float autoShoulderStartPitch = 12f; [Tooltip("Camera pitch (deg) where the look-up blend is complete.")] public float autoShoulderFullPitch = -8f; public float lookUpDistance = 8f; [Tooltip("Screen-space Y the avatar is pushed to while looking up (positive = lower on screen, so the camera aims higher).")] public float lookUpAimY = .22f; CinemachineRotationComposer composer; Vector2 composerBaseScreen; bool composerCached;
    [Tooltip("Mouse X continuously revolves the camera around the avatar (horizontal only; pitch stays locked to basePitch).")]
    public bool allowMouseOrbit = true;
    [Tooltip("Degrees of yaw per pixel of mouse-X movement.")]
    public float mouseYawSensitivity = 0.15f;
    [Tooltip("Degrees per second of yaw at full gamepad right-stick deflection.")]
    public float gamepadYawSpeed = 140f;
    public float rotateSmoothTime = 0.12f;
    [Range(-75f, 85f)] public float pitchLimitMin = -60f;
    [Range(5f, 85f)] public float pitchLimitMax = 75f;

    // ------------------------------------------------------------------ controls
    [Header("Controls")]
    [Tooltip("Movement heading may lag the real camera heading by at most this many degrees while a key is held.")]
    public float controlDriftLimit = 20f;

    // ------------------------------------------------------------------ debug
    [Header("Debug")]
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ state
    public static IsoCameraRig Active { get; private set; }

    readonly List<CameraZoneTrigger> activeZones = new List<CameraZoneTrigger>();
    float zoneYaw, zoneYawVel, zonePitch, zonePitchVel, zoneDist, zoneDistVel;
    float zoneBlend, zoneBlendVel; // 0 = plain iso base view, 1 = fully in zone pose
    CameraZoneTrigger lastZone;

    CinemachineOrbitalFollow orbital;

    float zoomDistance = 28f;
    float baseYawSm, baseYawVel;
    float controlYaw;

    public float CameraYaw => Wrap180(baseYawSm);
    public float ZoomDistance => zoomDistance;
    public float FirstPersonBlend => 1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.08f,1.1f,zoomDistance));
    public float OverviewBlend => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(10f,26f,zoomDistance));
    public float CameraPitch => pitchSmooth;

    // ================================================================== camera profiles
    /// <summary>A named framing the rig can ease into on top of the free iso view (story beats, interactions).</summary>
    [System.Serializable]
    public class Profile
    {
        public string name = "Profile";
        [Range(-10f, 85f)] public float pitch = 35f;
        public float distance = 9f;
        [Range(10f, 70f)] public float fov = 28f;
        [Tooltip("Fixed heading in degrees; NaN = keep the player's free heading.")]
        public float yaw = float.NaN;
        [Tooltip("Seconds to ease in / out.")]
        public float blend = 1.2f;
        public static Profile IntimateCompanion => new Profile { name = "IntimateCompanion", pitch = 35, distance = 9, fov = 28, blend = 1.6f };
        public static Profile WindowHold(float yaw) => new Profile { name = "WindowHold", pitch = 22, distance = 5.5f, fov = 26, yaw = yaw, blend = 1.2f };
    }
    Profile profile, lastProfile;
    float profileW, profileWVel, profileYawSm, profileYawVel;
    /// <summary>Current profile weight (0 = plain Explore view).</summary>
    public float ProfileWeight => profileW;
    public string ProfileName => profile != null ? profile.name : "Explore";
    /// <summary>Ease into a profile (null = back to the Explore view).</summary>
    public void SetProfile(Profile p) { if (p != null) { if (profile == null) profileYawSm = CameraYaw; lastProfile = p; } profile = p; }
    /// <summary>FOV with the active profile blended in (the controller asks for this).</summary>
    public float ProfileFov(float baseFov) => lastProfile != null ? Mathf.Lerp(baseFov, lastProfile.fov, profileW) : baseFov;

    void UpdateProfile(float dt)
    {
        var p = profile ?? lastProfile;
        float target = profile != null ? 1f : 0f;
        profileW = Mathf.SmoothDamp(profileW, target, ref profileWVel, Mathf.Max(.05f, (p != null ? p.blend : 1f) * .5f), Mathf.Infinity, dt);
        if (p != null && !float.IsNaN(p.yaw)) profileYawSm = Mathf.SmoothDampAngle(profileYawSm, p.yaw, ref profileYawVel, Mathf.Max(.05f, p.blend * .5f), Mathf.Infinity, dt);
        else profileYawSm = Mathf.SmoothDampAngle(profileYawSm, CameraYaw, ref profileYawVel, .15f, Mathf.Infinity, dt);
        if (profile == null && profileW < .001f) lastProfile = null;
    }

    // ================================================================== public API (used by the controller)

    /// <summary>Called every frame by IslandThirdPersonController with its smoothed zoom radius.</summary>
    public void SetZoomDistance(float d) { zoomDistance = Mathf.Max(.02f, d); }

    /// <summary>
    /// Horizontal movement heading for "stick up". While input is active the heading is latched, so a camera turn
    /// does not change where the avatar walks; it is released whenever input stops.
    /// </summary>
    public Vector3 ControlForward(bool inputActive)
    {
        float cam = CameraYaw;
        if (!inputActive) controlYaw = cam;
        else
        {
            float d = Mathf.DeltaAngle(controlYaw, cam);
            if (Mathf.Abs(d) > controlDriftLimit) controlYaw = cam - Mathf.Sign(d) * controlDriftLimit;
        }
        return Quaternion.Euler(0f, controlYaw, 0f) * Vector3.forward;
    }

    /// <summary>Call after teleporting the avatar. Kept for the controller's API; the rig has no per-position state to reset.</summary>
    public void OnTargetWarped() { }

    /// <summary>Called by a CameraZoneTrigger when the player enters it.</summary>
    public void RegisterZone(CameraZoneTrigger z)
    {
        if (!activeZones.Contains(z)) activeZones.Add(z);
    }

    /// <summary>Called by a CameraZoneTrigger when the player leaves it.</summary>
    public void UnregisterZone(CameraZoneTrigger z)
    {
        activeZones.Remove(z);
    }

    /// <summary>Highest-priority zone the player currently stands in, or null if none.</summary>
    CameraZoneTrigger CurrentZone()
    {
        CameraZoneTrigger best = null;
        for (int i = 0; i < activeZones.Count; i++)
        {
            var z = activeZones[i];
            if (!z) continue;
            if (best == null || z.priority > best.priority) best = z;
        }
        return best;
    }

    [ContextMenu("Snap to base view")]
    public void SnapToBase()
    {
        baseYawSm = baseYaw; baseYawVel = 0f;
        if (orbital) ApplyToCinemachine();
    }

    // ================================================================== lifecycle

    void OnEnable() { Active = this; }
    void OnDisable() { if (Active == this) Active = null; }

    void Reset() { vcam = GetComponent<CinemachineCamera>(); }

    void Awake()
    {
        Resolve();
        baseYawSm = baseYaw;
        pitchSmooth = basePitch;
        if (orbital)
        {
            ConfigureAxes();
            if (orbital.Radius > 0.5f) zoomDistance = orbital.Radius;
            ApplyToCinemachine();
        }
        controlYaw = CameraYaw;
    }

    void Update()
    {
        if (!orbital || !focus) { Resolve(); if (!orbital || !focus) return; }
        // keep the orbital's pitch range/center and horizontal wrap in sync with basePitch/pitchLimitMin/Max
        // every frame so runtime/Inspector tuning takes effect immediately.
        ConfigureAxes();

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        HandleRotateInput();  shoulderBlend=Mathf.Lerp(shoulderBlend,shoulderView?1:0,1-Mathf.Exp(-6*dt)); float lu=autoShoulder?Mathf.SmoothStep(0,1,Mathf.InverseLerp(autoShoulderStartPitch,autoShoulderFullPitch,basePitch)):0; lookUpBlend=Mathf.Lerp(lookUpBlend,lu,1-Mathf.Exp(-5*dt)); pitchSmooth=Mathf.SmoothDamp(pitchSmooth,basePitch,ref pitchVelocity,.12f,Mathf.Infinity,dt);

        if (dt > 0f)
            baseYawSm = Mathf.SmoothDampAngle(baseYawSm, baseYaw, ref baseYawVel, Mathf.Max(0.01f, rotateSmoothTime), Mathf.Infinity, dt);

        UpdateZonePose(dt);
        UpdateProfile(dt);
        ApplyToCinemachine();
    }

    // ================================================================== setup helpers

    void Resolve()
    {
        if (!vcam) vcam = GetComponent<CinemachineCamera>();
        if (vcam)
        {
            if (!orbital) orbital = vcam.GetComponent<CinemachineOrbitalFollow>();
            if (!focus) focus = vcam.Follow;
        }
        if (focus && !avatarRoot)
        {
            var cc = focus.GetComponentInParent<CharacterController>();
            avatarRoot = cc ? cc.transform : focus.root;
        }
    }

    void ConfigureAxes()
    {
        if (!orbital) return;
        orbital.HorizontalAxis.Range = new Vector2(-180f, 180f);
        orbital.HorizontalAxis.Wrap = true;
        orbital.VerticalAxis.Range = new Vector2(pitchLimitMin, pitchLimitMax);
        orbital.VerticalAxis.Center = basePitch;
        orbital.VerticalAxis.Wrap = false;
    }

    // ================================================================== input

    void HandleRotateInput()
    {
        if (!allowMouseOrbit) return;
        var ms = Mouse.current;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (ms != null)
        {
            Vector2 delta = ms.delta.ReadValue();
            if (verticalOrbit) basePitch = Mathf.Clamp(basePitch-delta.y*mouseYawSensitivity,pitchLimitMin,pitchLimitMax);
            baseYaw = Mathf.Repeat(baseYaw+delta.x*mouseYawSensitivity,360f);
        }

        var gp = Gamepad.current;
        if (gp != null)
        {
            float stick = gp.rightStick.ReadValue().x; if(verticalOrbit)basePitch=Mathf.Clamp(basePitch-gp.rightStick.ReadValue().y*gamepadYawSpeed*Time.deltaTime,pitchLimitMin,pitchLimitMax);
            if (Mathf.Abs(stick) > 0.05f)
                baseYaw = Mathf.Repeat(baseYaw + stick * gamepadYawSpeed * Time.deltaTime, 360f);
        }
    }

    // ================================================================== zones

    /// <summary>Recomputes the current zone's desired pose (if any) and blends zoneBlend toward
    /// 1 when a zone is active, 0 when none is - so leaving a zone eases back to the plain iso
    /// base view using the same style of smoothing as everything else in this file.</summary>
    void UpdateZonePose(float dt)
    {
        var zone = CurrentZone();
        float targetBlend = zone ? 1f : 0f;
        float blendTime = zone ? zone.blendTime : (activeZones.Count == 0 && lastZone ? lastZone.blendTime : 0.6f);
        if (zone) lastZone = zone;

        if (zone)
        {
            float desiredYaw;
            float avatarFacingYaw = avatarRoot ? avatarRoot.eulerAngles.y : baseYawSm;

            if (zone.mode == CameraZoneTrigger.Mode.DynamicSide)
            {
                // continuously tracks 90 degrees off the player's CURRENT facing, every frame - genuinely
                // dynamic rather than a one-time snap, so turning on the bridge keeps the profile shot framed.
                float side = zone.sideIsPlayerLeft ? -90f : 90f;
                desiredYaw = avatarFacingYaw + side;
            }
            else // LevelFront: face the player head-on, cinematic 'looking at them' angle
            {
                desiredYaw = avatarFacingYaw + 180f;
            }

            float desiredPitch = zone.pitch;
            float desiredDist = zone.distance > 0.01f ? zone.distance : zoomDistance;

            // shortest-path blend for yaw (wrap-aware), plain SmoothDamp for pitch/distance
            zoneYaw = SmoothDampAngleTo(zoneYaw, desiredYaw, ref zoneYawVel, blendTime, dt);
            zonePitch = Mathf.SmoothDamp(zonePitch, desiredPitch, ref zonePitchVel, Mathf.Max(0.01f, blendTime), Mathf.Infinity, dt);
            zoneDist = Mathf.SmoothDamp(zoneDist, desiredDist, ref zoneDistVel, Mathf.Max(0.01f, blendTime), Mathf.Infinity, dt);
        }

        zoneBlend = Mathf.SmoothDamp(zoneBlend, targetBlend, ref zoneBlendVel, Mathf.Max(0.01f, blendTime), Mathf.Infinity, dt);
    }

    static float SmoothDampAngleTo(float current, float target, ref float vel, float time, float dt)
    {
        return Mathf.SmoothDampAngle(current, target, ref vel, Mathf.Max(0.01f, time), Mathf.Infinity, dt);
    }

    // ================================================================== output

    void ApplyToCinemachine()
    {
        float isoYaw = CameraYaw;
        float isoPitch = Mathf.Lerp(Mathf.Clamp(pitchSmooth, pitchLimitMin, pitchLimitMax), 42f, OverviewBlend);
        float sb=(1-OverviewBlend)*(1-FirstPersonBlend);
        float isoDist = zoomDistance;
        orbital.TargetOffset = new Vector3(shoulderSide*sb,.15f*sb,0); ApplyLookUpAim();

        if (zoneBlend > 0.001f)
        {
            float t = zoneBlend*(1-FirstPersonBlend);
            orbital.HorizontalAxis.Value = Mathf.LerpAngle(isoYaw, zoneYaw, t);
            orbital.VerticalAxis.Value = Mathf.Lerp(isoPitch, zonePitch, t); // pitch intentionally allowed outside pitchLimitMin/Max while blended into a zone (e.g. level-front shots can be lower than the normal iso clamp)
            orbital.Radius = Mathf.Lerp(isoDist, zoneDist, t);
        }
        else
        {
            orbital.HorizontalAxis.Value = isoYaw;
            orbital.VerticalAxis.Value = isoPitch;
            orbital.Radius = isoDist;
        }
        ApplyProfile();
    }

    void ApplyLookUpAim()
    {
        if (!composer && vcam) composer = vcam.GetComponent<CinemachineRotationComposer>();
        if (!composer) return;
        if (!composerCached) { composerBaseScreen = composer.Composition.ScreenPosition; composerCached = true; }
        var c = composer.Composition;
        c.ScreenPosition = new Vector2(Mathf.Lerp(composerBaseScreen.x,-.13f,(1-OverviewBlend)*(1-FirstPersonBlend)), Mathf.Lerp(composerBaseScreen.y, Mathf.Clamp(lookUpAimY,-.4f,.4f), lookUpBlend*(1-zoneBlend)*(1-OverviewBlend)*(1-FirstPersonBlend)));
        composer.Composition = c;
    }

    void ApplyProfile()
    {
        var p = lastProfile; if (p == null || profileW <= .0001f) return;
        float w = Mathf.SmoothStep(0, 1, profileW);
        orbital.VerticalAxis.Value = Mathf.Lerp(orbital.VerticalAxis.Value, p.pitch, w);
        orbital.Radius = Mathf.Lerp(orbital.Radius, p.distance, w);
        if (!float.IsNaN(p.yaw)) orbital.HorizontalAxis.Value = Mathf.LerpAngle(orbital.HorizontalAxis.Value, profileYawSm, w);
    }

    static float Wrap180(float a) { return Mathf.Repeat(a + 180f, 360f) - 180f; }

    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || !focus) return;
        Vector3 f = focus.position;
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawLine(f, transform.position);
    }
}


