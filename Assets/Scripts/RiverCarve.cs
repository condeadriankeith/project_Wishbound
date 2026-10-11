using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Phase 3.3: carves river channels + summit lake basin into the height field.
/// TerrainMath.H = HRaw - Cut (bilinear raster). Bed per channel is non-increasing downstream.</summary>
public static class RiverCarve
{
    public const double Res = 0.75;         // cut raster resolution (m)
    public const double Ds = 1.0;           // channel sample spacing (m)
    public const double LakeDepth = 3.0;    // lake bowl depth below level (m)
    const double E = TerrainMath.E;

    public class Channel
    {
        public int river; public bool lake, trunc;
        public double[] x, z, s, bed, hw, depth, bank, q;
    }

    public static List<Channel> Channels = new List<Channel>();
    public static List<Vector3> Falls = new List<Vector3>(); public static float[] Cut; public static float[] Wet; public static int NR; public static int Hash;
    static bool building;

    public static double Apply(double x, double z, double h)
    {
        if (Cut == null) { if (building) return h; Build(1337); }
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return h;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Cut[k] + (Cut[k + 1] - Cut[k]) * tx, b = Cut[k + NR] + (Cut[k + NR + 1] - Cut[k + NR]) * tx;
        return h - (a + (b - a) * tz);
    }

    static float Sm(double u) { u = u < 0 ? 0 : u > 1 ? 1 : u; return (float)(u * u * (3 - 2 * u)); }

    /// <summary>0..1 river/lake wetness at (x,z): 1 inside the channel, fading to 0 a few metres past the
    /// banks. Bilinear over the Wet raster built next to Cut. Used to paint damp banks.</summary>
    public static double WetAt(double x, double z)
    {
        if (Wet == null) return 0;
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return 0;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Wet[k] + (Wet[k + 1] - Wet[k]) * tx, b = Wet[k + NR] + (Wet[k + NR + 1] - Wet[k + NR]) * tx;
        return a + (b - a) * tz;
    }
    static void RunMin(double[] b) { for (int m = 1; m < b.Length; m++) b[m] = Math.Min(b[m], b[m - 1] - 1e-3); }

    public static void Build(int seed = 1337)
    {
        building = true;
        try
        {
            FlowNet.Build(seed);
            double cell = WorldField.Cell;
            Channels = new List<Channel>();
            var rivers = new List<FlowNet.River>(FlowNet.Rivers);
            rivers.Sort((a, c) => c.cells.Length != a.cells.Length ? c.cells.Length.CompareTo(a.cells.Length) : a.id.CompareTo(c.id));
            var seen = new HashSet<int>(); var jBed = new Dictionary<int, double>();
            foreach (var r in rivers)
            {
                var cl = new List<int>(); bool trunc = false;
                for (int i = 0; i < r.cells.Length; i++)
                {
                    cl.Add(r.cells[i]);
                    if (i > 0 && seen.Contains(r.cells[i])) { trunc = true; break; }
                }
                if (cl.Count < 2) { foreach (int c in cl) seen.Add(c); continue; }
                bool lake = r.fromLake; int o = lake ? 1 : 0, np = cl.Count + o;
                var px = new double[np]; var pz = new double[np]; var qa = new double[np];
                if (lake)
                {
                    double dx = FlowNet.X(cl[0]) - FlowNet.LakeX, dz = FlowNet.Z(cl[0]) - FlowNet.LakeZ, dd = Math.Sqrt(dx * dx + dz * dz);
                    px[0] = FlowNet.LakeX + dx / dd * (FlowNet.LakeR - 1); pz[0] = FlowNet.LakeZ + dz / dd * (FlowNet.LakeR - 1);
                }
                for (int i = 0; i < cl.Count; i++)
                {
                    int c = cl[i]; px[o + i] = FlowNet.X(c); pz[o + i] = FlowNet.Z(c);
                    qa[o + i] = (i > 0 && (FlowNet.Sea[c] || (trunc && i == cl.Count - 1))) ? qa[o + i - 1] : FlowNet.Acc[c];
                }
                if (lake) qa[0] = qa[1];
                for (int it = 0; it < 10; it++)
                {
                    double ax = px[0], az = pz[0];
                    for (int i = 1; i < np - 1; i++)
                    {
                        double ox = px[i], oz = pz[i];
                        px[i] = 0.25 * ax + 0.5 * ox + 0.25 * px[i + 1]; pz[i] = 0.25 * az + 0.5 * oz + 0.25 * pz[i + 1];
                        ax = ox; az = oz;
                    }
                }
                var cum = new double[np];
                for (int i = 1; i < np; i++) cum[i] = cum[i - 1] + Math.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (pz[i] - pz[i - 1]) * (pz[i] - pz[i - 1]));
                double L = cum[np - 1]; int M = Math.Max(2, (int)Math.Round(L / Ds) + 1);
                var ch = new Channel { river = r.id, lake = lake, trunc = trunc, x = new double[M], z = new double[M], s = new double[M], bed = new double[M],
                    hw = new double[M], depth = new double[M], bank = new double[M], q = new double[M] };
                var ha = new double[M]; int seg = 0;
                for (int m = 0; m < M; m++)
                {
                    double s = L * m / (M - 1);
                    while (seg < np - 2 && cum[seg + 1] < s) seg++;
                    double sl = cum[seg + 1] - cum[seg], t = sl > 1e-9 ? Math.Max(0, Math.Min(1, (s - cum[seg]) / sl)) : 0;
                    ch.x[m] = px[seg] + (px[seg + 1] - px[seg]) * t; ch.z[m] = pz[seg] + (pz[seg + 1] - pz[seg]) * t; ch.s[m] = s;
                    double Q = (qa[seg] + (qa[seg + 1] - qa[seg]) * t) * cell * cell * FlowNet.Runoff; ch.q[m] = Q;
                    ch.hw[m] = 0.9 + 2.4 * Math.Sqrt(Q); ch.depth[m] = 0.4 + 1.2 * Math.Sqrt(Q); ch.bank[m] = 1.5 + 0.8 * ch.hw[m];
                    ha[m] = TerrainMath.HRaw(ch.x[m], ch.z[m]);
                }
                var b = new double[M];
                for (int m = 0; m < M; m++) b[m] = ha[m] - ch.depth[m];
                RunMin(b);
                for (int pass = 0; pass < 3; pass++)
                {
                    var t2 = new double[M];
                    for (int m = 0; m < M; m++) { double sum = 0; for (int k = -5; k <= 5; k++) sum += b[Math.Max(0, Math.Min(M - 1, m + k))]; t2[m] = sum / 11; }
                    b = t2;
                }
                for (int m = 0; m < M; m++) b[m] = Math.Min(b[m], ha[m] - ch.depth[m]);
                RunMin(b);
                if (trunc) { double jb = jBed[cl[cl.Count - 1]]; for (int m = 0; m < M; m++) b[m] = Math.Max(b[m], jb); }
                ch.bed = b; Channels.Add(ch);
                for (int i = 0; i < cl.Count; i++)
                {
                    seen.Add(cl[i]);
                    if (!jBed.ContainsKey(cl[i])) jBed[cl[i]] = b[Math.Max(0, Math.Min(M - 1, (int)Math.Round(cum[o + i] / L * (M - 1))))];
                }
            }

            // --- rasterise cut (+ wetness band for bank painting)
            NR = (int)Math.Ceiling(2 * E / Res) + 1;
            var cut = new float[NR * NR]; var wet = new float[NR * NR]; var hc = new float[NR * NR];
            for (int k = 0; k < hc.Length; k++) hc[k] = float.NaN;
            foreach (var ch in Channels)
                for (int m = 0; m < ch.x.Length - 1; m++)
                {
                    double x0 = ch.x[m], z0 = ch.z[m], vx = ch.x[m + 1] - x0, vz = ch.z[m + 1] - z0, l2 = vx * vx + vz * vz;
                    if (l2 < 1e-12) continue;
                    double R = Math.Max(ch.hw[m] + ch.bank[m], ch.hw[m + 1] + ch.bank[m + 1]);
                    int i0 = Math.Max(0, (int)Math.Floor((Math.Min(x0, x0 + vx) - R + E) / Res)), i1 = Math.Min(NR - 1, (int)Math.Ceiling((Math.Max(x0, x0 + vx) + R + E) / Res));
                    int j0 = Math.Max(0, (int)Math.Floor((Math.Min(z0, z0 + vz) - R + E) / Res)), j1 = Math.Min(NR - 1, (int)Math.Ceiling((Math.Max(z0, z0 + vz) + R + E) / Res));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            double qx = -E + i * Res, qz = -E + j * Res;
                            double t = Math.Max(0, Math.Min(1, ((qx - x0) * vx + (qz - z0) * vz) / l2));
                            double dx = qx - (x0 + t * vx), dz = qz - (z0 + t * vz), d = Math.Sqrt(dx * dx + dz * dz);
                            double hw = ch.hw[m] + (ch.hw[m + 1] - ch.hw[m]) * t, bk = ch.bank[m] + (ch.bank[m + 1] - ch.bank[m]) * t;
                            if (d >= hw + bk) continue;
                            double w = d <= hw ? 1 : 1 - Sm((d - hw) / bk);
                            int k = j * NR + i; if (float.IsNaN(hc[k])) hc[k] = (float)TerrainMath.HRaw(qx, qz);
                            double bed = ch.bed[m] + (ch.bed[m + 1] - ch.bed[m]) * t;
                            double head = ch.lake ? 1 : Sm((ch.s[m] + t * (ch.s[m + 1] - ch.s[m])) / 8);
                            double cap = hc[k] - bed; if (cap > 1.5) { double ar = TerrainMath.HRaw(x0 + t * vx, z0 + t * vz); cap = Math.Min(cap, Math.Max(0, ar - bed) + 1.5); }
 float cand = (float)(w * head * Math.Max(0, cap));
                            if (cand > cut[k]) cut[k] = cand;
                            float ww = d <= hw ? 1f : 1f - Sm((d - hw) / Math.Max(0.5, bk + 2.5));
                            if (ww > wet[k]) wet[k] = ww;
                        }
                }
            if (FlowNet.LakeCell >= 0)
            {
                double R = FlowNet.LakeR, RR = R + 8, lx = FlowNet.LakeX, lz = FlowNet.LakeZ;
                for (int j = Math.Max(0, (int)((lz - RR + E) / Res)); j <= Math.Min(NR - 1, (int)((lz + RR + E) / Res) + 1); j++)
                    for (int i = Math.Max(0, (int)((lx - RR + E) / Res)); i <= Math.Min(NR - 1, (int)((lx + RR + E) / Res) + 1); i++)
                    {
                        double qx = -E + i * Res, qz = -E + j * Res, d = Math.Sqrt((qx - lx) * (qx - lx) + (qz - lz) * (qz - lz));
                        if (d >= RR) continue;
                        double T, w;
                        if (d <= R) { T = FlowNet.LakeLevel - LakeDepth * (1 - (d / R) * (d / R)); w = 1; }
                        else { T = FlowNet.LakeLevel + (d - R) * 0.5; w = 1 - Sm((d - (R + 4)) / 4); }
                        int k = j * NR + i; if (float.IsNaN(hc[k])) hc[k] = (float)TerrainMath.HRaw(qx, qz);
                        float cand = (float)(w * Math.Max(0, hc[k] - T));
                        if (cand > cut[k]) cut[k] = cand;
                        float wl = d <= R ? 1f : 1f - Sm((d - R) / 6.0);
                        if (wl > wet[k]) wet[k] = wl;
                    }
            }
    Falls.Clear(); foreach (var fc in Channels) { int m0 = -1; for (int m = 1; m <= fc.x.Length; m++) { bool st = m < fc.x.Length && (fc.bed[m - 1] - fc.bed[m]) / Math.Max(0.01, fc.s[m] - fc.s[m - 1]) > 1.0; if (st && m0 < 0) m0 = m - 1; if (!st && m0 >= 0) { double dr = fc.bed[m0] - fc.bed[m - 1]; if (dr > 4) Falls.Add(new Vector3((float)fc.x[m0], (float)dr, (float)fc.z[m0])); m0 = -1; } } }
        Cut = cut; Wet = wet;
            unchecked { int hs = (int)2166136261u; for (int k = 0; k < cut.Length; k++) hs = (hs ^ (int)(cut[k] * 1000f)) * 16777619; Hash = hs; }
        }
        finally { building = false; }
    }

    public static string Validate(int seed = 1337)
    {
        Cut = null; Build(seed);
        var sb = new StringBuilder("[RiverCarve] seed " + seed + "\n");
        int rises = 0, samples = 0, badErr = 0, raised = 0, xs = 0, bad = 0; double maxErr = 0, maxStep = 0, maxCut = 0, totalLen = 0, maxW = 0;
        foreach (var ch in Channels)
        {
            int M = ch.x.Length; int up = 0; double chMax = 0;
            for (int m = 0; m < M; m++)
            {
                if (m < M - 1) { if (ch.bed[m + 1] > ch.bed[m] + 1e-9) up++; maxStep = Math.Max(maxStep, ch.bed[m] - ch.bed[m + 1]); }
                if (!ch.lake && ch.s[m] < 8) continue;
                samples++;
                double err = Math.Abs(TerrainMath.H(ch.x[m], ch.z[m]) - ch.bed[m]);
                if (err > chMax) chMax = err; if (err > 0.5) badErr++;
                if (err > maxErr) maxErr = err;
                maxW = Math.Max(maxW, 2 * (ch.hw[m] + ch.bank[m]));
                if (m % 4 == 0 && m < M - 1)
                {
                    double tx = ch.x[m + 1] - ch.x[m], tz = ch.z[m + 1] - ch.z[m], tl = Math.Sqrt(tx * tx + tz * tz); if (tl < 1e-9) continue;
                    for (int k = -10; k <= 10; k++)
                    {
                        double qx = ch.x[m] - tz / tl * k * 0.75, qz = ch.z[m] + tx / tl * k * 0.75; xs++;
                        double rh = TerrainMath.HRaw(qx, qz), cr = TerrainMath.H(qx, qz);
                        if (cr > rh + 0.01) raised++;
                        if (double.IsNaN(cr) || rh - cr > 40) bad++;
                    }
                }
            }
            rises += up; totalLen += ch.s[M - 1];
            sb.AppendLine(string.Format("  R{0}{1}{2} len={3:F0}m bed {4:F1}->{5:F1}m width {6:F1}..{7:F1}m rises={8} axisErr<={9:F2}m",
                ch.river, ch.lake ? "[lake]" : "", ch.trunc ? "[trib]" : "", ch.s[M - 1], ch.bed[0], ch.bed[M - 1], 2 * (ch.hw[0] + ch.bank[0]), 2 * (ch.hw[M - 1] + ch.bank[M - 1]), up, chMax));
        }
        for (int k = 0; k < Cut.Length; k++) { if (float.IsNaN(Cut[k]) || Cut[k] < 0) bad++; if (Cut[k] > maxCut) maxCut = Cut[k]; }
        int h1 = Hash; Cut = null; Build(seed); bool det = h1 == Hash;
        double lc = FlowNet.LakeCell >= 0 ? TerrainMath.H(FlowNet.LakeX, FlowNet.LakeZ) : 0;
        bool lakeOk = FlowNet.LakeCell < 0 || lc <= FlowNet.LakeLevel - 1.5;
        bool pass = rises == 0 && raised == 0 && bad == 0 && badErr <= samples / 20 && det && lakeOk && Channels.Count >= 3;
        sb.AppendLine(string.Format("  channels {0} ({1:F0}m) | bed rises {2} | raised-above-raw cells {3}/{4} | NaN/neg/huge {5} | axis err max {6:F2}m (>0.5m: {7}/{8})", Channels.Count, totalLen, rises, raised, xs, bad, maxErr, badErr, samples));
        sb.AppendLine(string.Format("  max cut {0:F1}m | max section width {1:F1}m | steepest bed step {2:F2}m/m | lake centre H {3:F1}m vs level {4:F1}m | deterministic {5}", maxCut, maxW, maxStep, lc, FlowNet.LakeLevel, det));
        sb.AppendLine(pass ? "  RESULT: PASS" : "  RESULT: FAIL");
        return sb.ToString();
    }
}

#if UNITY_EDITOR
static class RiverCarveMenu
{
    [UnityEditor.MenuItem("Island/Phase 3.3 Validate Carving")]
    static void Run() { Debug.Log(RiverCarve.Validate()); }
}
#endif
