using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static TerrainMath;

/// <summary>
/// Draws the graded roads from RoadBuild in the island's toon style.
///  Surface  one ribbon per road (Island/GhibliRoad). Vertex rgb = the terrain's own vertex colour under the road,
///           so the shader can fade the verge by alpha straight into the ground.
///  Pebbles  sparse, dark low-poly stones (Island/ToonWood, grain off).
///  Bridges  timber only (Island/ToonWood). Deck height = water surface (WaterField.Query) + clearance + waterfall
///           pool allowance, raised into a grade-limited hump; posts, bents and rails follow the deck.
/// Built in Unity space (TerrainMath.U). Deterministic per road.
/// </summary>
public static class RoadRenderer
{
    const float Step = 0.4f, Lift = 0.14f, EdgePad = 0.8f, Chunk = 96f;
    const int Cols = 11;
    const float MaxGrade = 0.16f;     // max deck slope (rise/run) for the hump
    const float DeckClear = 1.3f;     // deck height over the water surface

    static readonly Color Moss = new Color(0.30f, 0.44f, 0.20f, 0f);
    static readonly Color[] PebPal =
    {
        new Color(0.40f, 0.37f, 0.33f), new Color(0.46f, 0.41f, 0.33f), new Color(0.34f, 0.31f, 0.28f),
        new Color(0.50f, 0.47f, 0.42f), new Color(0.31f, 0.31f, 0.33f), new Color(0.44f, 0.35f, 0.28f)
    };
    static readonly Color WoodPlank = new Color(0.64f, 0.47f, 0.30f);
    static readonly Color WoodPost = new Color(0.46f, 0.33f, 0.22f);
    static readonly Color WoodRail = new Color(0.58f, 0.42f, 0.27f);

    class MB
    {
        public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public List<Color> c = new List<Color>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> t = new List<int>();
        public int Count { get { return v.Count; } }
    }

    struct Smp { public double x, z; public float y, so, sd; public Vector3 p, t, r; }
    class BridgeSpan { public int iA, iB, jA, jB; public float[] top; public RoadBuild.Cross cr; }
    class RoadData
    {
        public RoadBuild.GradedRoad g; public int id; public float hw, L;
        public Smp[] d; public float[] mode; public bool[] inBridge, noStone;
        public List<BridgeSpan> bridges = new List<BridgeSpan>();
    }

    struct HS { public float x, z, y, hw, sd; public int road; }
    static readonly List<HS> hs = new List<HS>();
    static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    const float HCell = 4f;

    // ---- terrain vertex colour sampler (nearest rendered terrain vertex) ----
    static Vector3[] tPos; static Color[] tCol;
    static readonly Dictionary<long, List<int>> tGrid = new Dictionary<long, List<int>>();
    const float TCell = 3f;
    static readonly Color TerrFallback = new Color(0.40f, 0.55f, 0.28f);

    static void InitTerrainColors()
    {
        tPos = null; tCol = null; tGrid.Clear();
        var ib = UnityEngine.Object.FindObjectOfType<IslandBuilder>();
        if (ib == null) return;
        var mf = ib.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var cols = mf.sharedMesh.colors; var vs = mf.sharedMesh.vertices;
        if (cols == null || cols.Length != vs.Length) return;
        tPos = new Vector3[vs.Length]; tCol = cols;
        for (int i = 0; i < vs.Length; i++)
        {
            tPos[i] = mf.transform.TransformPoint(vs[i]);
            long k = Key(Mathf.FloorToInt(tPos[i].x / TCell), Mathf.FloorToInt(tPos[i].z / TCell));
            List<int> l; if (!tGrid.TryGetValue(k, out l)) { l = new List<int>(); tGrid[k] = l; }
            l.Add(i);
        }
    }

    static Color TerrainColor(Vector3 p)
    {
        if (tPos == null) return TerrFallback;
        int cx = Mathf.FloorToInt(p.x / TCell), cz = Mathf.FloorToInt(p.z / TCell);
        float best = 1e9f; int bi = -1;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                List<int> l; if (!tGrid.TryGetValue(Key(cx + dx, cz + dz), out l)) continue;
                for (int q = 0; q < l.Count; q++)
                {
                    float ex = tPos[l[q]].x - p.x, ez = tPos[l[q]].z - p.z, d = ex * ex + ez * ez;
                    if (d < best) { best = d; bi = l[q]; }
                }
            }
        return bi < 0 ? TerrFallback : tCol[bi];
    }

    static float Rf(System.Random r) { return (float)r.NextDouble(); }
    static float Hash01(int a, int b)
    {
        unchecked { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f; }
    }
    static long Key(int a, int b) { return ((long)a << 32) ^ (uint)b; }
    static float GroundAt(float ux, float uz) { return Hm(ux, MirrorZ ? -uz : uz); }
    static float GroundAt(Vector3 u) { return GroundAt(u.x, u.z); }

    static bool Wet(float ux, float uz, float gy)
    {
        double sx = ux, sz = MirrorZ ? -uz : uz;
        if (gy <= WorldField.SeaLevel + 0.12) return true;
        if (RiverCarve.WetAt(sx, sz) > 0.4f) return true;
        if (FlowNet.LakeCell >= 0)
        {
            double dx = sx - FlowNet.LakeX, dz = sz - FlowNet.LakeZ, lr = FlowNet.LakeR + 0.8;
            if (dx * dx + dz * dz < lr * lr) return true;
        }
        return false;
    }

    static float Taper(RoadData rd, float sd)
    {
        float e = Mathf.Min(sd, rd.L - sd);
        return Mathf.Lerp(0.62f, 1f, Mathf.SmoothStep(0f, 1f, e / 4.5f));
    }

    // ---- mesh primitives (flat shaded, uv0 = metres along / across the longest edge) ----
    static void Face(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-12f) { n = Vector3.Cross(c - a, d - a); if (n.sqrMagnitude < 1e-12f) return; }
        n.Normalize();
        if (Vector3.Dot(n, outward) < 0f) { Vector3 tmp = b; b = d; d = tmp; n = -n; }
        col.a = 0f;
        float e1 = (b - a).magnitude, e2 = (d - a).magnitude;
        bool sw = e2 > e1; float lo = sw ? e2 : e1, sh = sw ? e1 : e2;
        int k = m.v.Count;
        m.v.Add(a); m.v.Add(b); m.v.Add(c); m.v.Add(d);
        m.n.Add(n); m.n.Add(n); m.n.Add(n); m.n.Add(n);
        m.c.Add(Wc(col, a.y)); m.c.Add(Wc(col, b.y)); m.c.Add(Wc(col, c.y)); m.c.Add(Wc(col, d.y));
        if (!sw) { m.uv.Add(new Vector2(0, 0)); m.uv.Add(new Vector2(lo, 0)); m.uv.Add(new Vector2(lo, sh)); m.uv.Add(new Vector2(0, sh)); }
        else { m.uv.Add(new Vector2(0, 0)); m.uv.Add(new Vector2(0, sh)); m.uv.Add(new Vector2(lo, sh)); m.uv.Add(new Vector2(lo, 0)); }
        m.t.Add(k); m.t.Add(k + 1); m.t.Add(k + 2); m.t.Add(k); m.t.Add(k + 2); m.t.Add(k + 3);
    }

    static void Tri(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-12f) return;
        n.Normalize();
        if (Vector3.Dot(n, outward) < 0f) { Vector3 tmp = b; b = c; c = tmp; n = -n; }
        col.a = 0f;
        int k = m.v.Count;
        m.v.Add(a); m.v.Add(b); m.v.Add(c);
        m.n.Add(n); m.n.Add(n); m.n.Add(n);
        m.c.Add(col); m.c.Add(col); m.c.Add(col);
        m.uv.Add(Vector2.zero); m.uv.Add(new Vector2((b - a).magnitude, 0)); m.uv.Add(new Vector2(0, (c - a).magnitude));
        m.t.Add(k); m.t.Add(k + 1); m.t.Add(k + 2);
    }

    static void Box(MB m, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 size, Color col)
    {
        Vector3 x = ax * (size.x * 0.5f), y = ay * (size.y * 0.5f), z = az * (size.z * 0.5f);
        Vector3 a0 = c - x - y - z, a1 = c + x - y - z, a2 = c + x + y - z, a3 = c - x + y - z;
        Vector3 b0 = c - x - y + z, b1 = c + x - y + z, b2 = c + x + y + z, b3 = c - x + y + z;
        Face(m, a1, a2, b2, b1, ax, col);
        Face(m, a0, b0, b3, a3, -ax, col);
        Face(m, a3, b3, b2, a2, ay, col);
        Face(m, a0, a1, b1, b0, -ay, col * 0.8f);
        Face(m, b0, b1, b2, b3, az, col);
        Face(m, a0, a3, a2, a1, -az, col);
    }

    static void Beam(MB m, Vector3 a, Vector3 b, Vector3 upHint, float w, float h, Color col)
    {
        Vector3 d = b - a; float len = d.magnitude;
        if (len < 1e-3f) return;
        Vector3 ax = d / len;
        Vector3 side = Vector3.Cross(upHint, ax);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.right, ax);
        side.Normalize();
        Vector3 ay = Vector3.Cross(ax, side).normalized;
        Box(m, (a + b) * 0.5f, ax, ay, side, new Vector3(len, h, w), col);
    }

    static Color PickPeb(System.Random rng)
    {
        float r = Rf(rng);
        int i = r < 0.30f ? 0 : r < 0.52f ? 1 : r < 0.72f ? 2 : r < 0.84f ? 3 : r < 0.94f ? 4 : 5;
        Color c = PebPal[i]; float j = 0.92f + 0.16f * Rf(rng);
        return new Color(c.r * j, c.g * j, c.b * j, 0f);
    }

    static void Pebble(MB m, Vector3 pos, float r, float flat, float yaw, Color col, float moss, System.Random rng)
    {
        const int K = 5;
        Vector3[] r0 = new Vector3[K], r1 = new Vector3[K];
        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
        for (int i = 0; i < K; i++)
        {
            float a = (i + 0.4f * Rf(rng)) * Mathf.PI * 2f / K;
            float rr = r * (0.85f + 0.30f * Rf(rng));
            float ox = Mathf.Cos(a) * rr, oz = Mathf.Sin(a) * rr * 0.8f;
            float x = ox * cy - oz * sy, z = ox * sy + oz * cy;
            r0[i] = pos + new Vector3(x, -0.28f * r * flat, z);
            r1[i] = pos + new Vector3(x * 0.78f, 0.55f * r * flat * (0.9f + 0.2f * Rf(rng)), z * 0.78f);
        }
        Vector3 apex = pos + new Vector3((Rf(rng) - 0.5f) * 0.25f * r, 0.82f * r * flat * (0.9f + 0.2f * Rf(rng)), (Rf(rng) - 0.5f) * 0.25f * r);
        for (int i = 0; i < K; i++)
        {
            int j = (i + 1) % K;
            Color cc = col * (0.92f + 0.16f * Rf(rng));
            Vector3 cen = (r0[i] + r0[j] + r1[i] + r1[j]) * 0.25f - pos;
            Face(m, r0[i], r0[j], r1[j], r1[i], new Vector3(cen.x, 0.15f, cen.z), cc);
            Color ct = Color.Lerp(col * (0.96f + 0.1f * Rf(rng)), Moss, moss);
            Vector3 cen2 = (r1[i] + r1[j] + apex) / 3f - pos;
            Tri(m, r1[i], r1[j], apex, new Vector3(cen2.x, Mathf.Abs(cen2.y) + 0.3f, cen2.z), ct);
        }
    }

    static MB ChunkFor(Dictionary<long, MB> d, Vector3 p)
    {
        long k = Key(Mathf.FloorToInt(p.x / Chunk), Mathf.FloorToInt(p.z / Chunk));
        MB m;
        if (!d.TryGetValue(k, out m)) { m = new MB(); d[k] = m; }
        return m;
    }

    static GameObject Emit(MB m, string name, Transform parent, Material mat, bool shadows, bool recalc)
    {
        if (m.Count == 0) return null;
        var mesh = new Mesh { name = name, indexFormat = m.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(m.v);
        mesh.SetUVs(0, m.uv);
        mesh.SetColors(m.c);
        mesh.SetTriangles(m.t, 0);
        if (recalc) mesh.RecalculateNormals(); else mesh.SetNormals(m.n);
        mesh.RecalculateBounds();
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;
        return go;
    }

    // ---- centreline densification ----
    static double CR(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    static Smp[] Densify(RoadBuild.GradedRoad g)
    {
        int M = g.x.Length;
        var fx = new List<double>(); var fz = new List<double>(); var fso = new List<float>(); var fy = new List<float>();
        for (int i = 0; i < M - 1; i++)
        {
            int i0 = Math.Max(0, i - 1), i3 = Math.Min(M - 1, i + 2);
            for (int k = 0; k < 8; k++)
            {
                double t = k / 8.0;
                fx.Add(CR(g.x[i0], g.x[i], g.x[i + 1], g.x[i3], t));
                fz.Add(CR(g.z[i0], g.z[i], g.z[i + 1], g.z[i3], t));
                fso.Add((float)(g.s[i] + (g.s[i + 1] - g.s[i]) * t));
                fy.Add((float)(g.prof[i] + (g.prof[i + 1] - g.prof[i]) * t));
            }
        }
        fx.Add(g.x[M - 1]); fz.Add(g.z[M - 1]); fso.Add((float)g.s[M - 1]); fy.Add((float)g.prof[M - 1]);
        int F = fx.Count;
        var cum = new double[F];
        for (int i = 1; i < F; i++)
        {
            double dx = fx[i] - fx[i - 1], dz = fz[i] - fz[i - 1];
            cum[i] = cum[i - 1] + Math.Sqrt(dx * dx + dz * dz);
        }
        double Ltot = cum[F - 1];
        int n = Math.Max(4, (int)Math.Round(Ltot / Step) + 1);
        var d = new Smp[n];
        int seg = 0;
        for (int k = 0; k < n; k++)
        {
            double target = Ltot * k / (n - 1);
            while (seg < F - 2 && cum[seg + 1] < target) seg++;
            double sl = cum[seg + 1] - cum[seg];
            double t = sl > 1e-9 ? Math.Min(1.0, Math.Max(0.0, (target - cum[seg]) / sl)) : 0.0;
            d[k].x = fx[seg] + (fx[seg + 1] - fx[seg]) * t;
            d[k].z = fz[seg] + (fz[seg + 1] - fz[seg]) * t;
            d[k].y = (float)(fy[seg] + (fy[seg + 1] - fy[seg]) * t);
            d[k].so = (float)(fso[seg] + (fso[seg + 1] - fso[seg]) * t);
            d[k].sd = (float)target;
            d[k].p = U(d[k].x, d[k].y, d[k].z);
        }
        for (int k = 0; k < n; k++)
        {
            Vector3 a = d[Math.Max(0, k - 1)].p, b = d[Math.Min(n - 1, k + 1)].p;
            Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (t.sqrMagnitude < 1e-10f) t = k > 0 ? d[k - 1].t : Vector3.forward;
            t.Normalize();
            d[k].t = t; d[k].r = Vector3.Cross(Vector3.up, t);
        }
        return d;
    }

    static int FirstIndexSo(Smp[] d, float so) { for (int k = 0; k < d.Length; k++) if (d[k].so >= so) return k; return d.Length - 1; }
    static int LastIndexSo(Smp[] d, float so) { for (int k = d.Length - 1; k >= 0; k--) if (d[k].so <= so) return k; return 0; }

static RoadData Prep(RoadBuild.GradedRoad g, int id)
    {
        var rd = new RoadData { g = g, id = id, hw = (float)g.halfW[0] };
        rd.d = Densify(g);
        int n = rd.d.Length;
        rd.L = rd.d[n - 1].sd;
        rd.mode = new float[n]; rd.inBridge = new bool[n]; rd.noStone = new bool[n];
        int M = g.x.Length;
        foreach (var cr in g.crosses)
        {
            int a = Math.Max(0, cr.i0 - 1), b = Math.Min(M - 1, cr.i1 + 1);
            int iA = FirstIndexSo(rd.d, (float)g.s[a]), iB = LastIndexSo(rd.d, (float)g.s[b]);
            if (iB - iA < 4) continue;
            if (cr.bridge)
            {
                // extend the span a couple of metres onto firm ground so the abutments bed into the banks
                // ends are chosen by SmartEnds: dry, flat, close to deck level
                int jA, jB; SmartEnds(rd, iA, iB, out jA, out jB);
                var sp = new BridgeSpan { iA = iA, iB = iB, jA = jA, jB = jB, cr = cr };
                rd.bridges.Add(sp);
                for (int k = sp.jA + 1; k < sp.jB; k++) rd.inBridge[k] = true;
                for (int k = Math.Max(0, sp.jA - 3); k <= Math.Min(n - 1, sp.jB + 3); k++) rd.noStone[k] = true;
            }
            else for (int k = iA; k <= iB; k++) rd.mode[k] = 1f;
        }
        var m2 = new float[n];
        for (int k = 0; k < n; k++)
        {
            float acc = 0f; int cnt = 0;
            for (int j = -6; j <= 6; j++) { int q = k + j; if (q < 0 || q >= n) continue; acc += rd.mode[q]; cnt++; }
            m2[k] = acc / Mathf.Max(1, cnt);
        }
        rd.mode = m2;
        foreach (var sp in rd.bridges) { NudgeFromWalls(rd, sp); StraightenSpan(rd, sp); }
        return rd;
    }

// How good is road sample k as a bridge landing? Lower is better: dry, flat across the width and along the road.
static float LandingScore(RoadData rd, int k, int towardBridge)
    {
        var s = rd.d[k]; float hw = rd.hw;
        Vector3 pl = s.p - s.r * hw, pr = s.p + s.r * hw, pf = s.p + s.t * 1.2f, pb = s.p - s.t * 1.2f;
        float g0 = GroundAt(s.p), gl = GroundAt(pl), gr = GroundAt(pr), gf = GroundAt(pf), gb = GroundAt(pb);
        float rough = Mathf.Max(Mathf.Max(Mathf.Abs(gl - g0), Mathf.Abs(gr - g0)), Mathf.Max(Mathf.Abs(gf - g0), Mathf.Abs(gb - g0)));
        float wet = (Wet(s.p.x, s.p.z, g0) ? 6f : 0f) + (Wet(pl.x, pl.z, gl) ? 3f : 0f) + (Wet(pr.x, pr.z, gr) ? 3f : 0f);
        float edge = Mathf.Abs(gf - gb) * 1.5f;   // avoid landing on a slope that tilts the abutment
        // Run-out check: walk a few metres further AWAY from the water (away from the bridge) and
        // measure how much the ground still climbs/falls. A true landing sits on a shelf, not mid-slope,
        // so this should be small; on a cliff face it stays large no matter where you sample along it.
        float runOut = 0f;
        Vector3 away = s.t * (-towardBridge);
        float prevH = g0;
        for (int step = 1; step <= 4; step++)
        {
            Vector3 probe = s.p + away * (step * 1.5f);
            float ph = GroundAt(probe);
            runOut += Mathf.Abs(ph - prevH);
            prevH = ph;
        }
        // Wall term: penalise landings with terrain rising beside the deck (cliff wall within a few metres either side)
        float wall = 0f;
        foreach (float lo in new float[] { hw + 1.5f, hw + 3.5f })
        {
            float gL = GroundAt(s.p - s.r * lo), gR = GroundAt(s.p + s.r * lo);
            wall += Mathf.Max(0f, gL - g0 - 0.8f) + Mathf.Max(0f, gR - g0 - 0.8f);
        }
        return rough * 2f + wet + edge + runOut * 3f + wall * 2.5f;
    }

    // Walk outward from the crossing (up to ~5 m) and take the best landing on each bank.
static void SmartEnds(RoadData rd, int iA, int iB, out int jA, out int jB)
    {
        int n = rd.d.Length;
        jA = Math.Max(0, iA - 3); jB = Math.Min(n - 1, iB + 3);
        float bestA = 1e9f, bestB = 1e9f;
        int qualA = -1, qualB = -1; float qualAScore = 1e9f, qualBScore = 1e9f; int nqA = 0, nqB = 0;
        const float FlatEnough = 0.35f;   // run-out below this = genuine shelf
        const float WallOk = 0.6f;        // wall term below this = no cliff beside the deck
        for (int st = 2; st <= 60; st++)
        {
            int ka = iA - st, kb = iB + st;
            if (ka >= 0)
            {
                float sc = LandingScore(rd, ka, +1) + 0.03f * st;
                if (sc < bestA) { bestA = sc; jA = ka; }
                if (RunOutOnly(rd, ka, +1) < FlatEnough && WallOnly(rd, ka) < WallOk) { nqA++; if (sc < qualAScore) { qualAScore = sc; qualA = ka; } }
            }
            if (kb < n)
            {
                float sc = LandingScore(rd, kb, -1) + 0.03f * st;
                if (sc < bestB) { bestB = sc; jB = kb; }
                if (RunOutOnly(rd, kb, -1) < FlatEnough && WallOnly(rd, kb) < WallOk) { nqB++; if (sc < qualBScore) { qualBScore = sc; qualB = kb; } }
            }
        }
        if (qualA >= 0) jA = qualA;
        if (qualB >= 0) jB = qualB;
    }

    // Isolated run-out term (see LandingScore) so SmartEnds can gate on "is this actually flat" separately
    // from the composite score used to rank candidates.
    static float RunOutOnly(RoadData rd, int k, int towardBridge)
    {
        var s = rd.d[k];
        Vector3 away = s.t * (-towardBridge);
        float prevH = GroundAt(s.p), runOut = 0f;
        for (int step = 1; step <= 4; step++)
        {
            Vector3 probe = s.p + away * (step * 1.5f);
            float ph = GroundAt(probe);
            runOut += Mathf.Abs(ph - prevH);
            prevH = ph;
        }
        return runOut;
    }


    // A bridge is a straight beam: replace the winding road centreline between the abutments with the chord,
    // and ease the road tangent into it over a few metres so the approach does not kink.
    static void StraightenSpan(RoadData rd, BridgeSpan sp)
    {
        var D = rd.d; int n = D.Length;
        int jA = sp.jA, jB = sp.jB; if (jB - jA < 2) return;
        Vector3 pa = D[jA].p, pb = D[jB].p;
        Vector3 chord = new Vector3(pb.x - pa.x, 0f, pb.z - pa.z);
        if (chord.sqrMagnitude < 1e-6f) return;
        chord.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, chord);
        const int Blend = 10;
        for (int k = Math.Max(0, jA - Blend); k < jA; k++)
        {
            float w = Mathf.SmoothStep(0f, 1f, (float)(k - (jA - Blend)) / Blend);
            Vector3 t = Vector3.Lerp(D[k].t, chord, w).normalized;
            D[k].t = t; D[k].r = Vector3.Cross(Vector3.up, t);
        }
        for (int k = jB + 1; k <= Math.Min(n - 1, jB + Blend); k++)
        {
            float w = Mathf.SmoothStep(0f, 1f, 1f - (float)(k - jB) / Blend);
            Vector3 t = Vector3.Lerp(D[k].t, chord, w).normalized;
            D[k].t = t; D[k].r = Vector3.Cross(Vector3.up, t);
        }
        for (int k = jA; k <= jB; k++)
        {
            float f = (float)(k - jA) / (jB - jA);
            float px = Mathf.Lerp(pa.x, pb.x, f), pz = Mathf.Lerp(pa.z, pb.z, f);
            D[k].p = new Vector3(px, Mathf.Lerp(pa.y, pb.y, f), pz);
            D[k].x = px; D[k].z = MirrorZ ? -pz : pz;
            D[k].t = chord; D[k].r = right;
        }
    }

    static void AddToHash(RoadData rd)
    {
        for (int k = 0; k < rd.d.Length; k += 4)
        {
            var s = rd.d[k];
            hs.Add(new HS { x = s.p.x, z = s.p.z, y = s.p.y, hw = rd.hw, sd = s.sd, road = rd.id });
            long key = Key(Mathf.FloorToInt(s.p.x / HCell), Mathf.FloorToInt(s.p.z / HCell));
            List<int> l;
            if (!grid.TryGetValue(key, out l)) { l = new List<int>(); grid[key] = l; }
            l.Add(hs.Count - 1);
        }
    }

    static bool Conflict(Vector3 p, float y, int road, float sd)
    {
        int cx = Mathf.FloorToInt(p.x / HCell), cz = Mathf.FloorToInt(p.z / HCell);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                List<int> l;
                if (!grid.TryGetValue(Key(cx + dx, cz + dz), out l)) continue;
                for (int q = 0; q < l.Count; q++)
                {
                    HS h = hs[l[q]];
                    if (h.road == road && Mathf.Abs(h.sd - sd) < 14f) continue;
                    if (Mathf.Abs(h.y - y) > 1.6f) continue;
                    float ex = p.x - h.x, ez = p.z - h.z, lim = h.hw + 0.3f;
                    if (ex * ex + ez * ez < lim * lim) return true;
                }
            }
        return false;
    }

    // ---- road surface ----
    static void Ribbon(MB m, Smp[] D, List<int> idx, float[] hwA, int cols, float[] alpha, float lift)
    {
        if (idx.Count < 2) return;
        int b0 = m.v.Count;
        for (int ii = 0; ii < idx.Count; ii++)
        {
            int k = idx[ii]; Smp s = D[k]; float hwv = hwA[k];
            for (int j = 0; j < cols; j++)
            {
                float u = -1f + 2f * j / (cols - 1);
                Vector3 pos = s.p + s.r * (u * hwv);
                float gy = Mathf.Max(GroundAt(pos), Mathf.Max(GroundAt(pos + s.t * 0.3f), GroundAt(pos - s.t * 0.3f)));
                gy = Mathf.Max(gy, Mathf.Max(GroundAt(pos + s.r * 0.2f), GroundAt(pos - s.r * 0.2f)));
                pos.y = Mathf.Max(s.p.y, gy) + lift;
                Color tc = TerrainColor(pos);
                m.v.Add(pos); m.n.Add(Vector3.up);
                m.uv.Add(new Vector2(u, s.sd));
                m.c.Add(new Color(tc.r, tc.g, tc.b, alpha[k]));
            }
        }
        for (int ii = 0; ii < idx.Count - 1; ii++)
            for (int j = 0; j < cols - 1; j++)
            {
                int v00 = b0 + ii * cols + j, v01 = v00 + 1, v10 = v00 + cols, v11 = v10 + 1;
                m.t.Add(v00); m.t.Add(v10); m.t.Add(v11);
                m.t.Add(v00); m.t.Add(v11); m.t.Add(v01);
            }
    }

    static void SurfaceFor(RoadData rd, Transform parent, Material mat)
    {
        var D = rd.d; int n = D.Length;
        var mb = new MB();
        var hwA = new float[n]; var alp = new float[n];
        for (int k = 0; k < n; k++) { hwA[k] = (rd.hw + EdgePad) * Taper(rd, D[k].sd); alp[k] = rd.mode[k] * 0.5f; }
        int k0 = 0;
        while (k0 < n)
        {
            if (rd.inBridge[k0]) { k0++; continue; }
            int k1 = k0;
            while (k1 + 1 < n && !rd.inBridge[k1 + 1]) k1++;
            var idx = new List<int>();
            for (int k = k0; k <= k1; k += 1) idx.Add(k);
            Ribbon(mb, D, idx, hwA, Cols, alp, Lift);
            k0 = k1 + 1;
        }
        Emit(mb, "Road_" + (rd.g.name ?? rd.id.ToString()), parent, mat, false, true);
    }

    // ---- pebbles (darker, sparser) ----
    static int EdgeStones(RoadData rd, Dictionary<long, MB> peb, System.Random rng)
    {
        var D = rd.d; int n = D.Length, count = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            float acc = 0f, next = 1.0f * Rf(rng);
            for (int k = 0; k < n; k++)
            {
                if (rd.noStone[k]) continue;
                acc += Step; if (acc < next) continue;
                acc = 0f; next = 1.1f + 1.4f * Rf(rng);
                Smp s = D[k];
                float hwv = rd.hw * Taper(rd, s.sd);
                float off = hwv + 0.02f + (Rf(rng) - 0.45f) * 0.5f;
                Vector3 pos = s.p + s.r * (side * off);
                float gy = GroundAt(pos);
                if (Wet(pos.x, pos.z, gy)) continue;
                if (Conflict(pos, gy, rd.id, s.sd)) continue;
                pos.y = off < hwv + 0.15f ? Mathf.Max(s.p.y, gy) + Lift * 0.5f : gy;
                float size = 0.10f + 0.09f * Rf(rng);
                if (Rf(rng) < 0.06f) size *= 1.4f;
                Pebble(ChunkFor(peb, pos), pos, size, 0.55f + 0.2f * Rf(rng), Rf(rng) * 6.283f, PickPeb(rng), Rf(rng) * 0.55f, rng); count++;
                if (Rf(rng) < 0.22f)
                {
                    Vector3 p2 = pos + s.r * (side * (0.12f + 0.25f * Rf(rng))) + s.t * ((Rf(rng) - 0.5f) * 0.5f);
                    float g2 = GroundAt(p2);
                    if (!Wet(p2.x, p2.z, g2)) { p2.y = g2; Pebble(ChunkFor(peb, p2), p2, size * 0.5f, 0.6f, Rf(rng) * 6.283f, PickPeb(rng), Rf(rng) * 0.4f, rng); count++; }
                }
            }
        }
        return count;
    }

    static int ScatterPebbles(RoadData rd, Dictionary<long, MB> peb, System.Random rng)
    {
        var D = rd.d; int n = D.Length, count = 0;
        for (int k = 0; k < n; k++)
        {
            if (rd.noStone[k] || rd.inBridge[k] || Rf(rng) > 0.07f) continue;
            Smp s = D[k];
            float hwv = rd.hw * Taper(rd, s.sd);
            Vector3 pos = s.p + s.r * ((Rf(rng) - 0.5f) * 2f * 0.85f * hwv) + s.t * ((Rf(rng) - 0.5f) * Step);
            float gy = GroundAt(pos);
            if (Wet(pos.x, pos.z, gy)) continue;
            pos.y = Mathf.Max(s.p.y, gy) + Lift;
            Pebble(ChunkFor(peb, pos), pos, 0.03f + 0.04f * Rf(rng), 0.55f, Rf(rng) * 6.283f, PickPeb(rng), 0f, rng); count++;
        }
        return count;
    }

    static int FordStones(RoadData rd, Dictionary<long, MB> peb, System.Random rng)
    {
        var D = rd.d; int n = D.Length, count = 0;
        for (int k = 0; k < n; k += 3)
        {
            if (rd.mode[k] < 0.4f || rd.inBridge[k]) continue;
            if (Rf(rng) < 0.55f) continue;
            Smp s = D[k];
            Vector3 pos = s.p + s.r * ((Rf(rng) - 0.5f) * 1.8f * rd.hw) + s.t * ((Rf(rng) - 0.5f) * 0.8f);
            pos.y = Mathf.Max(s.p.y, GroundAt(pos)) + Lift;
            Color c = Color.Lerp(PickPeb(rng), PebPal[4], 0.3f);
            Pebble(ChunkFor(peb, pos), pos, 0.16f + 0.14f * Rf(rng), 0.38f, Rf(rng) * 6.283f, c, 0f, rng); count++;
        }
        return count;
    }

    // ---- bridge system removed; rebuilt from scratch ----
    static float wetY = -1e9f;
    static Color Wc(Color c, float y) { c.a = wetY < -1e8f ? 0f : Mathf.Clamp01(1f - (y - wetY) / 1.5f); return c; }

    // ---- materials ----
    static Material MakeRoadMat()
    {
        Shader sh = Shader.Find("Island/GhibliRoad");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = "IslandRoad" };
        if (m.HasProperty("_Tint")) m.SetColor("_Tint", new Color(0.83f, 0.69f, 0.49f));
        if (m.HasProperty("_EdgeColor")) m.SetColor("_EdgeColor", new Color(0.58f, 0.55f, 0.34f));
        return m;
    }

    static Material MakeWoodMat(string name, float grain)
    {
        Shader sh = Shader.Find("Island/ToonWood");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_Tint")) m.SetColor("_Tint", Color.white);
        if (m.HasProperty("_Grain")) m.SetFloat("_Grain", grain);
        return m;
    }

    // ---- entry points ----
    public static void Clear()
    {
        var old = GameObject.Find("IslandRoads");
        if (old == null) return;
        old.name = "IslandRoads_old";
        if (Application.isPlaying) UnityEngine.Object.Destroy(old); else UnityEngine.Object.DestroyImmediate(old);
    }



    // ---- Wooden bridges: real abutments into terrain, arched clearance over water, individual planks, braced rails ----
    static int BuildBridge(RoadData rd, BridgeSpan sp, Transform parent, Material matWood, int idx)
    {
        int jA = sp.jA, jB = sp.jB;
        if (jB - jA < 2) return 0;
        float hw = rd.hw;
        int n = jB - jA + 1;
        var rng = new System.Random(9001 + idx * 57);

        // Sample ground height and water surface at every point so the deck, abutments and posts all key off reality.
        var cx = new float[n]; var cz = new float[n]; var ground = new float[n]; var groundC = new float[n]; var water = new float[n]; var hasWater = new bool[n];
        var r = new Vector3[n]; var fwd = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            var s = rd.d[jA + k];
            cx[k] = s.p.x; cz[k] = s.p.z;
            r[k] = s.r; fwd[k] = s.t;
            ground[k] = GroundAt(s.p); groundC[k] = ground[k];
            ground[k] = Mathf.Max(ground[k], Mathf.Max(GroundAt(s.p + s.r * (hw * 0.9f)), GroundAt(s.p - s.r * (hw * 0.9f))));
            if (k < 4 || k > n - 5)
            {
                // near the abutments the bank can be steep/uneven between sparse samples; probe a denser
                // footprint fore/aft and across the full width so the deck/crib never clips through terrain here.
                for (int fo = -1; fo <= 1; fo += 2)
                {
                    Vector3 probe = s.p + s.t * (fo * 0.6f);
                    ground[k] = Mathf.Max(ground[k], GroundAt(probe));
                    ground[k] = Mathf.Max(ground[k], Mathf.Max(GroundAt(probe + s.r * hw), GroundAt(probe - s.r * hw)));
                }
            }
            WaterField.S ws; if (WaterField.Query(s.p, out ws)) { hasWater[k] = true; water[k] = ws.surf; } else water[k] = ground[k];
        }

        // Deck height: clear the highest local water surface by DeckClear, blended into an arch that peaks mid-span
        // and drops to match the bank ground height at both ends (so it beds into the terrain, not floats above it).
        float waterMax = 0f; for (int k = 0; k < n; k++) if (hasWater[k]) waterMax = Mathf.Max(waterMax, water[k]);
        float peakY = waterMax + DeckClear;
        float bankA = groundC[0] + 0.08f, bankB = groundC[n - 1] + 0.08f;
        float spanLen = new Vector2(cx[0] - cx[n - 1], cz[0] - cz[n - 1]).magnitude; peakY = Mathf.Max(Mathf.Max(bankA, bankB) + 0.05f, Mathf.Min(peakY, (bankA + bankB) * 0.5f + MaxGrade * spanLen * 0.5f));
        var deckY = new float[n];
        for (int k = 0; k < n; k++)
        {
            float t = n > 1 ? (float)k / (n - 1) : 0f;
            float arch = Mathf.Sin(t * Mathf.PI); // 0 at ends, 1 at middle
            float baseline = Mathf.Lerp(bankA, bankB, t);
            deckY[k] = Mathf.Lerp(baseline, peakY, arch);
            deckY[k] = Mathf.Max(deckY[k], groundC[k] + 0.08f); // never sink into the bank
        }

        for (int pass = 0; pass < 6; pass++) { var tmp = (float[])deckY.Clone(); for (int k = 1; k < n - 1; k++) tmp[k] = (deckY[k - 1] + deckY[k] * 2f + deckY[k + 1]) / 4f; deckY = tmp; for (int k = 0; k < n; k++) { deckY[k] = Mathf.Max(deckY[k], groundC[k] + 0.12f); if (hasWater[k]) deckY[k] = Mathf.Max(deckY[k], water[k] + DeckClear * 0.8f); } deckY[0] = Mathf.Max(deckY[0], bankA + 0.02f); deckY[n - 1] = Mathf.Max(deckY[n - 1], bankB + 0.02f); }
        for (int k = 1; k < n; k++) { float sg = Mathf.Max(0.05f, new Vector2(cx[k] - cx[k - 1], cz[k] - cz[k - 1]).magnitude); deckY[k] = Mathf.Max(deckY[k], deckY[k - 1] - 0.30f * sg); }
        for (int k = n - 2; k >= 0; k--) { float sg = Mathf.Max(0.05f, new Vector2(cx[k + 1] - cx[k], cz[k + 1] - cz[k]).magnitude); deckY[k] = Mathf.Max(deckY[k], deckY[k + 1] - 0.30f * sg); }
        var c = new Vector3[n]; var u = new Vector3[n];
        for (int k = 0; k < n; k++) { c[k] = new Vector3(cx[k], deckY[k], cz[k]); u[k] = Vector3.up; }

        bool wide = (float)sp.cr.width > 6f;
        var mDeck = new MB(); var mTrim = new MB();
        Color gMoss = new Color(0.40f, 0.50f, 0.27f), gWet = new Color(0.30f, 0.36f, 0.23f), gRope = new Color(0.74f, 0.63f, 0.42f);
        var gTan = new Vector3[n]; var gUp = new Vector3[n];
        for (int k = 0; k < n; k++) { gTan[k] = (c[Mathf.Min(n - 1, k + 1)] - c[Mathf.Max(0, k - 1)]).normalized; gUp[k] = (Vector3.up - gTan[k] * Vector3.Dot(Vector3.up, gTan[k])).normalized; }
        void Box(MB gm, Vector3 pa, Vector3 pb, float gw, float gh, Color gc, Vector3? gref = null)
        {
            var dir = (pb - pa).normalized; var rf = gref ?? Vector3.up; if (Mathf.Abs(Vector3.Dot(dir, rf)) > 0.9f) rf = Vector3.right;
            var rr = Vector3.Cross(rf, dir).normalized; var uu = Vector3.Cross(dir, rr).normalized; gc.a = 1f;
            Sweep(gm, new Vector3[2] { pa, pb }, new Vector3[2] { rr, rr }, new Vector3[2] { uu, uu }, new float[2] { gw, gw }, new float[2] { gh, gh }, gc, rng, true);
        }
        float GH(Vector3 gp) { return GroundAt(gp); }
        Vector3 Pos(int gk, float gs, float gd) { return c[gk] + r[gk] * gs * (hw + gd); }

        // ---- Chunky irregular planks: each its own slab, slightly yawed/tilted/uneven, some mossy ----
        float gAcc = 0f, gNext = 0.40f; int gLk = 0;
        for (int k = 1; k < n; k++)
        {
            gAcc += (c[k] - c[k - 1]).magnitude;
            if (gAcc < gNext && k < n - 1) continue;
            float gLen = gAcc; gAcc = 0f; gNext = 0.34f + 0.18f * (float)rng.NextDouble();
            int gm = (gLk + k) / 2; Vector3 mid = (c[gLk] + c[k]) * 0.5f;
            float ov = 0.02f + 0.05f * (float)rng.NextDouble();
            Vector3 rrY = Quaternion.AngleAxis(((float)rng.NextDouble() - 0.5f) * 5f, gUp[gm]) * r[gm];
            Vector3 pc = mid - gUp[gm] * 0.045f + gUp[gm] * (((float)rng.NextDouble() - 0.5f) * 0.02f);
            Color gcol = WoodPlank * (0.88f + 0.2f * (float)rng.NextDouble());
            if (rng.NextDouble() < 0.12) gcol = Color.Lerp(gcol, gMoss, 0.35f);
            Box(mDeck, pc - rrY * (hw + ov), pc + rrY * (hw + ov), Mathf.Max(0.05f, gLen * 0.5f - 0.012f), 0.045f, gcol, gUp[gm]);
            gLk = k;
        }

        // ---- Heavy side beams + inner beams under the deck ----
        var gW = new float[n]; var gH = new float[n]; var gP = new Vector3[n];
        for (int k = 0; k < n; k++) { gW[k] = 0.10f; gH[k] = 0.20f; }
        foreach (float gs in new float[] { -1f, 1f, 0f })
        {
            if (gs == 0f && !wide) continue;
            for (int k = 0; k < n; k++) gP[k] = c[k] + r[k] * gs * (gs == 0f ? 0f : (hw + 0.02f)) - gUp[k] * 0.29f;
            Sweep(mTrim, (Vector3[])gP.Clone(), r, gUp, gW, gH, WoodPost, rng, true);
        }

        // ---- Posts, caps, rails, sagging ropes ----
        var gPk = new List<int>(); gPk.Add(0); float gAp = 0f;
        for (int k = 1; k < n; k++) { gAp += (c[k] - c[k - 1]).magnitude; if (gAp >= 1.5f) { gPk.Add(k); gAp = 0f; } }
        if (gPk[gPk.Count - 1] != n - 1) { if (n - 1 - gPk[gPk.Count - 1] < 2 && gPk.Count > 1) gPk[gPk.Count - 1] = n - 1; else gPk.Add(n - 1); }
        float railH = wide ? 1.0f : 0.85f;
        foreach (float gs in new float[] { -1f, 1f })
        {
            for (int i = 0; i < gPk.Count; i++)
            {
                int k = gPk[i]; bool endP = (i == 0 || i == gPk.Count - 1);
                Vector3 pb2 = Pos(k, gs, 0.17f); float ph = railH + (endP ? 0.25f : 0f);
                Box(mTrim, pb2 - gUp[k] * 0.5f, pb2 + gUp[k] * ph, 0.08f, 0.08f, WoodPost);
                Box(mTrim, pb2 + gUp[k] * ph, pb2 + gUp[k] * (ph + 0.07f), 0.115f, 0.115f, Color.Lerp(WoodRail, gMoss, 0.12f));
                if (i + 1 < gPk.Count)
                {
                    int k2 = gPk[i + 1]; Vector3 q2 = Pos(k2, gs, 0.17f);
                    foreach (float lv in new float[] { 0.55f, 0.25f })
                    {
                        var rp = new Vector3[6]; var rr2 = new Vector3[6]; var ru = new Vector3[6]; var rw = new float[6]; var rh = new float[6];
                        for (int j = 0; j < 6; j++)
                        {
                            float tj = j / 5f;
                            rp[j] = Vector3.Lerp(pb2, q2, tj) + gUp[k] * railH * lv - gUp[k] * (0.11f * Mathf.Sin(tj * Mathf.PI));
                            rr2[j] = r[k]; ru[j] = gUp[k]; rw[j] = 0.022f; rh[j] = 0.022f;
                        }
                        Sweep(mTrim, rp, rr2, ru, rw, rh, gRope, rng, true);
                    }
                }
            }
            var tr = new Vector3[n]; var tw = new float[n]; var th = new float[n];
            for (int k = 0; k < n; k++) { tr[k] = Pos(k, gs, 0.17f) + gUp[k] * (railH - 0.04f); tw[k] = 0.05f; th[k] = 0.045f; }
            Sweep(mTrim, tr, r, gUp, tw, th, WoodRail, rng, true);
        }

        // ---- Splayed A-frame trestle bents with cap beam, cross + X bracing, mossy/wet lower legs ----
        float gAs = 0f;
        for (int k = 1; k < n - 1; k++)
        {
            gAs += (c[k] - c[k - 1]).magnitude; if (gAs < 3.0f) continue; gAs = 0f;
            float gTopY = deckY[k] - 0.49f; Vector3[] gTop = new Vector3[2], gBot = new Vector3[2];
            float gMinBed = 1e9f;
            for (int si = 0; si < 2; si++)
            {
                float gs = si == 0 ? -1f : 1f;
                Vector3 tp = c[k] + r[k] * gs * (hw * 0.85f); tp.y = gTopY;
                Vector3 bp = c[k] + r[k] * gs * (hw * 0.85f + 0.28f); bp.y = GH(bp) - 0.45f;
                gTop[si] = tp; gBot[si] = bp; gMinBed = Mathf.Min(gMinBed, bp.y);
            }
            if (gTopY - gMinBed < 0.6f) continue;
            float gWl = hasWater[k] ? water[k] : gMinBed;
            for (int si = 0; si < 2; si++)
            {
                float my = Mathf.Clamp(gWl + 0.35f, gBot[si].y + 0.1f, gTop[si].y - 0.1f); float tt2 = (my - gBot[si].y) / (gTop[si].y - gBot[si].y);
                Vector3 mp = Vector3.Lerp(gBot[si], gTop[si], tt2);
                Box(mTrim, gBot[si], mp, 0.105f, 0.105f, gWet);
                Box(mTrim, mp, gTop[si], 0.10f, 0.10f, WoodPost);
            }
            Box(mTrim, gTop[0] - r[k] * 0.28f - Vector3.up * 0.07f, gTop[1] + r[k] * 0.28f - Vector3.up * 0.07f, 0.09f, 0.08f, WoodPost);
            float gMy = Mathf.Lerp(gMinBed, gTopY, 0.55f);
            Vector3 l1 = Vector3.Lerp(gBot[0], gTop[0], 0.3f), l2 = Vector3.Lerp(gBot[0], gTop[0], 0.75f), r1 = Vector3.Lerp(gBot[1], gTop[1], 0.3f), r2 = Vector3.Lerp(gBot[1], gTop[1], 0.75f);
            Box(mTrim, l1, r2, 0.05f, 0.05f, WoodPost); Box(mTrim, r1, l2, 0.05f, 0.05f, WoodPost);
            Box(mTrim, l2, r2, 0.06f, 0.06f, WoodPost);
        }

        // ---- Log-cabin crib abutments sunk into each bank ----
        foreach (int gE in new int[] { 0, n - 1 })
        {
            Vector3 gOut = gE == 0 ? -gTan[0] : gTan[n - 1]; gOut.y = 0f; gOut.Normalize();
            float gmin = 1e9f;
            for (int li = -2; li <= 2; li++) for (int oi = 0; oi <= 3; oi++) { Vector3 q = new Vector3(cx[gE], 0f, cz[gE]) + r[gE] * (hw * 0.46f * li) + gOut * (oi * 0.5f); gmin = Mathf.Min(gmin, GH(q)); }
            float gy = gmin - 0.5f, gtop = deckY[gE] - 0.3f;
            int layers = Mathf.Clamp(Mathf.CeilToInt((gtop - gy) / 0.17f), 1, 16); float lstep = layers > 1 ? (gtop - gy - 0.1f) / (layers - 1) : 0.17f;
            for (int L = 0; L < layers; L++)
            {
                float y = gy + 0.1f + L * lstep; Vector3 bc = new Vector3(cx[gE], y, cz[gE]);
                Color lc = WoodPost * (0.88f + 0.2f * (float)rng.NextDouble()); if (L < 2) lc = Color.Lerp(lc, gMoss, 0.4f);
                foreach (float gs in new float[] { -1f, 1f }) Box(mTrim, bc + r[gE] * gs * hw * 0.92f, bc + r[gE] * gs * hw * 0.92f + gOut * 1.0f, 0.08f, 0.08f, lc);
                Box(mTrim, bc - r[gE] * (hw + 0.1f) + gOut * 0.05f, bc + r[gE] * (hw + 0.1f) + gOut * 0.05f, 0.08f, 0.08f, lc);
            }
        }
        var goDeck = Emit(mDeck, "Bridge_Deck_" + idx, parent, matWood, true, false);
        var goTrim = Emit(mTrim, "Bridge_Trim_" + idx, parent, matWood, true, false);
        if (goDeck != null)
        {
            var mf = goDeck.GetComponent<MeshFilter>();
            var mc = goDeck.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
        if (goTrim != null)
        {
            var mf2 = goTrim.GetComponent<MeshFilter>();
            var mc2 = goTrim.AddComponent<MeshCollider>();
            mc2.sharedMesh = mf2.sharedMesh;
            mc2.convex = false;
        }
        return 1;
    }


    public static void Build()
    {
        Clear();
        RoadBuild.Build();
        var roads = RoadBuild.Roads;
        if (roads == null || roads.Count == 0) { Debug.LogWarning("[RoadRenderer] RoadBuild produced no roads."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        InitTerrainColors();

        var root = new GameObject("IslandRoads");
        Material matRoad = MakeRoadMat(), matWood = MakeWoodMat("IslandBridgeWood", 1f), matStone = MakeWoodMat("IslandRoadStones", 0f);
        hs.Clear(); grid.Clear();

        var rds = new List<RoadData>();
        for (int i = 0; i < roads.Count; i++)
        {
            var g = roads[i];
            if (g.x == null || g.x.Length < 3) continue;
            var rd = Prep(g, rds.Count);
            rds.Add(rd); AddToHash(rd);
        }

        
        var peb = new Dictionary<long, MB>();
        int nTimber = 0, nFord = 0, pebbles = 0;
        double totalLen = 0;

        foreach (var rd in rds)
        {
            var rng = new System.Random(7001 + rd.id * 131);
            totalLen += rd.L;
            SurfaceFor(rd, root.transform, matRoad);
            pebbles += EdgeStones(rd, peb, rng);
            pebbles += ScatterPebbles(rd, peb, rng);
            pebbles += FordStones(rd, peb, rng);
            foreach (var c in rd.g.crosses) if (!c.bridge) nFord++;
            foreach (var sp in rd.bridges) nTimber += BuildBridge(rd, sp, root.transform, matWood, nTimber);
        }

        int chunks = 0;
        foreach (var kv in peb) { Emit(kv.Value, "RoadPebbles_" + chunks, root.transform, matStone, false, false); chunks++; }

        Debug.Log(string.Format("[RoadRenderer] {0} roads ({1:F0} m) | bridges {2}, fords {3} | {4} pebbles in {5} chunks | {6} | {7} ms",
            rds.Count, totalLen, nTimber, nFord, pebbles, chunks, "-", sw.ElapsedMilliseconds));
    }


static void QuadUV(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col, float u0, float u1, float v0, float v1)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-12f) { n = Vector3.Cross(c - a, d - a); if (n.sqrMagnitude < 1e-12f) return; }
        n.Normalize();
        bool flip = Vector3.Dot(n, outward) < 0f; if (flip) n = -n;
        col.a = 0f; int k = m.v.Count;
        m.v.Add(a); m.v.Add(b); m.v.Add(c); m.v.Add(d);
        for (int i = 0; i < 4; i++) m.n.Add(n);
        m.c.Add(Wc(col, a.y)); m.c.Add(Wc(col, b.y)); m.c.Add(Wc(col, c.y)); m.c.Add(Wc(col, d.y));
        m.uv.Add(new Vector2(u0, v0)); m.uv.Add(new Vector2(u1, v0)); m.uv.Add(new Vector2(u1, v1)); m.uv.Add(new Vector2(u0, v1));
        if (!flip) { m.t.Add(k); m.t.Add(k + 1); m.t.Add(k + 2); m.t.Add(k); m.t.Add(k + 2); m.t.Add(k + 3); }
        else { m.t.Add(k); m.t.Add(k + 2); m.t.Add(k + 1); m.t.Add(k); m.t.Add(k + 3); m.t.Add(k + 2); }
    }

    // Rectangular beam swept along a curve; uv.x = metres along the beam so the grain runs continuously along it.
    static void Sweep(MB m, Vector3[] c, Vector3[] r, Vector3[] u, float[] hw, float[] hh, Color col, System.Random rng, bool caps)
    {
        int n = c.Length; if (n < 2) return;
        var bl = new Vector3[n]; var br = new Vector3[n]; var tr = new Vector3[n]; var tl = new Vector3[n]; var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            bl[i] = c[i] - r[i] * hw[i] - u[i] * hh[i]; br[i] = c[i] + r[i] * hw[i] - u[i] * hh[i];
            tr[i] = c[i] + r[i] * hw[i] + u[i] * hh[i]; tl[i] = c[i] - r[i] * hw[i] + u[i] * hh[i];
            if (i > 0) s[i] = s[i - 1] + (c[i] - c[i - 1]).magnitude;
        }
        for (int i = 0; i < n - 1; i++)
        {
            int j = i + 1; Color cc = col * (0.93f + 0.14f * Rf(rng));
            Vector3 up = (u[i] + u[j]).normalized, rg = (r[i] + r[j]).normalized;
            QuadUV(m, tl[i], tl[j], tr[j], tr[i], up, cc, s[i], s[j], 0f, hw[i] * 2f);
            QuadUV(m, bl[i], bl[j], br[j], br[i], -up, cc * 0.8f, s[i], s[j], 0f, hw[i] * 2f);
            QuadUV(m, bl[i], bl[j], tl[j], tl[i], -rg, cc * 0.92f, s[i], s[j], 0f, hh[i] * 2f);
            QuadUV(m, br[i], br[j], tr[j], tr[i], rg, cc * 0.92f, s[i], s[j], 0f, hh[i] * 2f);
        }
        if (!caps) return;
        int e = n - 1;
        Vector3 t0 = (c[1] - c[0]).normalized, t1 = (c[e] - c[e - 1]).normalized;
        QuadUV(m, bl[0], br[0], tr[0], tl[0], -t0, col * 0.9f, 0f, hw[0] * 2f, 0f, hh[0] * 2f);
        QuadUV(m, bl[e], br[e], tr[e], tl[e], t1, col * 0.9f, 0f, hw[e] * 2f, 0f, hh[e] * 2f);
    }


static float WallOnly(RoadData rd, int k)
    {
        var s = rd.d[k]; float hw = rd.hw; float g0 = GroundAt(s.p); float wall = 0f;
        foreach (float lo in new float[] { hw + 1.5f, hw + 3.5f })
        {
            float gL = GroundAt(s.p - s.r * lo), gR = GroundAt(s.p + s.r * lo);
            wall += Mathf.Max(0f, gL - g0 - 0.8f) + Mathf.Max(0f, gR - g0 - 0.8f);
        }
        return wall;
    }


static void NudgeFromWalls(RoadData rd, BridgeSpan sp)
    {
        var D = rd.d; int n = D.Length;
        for (int e = 0; e < 2; e++)
        {
            int j = e == 0 ? sp.jA : sp.jB; int dir = e == 0 ? -1 : 1;
            float g00 = GroundAt(D[j].p);
            float w0 = LateralWall(rd, j, 0f, g00), bestW = w0, bestOff = 0f;
            if (w0 < 0.6f) continue;
            for (float off = -2.5f; off <= 2.5f; off += 0.5f)
            {
                float w = LateralWall(rd, j, off, g00) + 0.15f * Mathf.Abs(off);
                if (w < bestW) { bestW = w; bestOff = off; }
            }
            if (Mathf.Abs(bestOff) < 0.01f) continue;
            const int Blend = 12;
            for (int q = 0; q <= Blend; q++)
            {
                int k = j + dir * q; if (k < 0 || k >= n) break;
                float w = Mathf.SmoothStep(0f, 1f, 1f - (float)q / Blend);
                Vector3 p = D[k].p + D[k].r * (bestOff * w);
                p.y = D[k].p.y;
                D[k].p = p; D[k].x = p.x; D[k].z = MirrorZ ? -p.z : p.z;
            }
        }
    }


static float LateralWall(RoadData rd, int k, float off, float g00)
    {
        var s = rd.d[k]; float hw = rd.hw; Vector3 c = s.p + s.r * off; float g0 = GroundAt(c); float wall = Mathf.Abs(g0 - g00) * 2f;
        foreach (float lo in new float[] { hw + 1.5f, hw + 3.5f })
        {
            float gL = GroundAt(c - s.r * lo), gR = GroundAt(c + s.r * lo);
            wall += Mathf.Max(0f, gL - g0 - 0.8f) + Mathf.Max(0f, gR - g0 - 0.8f);
        }
        return wall;
    }
}
