using System.Collections.Generic;
using UnityEngine;

public enum ChildEmotion { None, Shy, Peek, Curious, Awe, Wave, Reach, Warm }

/// <summary>
/// A procedural acting layer that runs AFTER the child's Animator (idle / walk). It never replaces the clips, it adds to them:
///   * breathing, a slow weight shift while standing, a head that leads every turn and a spine that follows it
///   * the head and shoulders tracking a look target (the star, a settler, a fragment)
///   * emotion poses (shy, peeking, curious, awed, waving, reaching, grateful) blended in and out with smoothed weights
///   * hair follow-through: every hair chain lags behind head motion on a damped spring, then settles
/// Each bone is re-based every frame (it remembers the pose the Animator wrote), so nothing ever accumulates or drifts, whether
/// or not a clip drives that bone.
/// </summary>
[DefaultExecutionOrder(300)]
public class ChildPerformer : MonoBehaviour
{
    public ChildEmotion emotion = ChildEmotion.None;
    public Transform lookTarget;
    [Range(0, 1)] public float lookWeight = 1f;
    public Vector3? lookPoint;
    public float emotionSpeed = 3.5f;

    class Bone
    {
        public Transform t; public Quaternion baseRot, lastOut; public bool has;
        public void Begin()
        {
            var cur = t.localRotation;
            if (has && Quaternion.Angle(cur, lastOut) < .002f) cur = baseRot;   // nobody animated it this frame: keep the true base
            baseRot = cur; t.localRotation = cur; has = true;
        }
        public void End() { lastOut = t.localRotation; }
    }
    Bone hips, spine, head, armL, armR, legL, legR;
    readonly List<Bone> hairA = new List<Bone>(), hairB = new List<Bone>();
    readonly Dictionary<ChildEmotion, float> weights = new Dictionary<ChildEmotion, float>();
    CharacterController cc; Transform body;
    float yawLead, lookYaw, lookPitch, lastYaw, t0;
    // hair spring (shared across chains: a head's hair moves together)
    Vector3 hairS, hairV, lastHeadPos, headVel;
    bool ready;

    void Awake() { cc = GetComponent<CharacterController>(); t0 = Random.value * 20f; }

    void Bind()
    {
        Transform rig = null;
        foreach (var tr in GetComponentsInChildren<Transform>(true)) if (tr.name == "Rig" && tr.gameObject.activeInHierarchy && tr.GetComponentInParent<Animator>(true)) { rig = tr; break; }
        if (!rig) return;
        hairA.Clear(); hairB.Clear(); lastYaw = transform.eulerAngles.y;
        Bone Find(string n) { foreach (var tr in rig.GetComponentsInChildren<Transform>(true)) if (tr.name == n) return new Bone { t = tr }; return null; }
        hips = Find("hips"); spine = Find("spine"); head = Find("head"); armL = Find("arm.L"); armR = Find("arm.R"); legL = Find("leg.L"); legR = Find("leg.R");
        foreach (var tr in rig.GetComponentsInChildren<Transform>(true))
        {
            if (!tr.name.StartsWith("hair_")) continue;
            if (tr.name.EndsWith("_a")) hairA.Add(new Bone { t = tr }); else if (tr.name.EndsWith("_b")) hairB.Add(new Bone { t = tr });
        }
        body = rig;
        ready = hips != null && spine != null && head != null && armL != null && armR != null;
        if (ready) lastHeadPos = head.t.position;
    }

    float W(ChildEmotion e) => weights.TryGetValue(e, out var v) ? v : 0f;

    void LateUpdate()
    {
        if (!ready) { if (Time.frameCount % 30 == 0 || body == null) Bind(); if (!ready) return; }
        float dt = Mathf.Min(Time.deltaTime, .05f); if (dt <= 0) return;
        float time = Time.time + t0;
        foreach (ChildEmotion e in System.Enum.GetValues(typeof(ChildEmotion)))
        {
            if (e == ChildEmotion.None) continue;
            float w = W(e); weights[e] = Mathf.MoveTowards(w, emotion == e ? 1f : 0f, dt * emotionSpeed * (emotion == e ? 1f : 1.6f));
        }
        hips.Begin(); spine.Begin(); head.Begin(); armL.Begin(); armR.Begin();
        if (legL != null) legL.Begin(); if (legR != null) legR.Begin();

        Vector3 right = transform.right, up = Vector3.up, fwd = transform.forward;
        float speed = cc ? new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude : 0f;
        float still = 1f - Mathf.Clamp01(speed / .5f);

        // ---- breathing + weight shift
        float breath = Mathf.Sin(time * 1.8f), sway = Mathf.Sin(time * .42f) * still;
        // ---- the head leads turns, the spine follows a beat later
        float yaw = transform.eulerAngles.y; float rate = Mathf.DeltaAngle(lastYaw, yaw) / dt; lastYaw = yaw;
        yawLead = Mathf.Lerp(yawLead, Mathf.Clamp(rate * .08f, -22f, 22f), 1 - Mathf.Exp(-6 * dt));

        // ---- look target
        float tYaw = 0, tPitch = 0;
        Vector3? lp = lookTarget ? lookTarget.position + Vector3.up * .9f : lookPoint;
        if (lp.HasValue && lookWeight > 0)
        {
            Vector3 d = transform.InverseTransformDirection(lp.Value - head.t.position);
            tYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -70, 70) * lookWeight;
            tPitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -25, 30) * lookWeight;
        }
        lookYaw = Mathf.Lerp(lookYaw, tYaw, 1 - Mathf.Exp(-5 * dt)); lookPitch = Mathf.Lerp(lookPitch, tPitch, 1 - Mathf.Exp(-5 * dt));

        // ---- emotions (additive, in the character's own axes: pitch + = down / forward, arm "down" + = in toward the body)
        float shy = W(ChildEmotion.Shy), peek = W(ChildEmotion.Peek), cur = W(ChildEmotion.Curious), awe = W(ChildEmotion.Awe),
              wave = W(ChildEmotion.Wave), reach = W(ChildEmotion.Reach), warm = W(ChildEmotion.Warm);
        float spPitch = breath * 1.1f + shy * 7 + cur * 5 - awe * 4 + reach * 5 + peek * 4;
        float spRoll = sway * 1.4f + peek * 11 * -1f;
        float spYaw = lookYaw * .28f - yawLead * .25f + peek * 8;
        float hdPitch = lookPitch * .8f + shy * 15 + cur * 4 - awe * 10 - warm * 4 - breath * .5f;
        float hdYaw = lookYaw * .72f + yawLead * .7f;
        float hdRoll = cur * 12 + shy * 8 + peek * 9 + warm * 5 + wave * 5;
        float hipRoll = sway * 1.6f - peek * 4;

        hips.t.rotation = Quaternion.AngleAxis(hipRoll, fwd) * hips.t.rotation;
        spine.t.rotation = Quaternion.AngleAxis(spYaw, up) * Quaternion.AngleAxis(spPitch, right) * Quaternion.AngleAxis(spRoll, fwd) * spine.t.rotation;
        head.t.rotation = Quaternion.AngleAxis(hdYaw, up) * Quaternion.AngleAxis(hdPitch, right) * Quaternion.AngleAxis(hdRoll, fwd) * head.t.rotation;

        // arms: down (+ in) and swing (+ forward), L = +down about forward, R = -down
        float wv = Mathf.Sin(time * 7.5f);
        ArmDelta(armL, 1, shy * 12 - awe * 16 - warm * 14 + breath * .6f, shy * -10 + awe * 12 + warm * 8 + sway * 2, right, fwd);
        ArmDelta(armR, -1, shy * 12 - awe * 16 - warm * 14 - reach * 12 - wave * 88 + wv * wave * 14 + breath * -.6f, shy * -10 + awe * 12 + warm * 8 + reach * 72 + wave * 6, right, fwd);

        hips.End(); spine.End(); head.End(); armL.End(); armR.End();
        if (legL != null) legL.End(); if (legR != null) legR.End();

        // ---- hair follow-through
        Vector3 hv = (head.t.position - lastHeadPos) / dt; lastHeadPos = head.t.position;
        headVel = Vector3.Lerp(headVel, hv, 1 - Mathf.Exp(-14 * dt));
        Vector3 local = transform.InverseTransformDirection(headVel) * -.05f;                 // hair trails opposite to motion
        Vector3 goal = new Vector3(local.x, 0, local.z) + new Vector3(0, 0, Mathf.Sin(time * 1.3f) * .01f);
        hairV += ((goal - hairS) * 90f - hairV * 9f) * dt; hairS += hairV * dt;               // damped spring (under-damped: a little overshoot)
        float ax = Mathf.Clamp(hairS.z * 120f, -28f, 28f), az = Mathf.Clamp(-hairS.x * 120f, -28f, 28f);
        for (int i = 0; i < hairA.Count; i++) { var h = hairA[i]; h.Begin(); h.t.rotation = Quaternion.AngleAxis(ax * .55f, right) * Quaternion.AngleAxis(az * .55f, fwd) * h.t.rotation; h.End(); }
        for (int i = 0; i < hairB.Count; i++) { var h = hairB[i]; h.Begin(); h.t.rotation = Quaternion.AngleAxis(ax, right) * Quaternion.AngleAxis(az, fwd) * h.t.rotation; h.End(); }
    }

    static void ArmDelta(Bone b, int side, float down, float swing, Vector3 right, Vector3 fwd)
    {
        b.t.rotation = Quaternion.AngleAxis(-swing, right) * Quaternion.AngleAxis(side * down, fwd) * b.t.rotation;
    }

    /// <summary>Convenience for cutscenes: set an emotion and (optionally) who the child is looking at.</summary>
    public void Act(ChildEmotion e, Transform look = null) { emotion = e; if (look) lookTarget = look; }
}
