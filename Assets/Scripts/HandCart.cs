using UnityEngine;

/// <summary>
/// A two-wheeled hand cart. While hitched it trails its puller like a real trailer (the axle stays a fixed distance
/// behind the puller's hips, steering toward them), pitches with the ground, spins its wheels by the distance rolled
/// and rocks a little over bumps. When parked the shafts drop to the ground and anyone may pick it up and carry on -
/// including mid-delivery (the load stays on board).
/// </summary>
public class HandCart : MonoBehaviour
{
    public SettlerBrain puller;
    public bool reserved;                      // a job has claimed it
    public Res cargoRes; public int cargoQty;
    public float parkedAt;
    public float shaftLength = 1.95f, wheelRadius = .34f;
    public Transform wheelL, wheelR, handles, loadRoot, body;

    public bool Parked => puller == null;
    public bool HasCargo => cargoQty > 0;

    float wheelAngle, handleTilt = 16f, rock, lastGood; Vector3 lastPos; static readonly RaycastHit[] hits = new RaycastHit[10];

    void OnEnable() { if (VillageWorld.I && !VillageWorld.I.carts.Contains(this)) VillageWorld.I.carts.Add(this); lastPos = transform.position; parkedAt = Time.time; }
    void OnDestroy() { if (VillageWorld.I) VillageWorld.I.carts.Remove(this); }

    /// <summary>Where a settler should stand to take hold of the shafts.</summary>
    public Vector3 HitchPoint => transform.position + transform.forward * shaftLength;

    public void SetCargo(Res r, int qty)
    {
        cargoRes = r; cargoQty = Mathf.Max(0, qty);
        VillageKitV2.RefreshCartLoad(this);
    }
    public int Unload(out Res r) { r = cargoRes; int q = cargoQty; cargoQty = 0; VillageKitV2.RefreshCartLoad(this); return q; }

    public void Hitch(SettlerBrain who) { puller = who; reserved = true; }
    public void Park()
    {
        puller = null; reserved = false; parkedAt = Time.time;
        // settle level, shafts down
        var e = transform.eulerAngles; transform.rotation = Quaternion.Euler(0, e.y, 0);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (puller)
        {
            Transform pt = puller.transform;
            Vector3 hip = pt.position + pt.forward * .08f; hip.y = 0;
            Vector3 axle = transform.position; axle.y = 0;
            Vector3 d = hip - axle; float m = d.magnitude;
            if (m > .01f)
            {
                Vector3 dir = d / m;
                Vector3 newAxle = hip - dir * shaftLength;
                // smooth the follow so the cart doesn't snap when the puller turns on the spot
                newAxle = Vector3.Lerp(axle, newAxle, 1 - Mathf.Exp(-18 * dt));
                float gy = GroundY(newAxle, transform.position.y);
                Vector3 p = new Vector3(newAxle.x, gy, newAxle.z);
                Vector3 fwd = Flat(hip - newAxle).normalized; if (fwd.sqrMagnitude < .01f) fwd = transform.forward;
                float gyF = GroundY(p + fwd * .9f, gy), gyB = GroundY(p - fwd * .9f, gy);
                Vector3 slopeF = new Vector3(fwd.x * 1.8f, gyF - gyB, fwd.z * 1.8f);
                float travelled = Vector3.Dot(p - transform.position, fwd);
                transform.position = p;
                transform.rotation = Quaternion.LookRotation(slopeF.normalized, Vector3.up);
                wheelAngle += travelled / wheelRadius * Mathf.Rad2Deg;
                rock = Mathf.Lerp(rock, Mathf.Clamp(Mathf.Abs(travelled) / Mathf.Max(dt, .001f), 0, 2.5f), 1 - Mathf.Exp(-4 * dt));
            }
            handleTilt = Mathf.Lerp(handleTilt, 0f, 1 - Mathf.Exp(-8 * dt));
        }
        else
        {
            handleTilt = Mathf.Lerp(handleTilt, 17f, 1 - Mathf.Exp(-6 * dt));
            rock = Mathf.Lerp(rock, 0, 1 - Mathf.Exp(-6 * dt));
            // keep sitting on the ground even if terrain around it changes
            float gy = GroundY(transform.position, transform.position.y);
            if (Mathf.Abs(gy - transform.position.y) > .02f) transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, gy, .2f), transform.position.z);
        }
        if (wheelL) wheelL.localRotation = Quaternion.Euler(wheelAngle, 0, 90);
        if (wheelR) wheelR.localRotation = Quaternion.Euler(wheelAngle, 0, 90);
        if (handles) handles.localRotation = Quaternion.Euler(handleTilt, 0, 0);
        if (body) body.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 9f) * rock * .5f, 0, Mathf.Sin(Time.time * 6.3f) * rock * .7f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    static float GroundY(Vector3 p, float fallback)
    {
        int foliage = LayerMask.NameToLayer("TreeOcclusion"); int mask = ~0; if (foliage >= 0) mask &= ~(1 << foliage);
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, hits, 9f, mask, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c is CharacterController || c is CapsuleCollider) continue;
            if (c.GetComponentInParent<VillageKitPiece>() || c.GetComponentInParent<SettlerBrain>()) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }
}
