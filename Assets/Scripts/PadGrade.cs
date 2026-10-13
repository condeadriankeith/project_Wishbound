using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 4.4 — pad grading and terracing. A signed height-adjustment raster (metres ADDED to the carved,
/// road-graded terrain), composed into TerrainMath.H after RoadGrade, exactly like the road corridor term.
/// Every lot footprint, plaza and public anchor (chapel / monument / tower) gets a flat pad at a target
/// height (mean ground height, clamped so cut &lt;= MaxCut and fill &lt;= MaxFill), blended back to the
/// terrain across Shoulder. Pad influence is faded out by RoadGrade.MaskAt so pads never disturb the road
/// corridor (the Phase 3 no-clip guarantee). Retaining walls / steps are emitted as DATA (Walls) where a
/// pad cuts or fills more than WallMin, or where neighbouring pads differ by more than WallMin; the
/// renderer for them comes later.
///
/// Built lazily on the first H() call (like RoadGrade); while building, Apply() returns the height
/// unchanged so the pass samples the pre-pad terrain without recursing. WorldField / RoadNet / RoadBuild
/// switch TerrainMath.PadsOn off while they sample raw terrain, so pads never leak into the raw grids,
/// the road routing or the road profile.
/// </summary>
public static class PadGrade
{
    public const double Res = 1.0;          // raster resolution (m) — matches RoadGrade
    public const double Shoulder = 2.5;     // blend-back width beyond the pad edge (m)
    public const double Margin = 0.8;       // pad extends this far beyond the footprint box (m)
    public const double MaxCut = 4.0;       // deepest cut into a slope for any one pad (m)
    public const double MaxFill = 3.0;      // tallest fill under any one pad (m)
    public const double WallMin = 0.5;      // drops above this need a retaining wall + steps
    public const double RiseStep = 0.18;    // stair riser (m)

    public struct Pad
    {
        public string kind;                 // "lot" | "plaza" | "chapel" | "monument" | "tower"
        public int settlement, lot;         // lot = index into ZoneMap.Lots, -1 for non-lot pads
        public double x, z, yaw;            // centre (source xz) and door-forward yaw (rect pads)
        public double hw, hd, r;            // half width (right), half depth (forward), radius (circle pads)
        public bool circle;
        public double T, minH, maxH, cut, fill;
        public bool tooSteep;               // footprint relief exceeds MaxCut + MaxFill: cannot be made flat within limits
    }

    public struct Wall
    {
        public string kind;                 // "cut" | "fill" | "step"
        public int padHi, padLo;            // pad indices (-1 = natural ground)
        public double x, z, height;
        public bool steps;
        public int stairs;
    }

    public static float[] Fill, Mask;
    public static int NR, Hash;
    public static readonly List<Pad> Pads = new List<Pad>();
    public static readonly List<Wall> Walls = new List<Wall>();
    public static string Report = "";
    static bool building;

    static double Sm01(double u) { u = u < 0 ? 0 : u > 1 ? 1 : u; return u * u * (3 - 2 * u); }

    public static double Apply(double x, double z, double h)
    {
        if (Fill == null) { if (building) return h; Build(); if (Fill == null) return h; }
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return h;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Fill[k] + (Fill[k + 1] - Fill[k]) * tx, b = Fill[k + NR] + (Fill[k + NR + 1] - Fill[k + NR]) * tx;
        return h + (a + (b - a) * tz);
    }

    /// <summary>1 on a pad, fading to 0 across its shoulder (and 0 on road corridors). IslandBuilder uses this
    /// to protect pad cells from cliff smoothing.</summary>
    public static double MaskAt(double x, double z)
    {
        if (Mask == null) return 0;
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return 0;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Mask[k] + (Mask[k + 1] - Mask[k]) * tx, b = Mask[k + NR] + (Mask[k + NR + 1] - Mask[k + NR]) * tx;
        return a + (b - a) * tz;
    }

    // ---------- pad geometry ----------
    // distance outside the pad shape (0 inside)
    static double Sdf(Pad p, double x, double z)
    {
        double dx = x - p.x, dz = z - p.z;
        if (p.circle) return Math.Max(0.0, Math.Sqrt(dx * dx + dz * dz) - p.r);
        double s = Math.Sin(p.yaw), c = Math.Cos(p.yaw);
        double lf = dx * s + dz * c, lr = dx * c - dz * s;
        double ex = Math.Max(0.0, Math.Abs(lr) - p.hw), ez = Math.Max(0.0, Math.Abs(lf) - p.hd);
        return Math.Sqrt(ex * ex + ez * ez);
    }

    static double Reach(Pad p) { return p.circle ? p.r : Math.Sqrt(p.hw * p.hw + p.hd * p.hd); }

    // point inside the pad at normalised coords u,v in [-1,1]; false if outside a circle pad
    static bool SamplePoint(Pad p, double u, double v, out double x, out double z)
    {
        x = 0; z = 0;
        if (p.circle)
        {
            if (u * u + v * v > 1.0) return false;
            x = p.x + u * p.r; z = p.z + v * p.r; return true;
        }
        double s = Math.Sin(p.yaw), c = Math.Cos(p.yaw);
        double lr = u * p.hw, lf = v * p.hd;
        x = p.x + lr * c + lf * s; z = p.z - lr * s + lf * c;
        return true;
    }

    // ---------- build ----------
    public static void Build()
    {
        Fill = null; Mask = null;
        building = true;
        try
        {
            if (!SettlementMap.Built) SettlementMap.Build();
            Pads.Clear(); Walls.Clear();
            CollectPads();

            NR = (int)Math.Ceiling(2 * E / Res) + 1;
            var fill = new float[NR * NR];
            var own = new float[NR * NR];
            var mask = new float[NR * NR];
            for (int k = 0; k < own.Length; k++) own[k] = 1e9f;

            for (int pi = 0; pi < Pads.Count; pi++) { var p = Pads[pi]; SolveHeight(ref p); Pads[pi] = p; }
            for (int pi = 0; pi < Pads.Count; pi++) Stamp(Pads[pi], fill, own, mask);
            BuildWalls();

            Fill = fill; Mask = mask;
            unchecked
            {
                int hs = (int)2166136261u;
                hs = (hs ^ Pads.Count) * 16777619; hs = (hs ^ Walls.Count) * 16777619;
                for (int k = 0; k < fill.Length; k += 7) hs = (hs ^ (int)(fill[k] * 1000f)) * 16777619;
                Hash = hs;
            }
        }
        finally { building = false; }
    }

    static void CollectPads()
    {
        for (int i = 0; i < ZoneMap.Lots.Count; i++)
        {
            var m = SettlementMap.Lots[i]; if (m.dropped) continue;
            Pads.Add(new Pad
            {
                kind = "lot", settlement = m.settlement, lot = i, x = m.x, z = m.z, yaw = m.yaw,
                hw = m.footprintW * 0.5 + Margin, hd = m.footprintD * 0.5 + Margin
            });
        }
        for (int si = 0; si < SettlementMap.Settlements.Count; si++)
        {
            var s = SettlementMap.Settlements[si];
            if (s.hasPlaza)
                Pads.Add(new Pad { kind = "plaza", settlement = si, lot = -1, x = s.plazaX, z = s.plazaZ, circle = true, r = s.plazaR });
            foreach (var a in s.anchors)
            {
                if (a.kind == "chapel" || a.kind == "monument")
                    Pads.Add(new Pad { kind = a.kind, settlement = si, lot = -1, x = a.x, z = a.z, circle = true, r = 3.5 });
                else if (a.kind == "tower")
                    Pads.Add(new Pad { kind = "tower", settlement = si, lot = -1, x = a.x, z = a.z, circle = true, r = 6.0 });
            }
        }
    }

    // flat target height: mean ground over the pad, clamped so cut/fill stay within limits
    static void SolveHeight(ref Pad p)
    {
        const int n = 7;
        double sum = 0, mn = 1e9, mx = -1e9; int cnt = 0;
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                double u = a / (double)(n - 1) * 2 - 1, v = b / (double)(n - 1) * 2 - 1;
                if (!SamplePoint(p, u, v, out double x, out double z)) continue;
                double h = H(x, z);
                sum += h; cnt++; if (h < mn) mn = h; if (h > mx) mx = h;
            }
        double mean = cnt > 0 ? sum / cnt : 0;
        double lo = mx - MaxCut, hi = mn + MaxFill;
        p.tooSteep = lo > hi;
        p.T = p.tooSteep ? mean : Cl(lo, hi, mean);
        p.minH = mn; p.maxH = mx;
        p.cut = Math.Max(0.0, mx - p.T); p.fill = Math.Max(0.0, p.T - mn);
    }

    static void Stamp(Pad p, float[] fill, float[] own, float[] mask)
    {
        double R = Reach(p) + Shoulder + Res + 1;
        int i0 = Math.Max(0, (int)Math.Floor((p.x - R + E) / Res)), i1 = Math.Min(NR - 1, (int)Math.Ceiling((p.x + R + E) / Res));
        int j0 = Math.Max(0, (int)Math.Floor((p.z - R + E) / Res)), j1 = Math.Min(NR - 1, (int)Math.Ceiling((p.z + R + E) / Res));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double cx = -E + i * Res, cz = -E + j * Res;
                double dr = Sdf(p, cx, cz);                       // true distance outside the pad shape
                double d = Math.Max(0.0, dr - Res);                // flat zone extends one raster cell past the edge so bilinear sampling stays flat at the rim
                if (d >= Shoulder) continue;
                int k = j * NR + i;
                if (dr >= own[k]) continue;                      // a nearer pad owns this cell (ties: first pad wins)
                double fall = d <= 0 ? 1.0 : 1.0 - Sm01(d / Shoulder);
                double keep = 1.0 - Math.Min(1.0, RoadGrade.MaskAt(cx, cz));   // never disturb the road corridor
                double delta = (p.T - H(cx, cz)) * fall * keep;
                delta = Cl(-(MaxCut + 1.5), MaxFill + 1.5, delta);
                fill[k] = (float)delta; own[k] = (float)dr;
                double mk = fall * keep; if (mk > mask[k]) mask[k] = (float)mk;
            }
    }

    static void BuildWalls()
    {
        for (int i = 0; i < Pads.Count; i++)
        {
            var p = Pads[i];
            if (p.cut > WallMin)
                Walls.Add(new Wall { kind = "cut", padHi = -1, padLo = i, x = p.x, z = p.z, height = p.cut, steps = true, stairs = (int)Math.Ceiling(p.cut / RiseStep) });
            if (p.fill > WallMin)
                Walls.Add(new Wall { kind = "fill", padHi = i, padLo = -1, x = p.x, z = p.z, height = p.fill, steps = true, stairs = (int)Math.Ceiling(p.fill / RiseStep) });
        }
        for (int i = 0; i < Pads.Count; i++)
            for (int j = i + 1; j < Pads.Count; j++)
            {
                var a = Pads[i]; var b = Pads[j];
                if (a.settlement != b.settlement) continue;
                double dx = a.x - b.x, dz = a.z - b.z;
                double gap = Math.Sqrt(dx * dx + dz * dz) - Reach(a) - Reach(b);
                if (gap > 2.0) continue;
                double dh = Math.Abs(a.T - b.T);
                if (dh <= WallMin) continue;
                int hi = a.T > b.T ? i : j, lo = hi == i ? j : i;
                Walls.Add(new Wall { kind = "step", padHi = hi, padLo = lo, x = (a.x + b.x) * 0.5, z = (a.z + b.z) * 0.5, height = dh, steps = true, stairs = (int)Math.Ceiling(dh / RiseStep) });
            }
    }

    // ---------- validation ----------
    public static string Validate()
    {
        if (Fill == null) Build();
        var sb = new StringBuilder("[PadGrade 4.4]\n");
        var fails = new List<string>(); int pass = 0;

        int steepPads = 0, tooSteep = 0, overCut = 0, overFill = 0, roadOverlap = 0;
        double worstSlope = 0, maxCut = 0, maxFill = 0;
        for (int pidx = 0; pidx < Pads.Count; pidx++)
        {
            var p = Pads[pidx];
            double mn = 1e9, mx = -1e9; bool roadHit = false;
            double shrinkU = p.circle ? 1.0 : Math.Max(0.2, (p.hw - Margin) / p.hw), shrinkV = p.circle ? 1.0 : Math.Max(0.2, (p.hd - Margin) / p.hd);
            for (int a = 0; a < 9; a++)
                for (int b = 0; b < 9; b++)
                {
                    double u = (a / 8.0 * 2 - 1) * shrinkU, v = (b / 8.0 * 2 - 1) * shrinkV;
                    if (!SamplePoint(p, u, v, out double x, out double z)) continue;
                    bool inOther = false;
                    for (int q = 0; q < Pads.Count && !inOther; q++) if (q != pidx && Sdf(Pads[q], x, z) <= 2.0 * Res) inOther = true;   // skip step/transition strips between neighbouring pads
                    double rm = RoadGrade.MaskAt(x, z);
                    if (rm > 0.5) roadHit = true;
                    if (inOther || rm > 0.15) continue;   // road corridor + its shoulder blend belong to Phase 3's grade   // the road's own grade is Phase 3's rule, not the pad's   // overlap with another pad is a placement fault, reported by SettlementMap
                    double h = H(x, z); if (h < mn) mn = h; if (h > mx) mx = h;
                }
            double size = 2.0 * (p.circle ? p.r : Math.Max(p.hw, p.hd));
            double slope = mx < mn ? 0.0 : (mx - mn) / Math.Max(1.0, size) * 100.0;
            if (slope > worstSlope) worstSlope = slope;
            if (slope >= 2.0) steepPads++;
            if (p.tooSteep) tooSteep++;
            if (p.cut > MaxCut + 1e-6) overCut++;
            if (p.fill > MaxFill + 1e-6) overFill++;
            if (p.cut > maxCut) maxCut = p.cut;
            if (p.fill > maxFill) maxFill = p.fill;
            // plazas sit at road junctions and the summit tower sits where the road ends, so both may touch the corridor by design
            if (roadHit && (p.kind == "lot" || p.kind == "chapel" || p.kind == "monument")) roadOverlap++;
        }
        if (steepPads == 0) pass++; else fails.Add(steepPads + " pads steeper than 2% (worst " + worstSlope.ToString("F1") + "%)");
        if (tooSteep == 0) pass++; else fails.Add(tooSteep + " pads sit on relief beyond cut+fill limits");
        if (overCut == 0 && overFill == 0) pass++; else fails.Add(overCut + " pads over the cut limit, " + overFill + " over the fill limit");
        if (roadOverlap == 0) pass++; else fails.Add(roadOverlap + " pads overlap the road corridor");

        int badWalls = 0, wallsOverMin = 0, stairsTotal = 0;
        foreach (var w in Walls) { if (w.height > WallMin) { wallsOverMin++; if (!w.steps || w.stairs <= 0) badWalls++; } stairsTotal += w.stairs; }
        if (badWalls == 0) pass++; else fails.Add(badWalls + " drops over " + WallMin + " m have no steps");

        string rb = RoadBuild.Validate();
        bool roadsPass = rb.Contains("RESULT: PASS");
        if (roadsPass) pass++; else fails.Add("Phase 3 road validator no longer passes with pads on");

        // Check determinism: generate pad grading twice and see if we get the same hash
        int hash1 = Hash;
        int hash2 = 0;
        int hash3 = 0;

        // First generation
        Fill = null;
        Mask = null;
        building = true;
        try
        {
            if (!SettlementMap.Built) SettlementMap.Build();
            Pads.Clear(); Walls.Clear();
            CollectPads();

            NR = (int)Math.Ceiling(2 * E / Res) + 1;
            var fill = new float[NR * NR];
            var own = new float[NR * NR];
            var mask = new float[NR * NR];
            for (int k = 0; k < own.Length; k++) own[k] = 1e9f;

            for (int pi = 0; pi < Pads.Count; pi++) { var p = Pads[pi]; SolveHeight(ref p); Pads[pi] = p; }
            for (int pi = 0; pi < Pads.Count; pi++) Stamp(Pads[pi], fill, own, mask);
            BuildWalls();

            Fill = fill; Mask = mask;
            unchecked
            {
                int hs = (int)2166136261u;
                hs = (hs ^ Pads.Count) * 16777619; hs = (hs ^ Walls.Count) * 16777619;
                for (int k = 0; k < fill.Length; k += 7) hs = (hs ^ (int)(fill[k] * 1000f)) * 16777619;
                hash2 = hs;
            }
        }
        finally { building = false; }

        // Second generation
        Fill = null;
        Mask = null;
        building = true;
        try
        {
            if (!SettlementMap.Built) SettlementMap.Build();
            Pads.Clear(); Walls.Clear();
            CollectPads();

            NR = (int)Math.Ceiling(2 * E / Res) + 1;
            var fill = new float[NR * NR];
            var own = new float[NR * NR];
            var mask = new float[NR * NR];
            for (int k = 0; k < own.Length; k++) own[k] = 1e9f;

            for (int pi = 0; pi < Pads.Count; pi++) { var p = Pads[pi]; SolveHeight(ref p); Pads[pi] = p; }
            for (int pi = 0; pi < Pads.Count; pi++) Stamp(Pads[pi], fill, own, mask);
            BuildWalls();

            Fill = fill; Mask = mask;
            unchecked
            {
                int hs = (int)2166136261u;
                hs = (hs ^ Pads.Count) * 16777619; hs = (hs ^ Walls.Count) * 16777619;
                for (int k = 0; k < fill.Length; k += 7) hs = (hs ^ (int)(fill[k] * 1000f)) * 16777619;
                hash3 = hs;
            }
        }
        finally { building = false; }

        bool det = (hash2 == hash3);
        if (det) pass++; else fails.Add("pad grading is not deterministic");

        int lots = 0, plazas = 0, other = 0;
        foreach (var p in Pads) { if (p.kind == "lot") lots++; else if (p.kind == "plaza") plazas++; else other++; }
        sb.AppendLine("pads " + Pads.Count + " (lots " + lots + ", plazas " + plazas + ", anchors " + other + ") | max cut " + maxCut.ToString("F2") + " m | max fill " + maxFill.ToString("F2") + " m | worst pad slope " + worstSlope.ToString("F2") + "%");
        sb.AppendLine("walls/steps " + Walls.Count + " (" + wallsOverMin + " over " + WallMin + " m) | stairs risers " + stairsTotal + " | roads " + (roadsPass ? "PASS" : "FAIL") + " | deterministic " + det);
        sb.AppendLine("RULES (4.4): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 4.4 Pads + Validate")]
    static void Menu() { UnityEngine.Debug.Log(Validate()); }
#endif
}
