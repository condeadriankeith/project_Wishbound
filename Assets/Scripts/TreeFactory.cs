using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class InstancedBatch
{
    public Mesh mesh; public Material mat; public bool castShadows = true;
    readonly List<Matrix4x4[]> mats = new(); readonly List<Vector4[]> cols = new(); readonly List<int> counts = new();
    readonly MaterialPropertyBlock mpb = new();
    readonly List<Matrix4x4> tm = new(); readonly List<Vector4> tc = new();

    public InstancedBatch(Mesh m, Material mt) { mesh = m; mat = mt; if (mat) mat.enableInstancing = true; }
    public int Count => tm.Count;
    public void Add(Matrix4x4 m, Color c) { tm.Add(m); tc.Add(new Vector4(c.r, c.g, c.b, c.a)); }
    public void Bake()
    {
        mats.Clear(); cols.Clear(); counts.Clear();
        for (int i = 0; i < tm.Count; i += 1023)
        {
            int n = Mathf.Min(1023, tm.Count - i);
            mats.Add(tm.GetRange(i, n).ToArray()); cols.Add(tc.GetRange(i, n).ToArray()); counts.Add(n);
        }
    }
    public void Draw()
    {
        if (mats.Count == 0 || mesh == null || mat == null) return;
        for (int i = 0; i < mats.Count; i++)
        {
            mpb.Clear();
            mpb.SetVectorArray("_InstColor", cols[i]);
            Graphics.DrawMeshInstanced(mesh, 0, mat, mats[i], counts[i], mpb,
                castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true);
        }
    }
}

public static class TreeFactory
{
    public static Mesh Cyl(float rTop, float rBot, float h, int seg)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2, c = Mathf.Cos(a), s = Mathf.Sin(a);
            v.Add(new Vector3(c * rBot, -h / 2, s * rBot));
            v.Add(new Vector3(c * rTop, h / 2, s * rTop));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
        }
        int ci = v.Count; v.Add(new Vector3(0, -h / 2, 0));
        for (int i = 0; i < seg; i++) t.AddRange(new[] { ci, (i + 1) * 2, i * 2 });
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }
    public static Mesh Cone(float r, float h, int seg) => Cyl(0.02f, r, h, seg);

    // Procedural noisy sphere (stands in for three.js icosphere lobe)
    public static Mesh Lobe(Vector3 scale, int latSeg = 10, int lonSeg = 14)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        for (int la = 0; la <= latSeg; la++)
        {
            float th = la / (float)latSeg * Mathf.PI;
            float sy = Mathf.Cos(th), sr = Mathf.Sin(th);
            for (int lo = 0; lo <= lonSeg; lo++)
            {
                float ph = lo / (float)lonSeg * Mathf.PI * 2;
                Vector3 p = new Vector3(sr * Mathf.Cos(ph), sy, sr * Mathf.Sin(ph));
                float d = 1f + 0.18f * Mathf.Sin(p.x * 4f + p.y * 3f);
                v.Add(new Vector3(p.x * d * scale.x, p.y * d * scale.y, p.z * d * scale.z));
            }
        }
        for (int la = 0; la < latSeg; la++)
            for (int lo = 0; lo < lonSeg; lo++)
            {
                int a = la * (lonSeg + 1) + lo, b = a + lonSeg + 1;
                tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); return m;
    }

    static Mesh Combine(params (Mesh m, Matrix4x4 xf)[] parts)
    {
        var ci = new CombineInstance[parts.Length];
        for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].m, transform = parts[i].xf };
        var r = new Mesh { indexFormat = IndexFormat.UInt32 };
        r.CombineMeshes(ci, true, true); r.RecalculateNormals(); return r;
    }
    static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
    static Matrix4x4 TRz(float x, float y, float z, float rzRad, float ryRad = 0) =>
        Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0, ryRad * Mathf.Rad2Deg, rzRad * Mathf.Rad2Deg), Vector3.one);

    public static Mesh Oak()
    {
        var parts = new List<(Mesh, Matrix4x4)>
        {
            (Cyl(0.32f, 0.58f, 4.2f, 7), T(0, 2.1f, 0)),
            (Cyl(0.20f, 0.30f, 2.4f, 6), TRz(-0.9f, 3.6f, 0.2f, 0.65f)),
            (Cyl(0.18f, 0.28f, 2.2f, 6), TRz( 0.8f, 3.7f,-0.4f, -0.6f, 1.1f)),
        };
        (Vector3 p, Vector3 s)[] lobes = {
            (new Vector3(0,5.2f,0),       new Vector3(2.5f,2.2f,2.5f)),
            (new Vector3(-1.6f,4.6f,0.4f), new Vector3(1.8f,1.6f,1.7f)),
            (new Vector3(1.5f,4.8f,-0.3f),new Vector3(1.7f,1.6f,1.8f)),
            (new Vector3(0.3f,4.5f,1.5f),  new Vector3(1.6f,1.5f,1.6f)),
            (new Vector3(-0.4f,4.7f,-1.4f),new Vector3(1.6f,1.4f,1.5f)),
            (new Vector3(0.1f,6.5f,0.2f),   new Vector3(1.7f,1.5f,1.7f)) };
        foreach (var l in lobes) parts.Add((Lobe(l.s), T(l.p.x, l.p.y, l.p.z)));
        return Combine(parts.ToArray());
    }

    public static Mesh Cypress()
    {
        var p = new List<(Mesh, Matrix4x4)> { (Cyl(0.25f, 0.4f, 3.0f, 6), T(0, 1.5f, 0)) };
        (float y, float r, float h)[] t = { (3.5f,1.1f,3.5f), (6.2f,0.95f,3.2f), (8.6f,0.75f,2.8f), (10.6f,0.45f,2.4f) };
        foreach (var c in t) p.Add((Cone(c.r, c.h, 7), T(0, c.y, 0)));
        return Combine(p.ToArray());
    }

    public static Mesh Pine()
    {
        var p = new List<(Mesh, Matrix4x4)> { (Cyl(0.26f, 0.5f, 5.0f, 6), T(0, 2.5f, 0)) };
        (float y, float r, float h)[] b = { (3.8f,2.4f,2.8f), (5.6f,2.0f,2.5f), (7.2f,1.6f,2.2f), (8.6f,1.2f,1.9f), (9.8f,0.7f,1.5f) };
        foreach (var c in b) p.Add((Cone(c.r, c.h, 6), T(0, c.y, 0)));
        return Combine(p.ToArray());
    }

    public static Mesh Ivy()
    {
        var p = new List<(Mesh, Matrix4x4)>();
        (Vector3 pos, float s)[] c = {
            (new Vector3(0,0,0),1.2f), (new Vector3(0.5f,-0.9f,0.3f),1.0f),
            (new Vector3(0.2f,-1.9f,0.5f),0.85f), (new Vector3(-0.3f,-2.7f,0.4f),0.7f) };
        foreach (var k in c) p.Add((Lobe(Vector3.one * k.s), T(k.pos.x, k.pos.y, k.pos.z)));
        return Combine(p.ToArray());
    }

    public static Mesh GrassMesh()
    {
        var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            float a = k * 0.785f, dx = Mathf.Cos(a) * 0.22f, dz = Mathf.Sin(a) * 0.22f, b = (k % 2 == 1) ? 0.35f : -0.28f;
            int i0 = v.Count;
            v.Add(new Vector3(-dx, 0, -dz)); v.Add(new Vector3(dx, 0, dz)); v.Add(new Vector3(b, 2f, b * 0.6f));
            c.Add(new Color(.4f,.4f,.4f)); c.Add(new Color(.4f,.4f,.4f)); c.Add(Color.white);
            t.AddRange(new[] { i0, i0 + 2, i0 + 1 });
        }
        var m = new Mesh(); m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }

    public static Mesh BirdMesh()
    {
        var m = new Mesh();
        m.vertices = new[] {
            new Vector3(0,0,0.8f), new Vector3(0,0,-0.6f), new Vector3(-3.2f,0,-0.2f),
            new Vector3(0,0,0.8f), new Vector3(3.2f,0,-0.2f), new Vector3(0,0,-0.6f) };
        m.uv = new[] {
            new Vector2(0,0), new Vector2(0,0), new Vector2(1,0),
            new Vector2(0,0), new Vector2(1,0), new Vector2(0,0) };
        m.triangles = new[] { 0,1,2, 3,4,5 };
        m.RecalculateNormals(); return m;
    }
}
