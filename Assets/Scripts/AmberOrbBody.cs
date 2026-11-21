using UnityEngine;

/// <summary>
/// Procedural, physical-feeling body for the Star orb (visual only - the CharacterController stays the physics authority).
///   * Gait: while moving it hops with a rhythm tied to speed; each footfall squashes, each push-off stretches.
///     Walking is a gentle bob, running squishes harder and leans into the motion.
///   * Inertia: the body trails behind acceleration on a spring and recoils past centre when you stop or turn.
///   * Jump / land: stretches tall on take-off, squashes flat on landing (scaled by impact speed) and wobbles back.
///   * Idle: a slow breathing swell.
/// Deformation is volume-preserving (flattening widens it) and runs on damped springs, so it overshoots and settles.
/// </summary>
public class AmberOrbBody : MonoBehaviour
{
    public Transform body;
    [Min(.1f)] public float diameter = .85f;

    [Header("Gait")]
    public float walkSpeed = 5f, runSpeed = 9f;
    [Tooltip("Hops per second at walking / running speed.")]
    public float walkHopRate = 2.1f, runHopRate = 3.0f;
    public float walkHopHeight = .07f, runHopHeight = .16f;
    [Range(0, .5f)] public float walkSquash = .07f, runSquash = .2f;
    [Tooltip("How far the body leans into its direction of travel (degrees at run speed).")]
    public float runLean = 14f;

    [Header("Springs")]
    public float shapeStiffness = 160f, shapeDamping = 9f;
    public float offsetStiffness = 90f, offsetDamping = 10f;
    [Tooltip("How much acceleration pushes the body off-centre (inertia / recoil).")]
    public float inertia = .012f;

    [Header("Jump & land")]
    public float landSquashPerMps = .035f, maxLandSquash = .45f, takeoffStretch = .18f;

    CharacterController cc;
    Vector3 prevPos, vel, prevVel, rest;
    float squash, squashVel;             // + flattened, - stretched tall
    Vector3 offset, offsetVel;
    float hopPhase, lean, airVy; bool wasGrounded = true;

    void OnEnable()
    {
        if (!body) body = transform.Find("AmberStarBody");
        cc = GetComponentInParent<CharacterController>();
        if (body) rest = body.localPosition;
        prevPos = transform.position; vel = prevVel = Vector3.zero;
        squash = squashVel = 0; offset = offsetVel = Vector3.zero;
    }

    /// <summary>External impulse (e.g. a Starlight Pulse flare): positive stretches tall, then it wobbles back.</summary>
    public void Kick(float amount) { squashVel -= amount * 10f; }

    void LateUpdate()
    {
        if (!body) return;
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0) return;

        Vector3 pos = transform.position;
        Vector3 rawVel = (pos - prevPos) / dt; prevPos = pos;
        if (rawVel.sqrMagnitude > 900) rawVel = vel;                  // teleports don't count as motion
        vel = Vector3.Lerp(vel, rawVel, 1 - Mathf.Exp(-18 * dt));
        Vector3 accel = (vel - prevVel) / dt; prevVel = vel;

        Vector3 flatVel = new Vector3(vel.x, 0, vel.z);
        float speed = flatVel.magnitude;
        float run = Mathf.InverseLerp(walkSpeed * .9f, runSpeed, speed);   // 0 walk .. 1 run
        float moving = Mathf.InverseLerp(.25f, walkSpeed * .6f, speed);     // 0 idle .. 1 moving
        bool grounded = cc ? cc.isGrounded || Physics.Raycast(transform.position + Vector3.up * .2f, Vector3.down, .45f, ~0, QueryTriggerInteraction.Ignore) : true;

        // ---- jump / land impulses
        if (!grounded) airVy = vel.y;
        if (wasGrounded && !grounded && vel.y > 1f) squashVel -= takeoffStretch * 18f;
        if (!wasGrounded && grounded) squashVel += Mathf.Min(maxLandSquash, -airVy * landSquashPerMps) * 22f;
        wasGrounded = grounded;

        // ---- gait: rhythmic hop, sharp squash on each footfall
        float hopRate = Mathf.Lerp(walkHopRate, runHopRate, run) * Mathf.Lerp(.6f, 1f, moving);
        if (moving > .01f) hopPhase += dt * hopRate * Mathf.PI;
        float hop = Mathf.Abs(Mathf.Sin(hopPhase));                         // 0 at footfall, 1 at the top
        float hopH = Mathf.Lerp(walkHopHeight, runHopHeight, run) * moving * (grounded ? 1 : 0);
        float gaitSquash = Mathf.Lerp(walkSquash, runSquash, run) * moving * (grounded ? 1 : 0);
        float targetSquash = gaitSquash * (Mathf.Pow(1 - hop, 4) * 1.6f - .35f);
        targetSquash += Mathf.Sin(Time.time * 2.1f) * .018f * (1 - moving);   // idle breathing

        // ---- squash spring (overshoot = recoil wobble)
        squashVel += ((targetSquash - squash) * shapeStiffness - squashVel * shapeDamping) * dt;
        squash = Mathf.Clamp(squash + squashVel * dt, -.45f, .55f);

        // ---- inertial offset: the body lags acceleration and swings back past centre
        Vector3 targetOffset = Vector3.ClampMagnitude(-accel * inertia, .22f);
        targetOffset.y = Mathf.Clamp(targetOffset.y, -.12f, .12f) + hop * hopH;
        offsetVel += ((targetOffset - offset) * offsetStiffness - offsetVel * offsetDamping) * dt;
        offset += offsetVel * dt;

        // ---- lean into travel
        lean = Mathf.Lerp(lean, runLean * Mathf.Lerp(.35f, 1f, run) * moving, 1 - Mathf.Exp(-8 * dt));

        // ---- apply (volume preserving), in the travel frame, kept planted on the ground while squashing
        float sy = 1 - squash, sxz = 1 / Mathf.Sqrt(Mathf.Max(.2f, sy));
        Vector3 fwd = speed > .1f ? flatVel / speed : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < .01f) fwd = Vector3.forward;
        body.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(lean, 0, 0);
        body.position = transform.TransformPoint(rest) + offset - Vector3.up * (squash * diameter * .5f);
        body.localScale = new Vector3(sxz, sy, sxz * (1 + run * moving * .08f)) * diameter;
    }
}
