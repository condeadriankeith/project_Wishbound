using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Phase 3.2: flow network over WorldField. Data only. Source coords (x,z) like WorldField.
/// Priority-Flood+epsilon => every land cell drains strictly downhill (on conditioned Bed) to the sea.</summary>
public static class FlowNet
{
    public const double Runoff = 2e-5;       // m/s runoff per m2 of catchment
    public const double Eps = 1e-4;          // fill epsilon (m)
    public const double LakeR = 12.0;        // summit lake radius (m)
    public const int SpringCount = 6;        // incl. lake outlet
    public const double MinSpringSep = 80.0; // m

    public class River { public int id; public int[] cells; public bool fromLake; public double length, mouthQ; }

    public static int[] Dir;        // downstream cell index, -1 = root (sea/border)
    public static double[] Bed;     // conditioned (filled) surface, strictly decreasing downstream
    public static int[] Acc;        // upstream cell count incl. self
    public static bool[] Sea, InLake;
    public static List<int> Springs = new List<int>();   // Springs[0] = lake outlet (if lake exists)
    public static List<River> Rivers = new List<River>();
    public static double LakeX, LakeZ, LakeLevel;
    public static int LakeCell = -1, OutletCell = -1, Hash;
    static bool built; static int builtSeed;

    public static double X(int k) { return -TerrainMath.E + (k % WorldField.G) * WorldField.Cell; }
    public static double Z(int k) { return -TerrainMath.E + (k / WorldField.G) * WorldField.Cell; }
    public static int CellAt(double x, double z)
    {
        int g = WorldField.G;
        int i = Mathf.Clamp((int)Math.Round((x + TerrainMath.E) / WorldField.Cell), 0, g - 1);
        int j = Mathf.Clamp((int)Math.Round((z + TerrainMath.E) / WorldField.Cell), 0, g - 1);
        return j * g + i;
    }
    /// <summary>Discharge in m3/s at a cell.</summary>
    public static double Discharge(int k) { return Acc[k] * WorldField.Cell * WorldField.Cell * Runoff; }
    public static double Flow(double x, double z) { Build(builtSeed == 0 ? 1337 : builtSeed); return Discharge(CellAt(x, z)); }

    class Heap
    {
        double[] k; int[] v; public int n;
        public Heap(int cap) { k = new double[cap]; v = new int[cap]; }
        bool Less(int a, int b) { return k[a] < k[b] || (k[a] == k[b] && v[a] < v[b]); }
        void Swap(int a, int b) { double t = k[a]; k[a] = k[b]; k[b] = t; int u = v[a]; v[a] = v[b]; v[b] = u; }
        public void Push(double key, int val)
        {
            int i = n++; k[i] = key; v[i] = val;
            while (i > 0) { int p = (i - 1) / 2; if (!Less(i, p)) break; Swap(i, p); i = p; }
        }
        public int Pop()
        {
            int r = v[0]; n--;
            if (n > 0)
            {
                k[0] = k[n]; v[0] = v[n]; int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, rr = l + 1, m = i;
                    if (l < n && Less(l, m)) m = l;
                    if (rr < n && Less(rr, m)) m = rr;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }
            return r;
        }
    }

    public static void Build(int seed = 1337)
    {
        if (built && builtSeed == seed) return;
        WorldField.Build();
        int g = WorldField.G, n = g * g; double cell = WorldField.Cell;
        float[] h = WorldField.HeightGrid;
        Sea = new bool[n]; InLake = new bool[n]; Springs = new List<int>(); Rivers = new List<River>();
        LakeCell = OutletCell = -1;
        for (int k = 0; k < n; k++)
        {
            int i = k % g, j = k / g;
            Sea[k] = h[k] <= WorldField.SeaLevel || i == 0 || j == 0 || i == g - 1 || j == g - 1;
        }

        // --- summit lake site: flattest spot among the top 10% of heights
        double best = 1e18; int rc = (int)Math.Ceiling((LakeR + 1.5 * cell) / cell);
        float hiCut = WorldField.MaxHeight * 0.9f;
        for (int j = rc; j < g - rc; j++)
            for (int i = rc; i < g - rc; i++)
            {
                int k = j * g + i; if (h[k] < hiCut) continue;
                double mn = 1e9, mx = -1e9;
                for (int dj = -rc; dj <= rc; dj++)
                    for (int di = -rc; di <= rc; di++)
                    {
                        if (Math.Sqrt(di * di + dj * dj) * cell > LakeR + 1.5 * cell) continue;
                        double v = h[(j + dj) * g + i + di]; if (v < mn) mn = v; if (v > mx) mx = v;
                    }
                if (mx - mn < best) { best = mx - mn; LakeCell = k; }
            }
        var hm = new double[n];
        for (int k = 0; k < n; k++) hm[k] = h[k];
        if (LakeCell >= 0)
        {
            LakeX = X(LakeCell); LakeZ = Z(LakeCell);
            double ringMin = 1e9;
            for (int k = 0; k < n; k++)
            {
                double dx = X(k) - LakeX, dz = Z(k) - LakeZ;
                if (Math.Abs(dx) > LakeR + 2 * cell || Math.Abs(dz) > LakeR + 2 * cell) continue;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= LakeR) InLake[k] = true;
                else if (d <= LakeR + 1.5 * cell) ringMin = Math.Min(ringMin, h[k]);
            }
            for (int k = 0; k < n; k++) if (InLake[k]) hm[k] = ringMin;
        }

        // --- Priority-Flood + epsilon from the sea
        var f = new double[n]; var done = new bool[n]; var order = new int[n]; int oc = 0;
        Dir = new int[n]; var hp = new Heap(n);
        for (int k = 0; k < n; k++) { Dir[k] = -1; if (Sea[k]) { f[k] = hm[k]; done[k] = true; hp.Push(f[k], k); } }
        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, oz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (hp.n > 0)
        {
            int c = hp.Pop(); order[oc++] = c; int ci = c % g, cj = c / g;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + ox[d], nj = cj + oz[d];
                if (ni < 0 || nj < 0 || ni >= g || nj >= g) continue;
                int nb = nj * g + ni; if (done[nb]) continue;
                done[nb] = true; f[nb] = Math.Max(hm[nb], f[c] + Eps); Dir[nb] = c; hp.Push(f[nb], nb);
            }
        }
        Bed = f;

        // --- accumulation (reverse pop order: children before parents)
        Acc = new int[n];
        for (int k = 0; k < n; k++) Acc[k] = Sea[k] ? 0 : 1;
        for (int q = oc - 1; q >= 0; q--) { int k = order[q], p = Dir[k]; if (p >= 0) Acc[p] += Acc[k]; }

        // --- lake outlet = first non-lake cell on the lake's drain path
        if (LakeCell >= 0)
        {
            int c = LakeCell, guard = 0;
            while (InLake[c] && Dir[c] >= 0 && guard++ < n) c = Dir[c];
            OutletCell = c; LakeLevel = Bed[c]; Springs.Add(c);
        }

        // --- springs: seeded pick of channel heads on mid/upper slopes, spaced apart
        int[] lo = { 12, 3 }, hi = { 60, 400 };
        for (int pass = 0; pass < 2 && Springs.Count < SpringCount; pass++)
        {
            var cand = new List<int>();
            for (int k = 0; k < n; k++)
            {
                if (Sea[k] || InLake[k]) continue;
                if (h[k] < 0.2f * WorldField.MaxHeight || h[k] > 0.85f * WorldField.MaxHeight) continue;
                if (Acc[k] < lo[pass] || Acc[k] > hi[pass] || WorldField.SlopeGrid[k] > WorldField.CliffSlopeDeg) continue;
                cand.Add(k);
            }
            var rng = new System.Random(seed * 7919 + 13 + pass);
            for (int a = cand.Count - 1; a > 0; a--) { int b = rng.Next(a + 1); int t = cand[a]; cand[a] = cand[b]; cand[b] = t; }
            foreach (int c in cand)
            {
                if (Springs.Count >= SpringCount) break;
                bool ok = true;
                foreach (int s in Springs)
                {
                    double dx = X(c) - X(s), dz = Z(c) - Z(s);
                    if (Math.Sqrt(dx * dx + dz * dz) < MinSpringSep) { ok = false; break; }
                }
                if (!ok) continue;
                int len = 0, q = c; while (Dir[q] >= 0 && len < n) { q = Dir[q]; len++; }
                if (len < 15) continue;
                Springs.Add(c);
            }
        }

        // --- trace rivers spring -> sea
        for (int r = 0; r < Springs.Count; r++)
        {
            var path = new List<int>(); int c = Springs[r], guard = 0; double len = 0;
            while (true)
            {
                path.Add(c); if (Dir[c] < 0 || guard++ > n) break;
                int p = Dir[c]; len += (Math.Abs(p % g - c % g) + Math.Abs(p / g - c / g) == 2 ? 1.41421356 : 1.0) * cell; c = p;
            }
            Rivers.Add(new River { id = r, cells = path.ToArray(), fromLake = (r == 0 && OutletCell >= 0), length = len,
                mouthQ = Discharge(path[Math.Max(0, path.Count - 2)]) });
        }

        unchecked
        {
            int hs = (int)2166136261u;
            for (int k = 0; k < n; k++) { hs = (hs ^ Dir[k]) * 16777619; hs = (hs ^ Acc[k]) * 16777619; }
            foreach (int s in Springs) hs = (hs ^ s) * 16777619;
            Hash = hs;
        }
        built = true; builtSeed = seed;
    }

    public static string Validate(int seed = 1337)
    {
        Build(seed);
        int g = WorldField.G, n = g * g; float[] h = WorldField.HeightGrid;
        var sb = new StringBuilder("[FlowNet] seed " + seed + "\n");
        int land = 0, dead = 0, badBed = 0, filled = 0; double maxFill = 0;
        for (int k = 0; k < n; k++)
        {
            if (Sea[k]) continue;
            land++;
            if (Dir[k] < 0) dead++;
            else if (Bed[Dir[k]] >= Bed[k]) badBed++;
            if (!InLake[k]) { double fd = Bed[k] - h[k]; if (fd > 0.05) filled++; if (fd > maxFill) maxFill = fd; }
        }
        int riverFail = 0, rawUp = 0, steps = 0; double maxRise = 0;
        foreach (River r in Rivers)
        {
            int[] c = r.cells; bool ok = c[0] == Springs[r.id] && c.Length > 2 && h[c[c.Length - 1]] <= WorldField.SeaLevel;
            for (int t = 0; t < c.Length - 1; t++)
            {
                int a = c[t], b = c[t + 1]; steps++;
                if (Bed[b] >= Bed[a] || Acc[b] < Acc[a]) ok = false;
                double rise = h[b] - h[a]; if (rise > 0.05) { rawUp++; if (rise > maxRise) maxRise = rise; }
            }
            if (!ok) riverFail++;
            int s0 = c[0];
            sb.AppendLine(string.Format("  R{0}{1} {2} spring({3:F0},{4:F0}) h={5:F0}m len={6:F0}m grade={7:F1}% Qmouth={8:F2}m3/s",
                r.id, r.fromLake ? "[lake]" : "", ok ? "OK" : "FAIL", X(s0), Z(s0), h[s0], r.length, 100 * h[s0] / Math.Max(1, r.length), r.mouthQ));
        }
        int h1 = Hash; built = false; Build(seed); bool det = h1 == Hash;
        bool pass = dead == 0 && badBed == 0 && riverFail == 0 && Rivers.Count >= 3 && det;
        if (LakeCell >= 0)
            sb.AppendLine(string.Format("  lake c=({0:F0},{1:F0}) r={2}m level={3:F1}m outlet=({4:F0},{5:F0}) rawH={6:F1}m",
                LakeX, LakeZ, LakeR, LakeLevel, X(OutletCell), Z(OutletCell), h[OutletCell]));
        sb.AppendLine(string.Format("  land {0} | dead-ends {1} | non-downhill bed steps {2} | rivers {3} (fail {4}) | deterministic {5}", land, dead, badBed, Rivers.Count, riverFail, det));
        sb.AppendLine(string.Format("  info: depressions filled {0} cells, max fill {1:F2}m | raw-terrain uphill steps on rivers {2}/{3} (max {4:F2}m) -> 3.3 carving must cut these", filled, maxFill, rawUp, steps, maxRise));
        sb.AppendLine(pass ? "  RESULT: PASS" : "  RESULT: FAIL");
        return sb.ToString();
    }
}

#if UNITY_EDITOR
static class FlowNetMenu
{
    [UnityEditor.MenuItem("Island/Phase 3.2 Validate Flow")]
    static void Run() { Debug.Log(FlowNet.Validate()); }
}
#endif
