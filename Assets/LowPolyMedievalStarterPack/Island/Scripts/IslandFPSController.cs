using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Standard first-person controller for exploring the island.
///   WASD move · Mouse look · Shift sprint · C (or Ctrl) crouch · Space jump · Right mouse = zoom
///   F toggles fly mode (Space up, C down, Shift boost, mouse wheel = speed) for inspecting the world.
///   Esc releases the cursor, click to capture it again. Gamepad is supported as well.
/// This component owns the camera's field of view and near clip plane.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class IslandFPSController : MonoBehaviour
{
    public Transform cam;

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

    [Header("Fly mode (toggle with F)")]
    public float flySpeed = 25f;
    public float flyBoostMultiplier = 4f;

    [Header("Look")]
    public float lookSensitivity = 0.1f;      // degrees per mouse pixel
    public float gamepadLookSpeed = 160f;     // degrees per second at full stick
    public bool invertY = false;

    [Header("Camera")]
    [Range(60f, 120f)] public float horizontalFov = 90f;   // kept constant across aspect ratios
    [Range(10f, 60f)] public float zoomFov = 30f;
    public float sprintFovBoost = 6f;
    public float nearClip = 0.2f;

    [Header("Respawn")]
    public float respawnBelowY = -3f;         // fell/walked too deep into the sea -> back to spawn

    [Header("HUD")]
    public bool showHints = true;

    CharacterController cc;
    Camera cameraComp;
    float pitch, vy, fovCurrent;
    Vector3 hVel, flyVel;
    bool flying, crouching, sprinting, wantLock = true;
    int skipLookFrames;
    float standHeight, bottomY, eyeStand, eyeCrouch;
    Vector3 startPos; float startYaw, startPitch;
    GUIStyle style;

    // Double-jump fly toggle: tracks time since the last Space press so a
    // second press in the air within the window toggles fly mode.
    const float kDoubleJumpWindow = 0.4f;
    float timeSinceLastJump = float.MaxValue;
    bool wasGroundedLastFrame = true;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!cam) { var c = GetComponentInChildren<Camera>(); if (c) cam = c.transform; }
        cameraComp = cam ? cam.GetComponent<Camera>() : null;

        cc.minMoveDistance = 0f;
        standHeight = cc.height;
        bottomY = cc.center.y - cc.height * 0.5f;
        crouchHeight = Mathf.Min(crouchHeight, standHeight - 0.2f);
        eyeStand = cam ? cam.localPosition.y : 1.65f;
        eyeCrouch = eyeStand - (standHeight - crouchHeight);

        startPos = transform.position;
        startYaw = transform.eulerAngles.y;
        if (cam) { startPitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x); pitch = startPitch; }
        fovCurrent = horizontalFov;
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

    void Update()
    {
        if (!cam) return;
        var kb = Keyboard.current; var ms = Mouse.current; var gp = Gamepad.current;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        // ---- cursor capture: Esc releases, click recaptures
        if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        else if (ms != null && ms.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        // ---- gather input
        Vector2 move = Vector2.zero;
        bool sprintHeld = false, jumpPressed = false, jumpHeld = false, crouchHeld = false, flyToggle = false, zoomHeld = false;
        float scroll = 0f;
        Vector2 look = Vector2.zero;

        if (kb != null)
        {
            move.x += (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            move.y += (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            sprintHeld |= kb.leftShiftKey.isPressed;
            jumpPressed |= kb.spaceKey.wasPressedThisFrame;
            jumpHeld |= kb.spaceKey.isPressed;
            crouchHeld |= kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
            flyToggle |= kb.fKey.wasPressedThisFrame;
        }
        if (ms != null && locked)
        {
            if (skipLookFrames <= 0) look += ms.delta.ReadValue() * lookSensitivity;
            scroll = ms.scroll.ReadValue().y;
            zoomHeld |= ms.rightButton.isPressed;
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
            flyToggle |= gp.buttonNorth.wasPressedThisFrame;
            zoomHeld |= gp.leftTrigger.ReadValue() > 0.5f;
        }
        move = Vector2.ClampMagnitude(move, 1f);

        // ---- look (sensitivity scales down while zoomed)
        look *= Mathf.Clamp(fovCurrent / horizontalFov, 0.15f, 1f);
        transform.Rotate(0f, look.x, 0f, Space.World);
        pitch = Mathf.Clamp(pitch + (invertY ? look.y : -look.y), -89f, 89f);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // ---- body / movement
        if (flyToggle) SetFlying(!flying);
        UpdateCrouch(crouchHeld, dt);
        if (flying) UpdateFly(move, jumpHeld, crouchHeld, sprintHeld, scroll, dt);
        else UpdateWalk(move, sprintHeld, jumpPressed, dt);

        if (!flying && transform.position.y < respawnBelowY) Teleport(startPos, startYaw);

        UpdateCamera(zoomHeld, dt);
    }

    void SetFlying(bool on)
    {
        flying = on; vy = 0f; hVel = Vector3.zero; flyVel = Vector3.zero;
        timeSinceLastJump = float.MaxValue; // reset double-jump window
        if (on) crouching = false;
    }

    void UpdateWalk(Vector2 move, bool sprintHeld, bool jumpPressed, float dt)
    {
        bool grounded = cc.isGrounded;
        sprinting = sprintHeld && !crouching && move.y > 0.1f;
        float speed = crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed;

        Vector3 wish = (transform.right * move.x + transform.forward * move.y) * speed;
        float accel = grounded ? groundAcceleration : airAcceleration;
        hVel = Vector3.Lerp(hVel, wish, 1f - Mathf.Exp(-accel * dt));

        timeSinceLastJump += dt;

        if (jumpPressed)
        {
            if (grounded && !crouching)
            {
                // Normal jump — record the time so a second press can double-jump.
                vy = Mathf.Sqrt(2f * jumpHeightMeters * -gravityMps2);
                timeSinceLastJump = 0f;
            }
            else if (!grounded && timeSinceLastJump <= kDoubleJumpWindow)
            {
                // Second press while airborne within the window → enter fly mode.
                SetFlying(true);
                return;
            }
        }

        wasGroundedLastFrame = grounded;

        // keep glued to the ground when walking downhill instead of hopping off the slope
        if (grounded && vy < 0f)
            vy = -Mathf.Max(2f, hVel.magnitude * Mathf.Tan(cc.slopeLimit * Mathf.Deg2Rad));
        vy = Mathf.Max(vy + gravityMps2 * dt, -maxFallSpeed);

        cc.Move((hVel + Vector3.up * vy) * dt);
        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;   // bonked a ceiling
    }

    void UpdateFly(Vector2 move, bool up, bool down, bool boost, float scroll, float dt)
    {
        sprinting = false;
        if (Mathf.Abs(scroll) > 0.01f)
            flySpeed = Mathf.Clamp(flySpeed * (scroll > 0f ? 1.2f : 1f / 1.2f), 2f, 500f);

        Vector3 dir = cam.forward * move.y + cam.right * move.x + Vector3.up * ((up ? 1f : 0f) - (down ? 1f : 0f));
        dir = Vector3.ClampMagnitude(dir, 1f);
        float speed = flySpeed * (boost ? flyBoostMultiplier : 1f);
        flyVel = Vector3.Lerp(flyVel, dir * speed, 1f - Mathf.Exp(-10f * dt));
        cc.Move(flyVel * dt);
    }

    void UpdateCrouch(bool crouchHeld, float dt)
    {
        if (flying) crouching = false;
        else if (crouchHeld) crouching = true;
        else if (crouching && CanStand()) crouching = false;

        float targetH = crouching ? crouchHeight : standHeight;
        float h = Mathf.MoveTowards(cc.height, targetH, 5f * dt);
        if (!Mathf.Approximately(h, cc.height))
        {
            cc.height = h;
            cc.center = new Vector3(cc.center.x, bottomY + h * 0.5f, cc.center.z);
        }
        float t = Mathf.InverseLerp(crouchHeight, standHeight, cc.height);
        var lp = cam.localPosition; lp.y = Mathf.Lerp(eyeCrouch, eyeStand, t); cam.localPosition = lp;
    }

    bool CanStand()
    {
        float r = cc.radius * 0.95f;
        Vector3 origin = transform.position + Vector3.up * (bottomY + cc.height - r);
        float dist = standHeight - cc.height + 0.05f;
        RaycastHit hit;
        return !Physics.SphereCast(origin, r, Vector3.up, out hit, dist, ~0, QueryTriggerInteraction.Ignore);
    }

    void UpdateCamera(bool zoomHeld, float dt)
    {
        if (!cameraComp) return;
        float target = zoomHeld ? zoomFov : horizontalFov + (sprinting ? sprintFovBoost : 0f);
        fovCurrent = Mathf.Lerp(fovCurrent, target, 1f - Mathf.Exp(-10f * dt));

        // Unity wants vertical FOV; derive it from the horizontal FOV so the view stays consistent on any window shape.
        float aspect = Mathf.Max(0.1f, cameraComp.aspect);
        float vfov = 2f * Mathf.Atan(Mathf.Tan(fovCurrent * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        cameraComp.fieldOfView = Mathf.Clamp(vfov, 5f, 100f);
        if (!Mathf.Approximately(cameraComp.nearClipPlane, nearClip)) cameraComp.nearClipPlane = nearClip;
    }

    public void Teleport(Vector3 pos, float yaw)
    {
        cc.enabled = false;                       // CharacterController ignores transform writes while enabled
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        cc.enabled = true;
        vy = 0f; hVel = Vector3.zero; flyVel = Vector3.zero; pitch = startPitch;
        if (cam) cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // ---------------------------------------------------------------- HUD
    void OnGUI()
    {
        if (!showHints) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = true };
        style.fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.022f));
        float w = Screen.width, h = Screen.height, pad = 16f, lh = style.fontSize * 1.7f;
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (!locked)
            DrawLabel(new Rect(0f, h * 0.45f, w, lh * 2f), "Click to capture the mouse\nEsc releases it", TextAnchor.MiddleCenter, 1f);
        if (flying)
            DrawLabel(new Rect(pad, pad, w - 2f * pad, lh), "FLY MODE  " + flySpeed.ToString("0") + " m/s    Space up · C down · Shift boost · Wheel speed · F or double-jump to land", TextAnchor.UpperLeft, 1f);

        float t = Time.timeSinceLevelLoad;
        if (locked && t < 14f)
            DrawLabel(new Rect(pad, h - pad - lh * 1.5f, w - 2f * pad, lh * 1.5f),
                "WASD move · Mouse look · Shift sprint · C crouch · Space jump · RMB zoom · F fly · Esc free cursor",
                TextAnchor.LowerLeft, Mathf.Clamp01((14f - t) / 2f));
    }

    void DrawLabel(Rect r, string text, TextAnchor anchor, float alpha)
    {
        style.alignment = anchor;
        var c = Color.black; c.a = 0.75f * alpha;
        style.normal.textColor = c;
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        c = Color.white; c.a = alpha;
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }
}
