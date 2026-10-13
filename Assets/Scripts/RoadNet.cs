using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Phase 3.6 — Road cost map + A* pathfinding over WorldField.
/// Data only, nothing rendered. Everything is in SOURCE coords (x, z) like WorldField/FlowNet,
/// so a later terrain swap behind WorldField.Height() won't break the network.
///
/// The terrain is a spiral of chalk ledges joined by low-grade ramps. The cost map makes those ramps
/// cheap (low grade) and cliffs / river crossings expensive, so A* naturally climbs beach -> summit
/// along the ramps and only branches where it must. Deterministic from a seed (the heap tie-breaks on
/// cell index, and branch targets are fixed), so the same graph is produced every run.
/// </summary>
public static class RoadNet
{
    public const int G = WorldField.G;          // reuse the coarse query grid
    public static readonly double Cell = WorldField.Cell;
    public const double MainMaxGrade = 0.12;    // 3.7 profile clamp for the spine
    public const double BranchMaxGrade = 0.16;  // branches may be a touch stubbier

    public class RoadPath
    {
        public string name;
        public bool main;
        public double[] x, z;        // dense centreline, source coords (cell centres)
        public double length;        // metres
        public double rise;          // signed climb (end - start)
        public double meanGrade;     // mean ABSOLUTE segment grade (rise/length)
        public double maxGrade;      // worst segment grade
        public int startCell, endCell;
    }

    public static List<RoadPath> Paths = new List<RoadPath>();
    public static int StartCell = -1, SummitCell = -1;
    public static double SummitX, SummitZ, SummitH, BeachH;
    public static int Hash;
    static bool built; static int builtSeed;

    public static double X(int k) => -E + (k % G) * Cell;
    public static double Z(int k) => -E + (k / G) * Cell;
    public static int CellAt(double x, double z)
    {
        int i = Mathf.Clamp((int)Math.Round((x + E) / Cell), 0, G - 1);
        int j = Mathf.Clamp((int)Math.Round((z + E) / Cell), 0, G - 1);
        return j * G + i;
    }

    // ---- min-heap keyed on f, tie-broken on cell index so A* is deterministic ----
    class Heap
    {
        double[] k; int[] v; public int n;
        public Heap(int cap) { k = new double[cap]; v = new int[cap]; }
        bool Less(int a, int b) => k[a] < k[b] || (k[a] == k[b] && v[a] < v[b]);
        void Swap(int a, int b) { double t = k[a]; k[a] = k[b]; k[b] = t; int u = v[a]; v[a] = v[b]; v[b] = u; }
        public void Push(double key, int val)
        {
            int i = n++; if (i >= k.Length) { Array.Resize(ref k, k.Length * 2); Array.Resize(ref v, v.Length * 2); }
            k[i] = key; v[i] = val;
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
    // ---- terrain rasters for the road grid: carved height, slope, water mask ----
    struct Grid { public float[] h, slope; public bool[] water; }
    static Grid BuildGrid()
    {
        WorldField.Build();
        int n = G * G;
        var gr = new Grid { h = new float[n], slope = new float[n], water = new bool[n] };
        bool ro = RoadsOn; RoadsOn = false;          // route on carved terrain, never on our own grading
        for (int k = 0; k < n; k++) gr.h[k] = (float)H(X(k), Z(k));
        RoadsOn = ro;
        for (int j = 1; j < G - 1; j++)
            for (int i = 1; i < G - 1; i++)
            {
                int k = j * G + i;
                float gx = (gr.h[k + 1] - gr.h[k - 1]) * 0.5f, gz = (gr.h[k + G] - gr.h[k - G]) * 0.5f;
                gr.slope[k] = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz) / (float)Cell) * Mathf.Rad2Deg;
            }
        for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                int k = j * G + i;
                bool border = i == 0 || j == 0 || i == G - 1 || j == G - 1;
                bool wat = gr.h[k] <= WorldField.SeaLevel || border;
                if (!wat && RiverCarve.WetAt(X(k), Z(k)) > 0.45f) wat = true;      // river channel
                if (!wat && FlowNet.LakeCell >= 0)
                {
                    double dx = X(k) - FlowNet.LakeX, dz = Z(k) - FlowNet.LakeZ;
                    if (dx * dx + dz * dz < FlowNet.LakeR * FlowNet.LakeR) wat = true;   // summit lake
                }
                gr.water[k] = wat;
            }
        return gr;
    }

    static double Heur(int a, int b)
    {
        double dx = (a % G) - (b % G), dz = (a / G) - (b / G);
        return Math.Sqrt(dx * dx + dz * dz) * Cell;   // metres; min cost/m is 1.0 -> admissible
    }

    // Cost of stepping a -> b. Steep uphill is punished hard (so the path hugs the ramps and keeps a
    // ~10% mean grade), cliffs and water are costly but never impassable (a road always exists).
static double StepCost(Grid gr, int a, int b, int dir, double cap)
    {
        double dist = (dir < 4 ? 1.0 : 1.41421356) * Cell;
        double g = (gr.h[b] - gr.h[a]) / dist;           // signed grade of this step
        if (Math.Abs(g) > cap) return 1e17;               // hard cap in BOTH directions (down-steps count too)
        double c = dist;                                  // base: one metre of road costs ~1
        // Climbing is free up to ~10%, then ramps hard. Since flat costs 1/m too, the cheapest way to gain
        // height is to climb at ~10% (minimises metres travelled) instead of wandering at 2%.
        if (g > 0) c += dist * 30.0 * Sm(0.10, 0.15, g);
        else c += dist * 12.0 * Sm(0.10, 0.15, -g);
        c += dist * 6.0 * Sm(24.0, 38.0, gr.slope[b]);    // approaching a cliff face
        if (gr.water[b]) c += 250.0 * dist;                // crossing a river / lake / shore
        return c;
    }

static List<int> AStar(Grid gr, int start, int goal, double cap)
    {
        return AStarMulti(gr, new List<int> { start }, goal, cap);
    }

    // Multi-source A*: every cell in 'starts' has g=0, so a branch attaches to the network wherever it is
    // actually cheapest to reach under the grade cap (not merely nearest in a straight line).
    static List<int> AStarMulti(Grid gr, ICollection<int> starts, int goal, double cap)
    {
        int n = G * G;
        var g = new double[n]; var from = new int[n]; var closed = new bool[n]; var isStart = new bool[n];
        for (int k = 0; k < n; k++) { g[k] = double.PositiveInfinity; from[k] = -1; }
        var hp = new Heap(1 << 14);
        var sorted = new List<int>(starts); sorted.Sort();      // deterministic seeding order
        foreach (int s in sorted) { isStart[s] = true; g[s] = 0; hp.Push(Heur(s, goal), s); }
        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, oz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (hp.n > 0)
        {
            int c = hp.Pop();
            if (closed[c]) continue;
            closed[c] = true;
            if (c == goal) break;
            int ci = c % G, cj = c / G;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + ox[d], nj = cj + oz[d];
                if (ni < 0 || nj < 0 || ni >= G || nj >= G) continue;
                int b = nj * G + ni;
                if (closed[b]) continue;
                double step = StepCost(gr, c, b, d, cap);
                if (step >= 1e16) continue;
                double ng = g[c] + step;
                if (ng < g[b]) { g[b] = ng; from[b] = c; hp.Push(ng + Heur(b, goal), b); }
            }
        }
        if (!isStart[goal] && from[goal] < 0) return null;
        var path = new List<int>();
        for (int cur = goal; cur >= 0; cur = from[cur]) { path.Add(cur); if (isStart[cur]) break; }
        path.Reverse();
        return path.Count > 1 && isStart[path[0]] ? path : null;
    }


    static void AddPath(string name, bool main, List<int> cells, Grid gr)
    {
        if (cells == null || cells.Count < 2) return;
        var p = new RoadPath { name = name, main = main, startCell = cells[0], endCell = cells[cells.Count - 1] };
        p.x = new double[cells.Count]; p.z = new double[cells.Count];
        double len = 0, absRise = 0, maxG = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            p.x[i] = X(cells[i]); p.z[i] = Z(cells[i]);
            if (i > 0)
            {
                double dx = p.x[i] - p.x[i - 1], dz = p.z[i] - p.z[i - 1];
                double d = Math.Sqrt(dx * dx + dz * dz);
                double gg = Math.Abs(gr.h[cells[i]] - gr.h[cells[i - 1]]) / Math.Max(1e-6, d);
                len += d; absRise += Math.Abs(gr.h[cells[i]] - gr.h[cells[i - 1]]); maxG = Math.Max(maxG, gg);
            }
        }
        p.length = len; p.rise = gr.h[cells[cells.Count - 1]] - gr.h[cells[0]];
        p.meanGrade = absRise / Math.Max(1e-6, len); p.maxGrade = maxG;
        Paths.Add(p);
    }

    // nearest cell already used by the network (for attaching branches)
    static int NearestNetworkCell(HashSet<int> used, double x, double z)
    {
        int best = -1; double bd = double.PositiveInfinity;
        foreach (int c in used)
        {
            double dx = X(c) - x, dz = Z(c) - z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }


    public static void Build(int seed = 1337)
    {
        if (built && builtSeed == seed) return;
        WorldField.Build();
        FlowNet.Build(seed);
        var gr = BuildGrid();
        Paths = new List<RoadPath>();
        var used = new HashSet<int>();

        // --- beach start: shoreline cell nearest the reference cove, low and dry ---
        double refX = 0, refZ = -300;
        StartCell = -1; double bs = double.PositiveInfinity;
        for (int k = 0; k < G * G; k++)
        {
            if (gr.water[k]) continue;
            if (gr.h[k] < 0.3f || gr.h[k] > 7f) continue;
            if (WorldField.SlopeGrid[k] > WorldField.FlatSlopeDeg + 6) continue;
            double dx = X(k) - refX, dz = Z(k) - refZ, d = dx * dx + dz * dz;
            if (d < bs) { bs = d; StartCell = k; }
        }
        if (StartCell < 0)
            for (int k = 0; k < G * G && StartCell < 0; k++) if (!gr.water[k] && gr.h[k] > 0.3f) StartCell = k;

        // --- summit: highest walkable (non-cliff) cell ---
        SummitCell = -1; float sh = -1e9f;
        for (int k = 0; k < G * G; k++)
        {
            if (gr.water[k] || gr.h[k] < 0.25f * WorldField.MaxHeight) continue;
            if (gr.slope[k] > WorldField.CliffSlopeDeg) continue;
            if (gr.h[k] > sh) { sh = gr.h[k]; SummitCell = k; }
        }
        if (SummitCell < 0) SummitCell = StartCell;
        SummitX = X(SummitCell); SummitZ = Z(SummitCell); SummitH = gr.h[SummitCell]; BeachH = gr.h[StartCell];

        // --- spine: beach -> summit. Tight cap first so the raw path is already near-walkable
        //     (ramps rise ~6-14%); relax only if the tight one has no route. ---
        List<int> spine = AStar(gr, StartCell, SummitCell, 0.15);
        if (spine == null) spine = AStar(gr, StartCell, SummitCell, 0.20);
        if (spine == null) spine = AStar(gr, StartCell, SummitCell, 0.25);
        AddPath("Spine", true, spine, gr);
        if (spine != null) foreach (int c in spine) used.Add(c);

        // --- branch 1: to the summit lake shore (from the nearest spine cell) ---
        if (FlowNet.LakeCell >= 0)
        {
            double lx = FlowNet.LakeX, lz = FlowNet.LakeZ;
            int anchor = NearestNetworkCell(used, lx, lz);
            double ax = X(anchor) - lx, az = Z(anchor) - lz, al = Math.Sqrt(ax * ax + az * az);
            double tx = lx + (al > 1e-6 ? ax / al : 1) * (FlowNet.LakeR + 2.5), tz = lz + (al > 1e-6 ? az / al : 0) * (FlowNet.LakeR + 2.5);
            int goal = CellAt(tx, tz);
            var br = AStarMulti(gr, used, goal, 0.16) ?? AStarMulti(gr, used, goal, 0.22) ?? AStarMulti(gr, used, goal, 0.25);
            AddPath("LakeRoad", false, br, gr);
            if (br != null) foreach (int c in br) used.Add(c);
        }

        // --- branch 2: to a harbour cove — the beach cell farthest from the start ---
        {
            int harbour = -1; double hd = -1;
            for (int k = 0; k < G * G; k++)
            {
                if (gr.water[k] || gr.h[k] < 0.3f || gr.h[k] > 7f) continue;
                if (WorldField.SlopeGrid[k] > WorldField.FlatSlopeDeg + 6) continue;
                double dx = X(k) - X(StartCell), dz = Z(k) - Z(StartCell), d = dx * dx + dz * dz;
                if (d > hd) { hd = d; harbour = k; }
            }
            if (harbour >= 0 && harbour != StartCell)
            {
                int anchor = NearestNetworkCell(used, X(harbour), Z(harbour));
                var br = AStarMulti(gr, used, harbour, 0.16) ?? AStarMulti(gr, used, harbour, 0.22) ?? AStarMulti(gr, used, harbour, 0.25);
                AddPath("HarbourRoad", false, br, gr);
                if (br != null) foreach (int c in br) used.Add(c);
            }
        }

        unchecked { int hs = (int)2166136261u; foreach (var p in Paths) { hs = (hs ^ p.x.Length) * 16777619; hs = (hs ^ p.startCell) * 16777619; hs = (hs ^ p.endCell) * 16777619; } Hash = hs; }
        built = true; builtSeed = seed;
    }


    public static string Validate(int seed = 1337)
    {
        Build(seed);
        var sb = new StringBuilder("[RoadNet] seed " + seed + "\n");
        bool connected = Paths.Count >= 3;
        foreach (var p in Paths)
            sb.AppendLine(string.Format("  {0,-11}{1} cells {2} len {3:F0}m rise {4:F0}m mean {5:F1}% max {6:F1}%",
                p.name, p.main ? "[main]" : "      ", p.x.Length, p.length, p.rise, 100 * p.meanGrade, 100 * p.maxGrade));
        var netCells = new HashSet<int>();
        foreach (var p in Paths) if (p.main) { for (int i = 0; i < p.x.Length; i++) netCells.Add(CellAt(p.x[i], p.z[i])); }
        foreach (var p in Paths)
        {
            if (p.main) continue;
            bool a = netCells.Contains(p.startCell), b = netCells.Contains(p.endCell);
            if (!a && !b) connected = false;
        }
        var spine = Paths.Find(p => p.main);
        bool reaches = spine != null && spine.endCell == SummitCell && SummitH - BeachH > 20;
        double mean = spine != null ? 100 * spine.meanGrade : 0, max = spine != null ? 100 * spine.maxGrade : 0;
        int h1 = Hash; built = false; Build(seed); bool det = h1 == Hash;
        bool pass = connected && reaches && mean > 5 && mean < 17 && max < 26 && det && Paths.Count >= 3;
        sb.AppendLine(string.Format("  spine beach h={0:F0}m -> summit h={1:F1}m ({2:F0},{3:F0}) | mean {4:F1}% max {5:F1}% | paths {6} | connected {7} | deterministic {8}",
            BeachH, SummitH, SummitX, SummitZ, mean, max, Paths.Count, connected, det));
        sb.AppendLine(pass ? "  RESULT: PASS" : "  RESULT: FAIL");
        return sb.ToString();
    }
}

#if UNITY_EDITOR
static class RoadNetMenu
{
    [UnityEditor.MenuItem("Island/Phase 3.6 Validate Roads")]
    static void Run() { UnityEngine.Debug.Log(RoadNet.Validate()); }
}
#endif

