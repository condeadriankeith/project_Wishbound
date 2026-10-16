using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Chunked GPU-instanced renderer for one mesh (+ per-submesh materials).
/// Instances are bucketed into square cells (&lt;=1023 per draw), frustum + distance culled,
/// and can carry a reduced "far" array (every Nth instance, enlarged) for cheap distance LOD.
/// Used by the Ghibli tree / shrub / grass / flower / rock renderers.
/// </summary>
public class GhibliBatch
{
    public Mesh mesh;
    public Material[] mats;
    public ShadowCastingMode shadows = ShadowCastingMode.On;
    public int Count { get; private set; }

    struct Chunk { public Matrix4x4[] m; public int n; public Matrix4x4[] far; public int farN; public Bounds b; }
    readonly List<Chunk> chunks = new List<Chunk>();

    public GhibliBatch(Mesh mesh, Material[] mats) { this.mesh = mesh; this.mats = mats; }

    /// <param name="cell">spatial cell size in metres</param>
    /// <param name="pad">extra bounds around instance positions (mesh extent x max scale)</param>
    /// <param name="farEvery">0/1 = no far LOD; N = far array keeps every Nth instance</param>
    /// <param name="farScale">uniform scale multiplier applied to far instances (compensates for thinning)</param>
    public void Build(List<Matrix4x4> all, float cell, Vector3 pad, int farEvery = 0, float farScale = 1f)
    {
        chunks.Clear();
        Count = all.Count;
        var map = new Dictionary<long, List<Matrix4x4>>();
        for (int i = 0; i < all.Count; i++)
        {
            Vector4 p = all[i].GetColumn(3);
            long k = ((long)Mathf.FloorToInt(p.x / cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / cell);
            List<Matrix4x4> lst;
            if (!map.TryGetValue(k, out lst)) { lst = new List<Matrix4x4>(); map[k] = lst; }
            lst.Add(all[i]);
        }
        foreach (var lst in map.Values)
            for (int i = 0; i < lst.Count; i += 1023)
            {
                int n = Mathf.Min(1023, lst.Count - i);
                var arr = lst.GetRange(i, n).ToArray();
                Vector3 mn = arr[0].GetColumn(3), mx = mn;
                for (int j = 1; j < n; j++) { Vector3 q = arr[j].GetColumn(3); mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q); }
                var c = new Chunk { m = arr, n = n, b = new Bounds((mn + mx) * 0.5f + Vector3.up * pad.y * 0.5f, (mx - mn) + pad) };
                if (farEvery > 1)
                {
                    var far = new List<Matrix4x4>(n / farEvery + 1);
                    var sc = Matrix4x4.Scale(Vector3.one * farScale);
                    for (int j = 0; j < n; j += farEvery) far.Add(farScale == 1f ? arr[j] : arr[j] * sc);
                    c.far = far.ToArray(); c.farN = far.Count;
                }
                chunks.Add(c);
            }
    }

    /// <param name="maxDist">chunks farther than this are skipped</param>
    /// <param name="nearDist">beyond this distance the thinned far array is drawn (if one exists)</param>
    /// <param name="keepDist">chunks within this distance are drawn even when off-screen (so shadows never pop)</param>
    public void Draw(Vector3 camPos, Plane[] planes, float maxDist, float nearDist, float keepDist)
    {
        if (mesh == null || mats == null || chunks.Count == 0) return;
        float maxD2 = maxDist * maxDist, nearD2 = nearDist * nearDist, keepD2 = keepDist * keepDist;
        int subs = mesh.subMeshCount;
        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            float d2 = c.b.SqrDistance(camPos);
            if (d2 > maxD2) continue;
            if (d2 > keepD2 && !GeometryUtility.TestPlanesAABB(planes, c.b)) continue;
            bool useFar = c.farN > 0 && d2 > nearD2;
            var arr = useFar ? c.far : c.m;
            int n = useFar ? c.farN : c.n;
            for (int s = 0; s < subs; s++)
            {
                var mat = mats[Mathf.Min(s, mats.Length - 1)];
                if (mat == null) continue;
                Graphics.DrawMeshInstanced(mesh, s, mat, arr, n, null, shadows, true);
            }
        }
    }
}
