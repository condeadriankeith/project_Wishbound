using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Phase 3.4: builds river ribbons, lake disc, waterfall mist and a few demo logs at Play.</summary>
public static class WaterBuilder
{
    public static bool SpawnDemoLogs = true;
    static GameObject root;

    // ---- detected waterfalls: Build() raises a falling-water curtain + layered mist for each ----
    // A fall stores its whole CENTRELINE path (lip -> pool) in Unity coords, so the curtain can be a
    // strip that follows the terrain down the cliff. That is what keeps the top glued to the flowing
    // ribbon instead of floating in mid-air over bare rock (the straight lip->pool chord cut across
    // the bend and detached from the cliff).
    public readonly struct Fall
    {
        public readonly Vector3[] pts;   // centreline from lip (on the visible ribbon) down to the plunge pool
        public readonly float width, drop;
        public Fall(Vector3[] pts, float width, float drop) { this.pts = pts; this.width = width; this.drop = drop; }
    }
    static readonly List<Fall> falls = new List<Fall>();

    /// <summary>Record a waterfall as the channel centreline run [i0..i1] (i0=lip on the visible ribbon,
    /// i1=plunge pool) in SOURCE coords. Called from RiverWater where cascades are detected.</summary>
public static void AddFall(IList<double> xs, IList<double> ys, IList<double> zs, int i0, int i1, float width, float drop)
    {
        int n = i1 - i0 + 1;
        if (n < 2) return;
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++) pts[i] = TerrainMath.U(xs[i0 + i], ys[i0 + i], zs[i0 + i]);
        falls.Add(new Fall(pts, width, drop));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Application.isPlaying || root != null || UnityEngine.Object.FindAnyObjectByType<IslandBuilder>() == null) return;
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline; if (rp != null) { var f = rp.GetType().GetField("m_RequireDepthTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); if (f != null) f.SetValue(rp, true); }   // water shader needs the depth texture
        Build();
    }

    public static void Build()
    {
        TerrainMath.H(0, 0); // forces RiverCarve.Build
        WaterField.Clear();
        falls.Clear();
        root = new GameObject("IslandWater");
        var V = new List<Vector3>(); var UV = new List<Vector2>(); var C = new List<Color>(); var T = new List<int>();
        RiverCarve.Channel longest = null; var UV2 = new List<Vector2>();

        RiverWater.Build(V, UV, UV2, C, T, out longest);

        if (FlowNet.LakeCell >= 0)
        {
            float R = (float)FlowNet.LakeR + 3f, lx = (float)FlowNet.LakeX, lz = (float)FlowNet.LakeZ, lv = (float)FlowNet.LakeLevel;
            WaterField.SetLake(lx, lz, R - 3f, lv);
            { var lr = new System.Random(77); for (int q = 0; q < 110; q++) { double an = lr.NextDouble() * 6.2832, d = (R - 3f) * (0.97 + lr.NextDouble() * 0.16); RiverRocks.Add(lx + Math.Cos(an) * d, lz + Math.Sin(an) * d, (float)(0.12 + 0.45 * Math.Pow(lr.NextDouble(), 2.2)), lr); } }
            int rings = 6, segs = 48, b0 = V.Count;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    float d = R * r / rings, an = s * Mathf.PI * 2f / segs;
                    double wx = lx + Mathf.Cos(an) * d, wz = lz + Mathf.Sin(an) * d;
                    double th = RiverWater.GH(wx, wz);
                    // rim tucks onto the shore where ground rises above the lake; submerged verts stay at level
                    double y = th > lv - 0.05 ? th + 0.15 : lv;
                    V.Add(TerrainMath.U(wx, y, wz)); UV.Add(Vector2.zero); UV2.Add(new Vector2(1f, 0f));
                    C.Add(new Color(0, 1, Mathf.Clamp01((d - 0.7f * R) / (0.3f * R)), 0));
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int s2 = (s + 1) % segs, i0 = b0 + r * segs + s, i1 = b0 + r * segs + s2, i2 = b0 + (r + 1) * segs + s, i3 = b0 + (r + 1) * segs + s2;
                    T.Add(i0); T.Add(i2); T.Add(i1); T.Add(i1); T.Add(i2); T.Add(i3);
                }
        }

        var mesh = new Mesh { name = "IslandWaterMesh", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(V); mesh.SetUVs(0, UV); mesh.SetUVs(1, UV2); mesh.SetColors(C); mesh.SetTriangles(T, 0); mesh.RecalculateBounds();
        var go = new GameObject("RiversAndLake"); go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Island/GhibliRiver"); if (sh == null) sh = Shader.Find("Sprites/Default");
        mr.sharedMaterial = new Material(sh) { renderQueue = 3001 };   // after the Ocean (3000): the fan always composites OVER the sea instead of being painted over by it depending on sort distance
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;

        BuildWaterfalls();
        RiverRocks.Build(root.transform);
        if (SpawnDemoLogs && longest != null) Logs(longest);
    }

    // ---- falling-water curtains ------------------------------------------------------------
    // One curved sheet per detected cascade, built with the (previously unused) Ghibli/Waterfall
    // shader. The sheet is BOWED OUT over the plunge pool (bulge in the middle of the fall), so even
    // a near-vertical drop reads as water LEAPING off the lip instead of a flat sheet buried inside
    // the cliff - that bow is what stops the curtain clipping into the rock behind it.
static float zSign = 1f;
    /// <summary>Height of the RENDERED terrain at Unity-space (x,z).</summary>
    static float G(float ux, float uz) => TerrainMath.Hm(ux, zSign * uz);

    static void BuildWaterfalls()
    {
        if (falls.Count == 0) return;
        var sh = Shader.Find("Ghibli/Waterfall"); if (sh == null) sh = Shader.Find("Sprites/Default");
        zSign = TerrainMath.U(0, 0, 1).z >= 0f ? 1f : -1f;
        Material Mk(float mode, int queue)
        {
            var m = new Material(sh) { renderQueue = queue };
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", mode);
            if (m.HasProperty("_Deep")) m.SetColor("_Deep", new Color(0.07f, 0.38f, 0.58f));
            if (m.HasProperty("_Shallow")) m.SetColor("_Shallow", new Color(0.36f, 0.80f, 0.80f));
            if (m.HasProperty("_Streak")) m.SetColor("_Streak", new Color(0.82f, 0.97f, 0.98f));
            if (m.HasProperty("_Foam")) m.SetColor("_Foam", new Color(0.97f, 0.99f, 1f));
            return m;
        }
        var matWet = Mk(1f, 2999); var matPool = Mk(2f, 3001); var matWater = Mk(0f, 3002);

        foreach (var f in falls)
        {
            if (f.pts.Length < 2) continue;
            Vector3 top = f.pts[0], bot = f.pts[f.pts.Length - 1];
            if (top.y <= bot.y + 0.5f) continue;                 // lip must be above the pool
            float drop = top.y - bot.y;
            PrepFall(f, out var cl, out var nrm);
            int R = cl.Length;

            float halfW = Mathf.Max(1.6f, f.width * 1.6f);
            float wTop = halfW * 0.85f;                                        // narrow at the lip
            float wBot = halfW * (1.15f + Mathf.Min(0.6f, drop * 0.008f));      // spreads as it falls
            float bowMax = Mathf.Min(1.6f, 0.25f + drop * 0.02f);              // leaps off the lip, then settles back on the face

            AddMesh("WaterfallWet", Sheet(cl, nrm, wTop * 1.7f + 1.0f, wBot * 1.9f + 1.4f, 11, 0.10f, 0f, drop), matWet);
            AddMesh("WaterfallCurtain", Sheet(cl, nrm, wTop, wBot, 15, 0.22f, bowMax, drop), matWater);

            float poolR = wBot * 1.7f + 1.0f;
            Vector2 nb = nrm[R - 1];
            Vector3 pc = cl[R - 1] + new Vector3(nb.x, 0f, nb.y) * (poolR * 0.3f);
            AddMesh("WaterfallPool", Disc(pc, poolR), matPool);
        }
    }

    static void AddMesh(string name, Mesh m, Material mat)
    {
        var go = new GameObject(name); if (root != null) go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
    }

    /// <summary>Resample a fall's centreline into evenly spaced rows and work out, per row, the horizontal
    /// direction pointing OUT of the cliff (downhill on the rendered terrain).</summary>
    static void PrepFall(Fall f, out Vector3[] cl, out Vector2[] nrm)
    {
        var p = f.pts; int P = p.Length;
        float len = 0f; for (int i = 0; i < P - 1; i++) len += Vector3.Distance(p[i], p[i + 1]);
        int R = Mathf.Clamp(Mathf.CeilToInt(len / 1.3f), 14, 120);
        cl = new Vector3[R]; nrm = new Vector2[R];
        int seg = 0; float acc = 0f, segLen = Vector3.Distance(p[0], p[1]);
        for (int r = 0; r < R; r++)
        {
            float s = r / (float)(R - 1) * len;
            while (seg < P - 2 && s > acc + segLen) { acc += segLen; seg++; segLen = Vector3.Distance(p[seg], p[seg + 1]); }
            float k = segLen > 1e-5f ? Mathf.Clamp01((s - acc) / segLen) : 0f;
            cl[r] = Vector3.Lerp(p[seg], p[seg + 1], k);
            if (r > 0 && cl[r].y > cl[r - 1].y) cl[r].y = cl[r - 1].y;     // water only ever goes down
        }
        const float e = 1.5f; var gl = new float[R];
        for (int r = 0; r < R; r++)
        {
            float x = cl[r].x, z = cl[r].z;
            float gx = (G(x + e, z) - G(x - e, z)) / (2f * e), gz = (G(x, z + e) - G(x, z - e)) / (2f * e);
            float m = Mathf.Sqrt(gx * gx + gz * gz); gl[r] = m;
            nrm[r] = m > 1e-4f ? new Vector2(-gx, -gz) / m : Vector2.zero;
        }
        for (int r = 0; r < R; r++)
            if (gl[r] < 0.12f)   // flat ground: fall back to the downstream direction
            {
                Vector3 a = cl[Mathf.Max(0, r - 1)], b = cl[Mathf.Min(R - 1, r + 1)];
                var tg = new Vector2(b.x - a.x, b.z - a.z);
                nrm[r] = tg.sqrMagnitude > 1e-6f ? tg.normalized : (r > 0 ? nrm[r - 1] : Vector2.up);
            }
        for (int pass = 0; pass < 3; pass++)
        {
            var c = (Vector2[])nrm.Clone();
            for (int r = 1; r < R - 1; r++) { var s2 = c[r - 1] + 2f * c[r] + c[r + 1]; if (s2.sqrMagnitude > 1e-6f) nrm[r] = s2.normalized; }
        }
    }

    /// <summary>A sheet that hugs the real cliff: for every row (a fixed height) and column (across the fall) it
    /// searches outward->inward along the cliff normal for the rock surface at that height and sits just in front
    /// of it. Columns with no rock behind them get alpha 0 so the sheet never hangs in open air.</summary>
    static Mesh Sheet(Vector3[] cl, Vector2[] nrm, float wTop, float wBot, int COL, float baseOff, float bowMax, float drop)
    {
        int R = cl.Length;
        var V = new Vector3[R * COL]; var UV = new Vector2[R * COL]; var UV1 = new Vector2[R * COL]; var C = new Color[R * COL];
        float wAvg = (wTop + wBot) * 0.5f;
        for (int r = 0; r < R; r++)
        {
            float u = r / (float)(R - 1);
            float w = Mathf.Lerp(wTop, wBot, Mathf.SmoothStep(0f, 1f, u));
            Vector2 n = nrm[r], a = new Vector2(-n.y, n.x);
            float y = cl[r].y, bow = bowMax * Mathf.Sin(Mathf.PI * u);
            for (int c = 0; c < COL; c++)
            {
                float v = c / (float)(COL - 1) * 2f - 1f;
                float px = cl[r].x + a.x * v * w, pz = cl[r].z + a.y * v * w;
                float target = y - 0.5f, t = 0f, sup = 0f, tt = 7f;
                int k = 0; bool found = false;
                for (; k <= 40; k++)
                {
                    tt = 7f - k * 0.35f;
                    if (G(px + n.x * tt, pz + n.y * tt) >= target) { found = true; break; }
                }
                if (found)
                {
                    sup = 1f;
                    if (k > 0)
                    {
                        float lo = tt, hi = tt + 0.35f;
                        for (int it = 0; it < 8; it++) { float mid = (lo + hi) * 0.5f; if (G(px + n.x * mid, pz + n.y * mid) >= target) lo = mid; else hi = mid; }
                        t = lo;
                    }
                }
                float off = t + baseOff + bow;
                float ox = px + n.x * off, oz = pz + n.y * off;
                float oy = Mathf.Max(y, G(ox, oz) + 0.2f);
                int i = r * COL + c;
                V[i] = new Vector3(ox, oy, oz);
                UV[i] = new Vector2(c / (float)(COL - 1), 1f - u);
                UV1[i] = new Vector2(drop, wAvg * 2f);
                C[i] = new Color(1f, 1f, 1f, sup);
            }
        }
        var T = new int[(R - 1) * (COL - 1) * 6]; int q = 0;
        for (int r = 0; r < R - 1; r++)
            for (int c = 0; c < COL - 1; c++)
            {
                int k0 = r * COL + c, k1 = k0 + 1, k2 = k0 + COL, k3 = k2 + 1;
                T[q++] = k0; T[q++] = k2; T[q++] = k1; T[q++] = k1; T[q++] = k2; T[q++] = k3;
            }
        var mesh = new Mesh { name = "WaterfallSheet", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = V; mesh.uv = UV; mesh.uv2 = UV1; mesh.colors = C; mesh.triangles = T; mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat foam disc at the plunge pool; conforms to the ground so it is never buried.</summary>
    static Mesh Disc(Vector3 c, float R)
    {
        const int SEG = 28, RINGS = 3;
        var V = new List<Vector3>(); var UV = new List<Vector2>(); var UV1 = new List<Vector2>(); var C = new List<Color>(); var T = new List<int>();
        float baseY = c.y + 0.1f;
        V.Add(new Vector3(c.x, Mathf.Max(baseY, G(c.x, c.z) + 0.12f), c.z)); UV.Add(new Vector2(0.5f, 0.5f)); UV1.Add(new Vector2(R * 2f, R * 2f)); C.Add(Color.white);
        for (int r = 1; r <= RINGS; r++)
            for (int s = 0; s < SEG; s++)
            {
                float rr = r / (float)RINGS, an = s * Mathf.PI * 2f / SEG;
                float x = c.x + Mathf.Cos(an) * R * rr, z = c.z + Mathf.Sin(an) * R * rr;
                V.Add(new Vector3(x, Mathf.Max(baseY, G(x, z) + 0.12f), z));
                UV.Add(new Vector2(0.5f + Mathf.Cos(an) * 0.5f * rr, 0.5f + Mathf.Sin(an) * 0.5f * rr));
                UV1.Add(new Vector2(R * 2f, R * 2f)); C.Add(Color.white);
            }
        for (int s = 0; s < SEG; s++) { int s2 = (s + 1) % SEG; T.Add(0); T.Add(1 + s2); T.Add(1 + s); }
        for (int r = 1; r < RINGS; r++)
            for (int s = 0; s < SEG; s++)
            {
                int s2 = (s + 1) % SEG, a0 = 1 + (r - 1) * SEG + s, a1 = 1 + (r - 1) * SEG + s2, b0 = 1 + r * SEG + s, b1 = 1 + r * SEG + s2;
                T.Add(a0); T.Add(b0); T.Add(a1); T.Add(a1); T.Add(b0); T.Add(b1);
            }
        var mesh = new Mesh { name = "WaterfallPool" };
        mesh.SetVertices(V); mesh.SetUVs(0, UV); mesh.SetUVs(1, UV1); mesh.SetColors(C); mesh.SetTriangles(T, 0); mesh.RecalculateBounds();
        return mesh;
    }

    static Texture2D soft;
    static Texture2D Soft()
    {
        if (soft) return soft;
        soft = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Mathf.Clamp01(1f - Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 15.5f); soft.SetPixel(x, y, new Color(1, 1, 1, d * d)); }
        soft.Apply(); return soft;
    }

    /// <summary>Cinematic two-layer mist: a soft rising plume climbing the falls PLUS a churning
    /// splash/foam ring bursting outward at the plunge pool. Scales with the drop.</summary>
public static void Mist(double x, double y, double z, float w, float drop)
    {
        var g = new GameObject("WaterfallMist"); if (root != null) g.transform.SetParent(root.transform, false);
        g.transform.position = TerrainMath.U(x, y + 1, z);
        float k = Mathf.Clamp(drop / 30f, 0.8f, 2f);   // taller falls throw more spray

        // --- rising plume: a soft column of spray climbing away from the pool (local space so it can't balloon) ---
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
        var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.6f * k, 3.0f * k); main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startColor = new Color(1, 1, 1, 0.20f); main.simulationSpace = ParticleSystemSimulationSpace.Local; main.gravityModifier = -0.04f;
        var em = ps.emission; em.rateOverTime = 14 + Mathf.Min(drop, 60) * 0.3f;
        var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 28; sp.radius = Mathf.Max(1.0f, w * 0.6f);
        sp.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.32f, 0.3f), new GradientAlphaKey(0, 1) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft() };
        pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
        ps.Play();

        // --- churning base spray: droplets bursting outward from the impact point ---
        var b = new GameObject("PoolFoam"); b.transform.SetParent(g.transform, false);
        b.transform.localPosition = Vector3.zero;
        var fps = b.AddComponent<ParticleSystem>(); fps.Stop();
        var fmain = fps.main; fmain.loop = true; fmain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        fmain.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f + drop * 0.012f); fmain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        fmain.startColor = new Color(0.95f, 0.98f, 1f, 0.4f); fmain.simulationSpace = ParticleSystemSimulationSpace.World; fmain.gravityModifier = 0.3f;
        var fem = fps.emission; fem.rateOverTime = 24 + Mathf.Min(drop, 60) * 0.3f;
        var fsh = fps.shape; fsh.shapeType = ParticleSystemShapeType.Circle; fsh.radius = Mathf.Max(1f, w * 0.9f); fsh.rotation = new Vector3(90, 0, 0);
        var voi = fps.velocityOverLifetime; voi.enabled = true; voi.space = ParticleSystemSimulationSpace.World;
        voi.radial = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        var fcol = fps.colorOverLifetime; fcol.enabled = true;
        var fgr = new Gradient(); fgr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.6f, 0.18f), new GradientAlphaKey(0, 1) });
        fcol.color = fgr;
        var fsz = fps.sizeOverLifetime; fsz.enabled = true; fsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.1f));
        var fpr = fps.GetComponent<ParticleSystemRenderer>();
        fpr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft() };
        fpr.shadowCastingMode = ShadowCastingMode.Off; fpr.receiveShadows = false;
        fps.Play();
    }

    static void Logs(RiverCarve.Channel c)
    {
        int M = c.x.Length, m = M / 3;
        for (int i = 0; i < 4; i++)
        {
            int k = Mathf.Min(M - 1, m + i * 4);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = "DemoLog"; g.transform.SetParent(root.transform, false);
            g.transform.localScale = new Vector3(0.5f, 1.3f, 0.5f);
            g.transform.position = TerrainMath.U(c.x[k], c.bed[k] + 0.45, c.z[k]);
            g.transform.rotation = Quaternion.Euler(0, i * 37f, 90);
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 40; rb.angularDamping = 0.5f;
            g.AddComponent<Buoyant>();
            g.GetComponent<Renderer>().sharedMaterial.color = new Color(0.45f, 0.3f, 0.18f);
        }
    }
}
