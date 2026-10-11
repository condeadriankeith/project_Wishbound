using UnityEngine;

/// <summary>Phase 3.5: water physics. Buoyancy + current drag, sampled from WaterField at 5 points under the collider.</summary>
[RequireComponent(typeof(Rigidbody))]
public class Buoyant : MonoBehaviour
{
    public float buoyancy = 1.7f, drag = 2.2f, angDrag = 1.5f;
    Rigidbody rb; Collider col;
    static readonly Vector2[] off = { new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) };

    void Awake() { rb = GetComponent<Rigidbody>(); col = GetComponent<Collider>(); }

    void FixedUpdate()
    {
        Bounds b = col ? col.bounds : new Bounds(transform.position, Vector3.one);
        float h = Mathf.Max(0.2f, b.size.y); bool any = false;
        for (int i = 0; i < off.Length; i++)
        {
            var p = new Vector3(b.center.x + off[i].x * b.extents.x * 0.8f, b.min.y, b.center.z + off[i].y * b.extents.z * 0.8f);
            WaterField.S s; if (!WaterField.Query(p, out s)) continue;
            float sub = Mathf.Clamp01((s.surf - p.y) / h); if (sub <= 0) continue;
            any = true; float k = rb.mass * sub / off.Length;
            rb.AddForceAtPosition(Vector3.up * (-Physics.gravity.y) * buoyancy * k, p);
            Vector3 v = rb.GetPointVelocity(p), tgt = new Vector3(s.flow.x, 0, s.flow.y);
            Vector3 rel = tgt - new Vector3(v.x, 0, v.z); rel.y = -v.y * 0.6f;
            rb.AddForceAtPosition(rel * drag * k, p);
        }
        if (any) rb.angularVelocity *= Mathf.Clamp01(1f - angDrag * Time.fixedDeltaTime);
    }
}
