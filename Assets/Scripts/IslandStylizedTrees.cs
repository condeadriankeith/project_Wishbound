using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

// Ghibli-style forest: smooth bark trunks with branches that physically reach every leaf canopy,
// puffy subdivided leaf clumps (no low-poly blobs), sparse scatter, high canopies so first-person view stays clear.
public class IslandStylizedTrees : MonoBehaviour
{
    public int seed = 4242;
    public float treeSpacing = 13f;
    public float minHeight = 3f, maxHeight = 240f;
    public float minSlopeY = 0.78f;
    public float forestLow = 0.4f, forestHigh = 0.6f, sparseChance = 0.03f;
    public float minScale = 0.9f, maxScale = 1.6f;
    public bool addBushes = true, addColliders = true;

    [Header("Ghibli package assets")]
    [Tooltip("Use meshes/shaders from '3D set of stylized nature - GHIBLI style' via Resources/GhibliNatureSet. Falls back to the old procedural trees if the set is missing.")]
    public bool usePackageAssets = true;
    GhibliNatureSet natureSet;
    bool pkgMode;
    List<GhibliBatch> pkgBatches;

    [Header("Distance LOD")]
    [Tooltip("Maximum distance at which trees are rendered. Beyond this, chunks are culled.")]
    public float maxRenderDistance = 300f;
    [Tooltip("Distance over which trees fade out. Set 0 to disable fading.")]
    public float fadeDistance = 50f;

    class Variant
    {
        public Mesh mesh; public Material leaf;
        public List<Matrix4x4> m = new List<Matrix4x4>();
        public List<Matrix4x4[]> chunks = new List<Matrix4x4[]>();
        public List<int> counts = new List<int>();
        public List<Bounds> bnds = new List<Bounds>();
    }
    Variant[] vars;
    readonly Plane[] planes = new Plane[6];
    Material trunkMat;

    [SerializeField, HideInInspector] bool started;   // serialized: survives a script reload during Play
    void Start() { if (!Application.isPlaying || !FindFirstObjectByType<IslandRuntimeLoader>()?.useBakedData == true) { StartCoroutine(StartRoutine()); started = true; } }
    void OnEnable() { if (Application.isPlaying && started && vars == null) StartCoroutine(StartRoutine()); }

    IEnumerator StartRoutine()
    {
        started = true;
        MeshFilter mf = null;
        while (true)
        {
            var ib = IslandBuilder.Instance;
            if (ib != null && ib.PathMap != null) { mf = ib.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0) break; }
            yield return null;
        }
        natureSet = usePackageAssets ? GhibliNatureSet.Load() : null;
        if (natureSet != null && natureSet.trees.Count > 0 && natureSet.shrubs.Count > 0)
        {
            // Package mode: same deterministic scatter (positions / yaw / scale untouched), only mesh + materials + size multiplier change.
            pkgMode = true;
            vars = new[] { new Variant(), new Variant(), new Variant(), new Variant() };   // matrix collectors only
            Scatter(mf.sharedMesh);
            yield break;
        }
        var toon = Resources.Load<Shader>("IslandToonFoliage");
        var ghibli = Resources.Load<Shader>("IslandGhibliFoliage");
        if (toon == null || ghibli == null) { Debug.LogError("[IslandStylizedTrees] Shaders missing in Assets/Resources."); yield break; }
        trunkMat = new Material(toon) { enableInstancing = true };
        trunkMat.SetColor("_Tint", new Color(0.85f, 0.66f, 0.48f));
        trunkMat.SetColor("_ShadeColor", new Color(0.52f, 0.46f, 0.66f));

        vars = new[]
        {
            new Variant { mesh = BuildRound(1), leaf = Ramp(ghibli, L(0.05f,0.12f,0.04f), L(0.12f,0.28f,0.08f), L(0.25f,0.48f,0.15f), L(0.55f,0.85f,0.35f)) },
            new Variant { mesh = BuildRound(2), leaf = Ramp(ghibli, L(0.07f,0.13f,0.03f), L(0.20f,0.33f,0.06f), L(0.38f,0.55f,0.10f), L(0.70f,0.88f,0.28f)) },
            new Variant { mesh = BuildPine(),   leaf = Ramp(ghibli, L(0.02f,0.08f,0.06f), L(0.05f,0.20f,0.12f), L(0.10f,0.34f,0.20f), L(0.30f,0.62f,0.40f)) },
            new Variant { mesh = BuildBush(),   leaf = Ramp(ghibli, L(0.06f,0.14f,0.04f), L(0.15f,0.32f,0.08f), L(0.30f,0.54f,0.14f), L(0.60f,0.88f,0.30f)) },
        };
        Scatter(mf.sharedMesh);
    }

    static Color L(float r, float g, float b) { return new Color(r, g, b, 1f).gamma; }

    static Material Ramp(Shader sh, Color c0, Color c1, Color c2, Color c3)
    {
        var m = new Material(sh) { enableInstancing = true };
        m.SetColor("_Col0", c0); m.SetColor("_Col1", c1); m.SetColor("_Col2", c2); m.SetColor("_Col3", c3);
        m.SetFloat("_Step1", 0.25f); m.SetFloat("_Step2", 0.45f); m.SetFloat("_Step3", 0.85f);
        return m;
    }

    bool Ok(double x, double z, Color[] cols, out float h)
    {
        h = Hm(x, z);   // rendered-mesh height: never floats above / sinks into smoothed relief
        if (h < minHeight || h > maxHeight) return false;
        // Ask the shared Surface classifier what the ground IS (no more guessing from vertex colour):
        // trees only grow on living ground (Grass / Field), never on sand, dirt, paths, scree or cliff walls.
        // Use the RENDERED mesh normal (NVm), not the analytic NV: cliff smoothing can make the rendered
        // surface much steeper here than the analytic field, which was letting trees/bushes pass this check
        // and then get placed (at the Hm height) on what is visibly a steep cliff face.
        double ny = NVm(x, z).y;
        if (ny < minSlopeY) return false;
        var st = Surface.Classify(x, z, h, ny, Surface.PathAt(x, z));
        return st == SurfaceType.Grass || st == SurfaceType.Field;
    }

    float Forest(double x, double z, double e)
    {
        float fx = (float)(x + e), fz = (float)(z + e);
        return Mathf.PerlinNoise(fx * 0.005f + seed * 0.37f, fz * 0.005f) * 0.65f
             + Mathf.PerlinNoise(fx * 0.02f + 50f, fz * 0.02f + 50f) * 0.35f;
    }

    void Scatter(Mesh mesh)
    {
        var cols = mesh.colors;
        var rng = new System.Random(seed);
        double e = E;
        float spacing = treeSpacing;
        int G = Mathf.CeilToInt((float)(2 * e / spacing));
        var tp = new Vector2[G * G];
        var has = new bool[G * G];
        GameObject cgo = null;
        if (addColliders) { var oldC = GameObject.Find("TreeColliders"); if (oldC != null) Destroy(oldC); cgo = new GameObject("TreeColliders"); }
        int trees = 0, bushes = 0;

        for (int gz = 0; gz < G; gz++)
        for (int gx = 0; gx < G; gx++)
        {
            double x = -e + (gx + 0.15 + rng.NextDouble() * 0.7) * spacing;
            double z = -e + (gz + 0.15 + rng.NextDouble() * 0.7) * spacing;
            float r1 = (float)rng.NextDouble(), r2 = (float)rng.NextDouble(), r3 = (float)rng.NextDouble(), r4 = (float)rng.NextDouble();
            float h;
            if (!Ok(x, z, cols, out h)) continue;
            float dens = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(forestLow, forestHigh, Forest(x, z, e)));
            if (r1 > dens * 0.55f + sparseChance) continue;

            float tn = Mathf.PerlinNoise((float)(x + e) * 0.012f + 200f, (float)(z + e) * 0.012f);
            int vi = (tn > 0.62f || (h > 90f && r2 < 0.8f)) ? 2 : (r2 < 0.5f ? 0 : 1);
            float s = Mathf.Lerp(minScale, maxScale, r3);
            Vector3 pos = U(x, h - 0.3, z);
            vars[vi].m.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, r4 * 360f, 0), Vector3.one * s));
            tp[gz * G + gx] = new Vector2(pos.x, pos.z); has[gz * G + gx] = true;
            trees++;
            if (cgo != null)
            {
                var cc = cgo.AddComponent<CapsuleCollider>();
                cc.radius = 0.6f * s; cc.height = 7f * s; cc.center = pos + Vector3.up * 3.5f * s;
            }
        }

        if (addBushes)
        {
            float bs = 9f;
            int BG = Mathf.CeilToInt((float)(2 * e / bs));
            for (int gz = 0; gz < BG; gz++)
            for (int gx = 0; gx < BG; gx++)
            {
                double x = -e + (gx + rng.NextDouble()) * bs;
                double z = -e + (gz + rng.NextDouble()) * bs;
                float r1 = (float)rng.NextDouble(), r3 = (float)rng.NextDouble(), r4 = (float)rng.NextDouble();
                float h;
                if (!Ok(x, z, cols, out h)) continue;
                float dens = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(forestLow, forestHigh, Forest(x, z, e)));
                if (r1 > 0.05f + 0.25f * dens) continue;
                int cx = Mathf.Clamp((int)((x + e) / spacing), 0, G - 1), cz = Mathf.Clamp((int)((z + e) / spacing), 0, G - 1);
                Vector3 pos = U(x, h - 0.15, z);
                bool near = false;
                for (int dz = -1; dz <= 1 && !near; dz++)
                for (int dx = -1; dx <= 1 && !near; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix < 0 || iz < 0 || ix >= G || iz >= G || !has[iz * G + ix]) continue;
                    if ((tp[iz * G + ix] - new Vector2(pos.x, pos.z)).sqrMagnitude < 25f) near = true;
                }
                if (near) continue;
                float s = Mathf.Lerp(0.6f, 1.05f, r3);
                vars[3].m.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, r4 * 360f, 0), new Vector3(s * 1.15f, s, s * 1.15f)));
                bushes++;
            }
        }

        if (pkgMode) { BuildPackageBatches(trees, bushes); return; }

        foreach (var v in vars)
        {
            var cellMap = new Dictionary<long, List<Matrix4x4>>();
            foreach (var m in v.m)
            {
                Vector4 p = m.GetColumn(3);
                long k = ((long)Mathf.FloorToInt(p.x / 80f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 80f);
                List<Matrix4x4> lst;
                if (!cellMap.TryGetValue(k, out lst)) { lst = new List<Matrix4x4>(); cellMap[k] = lst; }
                lst.Add(m);
            }
            foreach (var lst in cellMap.Values)
                for (int i = 0; i < lst.Count; i += 1023)
                {
                    int n = Mathf.Min(1023, lst.Count - i);
                    var arr = lst.GetRange(i, n).ToArray();
                    Vector3 mn = arr[0].GetColumn(3), mx = mn;
                    for (int j = 1; j < n; j++) { Vector3 q = arr[j].GetColumn(3); mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q); }
                    v.chunks.Add(arr); v.counts.Add(n);
                    v.bnds.Add(new Bounds((mn + mx) * 0.5f + Vector3.up * 20f, (mx - mn) + new Vector3(30f, 50f, 30f)));
                }
        }
        Debug.Log("[IslandStylizedTrees] " + trees + " trees, " + bushes + " bushes planted.");
    }

    // ---------------------------------------------------------------------
    //  Ghibli package mode
    //  vars[0] round1 / vars[1] round2 / vars[2] pine / vars[3] bush keep their ORIGINAL matrices.
    //  Each matrix is re-assigned (deterministically, by position hash) to a package piece from that slot's pool
    //  and uniformly scaled by  OldTop / pieceTop  so the final height equals the old tree's height.
    // ---------------------------------------------------------------------
    // measured top of the old procedural meshes above their origin (m): round1, round2, pine, bush
    static readonly float[] OldTop = { 13.31f, 16.32f, 18.60f, 1.65f };
    static readonly string[][] Pool =
    {
        new[] { "Tree_04", "Tree_04", "Tree_01", "Tree_01", "Tree_03" },   // round1: wide round canopy
        new[] { "Tree_02", "Tree_02", "Tree_01", "Tree_03" },              // round2: taller
        new[] { "Tree_05", "Tree_05", "Tree_02" },                          // pine slot (highland): narrow
        new[] { "Shrubs_01", "Shrubs_02", "Shrubs_03" },                    // bushes
    };
    const string HeroTree = "Big Tree";   // pink blossom tree: ~1.5% of the round1 slot

    void BuildPackageBatches(int trees, int bushes)
    {
        var lists = new Dictionary<string, List<Matrix4x4>>();
        var maxF = new Dictionary<string, float>();
        var piece = new Dictionary<string, GhibliPiece>();
        for (int vi = 0; vi < vars.Length; vi++)
        {
            var src = vi == 3 ? natureSet.shrubs : natureSet.trees;
            foreach (var m in vars[vi].m)
            {
                Vector4 p = m.GetColumn(3);
                uint h = (uint)(((int)(p.x * 10f)) * 73856093 ^ ((int)(p.z * 10f)) * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                string nm = (vi == 0 && h % 1000 < 15 && GhibliNatureSet.Find(src, HeroTree) != null) ? HeroTree : Pool[vi][(int)((h >> 8) % (uint)Pool[vi].Length)];
                GhibliPiece pc;
                if (!piece.TryGetValue(nm, out pc)) { pc = GhibliNatureSet.Find(src, nm); if (pc == null) continue; piece[nm] = pc; lists[nm] = new List<Matrix4x4>(); maxF[nm] = 0f; }
                float f = OldTop[vi] / Mathf.Max(0.1f, pc.maxY);
                lists[nm].Add(m * Matrix4x4.Scale(Vector3.one * f));
                if (f > maxF[nm]) maxF[nm] = f;
            }
        }
        pkgBatches = new List<GhibliBatch>();
        foreach (var kv in lists)
        {
            var pc = piece[kv.Key];
            float k = maxF[kv.Key] * 1.7f;   // 1.7 = max per-instance scale variation (1.6 trees / 1.05*1.15 bushes)
            var b = new GhibliBatch(pc.mesh, pc.materials) { shadows = UnityEngine.Rendering.ShadowCastingMode.On };
            b.Build(kv.Value, 80f, new Vector3(pc.MaxXZ * k, pc.maxY * k, pc.MaxXZ * k));
            pkgBatches.Add(b);
        }
        Debug.Log("[IslandStylizedTrees] Ghibli package mode: " + trees + " trees, " + bushes + " shrubs in " + pkgBatches.Count + " batches.");
    }

    public void LoadBakedData(TreeScatterData data)
    {
        if (data == null || data.variants == null) return;

        var ib = IslandBuilder.Instance;
        if (ib == null || ib.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            Debug.LogError("[IslandStylizedTrees] Island not built yet.");
            return;
        }

        var toon = Resources.Load<Shader>("IslandToonFoliage");
        var ghibli = Resources.Load<Shader>("IslandGhibliFoliage");
        if (toon == null || ghibli == null) { Debug.LogError("[IslandStylizedTrees] Shaders missing in Assets/Resources."); return; }

        trunkMat = new Material(toon) { enableInstancing = true };
        trunkMat.SetColor("_Tint", new Color(0.85f, 0.66f, 0.48f));
        trunkMat.SetColor("_ShadeColor", new Color(0.52f, 0.46f, 0.66f));

        vars = new Variant[data.variants.Count];
        for (int i = 0; i < data.variants.Count; i++)
        {
            var src = data.variants[i];
            var mesh = Resources.Load<Mesh>(src.meshPath);
            if (mesh == null)
            {
                // Fallback: rebuild mesh procedurally
                mesh = (i < 2) ? BuildRound(i + 1) : (i == 2 ? BuildPine() : BuildBush());
            }
            vars[i] = new Variant
            {
                mesh = mesh,
                leaf = src.leafMaterial,
                chunks = new List<Matrix4x4[]>(),
                counts = new List<int>(),
                bnds = new List<Bounds>()
            };
            foreach (var chunk in src.chunks)
            {
                vars[i].chunks.Add(chunk.matrices);
                vars[i].counts.Add(chunk.count);
                vars[i].bnds.Add(chunk.bounds);
            }
        }

        Debug.Log($"[IslandStylizedTrees] Loaded baked tree data: {data.variants.Count} variants.");
    }

    void Update()
    {
        if (vars == null) return;
        var cam = Camera.main; if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 cp = cam.transform.position;
        if (pkgBatches != null)
        {
            foreach (var pb in pkgBatches) pb.Draw(cp, planes, maxRenderDistance, 1e9f, 100f);   // keep <100 m chunks so shadows never pop
            return;
        }
        float maxDist2 = maxRenderDistance * maxRenderDistance;
        float fadeStart2 = (maxRenderDistance - fadeDistance) * (maxRenderDistance - fadeDistance);
        foreach (var v in vars)
            for (int i = 0; i < v.chunks.Count; i++)
            {
                float dist2 = v.bnds[i].SqrDistance(cp);
                if (dist2 > maxDist2) continue;
                
                float fade = 1f;
                if (fadeDistance > 0f && dist2 > fadeStart2)
                {
                    float dist = Mathf.Sqrt(dist2);
                    fade = 1f - Mathf.Clamp01((dist - (maxRenderDistance - fadeDistance)) / fadeDistance);
                    if (fade <= 0f) continue;
                }

                // keep anything on screen OR within shadow range so tree shadows never pop
                if (!GeometryUtility.TestPlanesAABB(planes, v.bnds[i]) && dist2 > 100f * 100f) continue;
                
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_Fade", fade);
                
                if (v.mesh.subMeshCount > 1)
                {
                    Graphics.DrawMeshInstanced(v.mesh, 0, trunkMat, v.chunks[i], v.counts[i], mpb, UnityEngine.Rendering.ShadowCastingMode.On, true);
                    Graphics.DrawMeshInstanced(v.mesh, 1, v.leaf, v.chunks[i], v.counts[i], mpb, UnityEngine.Rendering.ShadowCastingMode.On, true);
                }
                else
                    Graphics.DrawMeshInstanced(v.mesh, 0, v.leaf, v.chunks[i], v.counts[i], mpb, UnityEngine.Rendering.ShadowCastingMode.On, true);
            }
    }

    // =====================================================================
    //  Mesh building
    // =====================================================================
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

    // subdivided icosphere cache (level 1 = 42 verts, level 2 = 162 verts)
    static Vector3[][] sV = new Vector3[3][];
    static int[][] sF = new int[3][];
    static void Ico(int lv)
    {
        if (sV[lv] != null) return;
        var v = new List<Vector3>(IcoV); var f = new List<int>(IcoF);
        for (int s = 0; s < lv; s++)
        {
            var nf = new List<int>(); var mid = new Dictionary<long, int>();
            System.Func<int, int, int> M = (a, b) =>
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int id;
                if (!mid.TryGetValue(k, out id)) { id = v.Count; v.Add(((v[a] + v[b]) * 0.5f).normalized); mid[k] = id; }
                return id;
            };
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = M(a, b), bc = M(b, c), ca = M(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        sV[lv] = v.ToArray(); sF[lv] = f.ToArray();
    }

    // submesh 0 = bark, submesh 1 = leaves
    class MB
    {
        public List<Vector3> verts = new List<Vector3>(), nrm = new List<Vector3>();
        public List<Color> cols = new List<Color>();
        public List<int>[] t = { new List<int>(), new List<int>() };

        // One puffy leaf clump. Normals blend canopy-radial + blob-radial for soft, clumpy Ghibli shading.
        public void Blob(Vector3 c, float rad, Vector3 cc, Vector3 cr, float height, System.Random r, int lv, float sq)
        {
            Ico(lv);
            var iv = sV[lv]; var ifc = sF[lv];
            Quaternion q = Quaternion.Euler((float)r.NextDouble() * 360f, (float)r.NextDouble() * 360f, (float)r.NextDouble() * 360f);
            float ns = (float)r.NextDouble() * 100f;
            int b0 = verts.Count;
            for (int k = 0; k < iv.Length; k++)
            {
                Vector3 d = iv[k];
                float nz = Mathf.PerlinNoise(d.x * 1.8f + ns, (d.y + d.z) * 1.8f + ns);
                Vector3 o = q * (d * (rad * (0.84f + 0.32f * nz)));
                o.y *= sq;
                Vector3 p = c + o;
                Vector3 e = Vector3.Scale(p - cc, new Vector3(1f / cr.x, 1f / cr.y, 1f / cr.z));
                float depth = e.magnitude;
                Vector3 radial = depth > 1e-4f ? e / depth : Vector3.up;
                Vector3 n = (radial * 0.7f + o.normalized * 0.5f + Vector3.up * 0.2f).normalized;
                float ao = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.3f, 1.15f, depth)) * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(radial.y * 0.5f + 0.5f));
                verts.Add(p); nrm.Add(n); cols.Add(new Color(ao, ao, ao, Mathf.Clamp01(p.y / height)));
            }
            for (int f = 0; f < ifc.Length; f += 3)
            {
                int a = b0 + ifc[f], b = b0 + ifc[f + 1], d2 = b0 + ifc[f + 2];
                Vector3 pa = verts[a], pb = verts[b], pc = verts[d2];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), (pa + pb + pc) / 3f - c) < 0) { int tmp = b; b = d2; d2 = tmp; }
                t[1].Add(a); t[1].Add(b); t[1].Add(d2);
            }
        }

        // Smooth tapered bark tube with ridged grooves, root flare, gentle wobble and vertex-colour bark streaks.
        public void Tube(Vector3 a, Vector3 c, float r0, float r1, int sides, int rings, float seed, float flare)
        {
            Vector3 dir = c - a; Vector3 f = dir.normalized;
            Vector3 u = Vector3.Cross(f, Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 w = Vector3.Cross(f, u);
            int b0 = verts.Count;
            for (int i = 0; i <= rings; i++)
            {
                float tt = i / (float)rings;
                float wob = Mathf.Sin(tt * Mathf.PI) * 0.35f;
                Vector3 ctr = a + dir * tt + (u * (Mathf.PerlinNoise(seed, tt * 2f) - 0.5f) + w * (Mathf.PerlinNoise(seed + 9f, tt * 2f) - 0.5f)) * wob * 2f;
                float rad = Mathf.Lerp(r0, r1, tt);
                float fl = Mathf.Pow(1f - tt, 5f);
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k / (float)sides * Mathf.PI * 2f;
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    Vector3 rd = u * ca + w * sa;
                    float n = Mathf.PerlinNoise(ca * 1.4f + seed + 10f, sa * 1.4f + tt * 5f + seed);
                    float ridge = 0.5f + 0.5f * Mathf.Sin(ang * 6f + n * 5f + tt * 2f);
                    float rr = rad * (0.9f + 0.1f * ridge + 0.12f * (n - 0.5f));
                    rr *= 1f + flare * fl * (0.4f + 0.9f * Mathf.Abs(Mathf.Sin(ang * 2.5f + seed)));
                    verts.Add(ctr + rd * rr);
                    nrm.Add(rd);
                    float g = Mathf.Lerp(0.72f, 1.1f, ridge) * Mathf.Lerp(0.78f, 1f, n) * Mathf.Lerp(0.65f, 0.98f, Mathf.Clamp01(ctr.y / 8f));
                    cols.Add(new Color(g, g, g, 0f));
                }
            }
            for (int i = 0; i < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                int a0 = b0 + i * (sides + 1) + k, a1 = a0 + 1, a2 = a0 + sides + 1, a3 = a2 + 1;
                t[0].Add(a0); t[0].Add(a1); t[0].Add(a2);
                t[0].Add(a1); t[0].Add(a3); t[0].Add(a2);
            }
        }
    }

    static Mesh Bake(MB b)
    {
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(b.verts); m.SetNormals(b.nrm); m.SetColors(b.cols);
        if (b.t[0].Count > 0) { m.subMeshCount = 2; m.SetTriangles(b.t[0], 0); m.SetTriangles(b.t[1], 1); }
        else { m.subMeshCount = 1; m.SetTriangles(b.t[1], 0); }
        m.bounds = new Bounds(new Vector3(0, 10, 0), new Vector3(30, 40, 30));
        return m;
    }

    // Canopy = big clumps in a shell + small clumps on the surface for leafy bumps
    static void Canopy(MB b, Vector3 c, Vector3 R, int sd, float height, int nBig, int nSmall, float centre = 0.7f, float sq = 0.85f)
    {
        var r = new System.Random(sd * 7919 + 13);
        float avg = (R.x + R.y + R.z) / 3f;
        b.Blob(c, avg * centre, c, R, height, r, 2, sq);
        float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < nBig; i++)
        {
            Vector3 d = Dir(i, nBig, ga, r);
            Vector3 p = c + Vector3.Scale(d, R) * (0.55f + 0.3f * (float)r.NextDouble());
            b.Blob(p, avg * (0.38f + 0.12f * (float)r.NextDouble()), c, R, height, r, 2, sq);
        }
        for (int i = 0; i < nSmall; i++)
        {
            Vector3 d = Dir(i, nSmall, ga, r);
            Vector3 p = c + Vector3.Scale(d, R) * (0.88f + 0.15f * (float)r.NextDouble());
            b.Blob(p, avg * 0.2f * (0.7f + 0.6f * (float)r.NextDouble()), c, R, height, r, 1, sq);
        }
    }

    static Vector3 Dir(int i, int n, float ga, System.Random r)
    {
        float y = 1f - 2f * (i + 0.5f) / n, rr = Mathf.Sqrt(1f - y * y), th = ga * i + (float)r.NextDouble() * 0.5f;
        return new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr);
    }

    // branch from trunk straight into a canopy centre -> leaves are always attached
    static void Branch(MB b, Vector3 from, Vector3 to, float sd) { b.Tube(from, to, 0.3f, 0.1f, 7, 4, sd, 0f); }

    static Mesh BuildRound(int kind)
    {
        var b = new MB();
        if (kind == 1)
        {
            b.Tube(new Vector3(0, -0.6f, 0), new Vector3(0, 9f, 0), 0.9f, 0.3f, 12, 12, 1f, 0.9f);
            Branch(b, new Vector3(0, 5f, 0), new Vector3(2.9f, 7.9f, 0.7f), 2f);
            Branch(b, new Vector3(0, 5.6f, 0), new Vector3(-2.5f, 8.2f, -1.3f), 3f);
            Canopy(b, new Vector3(0, 9.4f, 0), new Vector3(3.7f, 2.9f, 3.7f), 1, 16f, 9, 16);
            Canopy(b, new Vector3(2.9f, 7.9f, 0.7f), new Vector3(2.3f, 1.9f, 2.3f), 2, 16f, 6, 10);
            Canopy(b, new Vector3(-2.5f, 8.2f, -1.3f), new Vector3(2.3f, 1.9f, 2.3f), 3, 16f, 6, 10);
            Canopy(b, new Vector3(0.3f, 11.6f, 0.3f), new Vector3(2.2f, 1.7f, 2.2f), 4, 16f, 5, 8);
        }
        else
        {
            b.Tube(new Vector3(0, -0.6f, 0), new Vector3(0, 11.5f, 0), 0.75f, 0.28f, 12, 14, 5f, 0.9f);
            Branch(b, new Vector3(0, 6.8f, 0), new Vector3(2f, 9.8f, 0.3f), 6f);
            Branch(b, new Vector3(0, 7.4f, 0), new Vector3(-1.8f, 10.2f, -0.8f), 7f);
            Canopy(b, new Vector3(0, 12f, 0), new Vector3(3.2f, 3.4f, 3.2f), 5, 18f, 9, 16);
            Canopy(b, new Vector3(2f, 9.8f, 0.3f), new Vector3(2.2f, 2f, 2.2f), 6, 18f, 5, 9);
            Canopy(b, new Vector3(-1.8f, 10.2f, -0.8f), new Vector3(2.1f, 2f, 2.1f), 7, 18f, 5, 9);
            Canopy(b, new Vector3(0, 14.4f, 0), new Vector3(2f, 2f, 2f), 8, 18f, 5, 8);
        }
        return Bake(b);
    }

    // pine: trunk runs through every tier up to the tip, so tiers can never float
    static Mesh BuildPine()
    {
        var b = new MB();
        b.Tube(new Vector3(0, -0.6f, 0), new Vector3(0, 18f, 0), 0.75f, 0.08f, 12, 16, 11f, 0.8f);
        for (int i = 0; i < 5; i++)
        {
            float y0 = 5.5f + i * 2.9f, rad = 4f - i * 0.75f;
            Canopy(b, new Vector3(0, y0, 0), new Vector3(rad, 1.5f, rad), 20 + i, 18f, 7, 12, 0.45f, 0.6f);
        }
        Canopy(b, new Vector3(0, 17.4f, 0), new Vector3(0.8f, 1.4f, 0.8f), 30, 18f, 3, 4, 0.6f, 0.9f);
        return Bake(b);
    }

    // low rounded bush, shorter than eye height
    static Mesh BuildBush()
    {
        var b = new MB();
        Canopy(b, new Vector3(0, 0.75f, 0), new Vector3(1.3f, 0.85f, 1.3f), 31, 4f, 5, 8);
        Canopy(b, new Vector3(1f, 0.6f, 0.5f), new Vector3(0.9f, 0.6f, 0.9f), 37, 4f, 3, 5);
        Canopy(b, new Vector3(-0.9f, 0.65f, -0.5f), new Vector3(0.9f, 0.6f, 0.9f), 43, 4f, 3, 5);
        return Bake(b);
    }
}
