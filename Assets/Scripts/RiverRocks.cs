using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static TerrainMath;

/// <summary>Procedural low-poly toon rocks and pebbles, merged into ONE mesh. Placed along banks, shallows, lake shore and waterfalls.</summary>
public static class RiverRocks
{
    struct R { public Vector3 p; public float s, yaw, moss; public Color baseC; public int seed; }
    static readonly List<R> rocks = new List<R>();
    static Vector3[] tpl; static int[] tri;

    public static void Clear() { rocks.Clear(); }

    public static void Add(double x, double z, float s, System.Random rnd)
    {
        float th = RiverWater.GH(x, z);
        var look = RockLook(x, z);
        rocks.Add(new R { p = TerrainMath.U(x, th + 0.1f * s, z), s = s, yaw = (float)(rnd.NextDouble() * 6.2832), moss = look.moss, baseC = look.col, seed = rnd.Next() });
    }

    /// <summary>Location-based rock colour + mossiness. Wet/dark &amp; mossy at the waterline, sandy pale near
    /// beaches, grey scree near cliffs, pale dry granite high up. Uses the same single source of truth as terrain,
    /// grass and trees (Surface / WorldField.DistToSea / height).</summary>
    static (Color col, float moss) RockLook(double x, double z)
    {
        double h = H(x, z);                       // analytic height (matches Surface/WorldField)
        double sea = WorldField.DistToSea(x, z);  // metres inland, 0 in sea
        double cliff = WorldField.CliffMask(x, z);// 0 gentle .. 1 cliff
        var st = Surface.At(x, z);

        // weights in 0..1
        double nearWater = 1.0 - Sm(2.0, 16.0, sea);                 // 1 right at the water's edge -> 0 inland
        double high = Sm(12.0, 70.0, h);                             // altitude ramp
        double sandy = Sm(1.2, 6.0, h) * (1.0 - Sm(4.0, 22.0, sea)); // low + a little inland = beach
        double scree = System.Math.Max(cliff, Sm(28.0, 45.0, WorldField.SlopeDeg(x, z))); // steep rock

        // base palette (linear-ish toon tints; shader multiplies by vertex colour)
        Color wet = new Color(0.34f, 0.37f, 0.40f);                  // dark wet basalt
        Color sand = new Color(0.80f, 0.74f, 0.58f);                 // sun-bleached beach stone
        Color rock = new Color(0.55f, 0.56f, 0.58f);                 // mid grey scree
        Color granite = new Color(0.76f, 0.74f, 0.71f);              // pale dry granite

        // blend: start at granite, pull down by altitude, then overlay wet / sand / scree influences
        Color c = Color.Lerp(wet, granite, (float)System.Math.Min(1.0, high + 0.35));
        c = Color.Lerp(c, sand, (float)System.Math.Min(1.0, sandy));
        c = Color.Lerp(c, rock, (float)System.Math.Min(1.0, scree * 0.8));
        c = Color.Lerp(c, wet, (float)System.Math.Min(1.0, nearWater * 0.85));
        if (st == SurfaceType.Sand) c = Color.Lerp(c, sand, 0.35f);

        // moss grows on damp, low, non-scree ground near water
        double moss = nearWater * (1.0 - scree) * (1.0 - high * 0.7) * Sm(0.4, 2.5, h);
        return (c, (float)System.Math.Clamp(moss, 0.0, 1.0));
    }

    static void Template()
    {
        if (tpl != null) return;
        tpl = new Vector3[19]; tpl[0] = new Vector3(0, 0.85f, 0);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f, c = Mathf.Cos(a), s = Mathf.Sin(a);
            tpl[1 + i] = new Vector3(c * 0.88f, 0.40f, s * 0.88f); tpl[7 + i] = new Vector3(c, -0.15f, s); tpl[13 + i] = new Vector3(c * 0.9f, -0.55f, s * 0.9f);
        }
        var t = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            t.Add(0); t.Add(1 + i); t.Add(1 + j);
            for (int r = 0; r < 2; r++) { int u = 1 + r * 6, l = 7 + r * 6; t.Add(u + i); t.Add(l + i); t.Add(l + j); t.Add(u + i); t.Add(l + j); t.Add(u + j); }
        }
        tri = t.ToArray();
    }

    public static void Build(Transform parent)
    {
        if (rocks.Count == 0) return;
        Template();
        var V = new List<Vector3>(); var N = new List<Vector3>(); var C = new List<Color>(); var T = new List<int>();
        var w = new Vector3[19];
        foreach (var r in rocks)
        {
            var rg = new System.Random(r.seed);
            float sx = r.s * (0.9f + 0.4f * (float)rg.NextDouble()), sz = r.s * (0.9f + 0.4f * (float)rg.NextDouble()), sy = r.s * (0.55f + 0.3f * (float)rg.NextDouble());
            var q = Quaternion.Euler(0, r.yaw * Mathf.Rad2Deg, 0);
            for (int i = 0; i < 19; i++)
            {
                float j = 1f + 0.36f * ((float)rg.NextDouble() - 0.5f);
                w[i] = r.p + q * new Vector3(tpl[i].x * sx * j, tpl[i].y * sy * (0.9f + 0.2f * (float)rg.NextDouble()), tpl[i].z * sz * j);
            }
            Color baseC = r.baseC;
            for (int f = 0; f < tri.Length; f += 3)
            {
                Vector3 a = w[tri[f]], b = w[tri[f + 1]], c = w[tri[f + 2]], n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, (a + b + c) / 3f - r.p) < 0) { var tmp = b; b = c; c = tmp; n = -n; }
                Color col = baseC * (0.9f + 0.2f * (float)rg.NextDouble()); col.a = 1;
                if (n.y > 0.45f) col = Color.Lerp(col, new Color(0.42f, 0.58f, 0.25f), r.moss * 0.6f);
                int k = V.Count; V.Add(a); V.Add(b); V.Add(c); N.Add(n); N.Add(n); N.Add(n); C.Add(col); C.Add(col); C.Add(col); T.Add(k); T.Add(k + 1); T.Add(k + 2);
            }
        }
        var mesh = new Mesh { name = "RiverRocks", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetColors(C); mesh.SetTriangles(T, 0); mesh.RecalculateBounds();
        var go = new GameObject("RiverRocks"); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Island/ToonFoliage"); if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(sh);
        mat.SetColor("_Tint", Color.white);              // keep vertex-colour location tint 1:1
        mat.SetColor("_ShadeColor", new Color(0.40f, 0.44f, 0.56f)); // cool toon shadow like the trees
        mat.SetFloat("_Bands", 3f);                      // same band count as trunks
        mat.SetFloat("_Sway", 0f);                       // rocks must not sway
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;     // rocks cast their own shadows
        mr.receiveShadows = true;                        // and receive tree/terrain shadows
        rocks.Clear();
    }
}
