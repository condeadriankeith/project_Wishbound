using System.Collections.Generic;
using UnityEngine;

/// <summary>Phase 3.4: queryable water (rivers, lake, sea). Gives surface height, depth and flow velocity at any point.</summary>
public static class WaterField
{
    public struct S { public float surf, depth; public Vector2 flow; public int kind; } // kind 1 river, 2 lake, 3 sea
    public struct Seg { public float x0, z0, x1, z1, y0, y1, w0, w1, v0, v1; }
    public const float Sea = 0f;
    static readonly List<Seg> segs = new List<Seg>();
    static bool lakeOn; static float lx, lz, lr, ll;

    public static void Clear() { segs.Clear(); lakeOn = false; }
    public static void Add(Seg s) { segs.Add(s); }
    public static void SetLake(float x, float z, float r, float level) { lx = x; lz = z; lr = r; ll = level; lakeOn = true; }

    /// <param name="p">Unity world position.</param>
    public static bool Query(Vector3 p, out S o)
    {
        o = default(S);
        float x = p.x, z = TerrainMath.MirrorZ ? -p.z : p.z;
        float best = float.MaxValue; Seg bs = default(Seg); float bt = 0; bool found = false;
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i]; float m = Mathf.Max(s.w0, s.w1);
            if (x < Mathf.Min(s.x0, s.x1) - m || x > Mathf.Max(s.x0, s.x1) + m || z < Mathf.Min(s.z0, s.z1) - m || z > Mathf.Max(s.z0, s.z1) + m) continue;
            float vx = s.x1 - s.x0, vz = s.z1 - s.z0, l2 = vx * vx + vz * vz; if (l2 < 1e-8f) continue;
            float t = Mathf.Clamp01(((x - s.x0) * vx + (z - s.z0) * vz) / l2);
            float dx = x - (s.x0 + t * vx), dz = z - (s.z0 + t * vz), d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d <= Mathf.Lerp(s.w0, s.w1, t) && d < best) { best = d; bs = s; bt = t; found = true; }
        }
        if (found)
        {
            float vx = bs.x1 - bs.x0, vz = bs.z1 - bs.z0, l = Mathf.Sqrt(vx * vx + vz * vz), sp = Mathf.Lerp(bs.v0, bs.v1, bt);
            o.surf = Mathf.Lerp(bs.y0, bs.y1, bt); o.kind = 1;
            o.flow = new Vector2(vx / l * sp, (TerrainMath.MirrorZ ? -vz : vz) / l * sp);
            o.depth = Mathf.Max(0.05f, o.surf - TerrainMath.Hf(x, z)); return true;
        }
        if (lakeOn && (x - lx) * (x - lx) + (z - lz) * (z - lz) < lr * lr) { o.surf = ll; o.kind = 2; o.depth = Mathf.Max(0.05f, ll - TerrainMath.Hf(x, z)); return true; }
        float h = TerrainMath.Hf(x, z);
        if (h < Sea) { o.surf = Sea; o.kind = 3; o.depth = Sea - h; return true; }
        return false;
    }
}
