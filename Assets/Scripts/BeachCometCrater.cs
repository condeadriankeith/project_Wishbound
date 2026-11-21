using System;
using System.Collections.Generic;
using UnityEngine;

// Comet impact on the south beach: a big, churned displacement of sand rather than a clean bowl.
// Coordinates are in the terrain's source space (world Z is mirrored: worldZ = -sourceZ).
//   u = along the comet's travel direction (positive = downrange, toward the sea)
//   v = across it
// Shape: a skid furrow ploughed into the beach upstream, an elongated bowl, a heaped lip and
// a lumpy ejecta apron thrown downrange, plus a handful of sand mounds. No glowing cracks.
public static class CometImpact
{
    public const double X = 49.52493, Z = -408.0403;
    // Travel direction in source space (inland -> sea, roughly radial from the island centre).
    public const double Dx = 0.12, Dz = -0.9928;
    public const double RadiusDown = 11.5, RadiusUp = 9.0, RadiusSide = 8.8;
    public const double BowlDepth = 2.5, LipDown = .8, LipUp = .35;   // lip lowered + widened (see Apply): the old 1.55 m narrow ring was steeper than the controller's slope limit
    public const double FurrowStart = 6, FurrowLength = 22;
    public const double Radius = 11; // nominal size

    public static void Local(double x, double z, out double u, out double v)
    {
        double dx = x - X, dz = z - Z;
        u = dx * Dx + dz * Dz;
        v = -dx * Dz + dz * Dx;
    }

    // Normalised elliptical distance from the impact centre with a lumpy outline (1 = crater lip).
    public static double R(double x, double z)
    {
        Local(x, z, out double u, out double v);
        double ru = u / (u > 0 ? RadiusDown : RadiusUp), rv = v / RadiusSide;
        double r = Math.Sqrt(ru * ru + rv * rv);
        return r * (1 + .2 * (TerrainMath.Fbm(x * .11 + 3.1, z * .11 - 7.4) - .5));
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // Lateral profile of the upstream skid furrow (0..1), and how far along it we are.
    static double Furrow(double u, double v, out double along)
    {
        double t = -u;
        along = TerrainMath.Cl(0, 1, (t - FurrowStart) / FurrowLength);
        if (t < FurrowStart - 4 || t > FurrowStart + FurrowLength + 4) return 0;
        double w = Lerp(3.4, 1.1, along);
        double fade = TerrainMath.Sm(FurrowStart - 4, FurrowStart + 1, t) * TerrainMath.Sm(FurrowStart + FurrowLength + 4, FurrowStart + FurrowLength - 6, t);
        return Math.Exp(-(v * v) / (w * w)) * fade;
    }

    public static bool Clear(double x, double z)
    {
        Local(x, z, out double u, out double v);
        if (Math.Abs(u) > 45 || Math.Abs(v) > 30) return false;
        return R(x, z) < 1.75 || Furrow(u, v, out _) > .08;
    }

    static readonly double[,] Mounds =
    {   // u, v, radius, height  (sand clods flung out by the impact)
        { 15.5, -4.0, 2.2, .55 }, { 17.0, 5.5, 1.7, .45 }, { 13.0, 9.5, 1.4, .35 }, { 20.5, 1.0, 1.5, .40 },
        { 11.0, -10.5, 1.3, .30 }, { 2.0, 12.5, 1.2, .28 }, { -1.5, -12.0, 1.1, .25 }, { 23.5, -6.5, 1.1, .25 },
    };

public static double Apply(double x, double z, double original)
    {
        Local(x, z, out double u, out double v);
        if (Math.Abs(u) > 45 || Math.Abs(v) > 30) return original;
        double r = R(x, z);
        double len = Math.Sqrt(u * u + v * v) + 1e-6;
        double downrange = TerrainMath.Sm(-.4, .9, u / len);
        // low-frequency lumps: the old .32 frequency made a bumpy, jittery floor
        double lumps = TerrainMath.Fbm(x * .14 + 11.7, z * .14 + 5.3) - .5;

        double f = Furrow(u, v, out double along);
        double furrowDepth = Lerp(1.25, .25, along);
        double w = Lerp(3.4, 1.1, along);
        double rampMask = Math.Min(1.0, f * 1.5);   // 1 along the furrow centre line

        // gentler wall: the smoothstep now spans the whole bowl instead of starting at r=.15
        double bowl = -BowlDepth * (1 - TerrainMath.Sm(.0, 1.15, r)) * (1 + .12 * downrange) + .10 * lumps * (1 - TerrainMath.Sm(.6, 1, r));
        // wide, low heaped lip (sigma .55 instead of .2) that eases away where the furrow leaves the bowl -> walk-out ramp
        double lipH = Lerp(LipUp, LipDown, downrange) * (1 + .12 * lumps) * (1 - .85 * rampMask);
        double lip = lipH * Math.Exp(-Math.Pow((r - 1.1) / .55, 2));
        double apron = (.4 * downrange + .06) * Math.Exp(-Math.Pow((r - 1.7) / .7, 2)) * (.75 + .5 * TerrainMath.Fbm(x * .12, z * .12));
        double outer = 1 - TerrainMath.Sm(2.2, 3.0, r);

        double leveeMask = TerrainMath.Sm(FurrowStart - 2, FurrowStart + 3, -u) * TerrainMath.Sm(FurrowStart + FurrowLength + 4, FurrowStart + FurrowLength - 6, -u);
        double levee = .26 * furrowDepth * Math.Exp(-Math.Pow((Math.Abs(v) - w * 1.45) / (w * .8), 2)) * leveeMask;
        // the furrow now fades in over a long distance, so there is no step where it meets the rim
        double furrow = (-furrowDepth * f + levee) * TerrainMath.Sm(.75, 1.6, r);

        double mounds = 0;
        for (int i = 0; i < Mounds.GetLength(0); i++)
        {
            double du = u - Mounds[i, 0], dv = v - Mounds[i, 1], rr = Mounds[i, 2] * 1.25;
            double d2 = (du * du + dv * dv) / (rr * rr);
            if (d2 < 4) mounds += Mounds[i, 3] * .7 * Math.Exp(-d2 * 1.6);
        }
        return original + (bowl + lip + apron) * outer + furrow + mounds;
    }

    public static double Disturbance(double x, double z)
    {
        Local(x, z, out double u, out double v);
        if (Math.Abs(u) > 45 || Math.Abs(v) > 30) return 0;
        double r = R(x, z);
        double crater = 1 - TerrainMath.Sm(1.1, 2.1, r);
        double downrange = TerrainMath.Sm(-.4, .9, u / (Math.Sqrt(u * u + v * v) + 1e-6));
        double apron = (1 - TerrainMath.Sm(1.6, 2.6, r)) * downrange * .7;
        return Math.Max(Math.Max(crater, apron), Math.Min(1, Furrow(u, v, out _) * 1.3));
    }

    public static Color Paint(double x, double z, Color original)
    {
        double d = Disturbance(x, z);
        if (d <= 0) return original;
        double r = R(x, z);
        double n = TerrainMath.Fbm(x * .45 + 2, z * .45 - 9);
        Color churned = new Color(.60f, .47f, .33f), damp = new Color(.47f, .37f, .28f), scorch = new Color(.30f, .25f, .23f);
        Color c = Color.Lerp(churned, damp, (float)TerrainMath.Sm(.35, .7, n));
        c = Color.Lerp(scorch, c, (float)TerrainMath.Sm(.12, .42, r));
        return Color.Lerp(original, c, (float)(d * (.75 + .25 * n)));
    }
}

/// <summary>
/// Props for the impact site: the half-buried comet heart, a few sand clods and lingering smoke/embers.
/// The crater shape itself lives in the terrain (CometImpact). When the opening sequence is going to
/// play (IntroPending), props start hidden and Reveal() is called at the moment of impact.
/// </summary>
[DefaultExecutionOrder(200)]
public class BeachCometCrater : MonoBehaviour
{
    public static bool IntroPending;   // set by WishboundIntro.Awake
    [Tooltip("Seconds for the fresh-impact smoke/embers to settle to the calm resting state.")]
    public float coolDownSeconds = 60f;
    [Tooltip("Alpha-blended particle material (Assets/Art/Comet/CometSmoke.mat).")] public Material smokeMaterial;
    [Tooltip("Additive particle material (Assets/Art/Comet/CometEmber.mat).")] public Material emberMaterial;

    public static BeachCometCrater Instance { get; private set; }
    public Vector3 Center { get; private set; }
    public Vector3 Downrange { get; private set; }
    public bool Revealed { get; private set; }

    readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    ParticleSystem smoke, embers;
    float revealTime = -999;

    void Awake() { Instance = this; ComputeFrame(); }
    void Start()
    {
        Amount = IntroPending ? 0 : 1;
        Build();
        if (IntroPending) { SetPropsVisible(false); ApplyAmount(0, 0, true); }
        else Reveal(false);
    }

    static Vector3 Ground(float x, float z) => new Vector3(x, FinalY(x, z), z);

    // ---------------------------------------------------------------- high-res sand patch
    // The island grid is ~2.7 m per cell - far too coarse for the furrow/lip/mounds. The island mesh is sunk
    // under the disturbed area (IslandBuilder) and this fine patch is laid on top, blending back into the
    // island's own (bilinear) surface where the disturbance fades out so there is no seam.
    const float PatchStep = .5f, PatchUMin = -36, PatchUMax = 32, PatchVHalf = 25;
    // Tiny lift so the detail patch never z-fights the (otherwise coplanar) island surface at its edges.
    const float PatchLift = .02f;
    /// <summary>True when the island vertex at source-space (sx,sz) lies at least one cell + diagonal inside the detail patch,
    /// so every triangle touching it is completely covered by the patch. IslandBuilder sinks those vertices; without that
    /// the island mesh sits ABOVE the crater bowl and buries it.</summary>
    public static bool InsidePatch(double sx, double sz)
    {
        CometImpact.Local(sx, sz, out double u, out double v);
        double d = Math.Min(Math.Min(u - PatchUMin, PatchUMax - u), PatchVHalf - Math.Abs(v));
        return d >= TerrainMath.S * 1.5;
    }

    static float GridBilinear(float[] g, double sx, double sz)
    {
        double fi = (sx + TerrainMath.E) / TerrainMath.S, fj = (sz + TerrainMath.E) / TerrainMath.S;
        int V = TerrainMath.V; int i = Mathf.Clamp((int)Math.Floor(fi), 0, V - 2), j = Mathf.Clamp((int)Math.Floor(fj), 0, V - 2);
        float tx = (float)(fi - i), tz = (float)(fj - j);
        // match the island's triangle split (k, k+V, k+1 / k+1, k+V, k+V+1)
        float a = g[j * V + i], b = g[j * V + i + 1], c = g[(j + 1) * V + i], d = g[(j + 1) * V + i + 1];
        return tx + tz <= 1 ? a + (b - a) * tx + (c - a) * tz : d + (c - d) * (1 - tx) + (b - d) * (1 - tz);
    }
    static Color ColorBilinear(Color[] g, double sx, double sz)
    {
        double fi = (sx + TerrainMath.E) / TerrainMath.S, fj = (sz + TerrainMath.E) / TerrainMath.S;
        int V = TerrainMath.V; int i = Mathf.Clamp((int)Math.Floor(fi), 0, V - 2), j = Mathf.Clamp((int)Math.Floor(fj), 0, V - 2);
        float tx = (float)(fi - i), tz = (float)(fj - j);
        return Color.Lerp(Color.Lerp(g[j * V + i], g[j * V + i + 1], tx), Color.Lerp(g[(j + 1) * V + i], g[(j + 1) * V + i + 1], tx), tz);
    }
    /// <summary>0 = untouched beach (before the comet lands), 1 = full crater. Animated by Reveal().</summary>
    public static float Amount = 1;

    /// <summary>Ground height without the crater (the island's own surface).</summary>
    public static float BaseY(float wx, float wz)
    {
        double sx = wx, sz = TerrainMath.MirrorZ ? -wz : wz;
        var g = IslandBuilder.HGrid;
        return g != null ? GridBilinear(g, sx, sz) : TerrainMath.Hf(sx, sz);
    }
    /// <summary>Crater displacement at a world XZ position (0 outside the impact site).</summary>
    public static float CraterDelta(float wx, float wz)
    {
        double sx = wx, sz = TerrainMath.MirrorZ ? -wz : wz;
        return (float)CometImpact.Apply(sx, sz, 0);
    }
    /// <summary>Height of the visible ground at a world XZ position, crater included at its current Amount.</summary>
    public static float SurfaceY(float wx, float wz) => BaseY(wx, wz) + CraterDelta(wx, wz) * Amount;
    /// <summary>Height of the ground once the crater has fully formed.</summary>
    public static float FinalY(float wx, float wz) => BaseY(wx, wz) + CraterDelta(wx, wz);

    void BuildPatch()
    {
        var island = IslandBuilder.Instance; if (!island) return;
        var mr = island.GetComponent<MeshRenderer>(); if (!mr) return;
        int nu = Mathf.CeilToInt((PatchUMax - PatchUMin) / PatchStep) + 1, nv = Mathf.CeilToInt(2 * PatchVHalf / PatchStep) + 1;
        var verts = new Vector3[nu * nv]; var cols = new Color[nu * nv];
        patchBase = new float[nu * nv]; patchDelta = new float[nu * nv]; patchCalm = new Color[nu * nv]; patchScarred = new Color[nu * nv];
        var baseCols = IslandBuilder.BaseColors;
        for (int b = 0; b < nv; b++) for (int a = 0; a < nu; a++)
            {
                double u = PatchUMin + a * PatchStep, v = -PatchVHalf + b * PatchStep;
                double sx = CometImpact.X + u * CometImpact.Dx - v * CometImpact.Dz, sz = CometImpact.Z + u * CometImpact.Dz + v * CometImpact.Dx;
                var w = TerrainMath.U(sx, 0, sz);
                int k = b * nu + a;
                patchBase[k] = BaseY(w.x, w.z); patchDelta[k] = CraterDelta(w.x, w.z);
                w.y = patchBase[k] + patchDelta[k] * Amount + PatchLift; verts[k] = w;
                Color baseC = baseCols != null ? ColorBilinear(baseCols, sx, sz) : new Color(.94f, .9f, .8f); baseC.a = 1;
                var c = CometImpact.Paint(sx, sz, baseC); c.a = 1;
                patchCalm[k] = baseC; patchScarred[k] = c; cols[k] = Color.Lerp(baseC, c, Mathf.Clamp01(Amount));
            }
        var tris = new int[(nu - 1) * (nv - 1) * 6]; int n = 0;
        for (int b = 0; b < nv - 1; b++) for (int a = 0; a < nu - 1; a++)
            {
                int k = b * nu + a;
                tris[n++] = k; tris[n++] = k + nu; tris[n++] = k + 1;
                tris[n++] = k + 1; tris[n++] = k + nu; tris[n++] = k + nu + 1;
            }
        // make sure the faces point up whatever the u/v handedness ended up being
        var t0 = Vector3.Cross(verts[nu] - verts[0], verts[1] - verts[0]);
        if (t0.y < 0) System.Array.Reverse(tris);
        var mesh = new Mesh { name = "Comet crater detail", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.triangles = tris; mesh.colors = cols; mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
        var go = new GameObject("Crater sand (detail)"); go.transform.SetParent(transform, false); go.layer = island.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
        patchCollider = go.AddComponent<MeshCollider>(); patchCollider.sharedMesh = mesh;
        patch = go; patchMesh = mesh; patchVerts = verts; patchCols = cols;
    }
    GameObject patch; Mesh patchMesh; MeshCollider patchCollider; Vector3[] patchVerts; Color[] patchCols;
    float[] patchBase, patchDelta; Color[] patchCalm, patchScarred;

    /// <summary>Re-shape the sand patch for a crater amount (overshoot > 1 heaps the lip as sand is thrown up).</summary>
    void ApplyAmount(float amount, float scar, bool updateCollider)
    {
        if (patchMesh == null) return;
        for (int k = 0; k < patchVerts.Length; k++)
        {
            patchVerts[k].y = patchBase[k] + patchDelta[k] * amount + PatchLift;
            patchCols[k] = Color.Lerp(patchCalm[k], patchScarred[k], scar);
        }
        patchMesh.vertices = patchVerts; patchMesh.colors = patchCols; patchMesh.RecalculateNormals(); patchMesh.RecalculateBounds();
        if (updateCollider) { patchCollider.sharedMesh = null; patchCollider.sharedMesh = patchMesh; }
    }

    void ComputeFrame()
    {
        float cx = (float)CometImpact.X, cz = (float)-CometImpact.Z;
        Center = Ground(cx, cz);
        Downrange = new Vector3((float)CometImpact.Dx, 0, -(float)CometImpact.Dz).normalized;
    }

    public void Build()
    {
        if (transform.childCount > 0) return;
        ComputeFrame();
        var shader = Shader.Find("Ghibli/ToonLit");
        Material Make(string name, Color color)
        {
            var m = new Material(shader) { name = name };
            m.SetColor("_Color", color); m.SetColor("_InstColor", Color.white);
            m.SetColor("_EmissionColor", Color.black); m.SetFloat("_BrushStrength", .12f);
            owned.Add(m); return m;
        }
        var heart = Make("Comet • cooled star-glass", new Color(.20f, .19f, .30f));
        var sand = Make("Comet • churned sand clod", new Color(.62f, .48f, .34f));
        var wet = Make("Comet • damp sand clod", new Color(.50f, .39f, .29f));
        var rng = new System.Random(41208);
        float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        Mesh RockMesh()
        {
            var points = new Vector3[8];
            points[0] = new Vector3(.12f, 1, 0); points[7] = new Vector3(-.1f, -.65f, .07f);
            for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; points[i + 1] = new Vector3(Mathf.Cos(a) * Rand(.7f, 1), Rand(-.15f, .15f), Mathf.Sin(a) * Rand(.7f, 1)); }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                int n = (i + 1) % 6 + 1;
                foreach (int j in new[] { 0, n, i + 1, 7, i + 1, n }) { triangles.Add(vertices.Count); vertices.Add(points[j]); }
            }
            var mesh = new Mesh { name = "Comet facets" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh); return mesh;
        }
        var rock = RockMesh();
        void Fragment(string name, Vector3 pos, Vector3 scale, Material mat, bool collider)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(Rand(-14, 14), Rand(0, 360), Rand(-14, 14)); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = rock; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) { var c = go.AddComponent<MeshCollider>(); c.sharedMesh = rock; c.convex = true; }
        }
        BuildPatch();
        Vector3 down = Downrange, side = Vector3.Cross(Vector3.up, down);
        var hp = Center + down * 2.2f; hp = Ground(hp.x, hp.z);
        Fragment("Comet heart (half buried)", hp + Vector3.down * .3f, new Vector3(1.4f, .9f, 1.15f), heart, true);
        for (int i = 0; i < 9; i++)
        {
            float u = Rand(10, 22), v = Rand(-11, 11), s = Rand(.35f, .8f);
            var p = Center + down * u + side * v; p = Ground(p.x, p.z);
            Fragment("Sand clod " + i, p + Vector3.up * s * .05f, new Vector3(s * 1.3f, s * .45f, s), i % 3 == 0 ? wet : sand, false);
        }
        smoke = MakeSmoke(); embers = MakeEmbers();
    }

    ParticleSystem MakeSmoke()
    {
        var go = new GameObject("Impact smoke"); go.transform.SetParent(transform, false); go.transform.position = Center + Vector3.up * .3f;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(5, 8); main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .6f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.2f); main.startColor = new Color(.42f, .40f, .45f, .11f); main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -.015f; main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
        var em = ps.emission; em.rateOverTime = 6;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.4f; sh.rotation = new Vector3(-90, 0, 0);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(.25f); vel.y = new ParticleSystem.MinMaxCurve(.35f); vel.z = new ParticleSystem.MinMaxCurve(.1f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.8f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial ? smokeMaterial : SoftMaterial("Comet • smoke", false);
        return ps;
    }

    ParticleSystem MakeEmbers()
    {
        var go = new GameObject("Impact embers"); go.transform.SetParent(transform, false); go.transform.position = Center + Vector3.up * .15f;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f); main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(.04f, .09f); main.startColor = new ParticleSystem.MinMaxGradient(new Color(.4f, .9f, 1f) * 2.2f, new Color(1f, .45f, .9f) * 2.2f); main.maxParticles = 100; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -.05f;
        var em = ps.emission; em.rateOverTime = 10;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.8f; sh.rotation = new Vector3(-90, 0, 0);
        var noise = ps.noise; noise.enabled = true; noise.strength = .35f; noise.frequency = .6f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) }); col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = emberMaterial ? emberMaterial : SoftMaterial("Comet • ember", true);
        return ps;
    }

    Material SoftMaterial(string name, bool additive)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0); m.renderQueue = 3000;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (additive) m.EnableKeyword("_BLENDMODE_ADD");
        m.SetTexture("_BaseMap", SoftDot()); m.SetColor("_BaseColor", Color.white);
        owned.Add(m); return m;
    }

    static Texture2D softDot;
    public static Texture2D SoftDot()
    {
        if (softDot) return softDot;
        const int N = 64; softDot = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Soft dot", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float a = Mathf.Clamp01(1 - d); a *= a * (3 - 2 * a);
                px[y * N + x] = new Color(1, 1, 1, a);
            }
        softDot.SetPixels(px); softDot.Apply(); return softDot;
    }

    void SetPropsVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer) && (!patch || r.gameObject != patch)) r.enabled = on;
        foreach (var c in GetComponentsInChildren<Collider>(true)) if (!patch || c.gameObject != patch) c.enabled = on;
    }

    /// <summary>Show the impact props. fresh = heavy just-landed smoke/embers that cool over coolDownSeconds.</summary>
    public void Reveal(bool fresh = true)
    {
        Revealed = true;
        revealTime = fresh ? Time.time : -999;
        if (!fresh) { Amount = 1; ApplyAmount(1, 1, true); SetPropsVisible(true); if (smoke) smoke.Play(); if (embers) embers.Play(); UpdateCooling(); return; }
        StartCoroutine(Impact());
    }

    [Header("Impact")]
    [Tooltip("Seconds for the sand to be blasted out into the crater shape.")]
    public float formSeconds = .9f;
    [Tooltip("Additive ring material for the ground shockwave (Assets/Art/Comet/Shockwave.mat).")]
    public Material shockwaveMaterial;
    bool propsShown;

    /// <summary>The landing: blinding flash light, the ground blasting outward into the crater, a racing
    /// shockwave ring, a sand-and-dust column, flung star-glass sparks, then the long smoulder.</summary>
    System.Collections.IEnumerator Impact()
    {
        var c = Center;
        var lgo = new GameObject("Impact flash"); lgo.transform.SetParent(transform, false); lgo.transform.position = c + Vector3.up * 3;
        var flash = lgo.AddComponent<Light>(); flash.type = LightType.Point; flash.color = new Color(1f, .93f, .8f); flash.range = 140; flash.intensity = 80; flash.shadows = LightShadows.None;
        var burst = MakeBurst(c); var debris = MakeDebris(c); var ring = MakeShockwave(c);
        burst.Play(); debris.Play();
        if (smoke) smoke.Play(); if (embers) embers.Play();
        float t = 0;
        while (t < formSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / formSeconds);
            float a = 1 - Mathf.Pow(1 - k, 3); a += Mathf.Sin(k * Mathf.PI) * .18f;
            Amount = a; ApplyAmount(a, Mathf.Clamp01(k * 1.6f), false);
            flash.intensity = Mathf.Lerp(80, 14, k); flash.color = Color.Lerp(new Color(1f, .93f, .8f), new Color(.55f, .8f, 1f), k);
            if (k > .35f && !propsShown) { propsShown = true; SetPropsVisible(true); }
            yield return null;
        }
        Amount = 1; ApplyAmount(1, 1, true); if (!propsShown) SetPropsVisible(true);
        for (float f = 0; f < 3.5f; f += Time.unscaledDeltaTime) { flash.intensity = Mathf.Lerp(14, 2.2f, f / 3.5f); flash.range = Mathf.Lerp(140, 14, f / 3.5f); yield return null; }
        flash.color = new Color(.5f, .85f, 1f); flash.range = 12; flash.intensity = 1.6f;   // the cooling glow that remains
        Destroy(burst.gameObject, 8); Destroy(debris.gameObject, 8); if (ring) Destroy(ring, 3);
    }

    ParticleSystem BaseBurst(string name, Vector3 at, Material mat)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }

    ParticleSystem MakeBurst(Vector3 c)
    {
        var ps = BaseBurst("Impact sand burst", c, smokeMaterial ? smokeMaterial : SoftMaterial("Comet - burst", false));
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 7f); m.startSpeed = new ParticleSystem.MinMaxCurve(6, 22); m.maxParticles = 220;
        m.startSize = new ParticleSystem.MinMaxCurve(3, 8); m.startColor = new ParticleSystem.MinMaxGradient(new Color(.78f, .66f, .52f, .75f), new Color(.55f, .5f, .55f, .6f)); m.gravityModifier = .35f;
        m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, 120), new ParticleSystem.Burst(.12f, 60) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 2.5f; sh.rotation = new Vector3(-90, 0, 0);
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.2f; lim.dampen = .12f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .85f), 0), new GradientColorKey(Color.white, .15f), new GradientColorKey(new Color(.8f, .8f, .9f), 1) }, new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.7f, .3f), new GradientAlphaKey(0, 1) }); col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, .5f, 1, 2.4f));
        return ps;
    }

    ParticleSystem MakeDebris(Vector3 c)
    {
        var ps = BaseBurst("Impact debris", c + Vector3.up * .5f, emberMaterial ? emberMaterial : SoftMaterial("Comet - spark", true));
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 3f); m.startSpeed = new ParticleSystem.MinMaxCurve(8, 26); m.maxParticles = 300;
        m.startSize = new ParticleSystem.MinMaxCurve(.08f, .3f); m.gravityModifier = 1.6f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(.5f, .95f, 1f) * 3f, new Color(1f, .55f, .95f) * 3f);
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, 220) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55; sh.radius = 1.2f; sh.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .6f), new GradientAlphaKey(0, 1) }); col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .06f; r.lengthScale = 1.5f;
        return ps;
    }

    GameObject MakeShockwave(Vector3 c)
    {
        if (!shockwaveMaterial) return null;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Impact shockwave"; Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false); go.transform.position = c + Vector3.up * .6f; go.transform.rotation = Quaternion.Euler(90, 0, 0);
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = shockwaveMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<ShockwaveRing>();
        return go;
    }

    void Update() { if (Revealed) UpdateCooling(); }

    void UpdateCooling()
    {
        float t = Mathf.Clamp01((Time.time - revealTime) / Mathf.Max(1, coolDownSeconds));
        float heat = 1 - Mathf.SmoothStep(0, 1, t);
        if (smoke) { var em = smoke.emission; em.rateOverTime = Mathf.Lerp(3, 10, heat); var m = smoke.main; m.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(.2f, .6f, heat), Mathf.Lerp(.5f, 1.8f, heat)); }
        if (embers) { var em = embers.emission; em.rateOverTime = Mathf.Lerp(3, 40, heat); }
    }

    void OnDestroy() { foreach (var item in owned) if (item) Destroy(item); if (Instance == this) Instance = null; }
}

/// <summary>Expanding, fading ground ring for the impact shockwave.</summary>
public class ShockwaveRing : MonoBehaviour
{
    public float duration = 1.6f, maxSize = 120f;
    float t; Renderer r; MaterialPropertyBlock mpb;
    void Awake() { r = GetComponent<Renderer>(); mpb = new MaterialPropertyBlock(); }
    void Update()
    {
        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
        float e = 1 - Mathf.Pow(1 - k, 2.4f);
        transform.localScale = Vector3.one * Mathf.Lerp(2, maxSize, e);
        r.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", new Color(1, 1, 1, (1 - k) * (1 - k))); r.SetPropertyBlock(mpb);
    }
}
