using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Phase 3.7 + 3.8 — turns the RoadNet (3.6) cell paths into graded, walkable roads and resolves every
/// water crossing as a bridge or a ford. Data only; RoadRenderer draws the meshes.
///
/// Per road: the cell centreline is Chaikin-smoothed, resampled to a fixed spacing, then a grade-limited
/// profile is solved so |dy/ds| never exceeds the cap (12% main, 16% branch). That profile both defines the
/// road surface AND drives a terrain-flattening raster (RoadGrade.Fill) that pulls the ground up to the road
/// inside the corridor — so the road never clips terrain. River/lake crossings are detected on the natural
/// carved ground: wide or deep ones become bridges (deck lifted above the channel, banks left open), shallow
/// narrow ones become fords (road dips to the bed and the water runs over it).
/// Deterministic from a seed.
/// </summary>
public static class RoadBuild
{
    public const double Res = 1.0;          // grading raster resolution (m) — matches RoadGrade
    public const double E = TerrainMath.E;
    public const double Ds = 2.0;           // centreline resample spacing (m)
    public const double Shoulder = 2.2;     // flattening half-width beyond the running surface
    public const double MaxCut = 7.0;       // deepest cut into a hill (bounds retaining walls)
    public const double MaxFill = 5.0;      // tallest embankment (bounds fill)
    public const double DeckClear = 1.1;    // bridge deck height above the channel bed

    public class Cross
    {
        public int i0, i1;          // centreline index range of the water span (inclusive)
        public bool bridge;         // true = bridge, false = ford
        public double width;        // arc length across the water (m)
        public double depth;        // channel incision across the span (m)
        public double deckY;        // bridge deck height (bridges only)
        public string kind;         // "river" | "lake" | "sea"
    }

    public class GradedRoad
    {
        public string name; public bool main;
        public double[] x, z, s, prof, halfW, natH;
        public List<Cross> crosses = new List<Cross>();
        public double length, meanGrade, maxGrade, maxWall;
    }

    public static List<GradedRoad> Roads = new List<GradedRoad>();
    public static int Hash;
    static bool built; static int builtSeed;

    public static double X(double x) => x;   // (paths already carry source x,z)
    public static int CellAt(double x, double z) => RoadNet.CellAt(x, z);

    // ---------- geometry helpers ----------
    // Chaikin corner-cutting; returns the smoothed polyline via out params.
    static void Chaikin(double[] px, double[] pz, int passes, out double[] ox, out double[] oz)
    {
        var ax = new List<double>(px); var az = new List<double>(pz);
        for (int p = 0; p < passes && ax.Count >= 3; p++)
        {
            var bx = new List<double>(ax.Count * 2); var bz = new List<double>(az.Count * 2);
            bx.Add(ax[0]); bz.Add(az[0]);
            for (int i = 0; i + 1 < ax.Count; i++)
            {
                bx.Add(0.75 * ax[i] + 0.25 * ax[i + 1]); bz.Add(0.75 * az[i] + 0.25 * az[i + 1]);
                bx.Add(0.25 * ax[i] + 0.75 * ax[i + 1]); bz.Add(0.25 * az[i] + 0.75 * az[i + 1]);
            }
            bx.Add(ax[ax.Count - 1]); bz.Add(az[az.Count - 1]);
            ax = bx; az = bz;
        }
        ox = ax.ToArray(); oz = az.ToArray();
    }

    // resample a polyline to fixed arc-length spacing
    static void Resample(double[] px, double[] pz, out double[] rx, out double[] rz, out double[] rs, double ds)
    {
        var cum = new double[px.Length];
        for (int i = 1; i < px.Length; i++)
            cum[i] = cum[i - 1] + Math.Sqrt((px[i] - px[i - 1]) * (px[i] - px[i - 1]) + (pz[i] - pz[i - 1]) * (pz[i] - pz[i - 1]));
        double L = cum[px.Length - 1];
        int M = Math.Max(2, (int)Math.Round(L / ds) + 1);
        rx = new double[M]; rz = new double[M]; rs = new double[M];
        int seg = 0;
        for (int m = 0; m < M; m++)
        {
            double target = L * m / (M - 1);
            while (seg < px.Length - 2 && cum[seg + 1] < target) seg++;
            double sl = cum[seg + 1] - cum[seg], t = sl > 1e-9 ? Math.Max(0, Math.Min(1, (target - cum[seg]) / sl)) : 0;
            rx[m] = px[seg] + (px[seg + 1] - px[seg]) * t;
            rz[m] = pz[seg] + (pz[seg + 1] - pz[seg]) * t;
            rs[m] = target;
        }
    }

    // Enforce |prof[b]-prof[a]| <= cap*ds by splitting the excess between the two points, so the
    // profile stays centred on the terrain instead of collapsing to one end. Iterated to a fixed point.
    static void Relax(double[] prof, int a, int b, double ds, double cap)
    {
        double d = prof[b] - prof[a], lim = cap * ds;
        if (d > lim) { double e = (d - lim) * 0.5; prof[a] += e; prof[b] -= e; }
        else if (d < -lim) { double e = (-d - lim) * 0.5; prof[a] -= e; prof[b] += e; }
    }

    // Solve a grade-limited road surface close to the natural carved ground.
    // prof must satisfy: |grade| <= cap, prof >= floor (bridge decks), prof within [natH-MaxCut, natH+MaxFill].
static double[] SolveProfile(double[] natH, double[] ds, double[] floor, double cap)
    {
        int M = natH.Length;
        var prof = new double[M];
        for (int m = 0; m < M; m++) prof[m] = Math.Max(natH[m], floor[m]);
        for (int m = 0; m < M; m++) prof[m] = Math.Max(floor[m], Cl(natH[m] - MaxCut, natH[m] + MaxFill, prof[m]));
        for (int iter = 0; iter < 400; iter++)
        {
            for (int m = 1; m < M; m++) Relax(prof, m - 1, m, ds[m], cap);
            for (int m = M - 2; m >= 0; m--) Relax(prof, m, m + 1, ds[m + 1], cap);
            for (int m = 0; m < M; m++) { prof[m] = Cl(natH[m] - MaxCut, natH[m] + MaxFill, prof[m]); prof[m] = Math.Max(prof[m], floor[m]); }
        }
        // Strict final guarantee of the grade cap. RAISE-ONLY: whichever end of an over-steep step is lower
        // is lifted, so bridge floors and 'road never below ground' can never be undone by this pass.
        // Monotone increasing and bounded, so it converges.
        for (int iter = 0; iter < 2000; iter++)
        {
            bool changed = false;
            for (int m = 1; m < M; m++)
            {
                double lim = cap * ds[m], d = prof[m] - prof[m - 1];
                if (d > lim + 1e-9) { prof[m - 1] = prof[m] - lim; changed = true; }
                else if (d < -lim - 1e-9) { prof[m] = prof[m - 1] - lim; changed = true; }
            }
            for (int m = M - 1; m >= 1; m--)
            {
                double lim = cap * ds[m], d = prof[m] - prof[m - 1];
                if (d > lim + 1e-9) { prof[m - 1] = prof[m] - lim; changed = true; }
                else if (d < -lim - 1e-9) { prof[m] = prof[m - 1] - lim; changed = true; }
            }
            if (!changed) break;
        }
        return prof;
    }

    // natural carved height (rivers applied, road grading OFF) at a source point
    static double NatH(double x, double z)
    {
        bool ro = RoadsOn; RoadsOn = false;
        double h = H(x, z);
        RoadsOn = ro;
        return h;
    }

    // water test on the natural carved ground
    static bool IsWater(double x, double z, double h)
    {
        if (h <= WorldField.SeaLevel + 0.2) return true;
        if (RiverCarve.WetAt(x, z) > 0.5) return true;
        if (FlowNet.LakeCell >= 0)
        {
            double dx = x - FlowNet.LakeX, dz = z - FlowNet.LakeZ;
            if (dx * dx + dz * dz < (FlowNet.LakeR + 1.5) * (FlowNet.LakeR + 1.5)) return true;
        }
        return false;
    }

    // half running-surface width (main roads wider)
    static double HalfWidth(bool main) => main ? 3.4 : 2.6;

    // 0..1 smoothstep (TerrainMath.Sm is the 3-arg a..b form; the corridor masks want plain smoothstep)
    static double Sm01(double u) { u = u < 0 ? 0 : u > 1 ? 1 : u; return u * u * (3 - 2 * u); }

    public static void Build(int seed = 1337)
    {
        if (built && builtSeed == seed && RoadGrade.Fill != null) return;
        RoadNet.Build(seed);
        Roads = new List<GradedRoad>();
        int NR = (int)Math.Ceiling(2 * E / Res) + 1;
        var fill = new float[NR * NR];
        var fillW = new float[NR * NR];   // distance to the nearest road centreline that owns each cell
        for (int k = 0; k < fillW.Length; k++) fillW[k] = 1e9f;
        var mask = new float[NR * NR];    // corridor footprint (1 on the bench -> 0 at the shoulder)

        foreach (var path in RoadNet.Paths)
        {
            if (path.x.Length < 3) continue;
            Chaikin(path.x, path.z, 3, out var cx, out var cz);
            Resample(cx, cz, out var rx, out var rz, out var rs, Ds);
            int M = rx.Length;
            var road = new GradedRoad { name = path.name, main = path.main, x = rx, z = rz, s = rs };
            road.natH = new double[M];
            var ds = new double[M];
            var floor = new double[M];
            var isBridge = new bool[M];
            var water = new bool[M];
            for (int m = 0; m < M; m++)
            {
                road.natH[m] = NatH(rx[m], rz[m]);
                water[m] = IsWater(rx[m], rz[m], road.natH[m]);
                floor[m] = -1e9;
                if (m > 0) ds[m] = Math.Max(0.5, rs[m] - rs[m - 1]);
            }
            ds[0] = ds.Length > 1 ? ds[1] : Ds;
            double cap = path.main ? MainMaxGradeFor() : BranchMaxGradeFor();

            // --- classify water spans -> bridge or ford ---
            int ms = 0;
            while (ms < M)
            {
                if (!water[ms]) { ms++; continue; }
                int i0 = ms, i1 = ms;
                int gap = 0;
                for (int q = ms + 1; q < M; q++) { if (water[q]) { i1 = q; gap = 0; } else if (++gap > 2) break; }
                // extend one dry sample each side for bank heights
                int a = Math.Max(0, i0 - 1), b = Math.Min(M - 1, i1 + 1);
                double width = rs[b] - rs[a];
                double bedMin = double.PositiveInfinity;
                for (int q = i0; q <= i1; q++) bedMin = Math.Min(bedMin, road.natH[q]);
                double bankMax = Math.Max(road.natH[a], road.natH[b]);
                double depth = bankMax - bedMin;
                bool seaSpan = road.natH[i0] <= WorldField.SeaLevel + 0.2 && road.natH[i1] <= WorldField.SeaLevel + 0.2 && (a == 0 || b == M - 1);
                if (!seaSpan)
                {
                    bool ford = width <= 7.0 && depth <= 1.3;
                    var cross = new Cross { i0 = i0, i1 = i1, bridge = !ford, width = width, depth = depth };
                    if (!ford)
                    {
                        double fallBonus = 0.0;
                        foreach (var fc in RiverCarve.Falls)
                        {
                            double fx = fc.x, fdrop = fc.y, fz = fc.z;
                            double dMin = double.PositiveInfinity;
                            for (int q = i0; q <= i1; q++)
                            {
                                double dx = road.x[q] - fx, dz = road.z[q] - fz;
                                double dd = Math.Sqrt(dx * dx + dz * dz);
                                if (dd < dMin) dMin = dd;
                            }
                            if (dMin < 14.0)
                            {
                                double influence = 1.0 - Cl(0, 1, dMin / 14.0);
                                fallBonus = Math.Max(fallBonus, Math.Min(fdrop * 0.55, 3.0) * influence);
                            }
                        }
                        double deckBase = bedMin + DeckClear + fallBonus;
                        for (int q = i0; q <= i1; q++)
                        {
                            double t = (i1 > i0) ? (q - i0) / (double)(i1 - i0) : 0;
                            double bank = road.natH[a] + (road.natH[b] - road.natH[a]) * t;
                            floor[q] = Math.Max(Math.Max(bank, deckBase), road.natH[q] + 0.35 + fallBonus);   // clear the local ground (and any nearby falls pool) too
                            isBridge[q] = true;
                        }
                        cross.deckY = floor[(i0 + i1) / 2];
                        cross.kind = ClassifyWater(i0, i1, road);
                    }
                    else cross.kind = "ford";
                    road.crosses.Add(cross);
                }
                ms = i1 + 1;
            }

            road.prof = SolveProfile(road.natH, ds, floor, cap);
            road.halfW = new double[M];
            for (int q = 0; q < M; q++) road.halfW[q] = HalfWidth(path.main);

            // stats
            double len = 0, absRise = 0, maxG = 0, maxWall = 0;
            for (int q = 1; q < M; q++)
            {
                double seg = rs[q] - rs[q - 1];
                len += seg;
                absRise += Math.Abs(road.prof[q] - road.prof[q - 1]);
                double gg = Math.Abs(road.prof[q] - road.prof[q - 1]) / Math.Max(1e-6, seg);
                maxG = Math.Max(maxG, gg);
                double wall = Math.Abs(road.natH[q] - road.prof[q]);
                if (!isBridge[q]) maxWall = Math.Max(maxWall, wall);
            }
            road.length = rs[M - 1];
            road.meanGrade = absRise / Math.Max(1e-6, len);
            road.maxGrade = maxG; road.maxWall = maxWall;

            // --- stamp the flattening raster (skip bridge spans: leave the channel open) ---
            StampFill(fill, fillW, mask, road, isBridge);
            Roads.Add(road);
        }

        RoadGrade.Fill = fill; RoadGrade.Mask = mask; RoadGrade.NR = NR;
        unchecked { int hs = (int)2166136261u; foreach (var r in Roads) { hs = (hs ^ r.x.Length) * 16777619; hs = (hs ^ r.crosses.Count) * 16777619; } for (int k = 0; k < fill.Length; k += 7) hs = (hs ^ (int)(fill[k] * 1000f)) * 16777619; Hash = hs; }
        built = true; builtSeed = seed;
    }

    static double MainMaxGradeFor() => RoadNet.MainMaxGrade;
    static double BranchMaxGradeFor() => RoadNet.BranchMaxGrade;

    static string ClassifyWater(int i0, int i1, GradedRoad road)
    {
        if (FlowNet.LakeCell >= 0)
        {
            double mx = 0, mz = 0;
            for (int q = i0; q <= i1; q++) { mx += road.x[q]; mz += road.z[q]; }
            mx /= (i1 - i0 + 1); mz /= (i1 - i0 + 1);
            double dx = mx - FlowNet.LakeX, dz = mz - FlowNet.LakeZ;
            if (dx * dx + dz * dz < (FlowNet.LakeR + 4) * (FlowNet.LakeR + 4)) return "lake";
        }
        return "river";
    }

    // Rasterise the corridor flattening: pull the ground toward the road surface inside the running
    // width, blending back to natural terrain across the shoulder. Bridge water-spans are skipped so the
    // channel stays open (the bridge deck carries the road over the gap). Closest road wins at junctions.
    static void StampFill(float[] fill, float[] fillW, float[] mask, GradedRoad road, bool[] isBridge)
    {
        int M = road.x.Length;
        for (int q = 0; q < M - 1; q++)
        {
            if (isBridge[q] && isBridge[q + 1]) continue;   // keep the channel open under the deck
            double x0 = road.x[q], z0 = road.z[q], x1 = road.x[q + 1], z1 = road.z[q + 1];
            double vx = x1 - x0, vz = z1 - z0, l2 = vx * vx + vz * vz; if (l2 < 1e-12) continue;
            double len = Math.Sqrt(l2), tx = vx / len, tz = vz / len;   // unit tangent (source xz)
            double R = Math.Max(road.halfW[q], road.halfW[q + 1]) + Shoulder + 1;
            int NRc = (int)Math.Ceiling(2 * E / Res) + 1;
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(x0, x1) - R + E) / Res)), i1 = Math.Min(NRc - 1, (int)Math.Ceiling((Math.Max(x0, x1) + R + E) / Res));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(z0, z1) - R + E) / Res)), j1 = Math.Min(NRc - 1, (int)Math.Ceiling((Math.Max(z0, z1) + R + E) / Res));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double cx = -E + i * Res, cz = -E + j * Res;
                    double t = Cl(0, 1, ((cx - x0) * vx + (cz - z0) * vz) / l2);
                    double dx = cx - (x0 + t * vx), dz = cz - (z0 + t * vz), d = Math.Sqrt(dx * dx + dz * dz);
                    double hw = road.halfW[q] + (road.halfW[q + 1] - road.halfW[q]) * t;
                    if (d >= hw + Shoulder + 1) continue;
                    double prof = road.prof[q] + (road.prof[q + 1] - road.prof[q]) * t;
                    double fall = d <= hw ? 1.0 : 1.0 - Sm01((d - hw) / (Shoulder + 1));
                    int k = j * NRc + i;
                    if (d >= fillW[k]) continue;   // a nearer centreline already owns this cell (nearest wins, so bends can't overwrite each other)
                    double nat = NatH(cx, cz);
                    double delta = (prof - nat) * fall;
                    delta = Cl(-(MaxCut + 2), MaxFill + 2, delta);
                    fill[k] = (float)delta; fillW[k] = (float)d;
                    if (fall > mask[k]) mask[k] = (float)fall;
                }
        }
    }




    public static string Validate(int seed = 1337)
    {
        Build(seed);
        var sb = new StringBuilder("[RoadBuild] seed " + seed + "\n");
        bool pass = Roads.Count >= 3;
        int badGradeMain = 0, badGradeBranch = 0, badClip = 0, badWater = 0, bridges = 0, fords = 0, samples = 0;
        double maxClip = 0;
        foreach (var r in Roads)
        {
            int M = r.x.Length;
            for (int q = 1; q < M; q++)
            {
                double seg = Math.Max(1e-6, r.s[q] - r.s[q - 1]);
                double g = Math.Abs(r.prof[q] - r.prof[q - 1]) / seg;
                if (r.main) { if (g > 0.125) badGradeMain++; } else if (g > 0.165) badGradeBranch++;
            }
            // terrain clipping: road surface must sit at/above the graded ground along the corridor
            for (int q = 0; q < M; q += 2)
            {
                samples++;
                double ground = TerrainMath.H(r.x[q], r.z[q]);   // H() now includes road grading
                double clip = ground - r.prof[q];               // >0 means ground pokes through the road
                if (clip > maxClip) maxClip = clip;
                if (clip > 0.35) badClip++;
            }
            foreach (var c in r.crosses)
            {
                if (c.bridge)
                {
                    bridges++;
                    // deck must clear the water at both banks and be continuous with the profile
                    for (int q = c.i0; q <= c.i1; q++)
                        if (IsWater(r.x[q], r.z[q], r.natH[q]) && r.prof[q] < r.natH[q] + 0.2) badWater++;
                }
                else fords++;
            }
            sb.AppendLine(string.Format("  {0,-11}{1} len {2:F0}m mean {3:F1}% max {4:F1}% wall {5:F1}m crossings {6}",
                r.name, r.main ? "[main]" : "      ", r.length, 100 * r.meanGrade, 100 * r.maxGrade, r.maxWall, r.crosses.Count));
            foreach (var c in r.crosses)
                sb.AppendLine(string.Format("      {0} {1} w={2:F1}m depth={3:F1}m deck={4:F1}m [{5}..{6}]",
                    c.bridge ? "BRIDGE" : "ford  ", c.kind, c.width, c.depth, c.deckY, c.i0, c.i1));
        }
        if (badGradeMain > 0 || badGradeBranch > 0) pass = false;
        if (badClip > 0) pass = false;
        if (badWater > 0) pass = false;
        int h1 = Hash; RoadGrade.Fill = null; built = false; Build(seed); bool det = h1 == Hash;
        sb.AppendLine(string.Format("  roads {0} | main>12.5% steps {1} | branch>16.5% steps {2} | clip>0.35m {3}/{4} (max {5:F2}m) | bridges {6} fords {7} | water-through-deck {8} | deterministic {9}",
            Roads.Count, badGradeMain, badGradeBranch, badClip, samples, maxClip, bridges, fords, badWater, det));
        sb.AppendLine(pass && det ? "  RESULT: PASS" : "  RESULT: FAIL");
        return sb.ToString();
    }
}

#if UNITY_EDITOR
static class RoadBuildMenu
{
    [UnityEditor.MenuItem("Island/Phase 3.7 Validate Grading")]
    static void RunGrade() { UnityEngine.Debug.Log(RoadBuild.Validate()); }
}
#endif

