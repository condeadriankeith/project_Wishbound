using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 4.5 -- door routes (lanes). DATA ONLY: no terrain is modified here, so the Phase 3 road rules and the
/// 4.4 pad rules are unaffected. A 1 m raster over the island is routed with Dijkstra:
///   1. every settlement plaza is connected to the road corridor (or already touches it);
///   2. every house door (centre + forward * (depth/2 + DoorOffset)) is routed to the nearest part of the
///      network = road corridor + connected plaza discs + lanes laid earlier (nearest-first, so lanes merge
///      into a small lane graph instead of every door running its own track to the road).
/// Blocked cells: water (sea, river channel, summit lake), cliffs, other houses' footprints (+0.3 m), chapel /
/// monument / tower circles. Grades over StepGrade (12%) are allowed but flagged as steps; the door route
/// carries a `steps` flag. A mild smooth noise term in the cost makes lanes wind instead of running straight.
/// Route length = own lane + distance from the join cell to the road graph (dRoad), so merged lanes are honest.
/// </summary>
public static class LaneNet
{
    public const double S = 1.0;               // raster cell (m)
    public const double StepGrade = 0.12;      // above this a segment is a flight of steps
    public const double MaxGrade = 0.60;       // hard cap on any lane step = a steep stair (rise 0.18 / run 0.27); only used where forced, e.g. at a pad's retaining edge
    public const double DoorOffset = 0.6;      // door point sits this far in front of the footprint
    public const double MaxRoute = 60.0;       // rule: door -> road graph
    public const double SearchCost = 260.0;    // Dijkstra gives up beyond this cost

    public struct Lane
    {
        public int id; public string kind;      // "plaza" | "door"
        public int settlement, lot;             // lot = index into ZoneMap.Lots, -1 for plaza links
        public double[] x, z;
        public double length, maxGrade;
        public int stepSegs; public bool hasSteps;
        public string joins;                    // "road" | "plaza" | "lane"
        public double route;                    // total metres from the start to the road graph
    }

    public struct Door
    {
        public int lot, settlement; public double x, z;
        public bool ok, steps; public double laneLen, route; public int lane;
        public int side;                        // 0 front (yaw direction), 1 right, 2 back, 3 left: which face of the footprint the door is on
    }

    public static readonly List<Lane> Lanes = new List<Lane>();
    public static Door[] Doors;                 // parallel to ZoneMap.Lots (dropped lots stay ok=false, lot=-1)
    public static double[] PlazaRoute;          // per settlement: metres plaza -> road graph, -1 not connected
    public static bool Built; public static int Hash; public static string Report = "";

    // ---- raster state ----
    static int N;
    static float[] hh; static sbyte[] terr; static byte[] roadSt; static int[] bld;
    static float[] dRoad; static bool[] net, netSteps; static byte[] kindAt;      // kindAt: 1 plaza disc, 2 lane
    static double[] gC; static int[] from, stamp, closed; static int run;

    static double CX(int i) { return -E + i * S; }
    static int CI(double v) { return Mathf.Clamp((int)Math.Round((v + E) / S), 0, N - 1); }
    static int CellAt(double x, double z) { return CI(z) * N + CI(x); }

    static double Hc(int k)
    {
        if (float.IsNaN(hh[k])) hh[k] = (float)H(CX(k % N), CX(k / N));
        return hh[k];
    }

    static bool IsRoad(int k)
    {
        if (roadSt[k] == 0) roadSt[k] = (byte)(RoadGrade.MaskAt(CX(k % N), CX(k / N)) > 0.5 ? 2 : 1);
        return roadSt[k] == 2;
    }

    static bool Free(int k)
    {
        if (terr[k] == 0)
        {
            double x = CX(k % N), z = CX(k / N); bool ok;
            if (IsRoad(k)) ok = true;           // road corridor is always walkable (bridges / fords are Phase 3's business)
            else
            {
                ok = WorldField.IsLand(x, z) && !WorldField.IsCliff(x, z) && RiverCarve.WetAt(x, z) <= 0.35f;
                if (ok && FlowNet.LakeCell >= 0)
                {
                    double dx = x - FlowNet.LakeX, dz = z - FlowNet.LakeZ;
                    if (dx * dx + dz * dz < FlowNet.LakeR * FlowNet.LakeR) ok = false;
                }
            }
            terr[k] = (sbyte)(ok ? 1 : -1);
        }
        return terr[k] > 0;
    }

    static bool Pass(int k, int own)
    {
        if (!Free(k)) return false;
        int b = bld[k];
        return b == 0 || b == own + 1;
    }

    // ---- min-heap keyed on cost, tie-broken on cell index so the result is deterministic ----
    class Heap
    {
        double[] k; int[] v; public int n;
        public Heap(int cap) { k = new double[cap]; v = new int[cap]; }
        bool Less(int a, int b) { return k[a] < k[b] || (k[a] == k[b] && v[a] < v[b]); }
        void Swap(int a, int b) { double t = k[a]; k[a] = k[b]; k[b] = t; int u = v[a]; v[a] = v[b]; v[b] = u; }
        public void Push(double key, int val)
        {
            if (n >= k.Length) { Array.Resize(ref k, k.Length * 2); Array.Resize(ref v, v.Length * 2); }
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

    // ---- build ----
    public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (PadGrade.Fill == null) PadGrade.Build();

        N = (int)Math.Ceiling(2 * E / S) + 1;
        int n2 = N * N;
        hh = new float[n2]; for (int k = 0; k < n2; k++) hh[k] = float.NaN;
        terr = new sbyte[n2]; roadSt = new byte[n2]; bld = new int[n2];
        dRoad = new float[n2]; net = new bool[n2]; netSteps = new bool[n2]; kindAt = new byte[n2];
        gC = new double[n2]; from = new int[n2]; stamp = new int[n2]; closed = new int[n2]; run = 0;

        Lanes.Clear();
        int nl = ZoneMap.Lots.Count, ns = SettlementMap.Settlements.Count;
        Doors = new Door[nl]; for (int i = 0; i < nl; i++) Doors[i].lot = -1;
        PlazaRoute = new double[ns]; for (int i = 0; i < ns; i++) PlazaRoute[i] = -1;

        RasteriseBuildings();
        ConnectPlazas();
        RouteDoors();

        unchecked
        {
            int hs = (int)2166136261u; hs = (hs ^ Lanes.Count) * 16777619;
            foreach (var l in Lanes) { hs = (hs ^ l.x.Length) * 16777619; hs = (hs ^ (int)(l.length * 10)) * 16777619; hs = (hs ^ (int)(l.route * 10)) * 16777619; }
            Hash = hs;
        }
        Built = true;
    }

    static void RasteriseBuildings()
    {
        for (int i = 0; i < SettlementMap.Lots.Length; i++)
        {
            var m = SettlementMap.Lots[i]; if (m.dropped) continue;
            double hw = m.footprintW * 0.5 + 0.3, hd = m.footprintD * 0.5 + 0.3, s = Math.Sin(m.yaw), c = Math.Cos(m.yaw);
            double reach = Math.Sqrt(hw * hw + hd * hd);
            for (int j = CI(m.z - reach); j <= CI(m.z + reach); j++)
                for (int a = CI(m.x - reach); a <= CI(m.x + reach); a++)
                {
                    double dx = CX(a) - m.x, dz = CX(j) - m.z;
                    double lf = dx * s + dz * c, lr = dx * c - dz * s;
                    if (Math.Abs(lr) <= hw && Math.Abs(lf) <= hd && bld[j * N + a] == 0) bld[j * N + a] = i + 1;
                }
        }
        foreach (var pc in SettlementMap.PublicCircles())
        {
            if (pc.kind == "plaza") continue;
            for (int j = CI(pc.z - pc.r); j <= CI(pc.z + pc.r); j++)
                for (int a = CI(pc.x - pc.r); a <= CI(pc.x + pc.r); a++)
                {
                    double dx = CX(a) - pc.x, dz = CX(j) - pc.z;
                    if (dx * dx + dz * dz <= pc.r * pc.r && bld[j * N + a] == 0) bld[j * N + a] = -1;   // -1 blocks everyone ( own + 1 is never 0 for own >= 0 )
                }
        }
    }

    // distance from (x,z) to the nearest road centreline sample (cheap ordering heuristic only)
    static double RoadDistEstimate(double x, double z)
    {
        double best = 1e18;
        foreach (var p in RoadNet.Paths)
            for (int q = 0; q < p.x.Length; q++)
            {
                double dx = p.x[q] - x, dz = p.z[q] - z, d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
        return Math.Sqrt(best);
    }

    static void ConnectPlazas()
    {
        var order = new List<KeyValuePair<double, int>>();
        for (int si = 0; si < SettlementMap.Settlements.Count; si++)
        {
            var s = SettlementMap.Settlements[si]; if (!s.hasPlaza) continue;
            order.Add(new KeyValuePair<double, int>(RoadDistEstimate(s.plazaX, s.plazaZ) + si * 1e-6, si));
        }
        order.Sort((a, b) => a.Key.CompareTo(b.Key));

        foreach (var kv in order)
        {
            int si = kv.Value; var s = SettlementMap.Settlements[si];
            int c0 = CellAt(s.plazaX, s.plazaZ);
            double route = -1; bool steps = false;

            // does the plaza disc already touch the road corridor?
            double touch = 1e18;
            for (int j = CI(s.plazaZ - s.plazaR); j <= CI(s.plazaZ + s.plazaR); j++)
                for (int a = CI(s.plazaX - s.plazaR); a <= CI(s.plazaX + s.plazaR); a++)
                {
                    double dx = CX(a) - s.plazaX, dz = CX(j) - s.plazaZ, d = Math.Sqrt(dx * dx + dz * dz);
                    if (d <= s.plazaR && d < touch && IsRoad(j * N + a)) touch = d;
                }
            if (touch < 1e17) route = touch;
            else if (Route(c0, -1, out var path, out int join))
            {
                int lid = AddLane("plaza", si, -1, s.plazaX, s.plazaZ, path, join, out double len, out bool st, out double total);
                route = total; steps = st;
            }
            if (route < 0) continue;
            PlazaRoute[si] = route;
            for (int j = CI(s.plazaZ - s.plazaR); j <= CI(s.plazaZ + s.plazaR); j++)
                for (int a = CI(s.plazaX - s.plazaR); a <= CI(s.plazaX + s.plazaR); a++)
                {
                    double dx = CX(a) - s.plazaX, dz = CX(j) - s.plazaZ; int k = j * N + a;
                    if (dx * dx + dz * dz > s.plazaR * s.plazaR || !Free(k) || bld[k] != 0) continue;
                    if (!net[k] || dRoad[k] > route) { net[k] = true; dRoad[k] = (float)route; netSteps[k] = steps; kindAt[k] = 1; }
                }
        }
    }

    static int StartForDoor(double x, double z, int own)
    {
        int c0 = CellAt(x, z), ci = c0 % N, cj = c0 / N, best = -1; double bd = 1e18;
        for (int pass = 0; pass < 2 && best < 0; pass++)
            for (int dj = -5; dj <= 5; dj++)
                for (int di = -5; di <= 5; di++)
                {
                    int a = ci + di, j = cj + dj; if (a < 0 || j < 0 || a >= N || j >= N) continue;
                    int k = j * N + a;
                    bool ok = pass == 0 ? (Free(k) && bld[k] == 0) : Pass(k, own);
                    if (!ok) continue;
                    double ex = CX(a) - x, ez = CX(j) - z, d = ex * ex + ez * ez;
                    if (d < bd) { bd = d; best = k; }
                }
        return best;
    }

static void RouteDoors()
    {
        var plazaPts = new List<double[]>();
        foreach (var s in SettlementMap.Settlements) if (s.hasPlaza) plazaPts.Add(new[] { s.plazaX, s.plazaZ });
        var order = new List<KeyValuePair<double, int>>();
        for (int i = 0; i < SettlementMap.Lots.Length; i++)
        {
            var m = SettlementMap.Lots[i]; if (m.dropped) continue;
            DoorPoint(m, 0, out double dx, out double dz);
            Doors[i] = new Door { lot = i, settlement = m.settlement, x = dx, z = dz, lane = -1 };
            double d = RoadDistEstimate(dx, dz);
            foreach (var p in plazaPts) { double ex = p[0] - dx, ez = p[1] - dz; d = Math.Min(d, Math.Sqrt(ex * ex + ez * ez)); }
            order.Add(new KeyValuePair<double, int>(d + i * 1e-6, i));
        }
        order.Sort((a, b) => a.Key.CompareTo(b.Key));

        // door side: the front (yaw direction) is preferred; if it cannot reach the network within MaxRoute (typically a
        // neighbour's wall a few tenths of a metre away in a dense block), try right / left / back and keep the shortest route.
        int[] sideOrder = { 0, 1, 3, 2 };
        foreach (var kv in order)
        {
            int i = kv.Value; var m = SettlementMap.Lots[i];
            int bestSide = -1, bestJoin = -1; List<int> bestPath = null; double bestTotal = 1e18, bx = 0, bz = 0;
            foreach (int side in sideOrder)
            {
                DoorPoint(m, side, out double dx, out double dz);
                int start = StartForDoor(dx, dz, i);
                if (start < 0 || !Route(start, i, out var path, out int join)) continue;
                double total = PathLen(dx, dz, path) + dRoad[join];
                if (total < bestTotal) { bestTotal = total; bestSide = side; bestPath = path; bestJoin = join; bx = dx; bz = dz; }
                if (side == 0 && total < MaxRoute) break;
            }
            if (bestSide < 0) continue;
            var d = Doors[i];
            int lid = AddLane("door", d.settlement, i, bx, bz, bestPath, bestJoin, out double len, out bool steps, out double tot);
            d.ok = true; d.steps = steps; d.laneLen = len; d.route = tot; d.lane = lid; d.side = bestSide; d.x = bx; d.z = bz;
            Doors[i] = d;
        }
    }

    // Dijkstra from `start` to the first road / network cell popped. own = lot whose footprint cells are passable.
    static bool Route(int start, int own, out List<int> path, out int join)
    {
        path = null; join = -1; run++;
        var hp = new Heap(1 << 12);
        gC[start] = 0; from[start] = -1; stamp[start] = run; hp.Push(0, start);
        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, oz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (hp.n > 0)
        {
            int c = hp.Pop();
            if (closed[c] == run) continue;
            closed[c] = run;
            if (gC[c] > SearchCost) break;
            if (IsRoad(c) || net[c])
            {
                join = c; path = new List<int>();
                for (int cur = c; cur >= 0; cur = from[cur]) path.Add(cur);
                path.Reverse();
                return true;
            }
            int ci = c % N, cj = c / N; double hc = Hc(c);
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + ox[d], nj = cj + oz[d];
                if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                int b = nj * N + ni;
                if (closed[b] == run || !Pass(b, own)) continue;
                if (d >= 4 && (!Pass(cj * N + ni, own) || !Pass(nj * N + ci, own))) continue;   // no corner cutting through buildings
                double dist = d < 4 ? S : S * 1.41421356;
                double gr = Math.Abs(Hc(b) - hc) / dist;
                if (gr > MaxGrade) continue;
                double cost = dist * (1.0 + 1.2 * Sm(0.06, 0.12, gr) + 6.0 * Sm(0.12, 0.30, gr) + 25.0 * Sm(0.30, 0.60, gr));
                if (bld[b] == own + 1) cost *= 4.0;                        // leave the own footprint quickly
                cost *= 0.8 + 0.4 * Mathf.PerlinNoise((float)(CX(ni) * 0.11 + 31.7), (float)(CX(nj) * 0.11 + 7.3));
                double ng = gC[c] + cost;
                if (stamp[b] != run || ng < gC[b]) { stamp[b] = run; gC[b] = ng; from[b] = c; hp.Push(ng, b); }
            }
        }
        return false;
    }

    // stores the lane, marks its cells as network (with distance-to-road + steps inherited from the join)
    static int AddLane(string kind, int settlement, int lot, double sx, double sz, List<int> path, int join, out double len, out bool steps, out double total)
    {
        int n = path.Count;
        var xs = new double[n + 1]; var zs = new double[n + 1];
        xs[0] = sx; zs[0] = sz;
        for (int i = 0; i < n; i++) { xs[i + 1] = CX(path[i] % N); zs[i + 1] = CX(path[i] / N); }
        len = 0; double maxG = 0; int stepSegs = 0;
        for (int i = 1; i <= n; i++)
        {
            double dx = xs[i] - xs[i - 1], dz = zs[i] - zs[i - 1]; len += Math.Sqrt(dx * dx + dz * dz);
        }
        for (int i = 1; i < n; i++)
        {
            double dx = xs[i + 1] - xs[i], dz = zs[i + 1] - zs[i], dd = Math.Sqrt(dx * dx + dz * dz);
            double gr = Math.Abs(Hc(path[i]) - Hc(path[i - 1])) / Math.Max(1e-6, dd);
            if (gr > maxG) maxG = gr; if (gr > StepGrade) stepSegs++;
        }
        bool inherited = netSteps[join];
        steps = stepSegs > 0 || inherited;
        total = len + dRoad[join];

        // mark cells from the join backwards
        double cum = 0; bool acc = inherited;
        for (int i = n - 2; i >= 0; i--)
        {
            int a = path[i], b = path[i + 1];
            double dist = (a % N != b % N && a / N != b / N) ? S * 1.41421356 : S;
            cum += dist;
            if (Math.Abs(Hc(a) - Hc(b)) / dist > StepGrade) acc = true;
            if (!net[a] || dRoad[a] > dRoad[join] + cum) { net[a] = true; dRoad[a] = (float)(dRoad[join] + cum); netSteps[a] = acc; kindAt[a] = 2; }
        }

        var l = new Lane
        {
            id = Lanes.Count, kind = kind, settlement = settlement, lot = lot, x = xs, z = zs, length = len, maxGrade = maxG,
            stepSegs = stepSegs, hasSteps = steps, route = total,
            joins = IsRoad(join) ? "road" : (kindAt[join] == 1 ? "plaza" : "lane")
        };
        Lanes.Add(l);
        return l.id;
    }

    // ---- validation ----
    public static string Validate()
    {
        if (!Built) Build();
        var sb = new StringBuilder("[LaneNet 4.5]\n"); var fails = new List<string>(); int pass = 0;

        int active = 0, routed = 0, tooLong = 0, withSteps = 0; double maxRoute = 0, sumRoute = 0;
        for (int i = 0; i < Doors.Length; i++)
        {
            if (SettlementMap.Lots[i].dropped) continue; active++;
            var d = Doors[i];
            if (!d.ok) continue; routed++; sumRoute += d.route; if (d.route > maxRoute) maxRoute = d.route;
            if (d.route >= MaxRoute) tooLong++; if (d.steps) withSteps++;
        }
        if (routed == active) pass++; else fails.Add((active - routed) + " doors have no lane to the road graph");
        if (tooLong == 0) pass++; else fails.Add(tooLong + " door routes are " + MaxRoute + " m or longer (worst " + maxRoute.ToString("F0") + " m)");

        int noPlaza = 0, plazaCount = 0;
        for (int si = 0; si < SettlementMap.Settlements.Count; si++)
            if (SettlementMap.Settlements[si].hasPlaza) { plazaCount++; if (PlazaRoute[si] < 0) noPlaza++; }
        if (noPlaza == 0) pass++; else fails.Add(noPlaza + " plazas are not connected to the road graph");

        int wet = 0, inBuilding = 0, overGrade = 0; double worstGrade = 0, totalLen = 0; int stepsSegsTotal = 0;
        foreach (var l in Lanes)
        {
            totalLen += l.length; stepsSegsTotal += l.stepSegs;
            if (l.maxGrade > worstGrade) worstGrade = l.maxGrade;
            if (l.maxGrade > MaxGrade + 1e-6) overGrade++;
            for (int q = 1; q < l.x.Length; q++)
            {
                int k = CellAt(l.x[q], l.z[q]);
                if (!Free(k)) wet++;
                int b = bld[k]; if (b != 0 && b != l.lot + 1) inBuilding++;
            }
        }
        if (wet == 0) pass++; else fails.Add(wet + " lane points are in water or on a cliff");
        if (inBuilding == 0) pass++; else fails.Add(inBuilding + " lane points run through a house or anchor");
        if (overGrade == 0) pass++; else fails.Add(overGrade + " lanes exceed " + (MaxGrade * 100) + "% grade");

        // Check determinism: generate lane network twice and see if we get the same hash
        int hash1 = Hash;
        int hash2 = 0;
        int hash3 = 0;

        // First generation
        Lanes.Clear();
        int nl = ZoneMap.Lots.Count, ns = SettlementMap.Settlements.Count;
        Doors = new Door[nl]; for (int i = 0; i < nl; i++) Doors[i].lot = -1;
        PlazaRoute = new double[ns]; for (int i = 0; i < ns; i++) PlazaRoute[i] = -1;

        N = (int)Math.Ceiling(2 * E / S) + 1;
        int n2 = N * N;
        hh = new float[n2]; for (int k = 0; k < n2; k++) hh[k] = float.NaN;
        terr = new sbyte[n2]; roadSt = new byte[n2]; bld = new int[n2];
        dRoad = new float[n2]; net = new bool[n2]; netSteps = new bool[n2]; kindAt = new byte[n2];
        gC = new double[n2]; from = new int[n2]; stamp = new int[n2]; closed = new int[n2]; run = 0;

        RasteriseBuildings();
        ConnectPlazas();
        RouteDoors();

        unchecked
        {
            int hs = (int)2166136261u; hs = (hs ^ Lanes.Count) * 16777619;
            foreach (var l in Lanes) { hs = (hs ^ l.x.Length) * 16777619; hs = (hs ^ (int)(l.length * 10)) * 16777619; hs = (hs ^ (int)(l.route * 10)) * 16777619; }
            hash2 = hs;
        }

        // Second generation
        Lanes.Clear();
        nl = ZoneMap.Lots.Count; ns = SettlementMap.Settlements.Count;
        Doors = new Door[nl]; for (int i = 0; i < nl; i++) Doors[i].lot = -1;
        PlazaRoute = new double[ns]; for (int i = 0; i < ns; i++) PlazaRoute[i] = -1;

        N = (int)Math.Ceiling(2 * E / S) + 1;
        n2 = N * N;
        hh = new float[n2]; for (int k = 0; k < n2; k++) hh[k] = float.NaN;
        terr = new sbyte[n2]; roadSt = new byte[n2]; bld = new int[n2];
        dRoad = new float[n2]; net = new bool[n2]; netSteps = new bool[n2]; kindAt = new byte[n2];
        gC = new double[n2]; from = new int[n2]; stamp = new int[n2]; closed = new int[n2]; run = 0;

        RasteriseBuildings();
        ConnectPlazas();
        RouteDoors();

        unchecked
        {
            int hs = (int)2166136261u; hs = (hs ^ Lanes.Count) * 16777619;
            foreach (var l in Lanes) { hs = (hs ^ l.x.Length) * 16777619; hs = (hs ^ (int)(l.length * 10)) * 16777619; hs = (hs ^ (int)(l.route * 10)) * 16777619; }
            hash3 = hs;
        }

        bool det = (hash2 == hash3);
        if (det) pass++; else fails.Add("lane network is not deterministic");

        // per-settlement summary
        sb.AppendLine("lanes " + Lanes.Count + " (total " + totalLen.ToString("F0") + " m) | doors routed " + routed + "/" + active + " | mean route " + (routed > 0 ? sumRoute / routed : 0).ToString("F1") + " m, max " + maxRoute.ToString("F1") + " m | doors with steps " + withSteps + " | step segments " + stepsSegsTotal + " | worst lane grade " + (worstGrade * 100).ToString("F1") + "%");
        sb.AppendLine("plazas connected " + (plazaCount - noPlaza) + "/" + plazaCount);
        foreach (var s in SettlementMap.Settlements)
        {
            double mx = 0, sm = 0; int c = 0, fl = 0;
            for (int i = 0; i < Doors.Length; i++)
            {
                if (SettlementMap.Lots[i].dropped || Doors[i].settlement != s.id) continue;
                if (!Doors[i].ok) { fl++; continue; }
                c++; sm += Doors[i].route; if (Doors[i].route > mx) mx = Doors[i].route;
            }
            sb.AppendLine("  " + s.name + ": doors " + c + (fl > 0 ? " (+" + fl + " unrouted)" : "") + " mean " + (c > 0 ? sm / c : 0).ToString("F0") + " m max " + mx.ToString("F0") + " m plaza->road " + (PlazaRoute[s.id] < 0 ? "none" : PlazaRoute[s.id].ToString("F0") + " m"));
        }
        sb.AppendLine("RULES (4.5): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 4.5 Lanes + Validate")]
    static void Menu() { Built = false; Build(); Debug.Log(Validate()); }
#endif


static void DoorPoint(SettlementMap.Massing m, int side, out double x, out double z)
    {
        double fx = Math.Sin(m.yaw), fz = Math.Cos(m.yaw), rx = fz, rz = -fx;     // forward / right
        double dx, dz, off;
        switch (side)
        {
            case 1: dx = rx; dz = rz; off = m.footprintW * 0.5 + DoorOffset; break;
            case 2: dx = -fx; dz = -fz; off = m.footprintD * 0.5 + DoorOffset; break;
            case 3: dx = -rx; dz = -rz; off = m.footprintW * 0.5 + DoorOffset; break;
            default: dx = fx; dz = fz; off = m.footprintD * 0.5 + DoorOffset; break;
        }
        x = m.x + dx * off; z = m.z + dz * off;
    }

    static double PathLen(double sx, double sz, List<int> path)
    {
        double len = 0, px = sx, pz = sz;
        for (int i = 0; i < path.Count; i++)
        {
            double x = CX(path[i] % N), z = CX(path[i] / N), dx = x - px, dz = z - pz; len += Math.Sqrt(dx * dx + dz * dz); px = x; pz = z;
        }
        return len;
    }
}
