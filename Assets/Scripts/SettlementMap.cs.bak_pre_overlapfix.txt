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
/// PICKUP NOTES (read this first if continuing):
///   - DONE: 4.1 Settlement definition, 4.2 Plazas/anchors, 4.3 House parcels/massing.
///   - TODO: 4.4 PadGrade.cs, 4.5 LaneNet.cs, 4.6 WaterAccess.cs, 4.7 FarmPlots.cs, 4.8 FieldWalls.cs,
///           4.9 finish Validate()/Paint()/DrawGizmos() to cover ALL phase 4 rules (right now Validate()
///           only checks 4.1-4.3 rules) and wire WorldFieldDebug mode 7.
///   - ZoneMap.Lots is a flat, ungrouped list with only a `group` int (hamlet cluster index, -1 meaningless
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
        ZoneMap.Build();   // idempotent; Phase 3.9 data (Zones, Lots, Suit, Hamlets)
        Settlements.Clear();
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
        PlacePlazasAndAnchors();   // 4.2
        MassLots();                // 4.3

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

    // ---- 4.2: plaza on flattest ground near the settlement centre + road, then anchors ----
static void PlacePlazasAndAnchors()
    {
        for (int si = 0; si < Settlements.Count; si++)
        {
            var s = Settlements[si];
            FindPlaza(s.cx, s.cz, s.radius, out double px, out double pz, out double slope);
            s.hasPlaza = slope < 3.0;
            s.plazaX = px; s.plazaZ = pz; s.plazaR = 7.0 + Math.Min(5.0, s.lotIdx.Count * 0.3);

            // well / fountain -- every settlement gets one at the plaza (the plaza itself is already
            // picked clear of lots by FindPlaza's slope scan in practice, but we still verify below)
            PlaceAnchorClear(s, "well", px, pz, 0.0, out var well); s.anchors.Add(well);

            // shrine/chapel for farm villages and hamlets; monument for the harbour
            if (s.role == Zone.Farm || s.role == Zone.Hamlet)
            { PlaceAnchorClear(s, "chapel", px, pz, s.plazaR + 4.0, out var chapel); s.anchors.Add(chapel); }
            if (s.role == Zone.Harbour)
            { PlaceAnchorClear(s, "monument", px, pz, s.plazaR + 3.0, out var monument); s.anchors.Add(monument); }

            // mill: only if a river runs within range of this settlement
            if (NearestRiverDist(s.cx, s.cz, 140.0, out double rx, out double rz))
                s.anchors.Add(new Anchor { kind = "mill", x = rx, z = rz, h = WorldField.Height(rx, rz) });

            // summit tower sits at the settlement centre (the summit itself), not the plaza
            if (s.role == Zone.Summit)
                s.anchors.Add(new Anchor { kind = "tower", x = RoadNet.SummitX, z = RoadNet.SummitZ, h = RoadNet.SummitH });

            Settlements[si] = s;
        }
    }

    // place an anchor starting `startDist` out from (px,pz), stepping further out in a spiral until it
    // clears every lot footprint in the settlement (so a well/chapel/monument never lands inside a house).
    static void PlaceAnchorClear(Settlement s, string kind, double px, double pz, double startDist, out Anchor result)
    {
        bool Clear(double x, double z)
        {
            foreach (int li in s.lotIdx)
            {
                var l = ZoneMap.Lots[li]; double dx = l.x - x, dz = l.z - z;
                double lim = Math.Max(l.w, l.d) * 0.5 + 1.5;
                if (dx * dx + dz * dz < lim * lim) return false;
            }
            return true;
        }
        if (startDist < 1e-6 && Clear(px, pz)) { result = new Anchor { kind = kind, x = px, z = pz, h = WorldField.Height(px, pz) }; return; }
        for (int ring = 0; ring < 10; ring++)
        {
            double r = startDist + ring * 4.0;
            int steps = 8 + ring * 4;
            for (int t = 0; t < steps; t++)
            {
                double a = P2 * t / steps;
                double x = px + Math.Cos(a) * r, z = pz + Math.Sin(a) * r;
                if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z)) continue;
                if (!Clear(x, z)) continue;
                result = new Anchor { kind = kind, x = x, z = z, h = WorldField.Height(x, z) };
                return;
            }
        }
        // last resort: plaza centre (flagged by the validator's overlap count if still inside a lot, so
        // the failure is visible rather than silently swallowed)
        result = new Anchor { kind = kind, x = px, z = pz, h = WorldField.Height(px, pz) };
    }

    static void OffsetAnchor(double cx, double cz, double dist, double angle, out double ax, out double az)
    { ax = cx + Math.Cos(angle) * dist; az = cz + Math.Sin(angle) * dist; }

    // scan a coarse ring of candidate points around the centre, pick lowest local slope that's on land,
    // not cliff/water. Plazas are small and ZoneMap already guaranteed the settlement's lots are
    // road-adjacent, so a direct slope scan (rather than re-deriving DRoad here) is sufficient.
    static void FindPlaza(double cx, double cz, double searchR, out double bestX, out double bestZ, out double bestSlope)
    {
        bestX = cx; bestZ = cz; bestSlope = WorldField.SlopeDeg(cx, cz);
        int rings = 5;
        for (int ring = 0; ring <= rings; ring++)
        {
            double r = searchR * ring / rings;
            int steps = ring == 0 ? 1 : 10 + ring * 4;
            for (int t = 0; t < steps; t++)
            {
                double a = P2 * t / steps;
                double x = cx + Math.Cos(a) * r, z = cz + Math.Sin(a) * r;
                if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z)) continue;
                double sl = WorldField.SlopeDeg(x, z);
                if (sl < bestSlope) { bestSlope = sl; bestX = x; bestZ = z; }
            }
        }
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

    // ---- 4.3: footprint / floors / size class / orientation, denser toward settlement centre ----
    static void MassLots()
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
            // dense core (small gap, more floors) -> loose ring (bigger yard, fewer floors)
            float margin = Mathf.Lerp(0.3f, 1.6f, (float)distFrac);
            float fw = Mathf.Max(3.5f, l.w - margin), fd = Mathf.Max(3.5f, l.d - margin);
            int floors = distFrac < 0.4 ? (rng.NextDouble() < 0.5 ? 2 : 3) : distFrac < 0.75 ? (rng.NextDouble() < 0.6 ? 2 : 1) : 1;
            if (l.zone == Zone.Summit) floors = 1;   // station building, single storey
            int sizeClass = fw * fd > 70 ? 2 : fw * fd > 40 ? 1 : 0;

            // orientation: face the settlement plaza if we have one, else face back toward the road (fx,fz
            // from ZoneMap.Lot already points lot -> nearest road)
            float yaw;
            if (si >= 0 && Settlements[si].hasPlaza)
            {
                var s = Settlements[si];
                double dx = s.plazaX - l.x, dz = s.plazaZ - l.z;
                yaw = (float)Math.Atan2(dx, dz);
            }
            else yaw = (float)Math.Atan2(l.fx, l.fz);

            Lots[i] = new Massing { footprintW = fw, footprintD = fd, floors = floors, sizeClass = sizeClass, yaw = yaw, settlement = si };
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
        int noPlaza = 0, anchorOverlap = 0;
        foreach (var s in Settlements)
        {
            if (!s.hasPlaza && s.lotIdx.Count >= 3) noPlaza++;
            foreach (var a in s.anchors)
                foreach (int li in s.lotIdx)
                {
                    var l = ZoneMap.Lots[li]; double dx = l.x - a.x, dz = l.z - a.z;
                    double lim = Math.Max(l.w, l.d) * 0.5 + 1.0;
                    if (dx * dx + dz * dz < lim * lim) anchorOverlap++;
                }
        }
        if (noPlaza == 0) pass++; else fails.Add(noPlaza + " settlements have no plaza under 3% slope");
        if (anchorOverlap == 0) pass++; else fails.Add(anchorOverlap + " public anchors overlap a house lot");

        int massOverlap = 0;
        for (int i = 0; i < ZoneMap.Lots.Count; i++)
            for (int j = i + 1; j < ZoneMap.Lots.Count; j++)
            {
                var a = ZoneMap.Lots[i]; var b = ZoneMap.Lots[j];
                double dx = a.x - b.x, dz = a.z - b.z;
                double lim = (Math.Max(Lots[i].footprintW, Lots[i].footprintD) + Math.Max(Lots[j].footprintW, Lots[j].footprintD)) * 0.5 * 0.85;
                if (dx * dx + dz * dz < lim * lim) massOverlap++;
            }
        if (massOverlap == 0) pass++; else fails.Add(massOverlap + " overlapping placeholder footprints");

        sb.AppendLine("SETTLEMENTS: " + Settlements.Count + " total");
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
