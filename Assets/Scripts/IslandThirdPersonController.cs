using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Third-person controller for exploring the island, driven through a Cinemachine orbital-follow camera.
///   WASD move (relative to the camera) · Shift run · Space jump / hold to float · C / Ctrl crouch
///   Mouse orbits the camera around the avatar · Mouse wheel changes camera distance
///   Esc releases the cursor, click captures it again. Gamepad is supported as well.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class IslandThirdPersonController : MonoBehaviour
{
    [Header("Camera rig")]
    public CinemachineCamera vcam;          // the Cinemachine follow camera
    public Transform cameraTarget;          // point the camera is locked onto (created at chest height if empty)
    public Transform cam;                   // Main Camera transform (movement is relative to it)

    [Header("Isometric rig (optional)")]
    [Tooltip("When assigned, IsoCameraRig owns the camera yaw / pitch / radius and movement uses its latched heading. Leave empty for the classic mouse-orbit camera.")]
    public IsoCameraRig isoRig;

    [Header("Movement (m/s)")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float crouchSpeed = 2.5f;
    public float groundAcceleration = 14f;
    public float airAcceleration = 3f;
    public float jumpHeightMeters = 1.1f;
    public float gravityMps2 = -22f;
    [Tooltip("Scales gravity (StarRadiance lightens it for a Tether-aligned star).")]
    public float gravityScale = 1f;
    [Tooltip("Story hook: multiplies walk/sprint speed (StarAwakening ramps this up after the impact).")]
    [Range(0f, 1.5f)] public float speedScale = 1f;
    [Tooltip("Story hook: temporary cap near fragile moments (ChildConversation slows the star near the child).")]
    [Range(0f, 1.5f)] public float speedCap = 1f;
    public float maxFallSpeed = 60f;

    [Header("Star float (tap Space = hop, hold Space = float)")]
    public bool floatEnabled = true;
    [Tooltip("How long Space must be held before the hop turns into a float (the hop's apex is ~0.3 s, so it flows on from it).")]
    public float holdToFloatSeconds = 0.3f;
    public float floatRiseSpeed = 3.4f;
    [Tooltip("Hard ceiling, metres above the terrain.")]
    public float floatMaxHeight = 24f;
    public float floatBob = 0.35f;
    public float floatAcceleration = 5f;
    [Tooltip("Story hook: set by StarRadiance from the alignment spectrum. >1 raises the float ceiling and rise speed (Tether); <1 lowers them (Roots). Float itself always stays available on both paths.")]
    public float floatReachScale = 1f;
    [Tooltip("After letting go of a float the star drifts down at no more than this (m/s).")]
    public float softFallSpeed = 5f;

    public bool IsFloating { get; private set; }
    public float FloatHeight { get; private set; }
    public int CameraMode { get; private set; } = 2;

    [Header("Crouch & turning")]
    public float crouchHeight = 1.1f;
    public float turnSpeedDegPerSec = 720f; // how fast the avatar turns to face its movement direction

    [Header("Camera orbit")]
    public float lookSensitivity = 0.1f;    // degrees per mouse pixel
    public float gamepadLookSpeed = 160f;   // degrees per second at full stick
    public bool invertY = false;
    [Range(5f, 85f)] public float minPitch = 22f;     // camera elevation above the avatar, in degrees
    [Range(5f, 85f)] public float maxPitch = 70f;
    [Range(5f, 85f)] public float startPitch = 44f;
    public bool lockPitch = true;           // true = mouse/stick only revolves the camera horizontally; vertical angle stays at startPitch

    [Header("Camera distance / lens")]
    public float distance = 20f;
    public float minDistance = 0.02f;
    public float maxDistance = 38f;
    public float zoomStep = 1.12f;          // distance multiplier per mouse-wheel notch
    public float distanceSmoothing = 8f;
    public float thirdPersonDistance = 12f;
    public float overviewDistance = 34f;
    public float distanceSmoothTime = 0.6f;
    [Range(10f, 70f)] public float verticalFov = 30f;  // narrow lens = flat, miniature look
    public float sprintFovBoost = 2.5f;
    public float nearClip = 0.05f;
    public float farClip = 4000f;
    public float targetHeight = 1.1f;       // height of the camera lock point above the avatar's feet

    [Header("Avatar")]
    public bool showAvatar = false;         // flip on once there is a model; the camera doesn't care

    [Header("Respawn")]
    public float respawnBelowY = -3f;

    CharacterController cc;
    CinemachineOrbitalFollow orbital;
    float camYaw, camPitch, vy, distCurrent, distTarget, fovCurrent, distVel;
    Vector3 hVel;
    bool crouching, sprinting, wantLock = true;
    int skipLookFrames;
    float standHeight, bottomY, targetStand, targetCrouch;
    Vector3 startPos; float startYaw;
    float heldSeconds, floatBegan; bool softFall;
    float modePitchGoal, modePitchVel, modePitchTimer;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        cc.minMoveDistance = 0f;
        standHeight = cc.height;
        bottomY = cc.center.y - cc.height * 0.5f;
        crouchHeight = Mathf.Min(crouchHeight, standHeight - 0.2f);

        if (!cameraTarget)
        {
            var t = transform.Find("CameraTarget");
            if (!t) { t = new GameObject("CameraTarget").transform; t.SetParent(transform, false); }
            cameraTarget = t;
        }
        targetStand = targetHeight;
        targetCrouch = targetHeight - (standHeight - crouchHeight);
        cameraTarget.localPosition = new Vector3(0f, targetStand, 0f);

        if (!cam && Camera.main) cam = Camera.main.transform;
        if (vcam)
        {
            orbital = vcam.GetComponent<CinemachineOrbitalFollow>();
            if (lockPitch && !isoRig && orbital)
            {
                orbital.VerticalAxis.Range = new Vector2(startPitch, startPitch);
                orbital.VerticalAxis.Center = startPitch;
                orbital.VerticalAxis.Wrap = false;
            }
            vcam.Lens.NearClipPlane = nearClip;
            vcam.Lens.FarClipPlane = farClip;
        }
        else Debug.LogWarning("[IslandThirdPersonController] No Cinemachine camera assigned.", this);

        startPos = transform.position;
        startYaw = transform.eulerAngles.y;
        camYaw = startYaw;
        camPitch = startPitch;
        distCurrent = distTarget = Mathf.Clamp(distance, minDistance, maxDistance);
        fovCurrent = verticalFov;

        ApplyAvatarVisibility();
        ApplyCamera(0f);
        SetCursorLocked(true);
    }

    void Start()
    {
        // The terrain is generated at runtime; if the spawn point ended up underground, lift the player onto the surface.
        var hits = Physics.RaycastAll(startPos + Vector3.up * 1500f, Vector3.down, 3000f, ~0, QueryTriggerInteraction.Ignore);
        float ground = float.NegativeInfinity;
        foreach (var h in hits)
            if (h.collider.gameObject != gameObject && h.point.y > ground) ground = h.point.y;
        if (ground > float.NegativeInfinity && startPos.y < ground)
        {
            startPos.y = ground + 0.1f;
            Teleport(startPos, startYaw);
        }
        ApplyAvatarVisibility();
    }

    void OnApplicationFocus(bool focus) { if (focus && wantLock) SetCursorLocked(true); }
    void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    public void CaptureCursor() => SetCursorLocked(true);
    public void ReleaseCursor() => SetCursorLocked(false);

    void SetCursorLocked(bool locked)
    {
        wantLock = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        skipLookFrames = 2;   // swallow the mouse-delta spike that comes with (un)locking
    }

    public void SetAvatarVisible(bool visible) { showAvatar = visible; ApplyAvatarVisibility(); }

    void ApplyAvatarVisibility()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = showAvatar;
    }

    void Update()
    {
        var kb = Keyboard.current; var ms = Mouse.current; var gp = Gamepad.current;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        // ---- cursor capture: Esc releases, click recaptures
        if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        else if (ms != null && ms.leftButton.wasPressedThisFrame && !LivingDayNight.PointerOverPanel && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // ---- gather input
        Vector2 move = Vector2.zero, look = Vector2.zero;
        bool sprintHeld = false, jumpPressed = false, jumpHeld = false, crouchHeld = false;
        float scroll = 0f;

        if (kb != null)
        {
            move.x += (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            move.y += (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            sprintHeld |= kb.leftShiftKey.isPressed;
            jumpPressed |= kb.spaceKey.wasPressedThisFrame;
            jumpHeld |= kb.spaceKey.isPressed;
            crouchHeld |= kb.cKey.isPressed || kb.leftCtrlKey.isPressed;

            if (kb.digit1Key.wasPressedThisFrame) SetCameraMode(1);
            else if (kb.digit2Key.wasPressedThisFrame) SetCameraMode(2);
            else if (kb.digit3Key.wasPressedThisFrame) SetCameraMode(3);
        }
        if (ms != null && locked)
        {
            if (skipLookFrames <= 0) look += ms.delta.ReadValue() * lookSensitivity;
            scroll = ms.scroll.ReadValue().y;
        }
        if (skipLookFrames > 0) skipLookFrames--;
        if (gp != null)
        {
            move += gp.leftStick.ReadValue();
            look += gp.rightStick.ReadValue() * gamepadLookSpeed * dt;
            sprintHeld |= gp.leftStickButton.isPressed;
            jumpPressed |= gp.buttonSouth.wasPressedThisFrame;
            jumpHeld |= gp.buttonSouth.isPressed;
            crouchHeld |= gp.buttonEast.isPressed;
        }
        move = Vector2.ClampMagnitude(move, 1f);

        // ---- camera orbit + zoom
        if (!isoRig)
            camYaw = Mathf.Repeat(camYaw + look.x + 180f, 360f) - 180f;

        if (!lockPitch) camPitch = Mathf.Clamp(camPitch + (invertY ? look.y : -look.y), minPitch, maxPitch);
        else camPitch = startPitch;

        // scroll-wheel zoom removed on purpose: camera distance only changes through the 1 / 2 / 3 camera modes

        if (isoRig && modePitchTimer > 0f)
        {
            modePitchTimer -= dt;
            isoRig.basePitch = Mathf.SmoothDamp(isoRig.basePitch, modePitchGoal, ref modePitchVel, 0.45f, float.PositiveInfinity, dt);
        }

        // ---- body / movement (camera-relative)
        UpdateCrouch(crouchHeld, dt);
        Vector3 fwd = isoRig ? isoRig.ControlForward(move.sqrMagnitude > 0.0004f) : (cam ? cam.forward : transform.forward);
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 wishDir = fwd * move.y + right * move.x;
        UpdateWalk(wishDir, sprintHeld, jumpPressed, jumpHeld, dt);

        if (transform.position.y < respawnBelowY) Teleport(startPos, startYaw);

        ApplyCamera(dt);
    }

    void UpdateWalk(Vector3 wishDir, bool sprintHeld, bool jumpPressed, bool jumpHeld, float dt)
    {
        bool grounded = cc.isGrounded;
        float inputMag = Mathf.Clamp01(wishDir.magnitude);
        sprinting = sprintHeld && !crouching && inputMag > 0.1f;
        UpdateFloat(jumpHeld, grounded, dt);

        float speed = (crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed) * speedScale * speedCap;
        Vector3 wish = wishDir * speed;
        float accel = IsFloating ? floatAcceleration : (grounded ? groundAcceleration : airAcceleration);
        hVel = Vector3.Lerp(hVel, wish, 1f - Mathf.Exp(-accel * dt));

        // face the direction of travel
        if (inputMag > 0.1f)
        {
            var targetRot = Quaternion.LookRotation(wishDir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDegPerSec * dt);
        }

        if (jumpPressed && grounded && !crouching)
            vy = Mathf.Sqrt(2f * jumpHeightMeters * (-gravityMps2) * gravityScale);

        if (IsFloating)
        {
            float ceiling = floatMaxHeight * floatReachScale;
            float rise = floatRiseSpeed * Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(0.4f, 1.6f, floatReachScale));
            float targetVy = FloatHeight < ceiling - 0.5f ? rise : (ceiling - FloatHeight) * 2f;
            targetVy += Mathf.Sin((Time.time - floatBegan) * 1.7f) * floatBob;
            vy = Mathf.Lerp(vy, targetVy, 1f - Mathf.Exp(-3.2f * dt));
        }
        else
        {
            // keep glued to the ground when walking downhill instead of hopping off the slope
            if (grounded && vy < 0f)
                vy = -Mathf.Max(2f, hVel.magnitude * Mathf.Tan(cc.slopeLimit * Mathf.Deg2Rad));
            vy = Mathf.Max(vy + gravityMps2 * gravityScale * dt, -(softFall ? softFallSpeed : maxFallSpeed));
        }

        cc.Move((hVel + Vector3.up * vy) * dt);
        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;   // bonked a ceiling
    }

    void UpdateFloat(bool jumpHeld, bool grounded, float dt)
    {
        bool canFloat = floatEnabled && !crouching && !WishboundIntro.IsPlaying && (!HouseEntrances.Instance || !HouseEntrances.Instance.Inside);
        heldSeconds = jumpHeld ? heldSeconds + dt : 0f;
        if (!canFloat || !jumpHeld)
        {
            if (IsFloating)
            {
                IsFloating = false;
                softFall = true;
            }
        }
        else if (!IsFloating && heldSeconds >= holdToFloatSeconds)
        {
            IsFloating = true;
            floatBegan = Time.time;
        }
        if (grounded && !IsFloating)
        {
            softFall = false;
        }
        FloatHeight = (IsFloating || softFall) ? Mathf.Max(0f, transform.position.y - TerrainProbe.GroundY(transform.position, transform.position.y)) : 0f;
    }

    void UpdateCrouch(bool crouchHeld, float dt)
    {
        if (crouchHeld) crouching = true;
        else if (crouching && CanStand()) crouching = false;

        float targetH = crouching ? crouchHeight : standHeight;
        float h = Mathf.MoveTowards(cc.height, targetH, 5f * dt);
        if (!Mathf.Approximately(h, cc.height))
        {
            cc.height = h;
            cc.center = new Vector3(cc.center.x, bottomY + h * 0.5f, cc.center.z);
        }
        float t = Mathf.InverseLerp(crouchHeight, standHeight, cc.height);
        var lp = cameraTarget.localPosition; lp.y = Mathf.Lerp(targetCrouch, targetStand, t); cameraTarget.localPosition = lp;
    }

    bool CanStand()
    {
        float r = cc.radius * 0.95f;
        Vector3 origin = transform.position + Vector3.up * (bottomY + cc.height - r);
        float dist = standHeight - cc.height + 0.05f;
        return !Physics.SphereCast(origin, r, Vector3.up, out _, dist, ~0, QueryTriggerInteraction.Ignore);
    }

public void SetCameraMode(int mode)
    {
        CameraMode = mode;
        IslandCameraFX.TiltGoal = mode switch { 1 => 0.5f, 2 => 1f, _ => 1.8f };   // eased in IslandCameraFX: subtle in 1st person, strongest in the high overview
        distTarget = mode switch
        {
            2 => thirdPersonDistance,
            1 => minDistance,
            _ => Mathf.Min(overviewDistance, maxDistance),
        };
        if (isoRig)
        {
            // every mode eases to its own pitch (1 = eye level, 2 = over-the-shoulder, 3 = high miniature overview)
            modePitchGoal = mode switch { 1 => 0f, 2 => 22f, _ => 52f };
            modePitchTimer = 1.4f;
            modePitchVel = 0f;
        }
    }

    public void PrepareThirdPerson()
    {
        CameraMode = 2; IslandCameraFX.TiltGoal = 1f;
        distTarget = distCurrent = thirdPersonDistance;
        distVel = 0f;
        if (isoRig)
        {
            isoRig.basePitch = 22f;
            isoRig.SetZoomDistance(distCurrent);
        }
        ApplyCamera(0f);
    }

    public void PrepareFirstPerson()
    {
        distTarget = distCurrent = minDistance;
        if (isoRig)
        {
            isoRig.basePitch = 0f;
            isoRig.SetZoomDistance(minDistance);
        }
        ApplyCamera(0f);
    }

    public void SetCameraDistance(float value)
    {
        distTarget = Mathf.Clamp(value, minDistance, maxDistance);
    }

    void ApplyCamera(float dt)
    {
        if (dt <= 0f)
        {
            distCurrent = distTarget;
            distVel = 0f;
        }
        else
        {
            float logD = Mathf.SmoothDamp(Mathf.Log(distCurrent + 0.8f), Mathf.Log(distTarget + 0.8f), ref distVel, distanceSmoothTime, float.PositiveInfinity, dt);
            distCurrent = Mathf.Max(minDistance, Mathf.Exp(logD) - 0.8f);
        }
        float tDistance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 26f, distCurrent));
        float tFirstPerson = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 1.1f, distCurrent));
        float fovTarget = Mathf.Lerp(Mathf.Lerp(55f, verticalFov, tDistance), 70f, tFirstPerson) + (sprinting ? sprintFovBoost : 0f);
        fovCurrent = dt <= 0f ? fovTarget : Mathf.Lerp(fovCurrent, fovTarget, 1f - Mathf.Exp(-6f * dt));

        if (isoRig)
        {
            isoRig.SetZoomDistance(distCurrent);
        }
        else if (orbital)
        {
            orbital.HorizontalAxis.Value = camYaw;
            orbital.VerticalAxis.Value = camPitch;
            orbital.Radius = distCurrent;
        }

        if (vcam)
        {
            vcam.Lens.FieldOfView = isoRig ? isoRig.ProfileFov(fovCurrent) : fovCurrent;
            vcam.Lens.NearClipPlane = Mathf.Lerp(0.05f, nearClip, tDistance);
        }
    }

    public void Teleport(Vector3 pos, float yaw)
    {
        Vector3 delta = pos - transform.position;
        cc.enabled = false;                       // CharacterController ignores transform writes while enabled
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        cc.enabled = true;
        vy = 0f; hVel = Vector3.zero;
        if (isoRig) isoRig.OnTargetWarped();
        else camYaw = yaw;
        if (vcam && cameraTarget) vcam.OnTargetObjectWarped(cameraTarget, delta);   // no camera swoosh after a teleport
    }
}
