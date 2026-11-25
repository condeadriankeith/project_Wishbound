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
    readonly Matrix4x4[] visible = new Matrix4x4[1023];
    public Mesh mesh;
    public Material[] mats;
    public ShadowCastingMode shadows = ShadowCastingMode.On;
    public int Count { get; private set; }

    struct Chunk { public Matrix4x4[] m; public int n; public Matrix4x4[] far; public int farN; public Bounds b; public Matrix4x4[] baseM, leafM; public bool touched; }
    readonly List<Chunk> chunks = new List<Chunk>();

    internal IReadOnlyList<Matrix4x4> Instances => src;

    public GhibliBatch(Mesh mesh, Material[] mats) { this.mesh = mesh; this.mats = mats; }

    /// <param name="cell">spatial cell size in metres</param>
    /// <param name="pad">extra bounds around instance positions (mesh extent x max scale)</param>
    /// <param name="farEvery">0/1 = no far LOD; N = far array keeps every Nth instance</param>
    /// <param name="farScale">uniform scale multiplier applied to far instances (compensates for thinning)</param>
    List<Matrix4x4> src; float srcCell; Vector3 srcPad; int srcFarEvery; float srcFarScale;

    /// <summary>Removes every instance whose position satisfies the predicate and rebuilds the chunks. Returns the number removed.</summary>
    public int RemoveWhere(System.Func<Vector3, bool> inside)
    {
        if (src == null || inside == null) return 0;
        int n = src.RemoveAll(m => inside((Vector3)m.GetColumn(3)));
        if (n > 0) Build(src, srcCell, srcPad, srcFarEvery, srcFarScale);
        return n;
    }

    public void Build(List<Matrix4x4> all, float cell, Vector3 pad, int farEvery = 0, float farScale = 1f)
    {
        src = all; srcCell = cell; srcPad = pad; srcFarEvery = farEvery; srcFarScale = farScale;
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
                var c = new Chunk { m = arr, n = n, baseM = (Matrix4x4[])arr.Clone(), b = new Bounds((mn + mx) * 0.5f + Vector3.up * pad.y * 0.5f, (mx - mn) + pad) };
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

    /// <summary>
    /// Leans instances about their trunk base (the comet's blast). lean(p) gives the angle in radians for an instance
    /// standing at p, and the lean points away from origin. reset puts back every instance this has leaned.
    /// Chunks outside reach are skipped, so nothing is paid for outside the blast.
    /// </summary>
    public void Pose(System.Func<Vector3, float> lean, Vector3 origin, float reach, bool reset = false, System.Func<Vector3, float> flutter = null)
    {
        if (mesh == null || chunks.Count == 0) return;
        float reach2 = reach * reach;
        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            if (reset ? !c.touched : c.b.SqrDistance(origin) > reach2) continue;
            bool any = false;
            if (!reset && flutter != null && c.leafM == null) c.leafM = new Matrix4x4[c.n];
            for (int j = 0; j < c.n; j++)
            {
                Matrix4x4 b = c.baseM[j];
                Vector3 p = b.GetColumn(3);
                float a = reset ? 0f : lean(p);
                float leaf = !reset && flutter != null ? flutter(p) : 0f;
                if (a == 0f && leaf == 0f)
                {
                    c.m[j] = b;
                    if (c.leafM != null) c.leafM[j] = b;
                    continue;
                }
                any = true;
                Vector3 away = p - origin; away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = Vector3.right;
                Vector3 axis = Vector3.Cross(Vector3.up, away.normalized);
                Matrix4x4 pivot = Matrix4x4.Translate(p);
                c.m[j] = pivot * Matrix4x4.Rotate(Quaternion.AngleAxis(a * Mathf.Rad2Deg, axis)) * pivot.inverse * b;
                if (c.leafM != null)
                {
                    Vector3 crown = c.m[j].MultiplyPoint3x4(Vector3.up * (mesh.bounds.max.y * .62f));
                    var leanCrown = Quaternion.AngleAxis(leaf * Mathf.Rad2Deg, axis + Vector3.up * .35f);
                    c.leafM[j] = Matrix4x4.Translate(crown) * Matrix4x4.Rotate(leanCrown) *
                        Matrix4x4.Translate(-crown) * c.m[j];
                }
            }
            if (c.far != null)
                for (int j = 0; j < c.n; j += srcFarEvery)
                    c.far[j / srcFarEvery] = srcFarScale == 1f ? c.m[j] : c.m[j] * Matrix4x4.Scale(Vector3.one * srcFarScale);
            c.touched = any;
            chunks[i] = c;
        }
    }

    /// <param name="maxDist">chunks farther than this are skipped</param>
    /// <param name="nearDist">beyond this distance the thinned far array is drawn (if one exists)</param>
    /// <param name="keepDist">chunks within this distance are drawn even when off-screen (so shadows never pop)</param>
    public void Draw(Vector3 camPos, Plane[] planes, float maxDist, float nearDist, float keepDist, Camera camera = null, float sightline = 0f)
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
                bool foliage = !useFar && c.touched && c.leafM != null &&
                    mat.name.IndexOf("leaves", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var matrices = foliage ? c.leafM : arr;
                int count = n;
                if (sightline > 0 && camera && d2 < (sightline+24)*(sightline+24))
                {
                    count = 0;
                    var view = camera.transform;
                    for (int j=0;j<n;j++)
                    {
                        var m = matrices[j];
                        Vector3 centre = view.InverseTransformPoint(m.MultiplyPoint3x4(mesh.bounds.center));
                        float radius = mesh.bounds.extents.magnitude * m.lossyScale.magnitude * .57735f;
                        if (centre.z > -radius && centre.z < sightline &&
                            Mathf.Abs(centre.x) < radius + Mathf.Max(0,centre.z)*.12f &&
                            Mathf.Abs(centre.y) < radius + Mathf.Max(0,centre.z)*.08f) continue;
                        visible[count++] = m;
                    }
                    matrices = visible;
                }
                if (count == 0) continue;
                Graphics.DrawMeshInstanced(mesh, s, mat, matrices, count, null, shadows, true, 0, camera);
            }
        }
    }
}
