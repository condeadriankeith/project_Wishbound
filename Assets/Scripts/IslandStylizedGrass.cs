using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Grass across the Island terrain using the 'GHIBLI style' package grass clumps (Gras_01..03) and its
/// 'Shader Graphs/Grass' material (via Resources/GhibliNatureSet). Placement rules are unchanged from the
/// previous custom-blade system (slope, cliff margin, Surface.Classify / GrassDensity).
/// Scattering is time-sliced over several frames so Play does not hitch. Far chunks draw a thinned, enlarged
/// copy of their clumps (cheap distance LOD).
/// Previous implementation: IslandStylizedGrass.cs.bak_pre_ghibli.txt   Docs: Assets/Docs/GHIBLI_NATURE_INTEGRATION.md
/// </summary>
public class IslandStylizedGrass : MonoBehaviour
{
    [Header("Legacy (old custom StylizedGrass shader - unused in Ghibli package mode)")]
    public Mesh grassMesh;
    public Material sourceMaterial;

    [Header("Ghibli package grass")]
    public bool usePackageAssets = true;
    [Tooltip("Total grass clumps planted over the terrain.")]
    public int clumpCount = 350000;
    public int seed = 777;
    public float minHeight = 1.5f;
    public float maxHeight = 330f;
    // Minimum normal.y for planting. 0.85 ~ 32 deg - keeps clumps off cliff faces.
    public float minSlopeY = 0.85f;
    // Radius (m) used to sample 4 neighbour normals; rejects points near cliff edges.
    public float cliffMarginMetres = 3f;
    [Tooltip("Uniform clump scale range (package clumps are ~0.3 m tall).")]
    public float minScale = 1.25f, maxScale = 2.0f;
    [Tooltip("Milliseconds of scatter work per frame while the grass is being planted.")]
    public float scatterBudgetMs = 6f;

    [Header("Distance LOD")]
    public float maxRenderDistance = 120f;
    [Tooltip("Beyond this distance chunks draw the thinned 'far' clumps.")]
    public float nearDistance = 35f;
    public int farEvery = 3;
    public float farScale = 1.7f;

    // ===== STYLIZED GRASS MASTER TOGGLE =====
    // true = grass on. (Was false 'for fast load times' before the Ghibli package; scatter is now time-sliced.)
    public const bool GRASS_ENABLED = true;

    GhibliNatureSet natureSet;
    readonly List<GhibliBatch> batches = new List<GhibliBatch>();
    readonly Plane[] planes = new Plane[6];
    [SerializeField, HideInInspector] bool started;   // serialized: survives a script reload during Play
    bool building;

    void Start()
    {
        if (!GRASS_ENABLED) return;
        if (!Application.isPlaying) return;
        var ldr = FindFirstObjectByType<IslandRuntimeLoader>();
        if (ldr != null && ldr.useBakedData && ldr.grassScatterData != null) return; // baked grass loaded by loader
        started = true;
        StartCoroutine(Reinit());
    }
    void OnEnable() { if (Application.isPlaying && started && batches.Count == 0 && !building) StartCoroutine(Reinit()); }

    IEnumerator Reinit()
    {
        while (IslandBuilder.Instance == null || IslandBuilder.Instance.PathMap == null) yield return null;
        yield return null;
        yield return StartCoroutine(Init());
    }

    // kept for the baked-data loader; baked grass is not used by the Ghibli package renderer
    public void LoadBakedData(GrassScatterData data, Texture2D terrainColorTex)
    {
        if (!GRASS_ENABLED || started) return;
        started = true;
        StartCoroutine(Reinit());
    }

    IEnumerator Init()
    {
        building = true;
        batches.Clear();
        natureSet = usePackageAssets ? GhibliNatureSet.Load() : null;
        if (natureSet == null || natureSet.grass.Count == 0) { Debug.LogError("[IslandStylizedGrass] GhibliNatureSet missing (run menu Island/Ghibli/Build Nature Set)"); building = false; yield break; }
        var ib = IslandBuilder.Instance;
        var mf = ib != null ? ib.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[IslandStylizedGrass] Island mesh not built yet."); building = false; yield break; }

        int np = natureSet.grass.Count;
        var lists = new List<Matrix4x4>[np];
        for (int i = 0; i < np; i++) lists[i] = new List<Matrix4x4>(clumpCount / np + 16);
        // piece weights: Gras_01 40%, Gras_02 35%, Gras_03 25% (extra pieces share the remainder evenly)
        float[] w = new float[np];
        for (int i = 0; i < np; i++) w[i] = i == 0 ? 0.40f : (i == 1 ? 0.35f : 0.25f / Mathf.Max(1, np - 2));

        var rng = new System.Random(seed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = System.Diagnostics.Stopwatch.StartNew();
        int accepted = 0, attempts = 0, guard = clumpCount * 60;
        double e = E;
        while (accepted < clumpCount && attempts++ < guard)
        {
            double x = (rng.NextDouble() * 2 - 1) * e;
            double z = (rng.NextDouble() * 2 - 1) * e;
            float r1 = (float)rng.NextDouble(), r2 = (float)rng.NextDouble(), r3 = (float)rng.NextDouble(), r4 = (float)rng.NextDouble();
            if (sw.Elapsed.TotalMilliseconds >= scatterBudgetMs) { yield return null; sw.Restart(); }

            float h = Hm(x, z);   // rendered-mesh height: clumps sit on visible ground
            if (h < minHeight || h > maxHeight) continue;
            float normalY = NV(x, z).y;   // one accurate analytic-normal check at the candidate point
            if (normalY < minSlopeY) continue;
            // Cheap cliff-margin check: approximate neighbour slope from mesh HEIGHT deltas (Hm, already
            // computed / cached-cheap) instead of 4x more NV() calls (each NV = 2x expensive H() evaluations).
            // This cut grass scatter time drastically (profiled: NV() was ~85% of per-candidate cost) while
            // keeping ~the same acceptance rate and keeping clumps off cliff edges.
            float m = cliffMarginMetres;
            float hx1 = Hm(x + m, z), hx2 = Hm(x - m, z), hz1 = Hm(x, z + m), hz2 = Hm(x, z - m);
            float maxDrop = Mathf.Max(Mathf.Max(Mathf.Abs(h - hx1), Mathf.Abs(h - hx2)), Mathf.Max(Mathf.Abs(h - hz1), Mathf.Abs(h - hz2)));
            // minSlopeY ~ 0.85 corresponds to ~32deg; over `m` metres that's roughly m*tan(32deg) rise/fall.
            float maxDropAllowed = m * 0.625f;  // tan(32deg) ~= 0.625
            if (maxDrop > maxDropAllowed) continue;
            double pth = Surface.PathAt(x, z);
            var st = Surface.Classify(x, z, h, normalY, pth);
            double dens = Surface.GrassDensityAt(x, z, st, h, normalY, pth);   // also thins along real RoadBuild roads
            if (dens <= 0) continue;
            dens *= Surface.WetSuppression(x, z);   // keep grass off damp riverbanks / lake edges
            if (dens <= 0 || rng.NextDouble() > dens) continue;

            int pi = 0; float acc = 0f;
            for (; pi < np - 1; pi++) { acc += w[pi]; if (r1 < acc) break; }
            float s = Mathf.Lerp(minScale, maxScale, r2);
            var rot = Quaternion.Euler(0f, r3 * 360f, 0f);
            lists[pi].Add(Matrix4x4.TRS(U(x, h - 0.03, z), rot, new Vector3(s, s * (0.85f + 0.4f * r4), s)));
            accepted++;
        }

        for (int i = 0; i < np; i++)
        {
            var pc = natureSet.grass[i];
            var b = new GhibliBatch(pc.mesh, pc.materials) { shadows = UnityEngine.Rendering.ShadowCastingMode.Off };
            b.Build(lists[i], 64f, new Vector3(pc.MaxXZ * maxScale * 1.6f, pc.maxY * maxScale * 1.5f, pc.MaxXZ * maxScale * 1.6f), farEvery, farScale);
            batches.Add(b);
        }
        building = false;
        Debug.Log("[IslandStylizedGrass] Ghibli package grass: " + accepted + " clumps planted in " + total.Elapsed.TotalSeconds.ToString("F1") + " s (attempts " + attempts + ").");
    }

    void Update()
    {
        if (batches.Count == 0) return;
        var cam = Camera.main; if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 cp = cam.transform.position;
        foreach (var b in batches) b.Draw(cp, planes, maxRenderDistance, nearDistance, 0f);
    }
}
