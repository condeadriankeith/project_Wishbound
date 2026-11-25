using UnityEngine;

/// <summary>
/// Procedural body animation for a settler (built from the child's rig: hips, spine, head, arm.L/R, leg.L/R).
/// No animation clips: every pose is a handful of angles in the character's own space (x = right, y = up, z = forward),
/// blended smoothly, with a speed-driven walk cycle layered on top. Poses: idle, dig, water, crouch, wave, scratch head,
/// talk, cower, point, carry, fish, sit, cheer. The head can track a look target.
/// </summary>
[DefaultExecutionOrder(100)]
public class SettlerRig : MonoBehaviour
{
    public enum Pose { Idle, Dig, Water, Crouch, Wave, Scratch, Talk, Cower, Point, Carry, Fish, Sit, Cheer, Sweep, PickUp, HandOver, Sow, Eat, Sleep, PullCart, ShoulderCarry, WarmHands }

    [Header("State (set by SettlerBrain)")]
    public Pose pose = Pose.Idle;
    public float speed;                   // m/s, drives the walk cycle
    public Transform lookTarget; public Vector3? lookPoint;
    [Range(0, 1)] public float energy = .6f;   // personality: how bouncy the walk and gestures are

    Transform avatar, hips, spine, head, armL, armR, legL, legR, elbowL, elbowR, kneeL, kneeR;
    Quaternion rHips, rSpine, rHead, rArmL, rArmR, rLegL, rLegR;   // rest, relative to the avatar
    Vector3 hipsRestLocal;
    float phase, t;
    public float MotionTime => t + phase;
    // blended pose values
    struct P { public float hipY, lean, twist, headP, headY, headR, aLd, aLs, aLo, aRd, aRs, aRo, lL, lR; }
    P cur, tgt;
    bool ready;

    public void Bind(Transform avatarRoot)
    {
        avatar = avatarRoot;
        foreach (var b in avatar.GetComponentsInChildren<Transform>(true))
            switch (b.name)
            {
                case "elbow.L": elbowL=b; break; case "elbow.R": elbowR=b; break; case "knee.L": kneeL=b; break; case "knee.R": kneeR=b; break;
                case "hips": hips = b; break; case "spine": spine = b; break; case "head": if (b.GetComponent<Renderer>() == null) head = b; break;
                case "arm.L": armL = b; break; case "arm.R": armR = b; break; case "leg.L": legL = b; break; case "leg.R": legR = b; break;
            }
        if (!hips || !spine || !head || !armL || !armR || !legL || !legR) { enabled = false; return; }
        Quaternion inv = Quaternion.Inverse(avatar.rotation);
        rHips = inv * hips.rotation; rSpine = inv * spine.rotation; rHead = inv * head.rotation;
        rArmL = inv * armL.rotation; rArmR = inv * armR.rotation; rLegL = inv * legL.rotation; rLegR = inv * legR.rotation;
        hipsRestLocal = avatar.InverseTransformPoint(hips.position);
        phase = Random.value * 10; ready = true;
        cur = Target(Pose.Idle, 0); tgt = cur;
    }

    // angles in degrees. arm "down" = how far the T-pose arm is lowered (85 = hanging), "swing" = + forward,
    // "out" = lifted sideways back up. legs: + forward. lean: + forward. headP: + looks down.
    P Target(Pose p, float time)
    {
        var o = new P { aLd = 58, aRd = 58 };
        float s = Mathf.Sin(time * 2.2f), s2 = Mathf.Sin(time * 4.4f);
        switch (p)
        {
            case Pose.PickUp: {float c=(1+Mathf.Sin(time*2.8f))*.5f; o.hipY=-.16f*c;o.lean=15+28*c;o.aLs=55;o.aRs=65;o.headP=25;o.lL=25*c;o.lR=22*c;break;}
            case Pose.WarmHands: o.aLs=58+Mathf.Sin(time*1.6f)*3; o.aRs=62-Mathf.Sin(time*1.6f)*3; o.lean=7; o.headP=12; o.aLo=5; o.aRo=5; break;
            case Pose.HandOver: o.aLs=70;o.aRs=78;o.lean=8;o.headP=6;break;
            case Pose.Sow: o.lean=26;o.aLs=35;o.aRs=50+s*22;o.twist=s*8;o.headP=30;break;
            case Pose.Eat: o.aRs=82+s*10;o.aLs=50;o.headP=10+s*3;break;
            case Pose.Sleep: o.hipY=-.19f;o.lL=55;o.lR=55;o.lean=12;o.headP=25+s*2;o.aLs=28;o.aRs=28;break;
            case Pose.PullCart: o.aLs=-18;o.aRs=-18;o.aLo=8;o.aRo=8;o.lean=12;o.headP=4;break;
            case Pose.ShoulderCarry: // one arm up holding the hoe shaft on the shoulder, the other hanging
                o.aLd = 10; o.aLs = 5; o.aLo = 4;            // left arm hangs
                o.aRd = -55; o.aRs = 10; o.aRo = -12;        // right arm lifts to shoulder height
                o.lean = 6; o.headP = 2; o.headY = 4; break;
            case Pose.Idle: o.aLs = 4 + s * 2; o.aRs = 4 - s * 2; o.headP = s * 2; o.hipY = s * .01f; break;
            case Pose.Dig:   // rhythmic hoe: both arms raise forward and chop down, body bobs with it
                { float c = Mathf.Sin(time * 3.2f); float up = Mathf.Max(0, c);
                  o.lean = 22 + up * 8; o.aLs = 42 + c * 20; o.aRs = 48 + c * 20; o.aLo = 6; o.aRo = 6; o.headP = 18; o.hipY = -.05f - up * .03f; o.lL = 12; o.lR = -8; break; }
            case Pose.Water: o.lean = 12; o.aRs = 70; o.aRo = 4; o.aLs = 20; o.headP = 22 + s * 3; o.twist = 6; break;
            case Pose.Crouch: o.hipY = -.10f; o.lean = 22; o.aLs = 55 + s2 * 10; o.aRs = 60 - s2 * 10; o.headP = 25; o.lL = 28; o.lR = 22; break;
            case Pose.Wave: o.aRd = -22; o.aRs = 8; o.aRo = Mathf.Sin(time * 7) * 12; o.aLs = 4; o.headR = 6; break;
            case Pose.Scratch: o.aRd = -18; o.aRs = 12; o.aRo = Mathf.Sin(time * 8) * 5; o.headR = 14; o.headP = -4; o.lean = -3; break;
            case Pose.Talk: o.aLs = 30 + Mathf.Sin(time * 3.1f) * 14; o.aRs = 25 + Mathf.Sin(time * 2.6f + 1) * 16; o.aRo = 10; o.aLo = 8; o.headP = Mathf.Sin(time * 3.5f) * 5; o.headR = Mathf.Sin(time * 1.3f) * 5; break;
            case Pose.Cower: o.lean = -12; o.hipY = -.08f; o.aLd = 45; o.aRd = 45; o.aLs = 48; o.aRs = 48; o.aLo = 6; o.aRo = 6; o.headP = 18; o.lL = -10; o.lR = 6; break;
            case Pose.Point: o.aRd = 70; o.aRs = 85; o.aLs = 4; o.headP = -4; break;
            case Pose.Carry: o.aLs = 70; o.aRs = 70; o.aLo = 4; o.aRo = 4; o.lean = -4; break;
            case Pose.Fish: o.aRs = 55; o.aLs = 45; o.aLo = 6; o.aRo = 6; o.headP = 8 + s * 2; o.hipY = -.02f; break;
            case Pose.Sit: o.hipY = -.19f; o.lL = 55; o.lR = 55; o.lean = -4; o.aLs = 30; o.aRs = 28; o.headP = s * 3; break;
            case Pose.Cheer: { float h = Mathf.Abs(Mathf.Sin(time * 6)); o.aLd = -60; o.aRd = -60; o.aLo = Mathf.Sin(time * 12) * 15; o.aRo = -Mathf.Sin(time * 12) * 15; o.hipY = h * .14f; o.headP = -10; break; }
            case Pose.Sweep: { float c = Mathf.Sin(time * 2.6f); o.lean = 18; o.aLs = 45 + c * 20; o.aRs = 50 + c * 20; o.twist = c * 14; o.headP = 15; break; }
        }
        return o;
    }

    static float Approach(float a, float b, float k) => a + (b - a) * k;

    void LateUpdate() => EvaluatePose(Time.deltaTime);

    // Explicit time step supports repeatable pose reviews without edit-mode callbacks.
    public void EvaluatePose(float dt)
    {
        if (!ready) return;
        dt = Mathf.Clamp(dt, 0, .1f); t += dt;
        tgt = Target(pose, t + phase);
        float k = 1 - Mathf.Exp(-7 * dt);
        cur.hipY = Approach(cur.hipY, tgt.hipY, k); cur.lean = Approach(cur.lean, tgt.lean, k); cur.twist = Approach(cur.twist, tgt.twist, k);
        cur.headP = Approach(cur.headP, tgt.headP, k); cur.headY = Approach(cur.headY, tgt.headY, k); cur.headR = Approach(cur.headR, tgt.headR, k);
        cur.aLd = Approach(cur.aLd, tgt.aLd, k); cur.aLs = Approach(cur.aLs, tgt.aLs, k); cur.aLo = Approach(cur.aLo, tgt.aLo, k);
        cur.aRd = Approach(cur.aRd, tgt.aRd, k); cur.aRs = Approach(cur.aRs, tgt.aRs, k); cur.aRo = Approach(cur.aRo, tgt.aRo, k);
        cur.lL = Approach(cur.lL, tgt.lL, k); cur.lR = Approach(cur.lR, tgt.lR, k);

        // ---- walk cycle layered on top (stronger with speed, bouncier with energy)
        float w = Mathf.Clamp01(speed / 1.2f) * (pose == Pose.Idle || pose == Pose.Carry || pose == Pose.PullCart || pose == Pose.Talk || pose == Pose.Cower ? 1 : .25f);
        int previousStep = Mathf.FloorToInt(phase / Mathf.PI);
        phase += dt * Mathf.Lerp(5f, 11f, Mathf.Clamp01(speed / 4f)) * (w > .01f ? 1 : 0);
        if (Application.isPlaying && speed > .25f && previousStep != Mathf.FloorToInt(phase / Mathf.PI))
            WorldSound.Cue(transform, "Step", 14f);
        float stride = Mathf.Lerp(18, 28, Mathf.Clamp01(speed / 4f)) * w;
        float swingL = cur.lL + Mathf.Sin(phase) * stride, swingR = cur.lR - Mathf.Sin(phase) * stride;
        float armSwing = Mathf.Sin(phase - .35f) * stride * .8f * (pose == Pose.Carry || pose == Pose.PullCart ? .08f : 1);
        float bob = Mathf.Abs(Mathf.Cos(phase)) * .05f * w * (.6f + energy * .8f);
        float roll = Mathf.Sin(phase) * 4 * w;

        // ---- life: breathing, a slow weight shift when still, and a head that leads every turn with the spine following
        float breath = Mathf.Sin(MotionTime * 1.9f), still = 1f - Mathf.Clamp01(speed / .6f);
        float yawNow = avatar.eulerAngles.y, yawRate = Mathf.DeltaAngle(lastYaw, yawNow) / Mathf.Max(dt, .001f); lastYaw = yawNow;
        leadY = Approach(leadY, Mathf.Clamp(yawRate * .09f, -24f, 24f), 1 - Mathf.Exp(-6 * dt));
        float idleShift = Mathf.Sin(MotionTime * .45f) * .012f * still;
        float counter = Mathf.Sin(phase) * w;
        // ---- head look-at (yaw/pitch toward a point, clamped)
        float lookY = 0, lookP = 0;
        Vector3? lp = lookTarget ? lookTarget.position + Vector3.up * .8f : lookPoint;
        if (lp.HasValue)
        {
            Vector3 d = avatar.InverseTransformDirection(lp.Value - head.position);
            lookY = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -70, 70);
            lookP = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -25, 30);
        }
        hY = Approach(hY, lookY, 1 - Mathf.Exp(-5 * dt)); hP = Approach(hP, lookP, 1 - Mathf.Exp(-5 * dt));

        // ---- apply (avatar space: x right, y up, z forward)
        Quaternion A = avatar.rotation;
        Quaternion qH = Quaternion.Euler(0, cur.twist * .3f + counter * 5f, roll * .5f + idleShift * 120f);
        Quaternion qS = qH * Quaternion.Euler(cur.lean + breath * 1.1f, cur.twist - counter * 7f + leadY * .35f, roll * .5f);
        hips.position = avatar.TransformPoint(hipsRestLocal + Vector3.up * (cur.hipY + bob) + Vector3.right * (Mathf.Sin(phase) * .012f * w + idleShift));
        hips.rotation = A * qH * rHips;
        spine.rotation = A * qS * rSpine;
        head.rotation = A * qS * Quaternion.Euler(cur.headP + hP - cur.lean * .5f - breath * .4f, cur.headY + hY + leadY * .7f, cur.headR) * rHead;
        // arms: start from T-pose, lower ("down") about the forward axis, then swing about the right axis, then lift out
        armL.rotation = A * qS * Quaternion.AngleAxis(-(cur.aLs + armSwing), Vector3.right) * Quaternion.AngleAxis(Mathf.Clamp(cur.aLd - cur.aLo, -45, 62), Vector3.forward) * rArmL;
        armR.rotation = A * qS * Quaternion.AngleAxis(-(cur.aRs - armSwing), Vector3.right) * Quaternion.AngleAxis(-Mathf.Clamp(cur.aRd - cur.aRo, -45, 62), Vector3.forward) * rArmR;
        legL.rotation = A * qH * Quaternion.AngleAxis(-swingL, Vector3.right) * rLegL;
        legR.rotation = A * qH * Quaternion.AngleAxis(-swingR, Vector3.right) * rLegR;
        float bend = pose == Pose.WarmHands ? 48 : pose == Pose.Carry ? 42 : pose == Pose.Fish ? 32 : pose == Pose.Dig || pose == Pose.Sweep ? 20 : pose == Pose.Talk ? 25 : 8;
        elbowBend = Approach(elbowBend,bend,k);
        if(elbowL) elbowL.localRotation=Quaternion.Euler(0,elbowBend,0);
        if(elbowR) elbowR.localRotation=Quaternion.Euler(0,-elbowBend,0);
        kneeBend=Approach(kneeBend,pose==Pose.Sit || pose==Pose.Sleep?75:pose==Pose.Crouch || pose==Pose.PickUp?38:0,k);
        if(kneeL) kneeL.localRotation=Quaternion.Euler(kneeBend+Mathf.Max(0,-Mathf.Sin(phase))*18*w,0,0);
        if(kneeR) kneeR.localRotation=Quaternion.Euler(kneeBend+Mathf.Max(0,Mathf.Sin(phase))*18*w,0,0);
        // Keep both hands on a common shaft rather than swinging straight arms through it.
        float workTarget = pose==Pose.Dig || pose==Pose.Sweep || pose==Pose.Fish ? 1 : 0;
        workBlend=Approach(workBlend,workTarget,k);
        if(workBlend>.001f && elbowL && elbowR)
        {
            float lift=pose==Pose.Dig?Mathf.Sin(MotionTime*3.2f)*.06f:0;
            Vector3 grip=new Vector3(.10f,.015f+lift,.32f);
            float pitch=pose==Pose.Dig?12+Mathf.Sin(MotionTime*3.2f)*18:0;
            Vector3 second=grip+Quaternion.Euler(pitch,0,0)*new Vector3(0,.14f,-.118f);
            SolveArm(armR,elbowR,1,spine.TransformPoint(grip),workBlend);
            SolveArm(armL,elbowL,-1,spine.TransformPoint(second),workBlend);
        }
    }
    void SolveArm(Transform upper,Transform elbow,int side,Vector3 target,float weight)
    {
        Transform hand=elbow.Find(side<0?"hand.L":"hand.R"); if(!hand)return;
        target=Vector3.Lerp(hand.position,target,weight);
        float a=Vector3.Distance(upper.position,elbow.position), b=Vector3.Distance(elbow.position,hand.position);
        Vector3 delta=target-upper.position;float d=Mathf.Clamp(delta.magnitude,.05f,a+b-.002f);Vector3 axis=delta.normalized;
        Vector3 outward=avatar.TransformDirection(new Vector3(side,-.35f,0));
        Vector3 bend=Vector3.ProjectOnPlane(outward,axis).normalized;
        float along=(a*a-b*b+d*d)/(2*d), away=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        Vector3 joint=upper.position+axis*along+bend*away;
        upper.rotation=Quaternion.FromToRotation(Vector3.right*side,joint-upper.position);
        elbow.rotation=Quaternion.FromToRotation(Vector3.right*side,upper.position+axis*d-elbow.position);
    }
    float hY, hP, elbowBend, kneeBend, workBlend, leadY, lastYaw;
}
