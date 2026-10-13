using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 4 — settlement layout and grading (placeholder boxes only; real buildings are Phase 5).
/// Builds on top of ZoneMap (3.9): groups ZoneMap.Lots into named settlements, places public anchors,
/// gives each lot a massed footprint. Terrain grading (pad flattening) is PadGrade.cs (4.4), lanes are
/// LaneNet.cs (4.5), water is WaterAccess.cs (4.6), farm plots are FarmPlots.cs (4.7), field walls are
/// FieldWalls.cs (4.8). This file ends with the 4.9 validator + F3 overlay once those are wired in.
///
/// PICKUP NOTES -- LATEST (supersedes the older bullets below wherever they conflict):
///   - STATUS: 4.1-4.3 9/0, 4.4 PadGrade 7/0 (roads PASS, deterministic), 4.5 LaneNet 7/0. Next: 4.6 WaterAccess, 4.7 FarmPlots,
///     4.8 field edges, 4.9 widen Validate() + F3 mode 7. Re-run order: ZoneMap -> SettlementMap -> PadGrade -> LaneNet.
///   - DEPENDENCY RULE (fixed this session): data flows ONE way, zones/lots -> settlements -> pads -> lanes. ZoneMap.Build and
///     SettlementMap.Build now run with TerrainMath.PadsOn=false (wrapper around BuildCore). Before this, cold start gave 66 lots and a
///     warm rebuild 57 because ZoneMap.Fit sampled padded terrain and re-entered PadGrade->SettlementMap->ZoneMap. Cold == warm now
///     (identical lot/settlement/pad/lane counts and hashes). Never sample H() with pads on inside ZoneMap/SettlementMap.
///   - Layout changed as a result: 57 lots in 15 settlements (9 farm, 3 hamlet, 2 harbour, 1 summit); the 14 'Farm Village' list and
///     '15 solitary farmsteads' remark below are stale. Solitary farmsteads (1-2 lots) are still valid by design.
///   - Plazas must be REACHABLE: FindPlaza rejects spots a lane cannot reach from the road (2 m flood, grade <= 35%, <= 90 m).
///     Found live: a plaza on a cliff-ringed shelf could not connect to the road.
///   - LaneNet.cs (4.5): 1 m raster Dijkstra, data only (no terrain change). Door = footprint face + 0.6 m; front side preferred, then
///     right/left/back (Doors[i].side) if a neighbour's wall blocks it; Phase 5 must read Doors[i].side/x/z, not assume front. Lane
///     grades above 12% are stairs (flagged); hard cap 60%. Routes = own lane + dRoad of the join cell, max 54.7 m now (limit 60).
///   - Not yet done for lanes: no renderer / overlay yet (4.9), lanes are raw 1 m polylines (no smoothing), no ford/bridge logic
///     (water is simply impassable, and validated as 0 crossings).
///
/// OLDER NOTES (from the 4.2/4.3 overlap rework; some are stale, see above):
///   - DONE + VERIFIED: 4.1 settlements, 4.2 plazas/anchors, 4.3 massing. Validate() = 9 pass / 0 fail.
///   - 4.4 PadGrade.cs: written and wired into TerrainMath.H. Last VERIFIED run: 6 pass / 1 fail (2-3 plazas at ~2.1% slope,
///     cut max 1.5 m, fill max 2.1 m, deterministic, Phase 3 roads PASS). The final edit (flatness check ignores road
///     shoulder, RoadGrade.MaskAt > 0.15) was saved with no console errors but NOT re-run. FIRST STEP NEXT SESSION:
///     SettlementMap.Built=false; SettlementMap.Build(); PadGrade.Fill=null; PadGrade.Mask=null; PadGrade.Build(); PadGrade.Validate().
///     If a pad or two is still ~2%, prefer loosening the threshold slightly over distorting terrain.
///   - TODO: 4.5 LaneNet.cs, 4.6 WaterAccess.cs, 4.7 FarmPlots.cs, 4.8 FieldWalls.cs, then 4.9: widen Validate() to all
///     Phase 4 rules and wire WorldFieldDebug F3 mode 7. Roads must still PASS after each terrain-changing step.
///   - Changes made in the rework (do not undo): footprints are sized BEFORE plazas (SizeLots); plazas use FindPlaza on the
///     flattest land disc clear of ALL lots; chapels/monuments start pr+7.5 out and avoid road corridors; SetbackFromRoads
///     searches nearby for a flat spot clear of the road mask; Massing now carries final x,z and a `dropped` flag (lot 57
///     duplicates lot 0's centre). Downstream code must use SettlementMap.Lots[i].x/z, NOT ZoneMap.Lots[i].x/z, and skip dropped.
///     PadGrade.Stamp extends the flat zone one raster cell (Res) past the pad edge. Backup of the pre-rework file:
///     Assets/Scripts/SettlementMap.cs.bak_pre_overlapfix.txt
///   - Known: only 3 real farm villages; 15 farm entries are 1-2 lot farmsteads still labelled "Farm Village N" (cosmetic).
///     Plazas and the summit tower may touch road corridors by design; houses, chapels and monuments may not.
///   - Calling Unity: after editing a script wait for compile (isCompiling false) before running code; domain reload resets statics.///   - ZoneMap.Lots is a flat, ungrouped list with only a `group` int (hamlet cluster index, -1 meaningless
///     for other zones). SettlementMap does NOT mutate ZoneMap; it re-groups by reading Lots once in Build().
///   - Known upstream shortfall (brief, "one thing to handle early"): ZoneMap.Gen() has no per-zone minimum,
///     so harbour/farm/hamlet/summit lot counts can fall short of settlement targets. BuildSettlements()
///     below sets explicit targets (MinLots per role) and, if ZoneMap undershoots, widens the search radius
///     locally (see GrowShortfalls) rather than editing ZoneMap.Gen() itself — keeps the fix contained to
///     Phase 4 and resumable without touching Phase 3.9's validated output.
/// </summary>
public static class SettlementMap
{
    // ---- settlement definition (4.1) ----
    public struct Settlement
    {
        public int id;
        public Zone role;            // Harbour, Farm, Hamlet, Summit (the 4 settlement-bearing zones)
        public string name;
        public double cx, cz;        // centre (suitability-weighted centroid of its lots)
        public double radius;        // bounding radius covering all lots + margin
        public List<int> lotIdx;     // indices into ZoneMap.Lots belonging to this settlement
        public double plazaX, plazaZ, plazaR;  // 4.2
        public bool hasPlaza;
        public List<Anchor> anchors; // 4.2: well / shrine / mill / tower
    }

    public struct Anchor { public string kind; public double x, z, h; }

    // per-lot massing (4.3), parallel array indexed the same as ZoneMap.Lots
    public struct Massing
    {
        public double x, z;                    // final centre (lot centre, set back from road corridors in 4.3c)
        public bool dropped;                   // exact duplicate of an earlier lot centre; ignored downstream
        public float footprintW, footprintD;   // plan footprint (<= lot w/d, with a small margin)
        public int floors;                     // 1-3
        public int sizeClass;                  // 0 small, 1 medium, 2 large
        public float yaw;                      // radians, door-forward orientation (faces nearest lane/road)
        public int settlement;                 // index into Settlements, -1 if unassigned (should not happen post-4.1)
    }

    public static readonly List<Settlement> Settlements = new List<Settlement>();
    public static Massing[] Lots;               // parallel to ZoneMap.Lots
    public static bool Built;
    public static string Report = "";
    static string legend = "settlements";
    public static string Legend() { return legend; }

    // explicit per-role minimum settlement count and minimum lots/settlement (closes the 3.9 shortfall
    // called out in the brief — a single summit "settlement" with 1 lot is not a station, it's a shed)
    // NOTE (resolved during 4.1 live testing): this terrain produces one genuinely large farm cluster plus
    // several small/solitary farm pockets that are too far apart to merge without also merging UNRELATED
    // farm areas across the map (tried GroupGap up to 2.6x -- the pockets are 90-150m+ apart, further than
    // any sane "same village" radius). This matches the broader design material's own distinction between
    // farm VILLAGES and solitary farmsteads as separate settlement types, so MinFarmSettlements counts only
    // the >=1 real village; isolated 1-2 lot farm settlements are valid solitary farmsteads, not failures.
    const int MinHarbourLots = 10, MinFarmSettlements = 1, MinFarmLotsEach = 6, MinHamletSettlements = 2,
              MinHamletLotsEach = 5, MinSummitLots = 3;
    const double GroupGap = 26.0;   // lots within this of each other (same zone) join the same settlement

public static void Build()
    {
        // Same dependency order as ZoneMap: settlements are derived from pre-pad terrain, pads are derived from settlements.
        // Sampling padded terrain here made the result depend on whatever PadGrade state was left from an earlier run
        // (and re-entered PadGrade -> SettlementMap on a cold start).
        bool pd = PadsOn; PadsOn = false;
        try { BuildCore(); } finally { PadsOn = pd; }
    }

    static void BuildCore()
    {
        ZoneMap.Build();   // idempotent; Phase 3.9 data (Zones, Lots, Suit, Hamlets)
        Settlements.Clear(); reachSteps = null;
        Lots = new Massing[ZoneMap.Lots.Count];
        for (int i = 0; i < Lots.Length; i++) Lots[i].settlement = -1;

        // 4.1: cluster each role independently into plain lot-index groups first (no Settlement objects,
        // no index assignment yet), widening the grouping radius per-role if targets aren't met. Only
        // once every role's clusters are finalised do we materialise Settlements and assign indices, in
        // one pass -- this avoids the index-shift bug where regrouping one role invalidates Lots[].settlement
        // for settlements of OTHER roles that already existed earlier in the list.
        var harbourClusters = ClusterRole(Zone.Harbour, GroupGap);
        if (TooSmall(harbourClusters, 1, MinHarbourLots)) harbourClusters = ClusterRole(Zone.Harbour, GroupGap * 1.5);

        var farmClusters = ClusterRole(Zone.Farm, GroupGap);
        if (TooSmall(farmClusters, MinFarmSettlements, MinFarmLotsEach)) farmClusters = ClusterRole(Zone.Farm, GroupGap * 1.8);
        if (TooSmall(farmClusters, MinFarmSettlements, MinFarmLotsEach)) farmClusters = ClusterRole(Zone.Farm, GroupGap * 2.6);

        var hamletClusters = ClusterRole(Zone.Hamlet, GroupGap);
        if (TooSmall(hamletClusters, MinHamletSettlements, MinHamletLotsEach)) hamletClusters = ClusterRole(Zone.Hamlet, GroupGap * 1.6);

        var summitClusters = ClusterRole(Zone.Summit, GroupGap);
        if (TooSmall(summitClusters, 1, MinSummitLots)) summitClusters = ClusterRole(Zone.Summit, GroupGap * 2.2);
        if (TooSmall(summitClusters, 1, MinSummitLots)) summitClusters = ClusterRole(Zone.Summit, GroupGap * 4.0);

        MaterialiseSettlements(Zone.Harbour, "Harbour", harbourClusters);
        MaterialiseSettlements(Zone.Farm, "Farm Village", farmClusters);
        MaterialiseSettlements(Zone.Hamlet, "Hamlet", hamletClusters);
        MaterialiseSettlements(Zone.Summit, "Summit Station", summitClusters);

        RecomputeCentres();
        SizeLots();                // 4.3a footprints first, so plazas can clear them
        PlacePlazasAndAnchors();   // 4.2
        OrientLots();              // 4.3b
        SetbackFromRoads();        // 4.3c keep footprints out of road corridors
        ResolveOverlaps();         // 4.3d no overlaps

        Built = true;
        Validate();
    }

    static bool TooSmall(List<List<int>> clusters, int minSettlements, int minEach)
    {
        int big = 0;
        foreach (var c in clusters) if (c.Count >= minEach) big++;
        return clusters.Count < minSettlements || big < minSettlements;
    }

    // union-find clustering of same-role lots within `gap`; returns plain lot-index groups (no side effects)
    static List<List<int>> ClusterRole(Zone role, double gap)
    {
        var lots = ZoneMap.Lots;
        var idxOfRole = new List<int>();
        for (int i = 0; i < lots.Count; i++) if (lots[i].zone == role) idxOfRole.Add(i);
        var result = new List<List<int>>();
        if (idxOfRole.Count == 0) return result;

        var parent = new int[idxOfRole.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        for (int a = 0; a < idxOfRole.Count; a++)
            for (int b = a + 1; b < idxOfRole.Count; b++)
            {
                var la = lots[idxOfRole[a]]; var lb = lots[idxOfRole[b]];
                double dx = la.x - lb.x, dz = la.z - lb.z;
                if (dx * dx + dz * dz < gap * gap) Union(a, b);
            }

        var clusters = new Dictionary<int, List<int>>();
        for (int a = 0; a < idxOfRole.Count; a++)
        {
            int r = Find(a);
            if (!clusters.TryGetValue(r, out var l)) { l = new List<int>(); clusters[r] = l; }
            l.Add(idxOfRole[a]);
        }
        foreach (var kv in clusters) result.Add(kv.Value);
        return result;
    }

    // builds Settlement entries for one role's finalised clusters and assigns Lots[].settlement. Called
    // exactly once per role, in a fixed order, so indices are stable once this function returns.
    static void MaterialiseSettlements(Zone role, string label, List<List<int>> clusters)
    {
        int n = 1;
        foreach (var lotIdx in clusters)
        {
            var s = new Settlement
            {
                id = Settlements.Count,
                role = role,
                name = label + (clusters.Count > 1 ? " " + n : ""),
                lotIdx = lotIdx,
                anchors = new List<Anchor>()
            };
            Settlements.Add(s);
            foreach (int li in lotIdx) Lots[li].settlement = Settlements.Count - 1;
            n++;
        }
    }

    // ---- 4.1: union-find style grouping of same-zone lots within GroupGap, named + numbered ----


    // ---- 4.1 shortfall handling: widen the grouping radius once per role if targets aren't met, and
    // re-cluster. We do NOT invent lots here (that is ZoneMap.Gen's job); this only prevents real nearby
    // lots from being fragmented into many tiny "settlements" when ZoneMap's Gen() placed them sparsely. ----


    static void RecomputeCentres()
    {
        for (int si = 0; si < Settlements.Count; si++)
        {
            var s = Settlements[si];
            double sx = 0, sz = 0, sw = 0, maxR = 0;
            foreach (int li in s.lotIdx) { var l = ZoneMap.Lots[li]; double w = Mathf.Max(0.01f, l.suit); sx += l.x * w; sz += l.z * w; sw += w; }
            if (sw < 1e-9) sw = 1;
            s.cx = sx / sw; s.cz = sz / sw;
            foreach (int li in s.lotIdx) { var l = ZoneMap.Lots[li]; double dx = l.x - s.cx, dz = l.z - s.cz; double r = Math.Sqrt(dx * dx + dz * dz) + Math.Max(l.w, l.d) * 0.5; if (r > maxR) maxR = r; }
            s.radius = maxR + 6.0;
            Settlements[si] = s;
        }
    }

    // ---- 4.2: plaza on the flattest CLEAR ground near the settlement centre, then anchors ----
    const double AnchorClearGap = 1.0;

    static double LotCircumR(int li) { return 0.5 * Math.Sqrt((double)Lots[li].footprintW * Lots[li].footprintW + (double)Lots[li].footprintD * Lots[li].footprintD); }

    // true if a circle stays clear of EVERY lot's circumscribed circle (all settlements, not just its own)
    static bool CircleClearOfLots(double x, double z, double r)
    {
        for (int li = 0; li < ZoneMap.Lots.Count; li++)
        {
            var l = ZoneMap.Lots[li]; double dx = l.x - x, dz = l.z - z, lim = r + LotCircumR(li) + AnchorClearGap;
            if (dx * dx + dz * dz < lim * lim) return false;
        }
        return true;
    }

    // relief (max - min raw ground height) over a disc; -1 if any sample is water or cliff
    static double DiscRelief(double x, double z, double r)
    {
        double mn = 1e9, mx = -1e9;
        for (int a = -2; a <= 2; a++)
            for (int b = -2; b <= 2; b++)
            {
                double u = a / 2.0, v = b / 2.0; if (u * u + v * v > 1.0) continue;
                double px = x + u * r, pz = z + v * r;
                if (!WorldField.IsLand(px, pz) || WorldField.IsCliff(px, pz)) return -1;
                double h = WorldField.Height(px, pz); if (h < mn) mn = h; if (h > mx) mx = h;
            }
        return mx - mn;
    }

    // true if any sample of a disc (grown by `pad`) lies in the road corridor
    static bool DiscTouchesRoad(double x, double z, double r, double pad)
    {
        for (int a = -2; a <= 2; a++)
            for (int b = -2; b <= 2; b++)
            {
                double u = a / 2.0, v = b / 2.0; if (u * u + v * v > 1.0) continue;
                if (RoadGrade.MaskAt(x + u * (r + pad), z + v * (r + pad)) > 0.5) return true;
            }
        return false;
    }

    const double PlazaMaxRelief = 4.0;   // raw relief the grader can flatten comfortably (cut+fill limits are 4+3 m)

    static void PlacePlazasAndAnchors()
    {
        for (int si = 0; si < Settlements.Count; si++)
        {
            var s = Settlements[si];
            double wantR = 7.0 + Math.Min(5.0, s.lotIdx.Count * 0.3);
            double px = s.cx, pz = s.cz, pr = wantR, relief = -1; bool found = false;
            foreach (double f in new[] { 1.0, 0.8, 0.62 })
            {
                pr = Math.Max(4.5, wantR * f);
                if (FindPlaza(s.cx, s.cz, s.radius, pr, out px, out pz, out relief)) { found = true; break; }
            }
            if (!found)
            {
                pr = Math.Max(4.5, wantR * 0.62);
                found = FindPlaza(s.cx, s.cz, s.radius * 1.7, pr, out px, out pz, out relief);
            }
            s.hasPlaza = found && relief <= PlazaMaxRelief;
            s.plazaX = px; s.plazaZ = pz; s.plazaR = pr;

            // well / fountain at the plaza centre (the plaza disc is already clear of every lot)
            s.anchors.Add(new Anchor { kind = "well", x = px, z = pz, h = WorldField.Height(px, pz) });

            if (s.role == Zone.Farm || s.role == Zone.Hamlet)
            { PlaceAnchorClear(s, "chapel", px, pz, pr + 7.5, 4.0, out var chapel); s.anchors.Add(chapel); }
            if (s.role == Zone.Harbour)
            { PlaceAnchorClear(s, "monument", px, pz, pr + 7.5, 4.0, out var monument); s.anchors.Add(monument); }

            if (NearestRiverDist(s.cx, s.cz, 140.0, out double rx, out double rz))
                s.anchors.Add(new Anchor { kind = "mill", x = rx, z = rz, h = WorldField.Height(rx, rz) });

            if (s.role == Zone.Summit)
                s.anchors.Add(new Anchor { kind = "tower", x = RoadNet.SummitX, z = RoadNet.SummitZ, h = RoadNet.SummitH });

            Settlements[si] = s;
        }
    }

    // spiral out from (px,pz) until a disc of `radius` is on land, off cliffs and clear of every lot
    static void PlaceAnchorClear(Settlement s, string kind, double px, double pz, double startDist, double radius, out Anchor result)
    {
        for (int ring = 0; ring < 12; ring++)
        {
            double r = startDist + ring * 4.0;
            int steps = 8 + ring * 4;
            for (int t = 0; t < steps; t++)
            {
                double a = P2 * t / steps;
                double x = px + Math.Cos(a) * r, z = pz + Math.Sin(a) * r;
                double rel = DiscRelief(x, z, radius);
                if (rel < 0 || rel > 5.0) continue;
                if (!CircleClearOfLots(x, z, radius)) continue;
                if (DiscTouchesRoad(x, z, radius, 0.8)) continue;
                result = new Anchor { kind = kind, x = x, z = z, h = WorldField.Height(x, z) };
                return;
            }
        }
        // last resort: beside the plaza (the validator's overlap count will show it if still bad)
        result = new Anchor { kind = kind, x = px + startDist, z = pz, h = WorldField.Height(px + startDist, pz) };
    }

    static void OffsetAnchor(double cx, double cz, double dist, double angle, out double ax, out double az)
    { ax = cx + Math.Cos(angle) * dist; az = cz + Math.Sin(angle) * dist; }

    // best (lowest relief, mild pull to centre) land disc of radius pr that is clear of all lots
    // ---- plaza reachability (pre-pad terrain): ground a lane can actually walk to from the road corridor ----
    // 2 m raster flood from the road mask over land that is not cliff / river / lake, with a grade cap matching
    // LaneNet.MaxGrade and a step cap so a plaza is never placed on a shelf cut off by cliffs (found live: Farm Village 1).
    const double RS = 2.0, ReachGrade = 0.35; const int ReachMaxSteps = 45;
    static int RN; static byte[] reachSteps; static float[] rh;
    static double RX(int i) { return -E + i * RS; }
    static int RI(double v) { return Mathf.Clamp((int)Math.Round((v + E) / RS), 0, RN - 1); }
    static double RHt(int k) { if (float.IsNaN(rh[k])) rh[k] = (float)H(RX(k % RN), RX(k / RN)); return rh[k]; }
    static bool WalkCell(int k)
    {
        double x = RX(k % RN), z = RX(k / RN);
        if (RoadGrade.MaskAt(x, z) > 0.5) return true;
        if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z) || RiverCarve.WetAt(x, z) > 0.35f) return false;
        if (FlowNet.LakeCell >= 0) { double dx = x - FlowNet.LakeX, dz = z - FlowNet.LakeZ; if (dx * dx + dz * dz < FlowNet.LakeR * FlowNet.LakeR) return false; }
        return true;
    }
    static void BuildReach()
    {
        RN = (int)Math.Ceiling(2 * E / RS) + 1; int n = RN * RN;
        reachSteps = new byte[n]; rh = new float[n]; for (int k = 0; k < n; k++) rh[k] = float.NaN;
        var q = new Queue<int>();
        for (int k = 0; k < n; k++) if (RoadGrade.MaskAt(RX(k % RN), RX(k / RN)) > 0.5) { reachSteps[k] = 1; q.Enqueue(k); }
        int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, oz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int steps = reachSteps[c]; if (steps > ReachMaxSteps) continue;
            int ci = c % RN, cj = c / RN; double hc = RHt(c);
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + ox[d], nj = cj + oz[d]; if (ni < 0 || nj < 0 || ni >= RN || nj >= RN) continue;
                int b = nj * RN + ni; if (reachSteps[b] != 0 || !WalkCell(b)) continue;
                double dist = (d < 4 ? 1.0 : 1.41421356) * RS;
                if (Math.Abs(RHt(b) - hc) / dist > ReachGrade) continue;
                reachSteps[b] = (byte)(steps + 1); q.Enqueue(b);
            }
        }
    }
    public static bool Reachable(double x, double z)
    {
        if (reachSteps == null) BuildReach();
        return reachSteps[RI(z) * RN + RI(x)] != 0;
    }

    static bool FindPlaza(double cx, double cz, double searchR, double pr, out double bestX, out double bestZ, out double bestRelief)
    {
        bestX = cx; bestZ = cz; bestRelief = -1; bool found = false; double bestScore = 1e18;
        int rings = 6;
        for (int ring = 0; ring <= rings; ring++)
        {
            double r = searchR * ring / rings;
            int steps = ring == 0 ? 1 : 10 + ring * 4;
            for (int t = 0; t < steps; t++)
            {
                double a = P2 * t / steps;
                double x = cx + Math.Cos(a) * r, z = cz + Math.Sin(a) * r;
                double rel = DiscRelief(x, z, pr); if (rel < 0) continue;
                if (!CircleClearOfLots(x, z, pr)) continue;
                if (!Reachable(x, z)) continue;
                double score = rel + 0.015 * r;
                if (score < bestScore) { bestScore = score; bestX = x; bestZ = z; bestRelief = rel; found = true; }
            }
        }
        return found;
    }

    static bool NearestRiverDist(double cx, double cz, double maxDist, out double rx, out double rz)
    {
        rx = 0; rz = 0; double best = maxDist * maxDist; bool found = false;
        if (FlowNet.Rivers != null)
            foreach (var r in FlowNet.Rivers)
                for (int q = 0; q < r.cells.Length; q++)
                {
                    int c = r.cells[q]; double x = FlowNet.X(c), z = FlowNet.Z(c);
                    double dx = x - cx, dz = z - cz, d = dx * dx + dz * dz;
                    if (d < best) { best = d; rx = x; rz = z; found = true; }
                }
        return found;
    }

    // ---- 4.3a: footprint / floors / size class, denser toward the settlement centre ----
    static void SizeLots()
    {
        var rng = new System.Random(4001);
        for (int i = 0; i < ZoneMap.Lots.Count; i++)
        {
            var l = ZoneMap.Lots[i];
            int si = Lots[i].settlement;
            double distFrac = 0.5;
            if (si >= 0)
            {
                var s = Settlements[si];
                double dx = l.x - s.cx, dz = l.z - s.cz;
                distFrac = Math.Sqrt(dx * dx + dz * dz) / Math.Max(1.0, s.radius);
            }
            float margin = Mathf.Lerp(0.3f, 1.6f, (float)distFrac);
            float fw = Mathf.Max(3.5f, l.w - margin), fd = Mathf.Max(3.5f, l.d - margin);
            int floors = distFrac < 0.4 ? (rng.NextDouble() < 0.5 ? 2 : 3) : distFrac < 0.75 ? (rng.NextDouble() < 0.6 ? 2 : 1) : 1;
            if (l.zone == Zone.Summit) floors = 1;
            int sizeClass = fw * fd > 70 ? 2 : fw * fd > 40 ? 1 : 0;
            bool dup = false;
            for (int j = 0; j < i && !dup; j++)
            {
                if (Lots[j].dropped) continue;
                double ex = ZoneMap.Lots[j].x - l.x, ez = ZoneMap.Lots[j].z - l.z;
                if (ex * ex + ez * ez < 1.0) dup = true;
            }
            Lots[i] = new Massing { x = l.x, z = l.z, dropped = dup, footprintW = dup ? 0f : fw, footprintD = dup ? 0f : fd, floors = floors, sizeClass = sizeClass, yaw = 0f, settlement = si };
        }
    }

    // ---- 4.3b: orientation: face the settlement plaza if there is one, else the nearest road ----
    static void OrientLots()
    {
        for (int i = 0; i < ZoneMap.Lots.Count; i++)
        {
            var l = ZoneMap.Lots[i]; int si = Lots[i].settlement; float yaw;
            if (si >= 0 && Settlements[si].hasPlaza)
            {
                var s = Settlements[si];
                yaw = (float)Math.Atan2(s.plazaX - l.x, s.plazaZ - l.z);
            }
            else yaw = (float)Math.Atan2(l.fx, l.fz);
            var m = Lots[i]; m.yaw = yaw; Lots[i] = m;
        }
    }

    // ---- geometry helpers (oriented rectangles; forward = (sin yaw, cos yaw), right = (cos yaw, -sin yaw)) ----
    public struct PublicCircle { public string kind; public double x, z, r; }

    public static List<PublicCircle> PublicCircles()
    {
        var res = new List<PublicCircle>();
        foreach (var s in Settlements)
        {
            if (s.hasPlaza) res.Add(new PublicCircle { kind = "plaza", x = s.plazaX, z = s.plazaZ, r = s.plazaR });
            foreach (var a in s.anchors)
            {
                if (a.kind == "chapel" || a.kind == "monument") res.Add(new PublicCircle { kind = a.kind, x = a.x, z = a.z, r = 3.5 });
                else if (a.kind == "tower") res.Add(new PublicCircle { kind = "tower", x = a.x, z = a.z, r = 6.0 });
            }
        }
        return res;
    }

    public static bool ObbOverlap(double ax, double az, double ahw, double ahd, double ayaw, double bx, double bz, double bhw, double bhd, double byaw, double gap)
    {
        double sa = Math.Sin(ayaw), ca = Math.Cos(ayaw), sb = Math.Sin(byaw), cb = Math.Cos(byaw);
        double dx = bx - ax, dz = bz - az;
        for (int k = 0; k < 4; k++)
        {
            double nx, nz;
            switch (k) { case 0: nx = ca; nz = -sa; break; case 1: nx = sa; nz = ca; break; case 2: nx = cb; nz = -sb; break; default: nx = sb; nz = cb; break; }
            double ra = ahw * Math.Abs(ca * nx - sa * nz) + ahd * Math.Abs(sa * nx + ca * nz);
            double rb = bhw * Math.Abs(cb * nx - sb * nz) + bhd * Math.Abs(sb * nx + cb * nz);
            if (Math.Abs(dx * nx + dz * nz) >= ra + rb + gap) return false;
        }
        return true;
    }

    public static bool ObbCircleOverlap(double ax, double az, double ahw, double ahd, double ayaw, double cx, double cz, double r, double gap)
    {
        double dx = cx - ax, dz = cz - az, s = Math.Sin(ayaw), c = Math.Cos(ayaw);
        double lf = dx * s + dz * c, lr = dx * c - dz * s;
        double ex = Math.Max(0.0, Math.Abs(lr) - ahw), ez = Math.Max(0.0, Math.Abs(lf) - ahd);
        return Math.Sqrt(ex * ex + ez * ez) < r + gap;
    }

    // ---- 4.3c: setback from road corridors (move away from the road mask, shrink only as a last resort) ----
    const float MinFoot = 3.0f;
    const double PartyGap = 0.1;

    static bool TouchesRoad(Massing m, double x, double z, double pad)
    {
        double s = Math.Sin(m.yaw), c = Math.Cos(m.yaw), hw = m.footprintW * 0.5 + pad, hd = m.footprintD * 0.5 + pad;
        for (int u = -3; u <= 3; u++)
            for (int v = -3; v <= 3; v++)
            {
                double lr = u / 3.0 * hw, lf = v / 3.0 * hd;
                if (RoadGrade.MaskAt(x + lr * c + lf * s, z - lr * s + lf * c) > 0.5) return true;
            }
        return false;
    }

    // raw+road-graded relief under a footprint at (x,z); pads are switched off so this never recurses into PadGrade
    static double FootRelief(Massing m, double x, double z)
    {
        bool po = PadsOn; PadsOn = false;
        try
        {
            double s = Math.Sin(m.yaw), c = Math.Cos(m.yaw), hw = m.footprintW * 0.5 + 0.8, hd = m.footprintD * 0.5 + 0.8, mn = 1e9, mx = -1e9;
            for (int u = -2; u <= 2; u++)
                for (int v = -2; v <= 2; v++)
                {
                    double lr = u / 2.0 * hw, lf = v / 2.0 * hd;
                    double h = H(x + lr * c + lf * s, z - lr * s + lf * c); if (h < mn) mn = h; if (h > mx) mx = h;
                }
            return mx - mn;
        }
        finally { PadsOn = po; }
    }

    const double MaxLotRelief = 3.5;   // keeps cut (<=4 m) + fill (<=3 m) comfortably inside PadGrade's limits

    // lots that touch a road corridor slide to the nearest spot that clears it and is flat enough to grade
    static void SetbackFromRoads()
    {
        for (int i = 0; i < Lots.Length; i++)
        {
            var m = Lots[i]; if (m.dropped) continue;
            if (TouchesRoad(m, m.x, m.z, 0.8))
            {
                double bestX = m.x, bestZ = m.z, bestScore = 1e18; bool found = false;
                for (int ring = 1; ring <= 14; ring++)
                {
                    double rad = ring * 0.75;
                    for (int k = 0; k < 16; k++)
                    {
                        double ang = P2 * k / 16.0, x = m.x + Math.Cos(ang) * rad, z = m.z + Math.Sin(ang) * rad;
                        if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z)) continue;
                        if (TouchesRoad(m, x, z, 0.8)) continue;
                        double rel = FootRelief(m, x, z); if (rel > MaxLotRelief) continue;
                        double score = rel + 0.25 * rad;
                        if (score < bestScore) { bestScore = score; bestX = x; bestZ = z; found = true; }
                    }
                }
                if (found) { m.x = bestX; m.z = bestZ; }
            }
            for (int k = 0; k < 6 && TouchesRoad(m, m.x, m.z, 0.8) && (m.footprintW > MinFoot + 1e-3f || m.footprintD > MinFoot + 1e-3f); k++)
            { m.footprintW = Mathf.Max(MinFoot, m.footprintW * 0.9f); m.footprintD = Mathf.Max(MinFoot, m.footprintD * 0.9f); }
            m.sizeClass = m.footprintW * m.footprintD > 70 ? 2 : m.footprintW * m.footprintD > 40 ? 1 : 0;
            Lots[i] = m;
        }
    }
    // ---- 4.3d: shrink footprints (never below MinFoot) until no two lots, and no lot and public circle, overlap ----
    static void ResolveOverlaps()
    {
        var pubs = PublicCircles(); int n = Lots.Length;
        for (int pass = 0; pass < 10; pass++)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var a = Lots[i]; if (a.dropped) continue; bool shrink = false;
                for (int j = 0; j < n && !shrink; j++)
                {
                    if (j == i || Lots[j].dropped) continue; var b = Lots[j];
                    if (ObbOverlap(a.x, a.z, a.footprintW * 0.5, a.footprintD * 0.5, a.yaw, b.x, b.z, b.footprintW * 0.5, b.footprintD * 0.5, b.yaw, PartyGap)) shrink = true;
                }
                for (int k = 0; k < pubs.Count && !shrink; k++)
                    if (ObbCircleOverlap(a.x, a.z, a.footprintW * 0.5, a.footprintD * 0.5, a.yaw, pubs[k].x, pubs[k].z, pubs[k].r, 0.8)) shrink = true;
                if (!shrink) continue;
                if (a.footprintW <= MinFoot + 1e-3f && a.footprintD <= MinFoot + 1e-3f) continue;
                a.footprintW = Mathf.Max(MinFoot, a.footprintW * 0.9f); a.footprintD = Mathf.Max(MinFoot, a.footprintD * 0.9f);
                a.sizeClass = a.footprintW * a.footprintD > 70 ? 2 : a.footprintW * a.footprintD > 40 ? 1 : 0;
                Lots[i] = a; any = true;
            }
            if (!any) break;
        }
    }
    // ---- 4.9 (partial): validator covers 4.1-4.3 rules only so far. Extend as 4.4-4.8 land. ----
    public static string Validate()
    {
        var sb = new StringBuilder(); var fails = new List<string>(); int pass = 0;
        int unassigned = 0;
        for (int i = 0; i < Lots.Length; i++) if (Lots[i].settlement < 0) unassigned++;
        if (unassigned == 0) pass++; else fails.Add(unassigned + " lots not assigned to any settlement");

        int harbourLots = 0, farmSettle = 0, farmBig = 0, hamletSettle = 0, hamletBig = 0, summitLots = 0;
        foreach (var s in Settlements)
        {
            if (s.role == Zone.Harbour) harbourLots += s.lotIdx.Count;
            if (s.role == Zone.Farm) { farmSettle++; if (s.lotIdx.Count >= MinFarmLotsEach) farmBig++; }
            if (s.role == Zone.Hamlet) { hamletSettle++; if (s.lotIdx.Count >= MinHamletLotsEach) hamletBig++; }
            if (s.role == Zone.Summit) summitLots += s.lotIdx.Count;
        }
        if (harbourLots >= MinHarbourLots) pass++; else fails.Add("harbour lots " + harbourLots + " < " + MinHarbourLots);
        if (farmBig >= MinFarmSettlements) pass++; else fails.Add("farm villages with >=" + MinFarmLotsEach + " lots: " + farmBig + " < " + MinFarmSettlements);
        if (hamletBig >= MinHamletSettlements) pass++; else fails.Add("hamlets with >=" + MinHamletLotsEach + " lots: " + hamletBig + " < " + MinHamletSettlements);
        if (summitLots >= MinSummitLots) pass++; else fails.Add("summit station lots " + summitLots + " < " + MinSummitLots);

        // a solitary farmstead (1-2 lots) is a real, intended settlement type (see brief), not a failed
        // village -- it doesn't need a full plaza, so only count noPlaza for settlements of 3+ lots.
        int noPlaza = 0;
        foreach (var s in Settlements) if (!s.hasPlaza && s.lotIdx.Count >= 3) noPlaza++;
        if (noPlaza == 0) pass++; else fails.Add(noPlaza + " settlements have no plaza (clear, land, relief <= " + PlazaMaxRelief + " m)");

        int nL = Lots.Length, lotLot = 0, lotPublic = 0, dropped = 0, roadTouch = 0; var pubs = PublicCircles();
        for (int i = 0; i < nL; i++)
        {
            var a = Lots[i]; if (a.dropped) { dropped++; continue; }
            if (TouchesRoad(a, a.x, a.z, 0.0)) roadTouch++;
            for (int j = i + 1; j < nL; j++)
            {
                var b = Lots[j]; if (b.dropped) continue;
                if (ObbOverlap(a.x, a.z, a.footprintW * 0.5, a.footprintD * 0.5, a.yaw, b.x, b.z, b.footprintW * 0.5, b.footprintD * 0.5, b.yaw, 0.0)) lotLot++;
            }
            for (int k = 0; k < pubs.Count; k++)
                if (ObbCircleOverlap(a.x, a.z, a.footprintW * 0.5, a.footprintD * 0.5, a.yaw, pubs[k].x, pubs[k].z, pubs[k].r, 0.0)) lotPublic++;
        }
        if (roadTouch == 0) pass++; else fails.Add(roadTouch + " house footprints reach into a road corridor");
        if (lotPublic == 0) pass++; else fails.Add(lotPublic + " plazas/anchors overlap a house footprint");
        if (lotLot == 0) pass++; else fails.Add(lotLot + " overlapping placeholder footprints");

        sb.AppendLine("SETTLEMENTS: " + Settlements.Count + " total | lots " + (nL - dropped) + " active (" + dropped + " duplicate dropped)");
        foreach (var s in Settlements)
        {
            string kind = s.role == Zone.Farm && s.lotIdx.Count < 3 ? "solitary farmstead" : s.role.ToString();
            sb.AppendLine("  " + s.name + " [" + kind + "] lots=" + s.lotIdx.Count + " centre=(" + s.cx.ToString("F0") + "," + s.cz.ToString("F0") + ") r=" + s.radius.ToString("F0") + " plaza=" + (s.hasPlaza ? "OK" : (s.lotIdx.Count >= 3 ? "FAIL" : "n/a")) + " anchors=" + s.anchors.Count);
        }
        sb.AppendLine("RULES (4.1-4.3 only; 4.4-4.9 pending): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        legend = "settlements: " + Settlements.Count + " | lots " + ZoneMap.Lots.Count + " (" + unassigned + " unassigned) | rules " + pass + "/" + (pass + fails.Count);
        return Report;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 4.1-4.3 Settlements + Validate")]
    static void Menu() { Build(); Debug.Log(Report); }
#endif
}
