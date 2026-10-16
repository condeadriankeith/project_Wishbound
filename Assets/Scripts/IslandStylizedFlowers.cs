using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Flowers across the Island terrain using the GHIBLI package flower prefabs (Flower_01_1 .. Flower_13)
/// via GhibliBatch GPU instancing. Follows the same lifecycle / placement pattern as IslandStylizedGrass
/// (time-sliced scatter, slope + cliff-margin rejection, Surface.Classify for valid ground), but flowers
/// only grow in sparse noise-driven PATCHES on grass/field ground (not uniformly like grass) - see
/// PatchWeight(). Phase 5 of the GHIBLI integration. Docs: Assets/Docs/GHIBLI_NATURE_INTEGRATION.md
/// </summary>
public class IslandStylizedFlowers : MonoBehaviour
{
    public bool usePackageAssets = true;
    [Tooltip("Total flower instances considered across the terrain (most patch area rejects; actual planted count is lower).")]
    public int attemptCount = 260000;
    public int seed = 991;
    public float minHeight = 1.8f;
    public float maxHeight = 220f;
    public float minSlopeY = 0.85f;
    public float cliffMarginMetres = 3f;
    [Tooltip("World-space size (m) of a flower patch; bigger = fewer, larger meadows.")]
    public float patchSize = 14f;
    [Tooltip("0..1 noise threshold a patch cell must clear before any flowers grow there - raise for sparser meadows.")]
    public float patchThreshold = 0.62f;
    [Tooltip("Chance per accepted point inside an active patch that a flower is actually planted (thins density within a patch).")]
    public float inPatchDensity = 0.35f;
    public float minScale = 0.8f, maxScale = 1.35f;
    public float scatterBudgetMs = 6f;

    [Header("Distance LOD")]
    public float maxRenderDistance = 90f;
    public float nearDistance = 30f;
    public int farEvery = 3;
    public float farScale = 1.6f;

    public const bool FLOWERS_ENABLED = true;

    GhibliNatureSet natureSet;
    readonly List<GhibliBatch> batches = new List<GhibliBatch>();
    readonly Plane[] planes = new Plane[6];
    [SerializeField, HideInInspector] bool started;
    bool building;

    void Start()
    {
        if (!FLOWERS_ENABLED) return;
        if (!Application.isPlaying) return;
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

    // value-noise patch mask: smoothly varying 0..1 field so flower meadows form soft-edged clumps, not scattered dots
    static float PatchNoise(double x, double z, float cell)
    {
        float fx = (float)(x / cell), fz = (float)(z / cell);
        return Mathf.PerlinNoise(fx + 311.7f, fz + 127.1f);
    }

    IEnumerator Init()
    {
        building = true;
        batches.Clear();
        natureSet = usePackageAssets ? GhibliNatureSet.Load() : null;
        if (natureSet == null || natureSet.flowers.Count == 0) { Debug.LogError("[IslandStylizedFlowers] GhibliNatureSet flowers missing (run menu Island/Ghibli/Build Nature Set)"); building = false; yield break; }
        var ib = IslandBuilder.Instance;
        var mf = ib != null ? ib.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[IslandStylizedFlowers] Island mesh not built yet."); building = false; yield break; }

        int np = natureSet.flowers.Count;
        var lists = new List<Matrix4x4>[np];
        for (int i = 0; i < np; i++) lists[i] = new List<Matrix4x4>(attemptCount / np / 4 + 16);

        var rng = new System.Random(seed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = System.Diagnostics.Stopwatch.StartNew();
        int accepted = 0, attempts = 0;
        double e = E;
        while (attempts++ < attemptCount)
        {
            double x = (rng.NextDouble() * 2 - 1) * e;
            double z = (rng.NextDouble() * 2 - 1) * e;
            if (sw.Elapsed.TotalMilliseconds >= scatterBudgetMs) { yield return null; sw.Restart(); }

            // patch gate first (cheap-ish, Perlin) before the more expensive terrain lookups
            float patch = PatchNoise(x, z, patchSize);
            if (patch < patchThreshold) continue;
            if (rng.NextDouble() > inPatchDensity) continue;

            float h = Hm(x, z);
            if (h < minHeight || h > maxHeight) continue;
            float normalY = NV(x, z).y;   // one accurate analytic-normal check at the candidate point
            if (normalY < minSlopeY) continue;
            // Cheap cliff-margin check (see IslandStylizedGrass.cs for the profiling note): mesh-height-delta
            // approximation instead of 4x extra NV() calls.
            float m = cliffMarginMetres;
            float hx1 = Hm(x + m, z), hx2 = Hm(x - m, z), hz1 = Hm(x, z + m), hz2 = Hm(x, z - m);
            float maxDrop = Mathf.Max(Mathf.Max(Mathf.Abs(h - hx1), Mathf.Abs(h - hx2)), Mathf.Max(Mathf.Abs(h - hz1), Mathf.Abs(h - hz2)));
            float maxDropAllowed = m * 0.625f;
            if (maxDrop > maxDropAllowed) continue;
            double pth = Surface.PathAt(x, z);
            var st = Surface.Classify(x, z, h, normalY, pth);
            if (st != SurfaceType.Grass && st != SurfaceType.Field) continue;   // flowers only in meadow/field ground

            int pi = rng.Next(np);
            float s = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
            var rot = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            lists[pi].Add(Matrix4x4.TRS(U(x, h - 0.02, z), rot, new Vector3(s, s, s)));
            accepted++;
        }

        for (int i = 0; i < np; i++)
        {
            if (lists[i].Count == 0) continue;
            var pc = natureSet.flowers[i];
            var b = new GhibliBatch(pc.mesh, pc.materials) { shadows = UnityEngine.Rendering.ShadowCastingMode.Off };
            b.Build(lists[i], 48f, new Vector3(pc.MaxXZ * maxScale * 1.6f, pc.maxY * maxScale * 1.5f, pc.MaxXZ * maxScale * 1.6f), farEvery, farScale);
            batches.Add(b);
        }
        building = false;
        Debug.Log("[IslandStylizedFlowers] Ghibli package flowers: " + accepted + " planted in " + total.Elapsed.TotalSeconds.ToString("F1") + " s (attempts " + attempts + ").");
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
