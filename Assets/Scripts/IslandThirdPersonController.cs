using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Third-person controller for exploring the island, driven through a Cinemachine orbital-follow camera.
///   WASD move (relative to the camera) · Shift run · Space jump · C / Ctrl crouch
///   Mouse orbits the camera around the avatar · Mouse wheel changes camera distance
///   Esc releases the cursor, click captures it again. Gamepad is supported as well.
/// The avatar is invisible by default (showAvatar); the camera stays locked onto it regardless.
/// This component owns the Cinemachine camera's lens (vertical FOV, clip planes) and orbit angles.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class IslandThirdPersonController : MonoBehaviour
{
    [Header("Camera rig")]
    public CinemachineCamera vcam;          // the Cinemachine follow camera
    public Transform cameraTarget;          // point the camera is locked onto (created at chest height if empty)
    public Transform cam;                   // Main Camera transform (movement is relative to it)

    [Header("Movement (m/s)")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float crouchSpeed = 2.5f;
    public float groundAcceleration = 14f;
    public float airAcceleration = 3f;
    public float jumpHeightMeters = 1.1f;
    public float gravityMps2 = -22f;
    public float maxFallSpeed = 60f;
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
    public float minDistance = 10f;
    public float maxDistance = 38f;
    public float zoomStep = 1.12f;          // distance multiplier per mouse-wheel notch
    public float distanceSmoothing = 8f;
    [Range(10f, 70f)] public float verticalFov = 30f;  // narrow lens = flat, miniature look
    public float sprintFovBoost = 2.5f;
    public float nearClip = 1f;
    public float farClip = 4000f;
    public float targetHeight = 1.1f;       // height of the camera lock point above the avatar's feet

    [Header("Avatar")]
    public bool showAvatar = false;         // flip on once there is a model; the camera doesn't care

    [Header("Respawn")]
    public float respawnBelowY = -3f;

    CharacterController cc;
    CinemachineOrbitalFollow orbital;
    float camYaw, camPitch, vy, distCurrent, distTarget, fovCurrent;
    Vector3 hVel;
    bool crouching, sprinting, wantLock = true;
    int skipLookFrames;
    float standHeight, bottomY, targetStand, targetCrouch;
    Vector3 startPos; float startYaw;

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
            if (lockPitch) { orbital.VerticalAxis.Range = new Vector2(startPitch, startPitch); orbital.VerticalAxis.Center = startPitch; orbital.VerticalAxis.Wrap = false; }
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
        else if (ms != null && ms.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // ---- gather input
        Vector2 move = Vector2.zero, look = Vector2.zero;
        bool sprintHeld = false, jumpPressed = false, crouchHeld = false;
        float scroll = 0f;

        if (kb != null)
        {
            move.x += (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            move.y += (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            sprintHeld |= kb.leftShiftKey.isPressed;
            jumpPressed |= kb.spaceKey.wasPressedThisFrame;
            crouchHeld |= kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
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
            crouchHeld |= gp.buttonEast.isPressed;
        }
        move = Vector2.ClampMagnitude(move, 1f);

        // ---- camera orbit + zoom
        camYaw = Mathf.Repeat(camYaw + look.x + 180f, 360f) - 180f;
        if (!lockPitch) camPitch = Mathf.Clamp(camPitch + (invertY ? look.y : -look.y), minPitch, maxPitch);
        else camPitch = startPitch;   // pitch locked: vertical input ignored
        if (Mathf.Abs(scroll) > 0.01f)
            distTarget = Mathf.Clamp(distTarget * (scroll > 0f ? 1f / zoomStep : zoomStep), minDistance, maxDistance);

        // ---- body / movement (camera-relative)
        UpdateCrouch(crouchHeld, dt);
        Vector3 fwd = cam ? cam.forward : transform.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 wishDir = fwd * move.y + right * move.x;
        UpdateWalk(wishDir, sprintHeld, jumpPressed, dt);

        if (transform.position.y < respawnBelowY) Teleport(startPos, startYaw);

        ApplyCamera(dt);
    }

    void UpdateWalk(Vector3 wishDir, bool sprintHeld, bool jumpPressed, float dt)
    {
        bool grounded = cc.isGrounded;
        float inputMag = Mathf.Clamp01(wishDir.magnitude);
        sprinting = sprintHeld && !crouching && inputMag > 0.1f;
        float speed = crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed;

        Vector3 wish = wishDir * speed;
        float accel = grounded ? groundAcceleration : airAcceleration;
        hVel = Vector3.Lerp(hVel, wish, 1f - Mathf.Exp(-accel * dt));

        // face the direction of travel
        if (inputMag > 0.1f)
        {
            var targetRot = Quaternion.LookRotation(wishDir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDegPerSec * dt);
        }

        if (jumpPressed && grounded && !crouching)
            vy = Mathf.Sqrt(2f * jumpHeightMeters * -gravityMps2);

        // keep glued to the ground when walking downhill instead of hopping off the slope
        if (grounded && vy < 0f)
            vy = -Mathf.Max(2f, hVel.magnitude * Mathf.Tan(cc.slopeLimit * Mathf.Deg2Rad));
        vy = Mathf.Max(vy + gravityMps2 * dt, -maxFallSpeed);

        cc.Move((hVel + Vector3.up * vy) * dt);
        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;   // bonked a ceiling
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

    void ApplyCamera(float dt)
    {
        distCurrent = dt <= 0f ? distTarget : Mathf.Lerp(distCurrent, distTarget, 1f - Mathf.Exp(-distanceSmoothing * dt));
        float fovTarget = verticalFov + (sprinting ? sprintFovBoost : 0f);
        fovCurrent = dt <= 0f ? fovTarget : Mathf.Lerp(fovCurrent, fovTarget, 1f - Mathf.Exp(-6f * dt));

        if (orbital)
        {
            orbital.HorizontalAxis.Value = camYaw;
            orbital.VerticalAxis.Value = camPitch;
            orbital.Radius = distCurrent;
        }
        if (vcam) vcam.Lens.FieldOfView = fovCurrent;
    }

    public void Teleport(Vector3 pos, float yaw)
    {
        Vector3 delta = pos - transform.position;
        cc.enabled = false;                       // CharacterController ignores transform writes while enabled
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        cc.enabled = true;
        vy = 0f; hVel = Vector3.zero;
        camYaw = yaw;
        if (vcam && cameraTarget) vcam.OnTargetObjectWarped(cameraTarget, delta);   // no camera swoosh after a teleport
    }
}
