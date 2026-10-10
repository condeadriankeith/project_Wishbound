#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Bakes game-ready leaf meshes out of Assets/Models/tree.fbx.
//
// tree.fbx is the raw Blender scene: its "Foliage" mesh is the realised particle-instance result
// (5,500 blobs x 334 verts = 1.8M verts / 3.2M tris PER clump), far too heavy to instance across an island.
// This tool reads the real blob layout out of that mesh and rebuilds each clump from a thinned, low-poly
// set of blobs, with the same radial "custom normals" (smooth big-sphere shading) the FBX carries.
//   Assets/Resources/TreeLeaves_Round.asset : the 5 Tree_Blob clumps, with the FBX's own positions/scales
//   Assets/Resources/TreeLeaves_Bush.asset  : a single clump
public static class IslandLeafBaker
{
    const float S = 100f;   // FBX mesh units -> metres (matches the x100 scale on the FBX objects)

    [MenuItem("Island/Bake leaf meshes from tree.fbx")]
    public static void BakeMenu() { Debug.Log(Bake(48, 70)); }

    static readonly Vector3[] IcoV = MakeIcoV();
    static readonly int[] IcoF =
    {
        0,11,5,  0,5,1,   0,1,7,   0,7,10,  0,10,11,
        1,5,9,   5,11,4,  11,10,2, 10,7,6,  7,1,8,
        3,9,4,   3,4,2,   3,2,6,   3,6,8,   3,8,9,
        4,9,5,   2,4,11,  6,2,10,  8,6,7,   9,8,1
    };
    static Vector3[] MakeIcoV()
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        for (int i = 0; i < v.Length; i++) v[i].Normalize();
        return v;
    }

    struct Blob { public Vector3 c; public float r; }

    public static string Bake(int blobsPerTreeClump, int blobsPerBushClump)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/tree.fbx");
        if (root == null) return "Assets/Models/tree.fbx not found";
        Mesh src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/tree.fbx"))
            if (o is Mesh && o.name == "Foliage") src = (Mesh)o;
        if (src == null) return "Foliage mesh not found in tree.fbx";

        // ---- 1. split the Foliage mesh into its blobs (connected components) ----
        var V = src.vertices; var T = src.triangles; int n = V.Length;
        var parent = new int[n]; for (int i = 0; i < n; i++) parent[i] = i;
        System.Func<int, int> find = null;
        find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        for (int i = 0; i < T.Length; i += 3)
        {
            int a = find(T[i]), b = find(T[i + 1]); if (a != b) parent[b] = a;
            a = find(a); int c = find(T[i + 2]); if (a != c) parent[c] = a;
        }
        var sum = new Dictionary<int, Vector3>(); var cnt = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int r = find(i); Vector3 s; sum.TryGetValue(r, out s); sum[r] = s + V[i];
            int k; cnt.TryGetValue(r, out k); cnt[r] = k + 1;
        }
        var cen = new Dictionary<int, Vector3>();
        foreach (var kv in sum) cen[kv.Key] = kv.Value / cnt[kv.Key];
        var rad = new Dictionary<int, float>();
        for (int i = 0; i < n; i++)
        {
            int r = find(i); float d; rad.TryGetValue(r, out d); rad[r] = d + (V[i] - cen[r]).magnitude;
        }
        var blobs = new List<Blob>();
        foreach (var kv in cen) blobs.Add(new Blob { c = kv.Value * S, r = rad[kv.Key] / cnt[kv.Key] * S });

        Vector3 cc = src.bounds.center * S, R = src.bounds.extents * S;
        float rmin = float.MaxValue, rmax = 0f;
        foreach (var b in blobs) { rmin = Mathf.Min(rmin, b.r); rmax = Mathf.Max(rmax, b.r); }

        // ---- 2. the 5 clump transforms exactly as laid out in the FBX ----
        var mats = new List<Matrix4x4>();
        foreach (Transform t in root.transform)
            if (t.name.StartsWith("Tree_Blob_") && !t.name.Contains("|"))
                mats.Add(Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale / S));
        if (mats.Count == 0) mats.Add(Matrix4x4.identity);

        // ---- 3. bake ----
        var round = new MeshData();
        for (int i = 0; i < mats.Count; i++)
            Clump(round, blobs, cc, R, rmin, rmax, blobsPerTreeClump, 1000 + i, mats[i]);
        var bush = new MeshData();
        Clump(bush, blobs, cc, R, rmin, rmax, blobsPerBushClump, 77, Matrix4x4.identity);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        Save(round, "Assets/Resources/TreeLeaves_Round.asset");
        Save(bush, "Assets/Resources/TreeLeaves_Bush.asset");
        AssetDatabase.SaveAssets();

        return "Baked leaves from " + blobs.Count + " source blobs (r " + rmin.ToString("F3") + ".." + rmax.ToString("F3") + " m). "
             + "Round: " + mats.Count + " clumps, " + (round.tris.Count / 3) + " tris. Bush: " + (bush.tris.Count / 3) + " tris.";
    }

    class MeshData { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Color> c = new List<Color>(); public List<int> tris = new List<int>(); }

    static void Save(MeshData d, string path)
    {
        var m = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(d.v); m.SetNormals(d.n); m.SetColors(d.c); m.SetTriangles(d.tris, 0);
        m.RecalculateBounds();
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    // Thin the thousands of source blobs down to `target` well-spread ones on the clump's outer shell, bigger
    // source blobs first, and size them so the shell stays covered.
    static void Clump(MeshData d, List<Blob> blobs, Vector3 cc, Vector3 R, float rmin, float rmax, int target, int seed, Matrix4x4 M)
    {
        var rng = new System.Random(seed);
        var cand = new List<KeyValuePair<float, int>>();
        for (int i = 0; i < blobs.Count; i++)
        {
            Vector3 e = Div(blobs[i].c - cc, R);
            if (e.magnitude < 0.55f) continue;                     // interior blobs are never visible
            float pr = blobs[i].r * (0.7f + 0.6f * (float)rng.NextDouble());
            cand.Add(new KeyValuePair<float, int>(-pr, i));
        }
        cand.Sort((a, b) => a.Key.CompareTo(b.Key));

        float avgR = (R.x + R.y + R.z) / 3f;
        float dmin = Mathf.Sqrt(4f * Mathf.PI * avgR * avgR / (target * 0.85f)) * 0.9f;
        var acc = new List<Blob>();
        for (int pass = 0; pass < 6 && acc.Count < target; pass++, dmin *= 0.88f)
        {
            acc.Clear();
            foreach (var kv in cand)
            {
                var b = blobs[kv.Value];
                bool ok = true;
                for (int j = 0; j < acc.Count && ok; j++) if ((acc[j].c - b.c).sqrMagnitude < dmin * dmin) ok = false;
                if (!ok) continue;
                acc.Add(b);
                if (acc.Count >= target) break;
            }
        }

        Matrix4x4 nm = M.inverse.transpose;
        foreach (var b in acc)
        {
            Vector3 e = Div(b.c - cc, R); float depth = e.magnitude;
            Vector3 p = cc + Vector3.Scale(e / Mathf.Max(depth, 1e-4f), R) * Mathf.Lerp(depth, 0.95f, 0.6f);
            float k = Mathf.InverseLerp(rmin, rmax, b.r);
            float radius = dmin * (0.7f + 0.45f * k);
            Emit(d, p, radius, cc, R, M, nm, rng);
        }
    }

    static Vector3 Div(Vector3 a, Vector3 b) { return new Vector3(a.x / b.x, a.y / b.y, a.z / b.z); }

    // icosahedron blob; normals radial from the clump centre (= the FBX's transferred custom normals)
    static void Emit(MeshData d, Vector3 c, float radius, Vector3 cc, Vector3 R, Matrix4x4 M, Matrix4x4 nm, System.Random r)
    {
        Quaternion q = Quaternion.Euler((float)r.NextDouble() * 360f, (float)r.NextDouble() * 360f, (float)r.NextDouble() * 360f);
        int b0 = d.v.Count;
        Vector3 blobWorld = M.MultiplyPoint3x4(c);
        for (int k = 0; k < IcoV.Length; k++)
        {
            float j = 0.82f + 0.36f * (float)r.NextDouble();
            Vector3 o = q * (IcoV[k] * (radius * j)); o.y *= 0.88f;
            Vector3 p = c + o;
            Vector3 e = Div(p - cc, R); float depth = e.magnitude;
            Vector3 nn = depth > 1e-4f ? e / depth : Vector3.up;
            nn = (nn + Vector3.up * 0.2f).normalized;
            float ao = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0.3f, 1.15f, depth)) * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(e.y * 0.5f + 0.5f));
            d.v.Add(M.MultiplyPoint3x4(p));
            d.n.Add(nm.MultiplyVector(nn).normalized);
            d.c.Add(new Color(ao, ao, ao, 0f));
        }
        for (int f = 0; f < IcoF.Length; f += 3)
        {
            int a = b0 + IcoF[f], b = b0 + IcoF[f + 1], c2 = b0 + IcoF[f + 2];
            Vector3 pa = d.v[a], pb = d.v[b], pc = d.v[c2];
            if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), (pa + pb + pc) / 3f - blobWorld) < 0) { int t = b; b = c2; c2 = t; }
            d.tris.Add(a); d.tris.Add(b); d.tris.Add(c2);
        }
    }
}
#endif
