using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using static TerrainMath;

public enum Zone : byte { Sea, Water, Cliff, Wild, Farm, Harbour, Hamlet, Summit }

/// <summary>Phase 3.9: suitability (slope, fresh water, sun, road distance) -> zones -> building lots + rule validator.</summary>
public static class ZoneMap
{
    public struct Lot { public double x, z, fx, fz, h; public float w, d, dRoad, suit; public Zone zone; public int group; }

    public static int G; public static double Cell;
    public static Zone[] Zones; public static float[] Suit, DRoad, DFresh, DBridge;
    public static readonly List<Lot> Lots = new List<Lot>();
    public static readonly List<Vector2> Hamlets = new List<Vector2>();
    public static bool Built; public static string Report = "";
    static float[] shade; static bool[] roadM, crossM;
    static readonly List<double> rx = new List<double>(), rz = new List<double>();
    static string legend = "zones";
    public static string Legend() { return legend; }

    const double SummitR = 85, HarbourDist = 90, HarbourH = 7, HamletR = 48, HamletSep = 120;
    static readonly Zone[] Key = { Zone.Harbour, Zone.Hamlet, Zone.Farm, Zone.Summit };

    static double Xg(int i) { return -E + i * Cell; }
    static int Ix(double v) { return Mathf.Clamp(Mathf.RoundToInt((float)((v + E) / Cell)), 0, G - 1); }
    static int K(double x, double z) { return Ix(z) * G + Ix(x); }
    static float S3(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static void Mark(bool[] m, double x, double z)
    {
        int i = (int)System.Math.Round((x + E) / Cell), j = (int)System.Math.Round((z + E) / Cell);
        if (i >= 0 && j >= 0 && i < G && j < G) m[j * G + i] = true;
    }
    static void Line(bool[] m, double x0, double z0, double x1, double z1)
    {
        double L = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)); int s = (int)(L / (Cell * 0.5)) + 1;
        for (int t = 0; t <= s; t++) { double u = (double)t / s; Mark(m, x0 + (x1 - x0) * u, z0 + (z1 - z0) * u); }
    }
    static float[] Dist(bool[] src)
    {
        int n = G; var d = new float[n * n]; float c = (float)Cell, c2 = c * 1.4142f;
        for (int k = 0; k < d.Length; k++) d[k] = src[k] ? 0f : 1e9f;
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                int k = j * n + i; float v = d[k];
                if (i > 0) v = Mathf.Min(v, d[k - 1] + c);
                if (j > 0) { v = Mathf.Min(v, d[k - n] + c); if (i > 0) v = Mathf.Min(v, d[k - n - 1] + c2); if (i < n - 1) v = Mathf.Min(v, d[k - n + 1] + c2); }
                d[k] = v;
            }
        for (int j = n - 1; j >= 0; j--) for (int i = n - 1; i >= 0; i--)
            {
                int k = j * n + i; float v = d[k];
                if (i < n - 1) v = Mathf.Min(v, d[k + 1] + c);
                if (j < n - 1) { v = Mathf.Min(v, d[k + n] + c); if (i < n - 1) v = Mathf.Min(v, d[k + n + 1] + c2); if (i > 0) v = Mathf.Min(v, d[k + n - 1] + c2); }
                d[k] = v;
            }
        return d;
    }

    public static void Build()
    {
        // Zones and lots are derived from the terrain BEFORE pad grading; pads are derived from the lots. Sampling the
        // padded terrain here made the first (cold) build re-enter PadGrade -> SettlementMap -> ZoneMap and yield a
        // different lot count (66) than a warm rebuild (57). Same switch WorldField.Build / RoadNet use.
        bool pd = PadsOn; PadsOn = false;
        try { BuildCore(); } finally { PadsOn = pd; }
    }

    static void BuildCore()
    {
        WorldField.Build();
        try { FlowNet.Build(); } catch (System.Exception e) { Debug.LogWarning("Zones: FlowNet " + e.Message); }
        try { RoadNet.Build(); RoadBuild.Build(); } catch (System.Exception e) { Debug.LogWarning("Zones: roads " + e.Message); }
        G = WorldField.G; Cell = WorldField.Cell; int n = G * G;
        roadM = new bool[n]; crossM = new bool[n]; var wat = new bool[n]; rx.Clear(); rz.Clear(); Lots.Clear(); Hamlets.Clear();

        bool any = false;
        if (RoadBuild.Roads != null)
            foreach (var r in RoadBuild.Roads)
            {
                any = true;
                for (int q = 0; q < r.x.Length; q++) { rx.Add(r.x[q]); rz.Add(r.z[q]); if (q > 0) Line(roadM, r.x[q - 1], r.z[q - 1], r.x[q], r.z[q]); }
                foreach (var c in r.crosses) for (int q = c.i0; q <= c.i1 && q < r.x.Length; q++) Mark(crossM, r.x[q], r.z[q]);
            }
        if (!any)
            foreach (var p in RoadNet.Paths)
                for (int q = 0; q < p.x.Length; q++) { rx.Add(p.x[q]); rz.Add(p.z[q]); if (q > 0) Line(roadM, p.x[q - 1], p.z[q - 1], p.x[q], p.z[q]); }

        if (FlowNet.Rivers != null)
            foreach (var rv in FlowNet.Rivers)
                for (int q = 0; q < rv.cells.Length; q++)
                {
                    int c = rv.cells[q]; double x = FlowNet.X(c), z = FlowNet.Z(c); Mark(wat, x, z);
                    if (q > 0) { int p = rv.cells[q - 1]; Line(wat, FlowNet.X(p), FlowNet.Z(p), x, z); }
                }
        double lr = FlowNet.LakeR;
        for (int j = 0; j < G; j++) for (int i = 0; i < G; i++)
            { double dx = Xg(i) - FlowNet.LakeX, dz = Xg(j) - FlowNet.LakeZ; if (dx * dx + dz * dz < lr * lr) wat[j * G + i] = true; }

        DRoad = Dist(roadM); DBridge = Dist(crossM); DFresh = Dist(wat);
        Zones = new Zone[n]; Suit = new float[n]; shade = new float[n];

        double sea = WorldField.SeaLevel; var HG = WorldField.HeightGrid; var SG = WorldField.SlopeGrid; var SD = WorldField.SeaDist;
        Vector3 L3 = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(0.35f, 0.7f, -0.6f);
        L3.Normalize(); float lz = MirrorZ ? -L3.z : L3.z; float c2 = (float)(2 * Cell);

        for (int j = 0; j < G; j++) for (int i = 0; i < G; i++)
            {
                int k = j * G + i; float h = HG[k], s = SG[k];
                float gx = (HG[j * G + System.Math.Min(i + 1, G - 1)] - HG[j * G + System.Math.Max(i - 1, 0)]) / c2;
                float gz = (HG[System.Math.Min(j + 1, G - 1) * G + i] - HG[System.Math.Max(j - 1, 0) * G + i]) / c2;
                float inv = 1f / Mathf.Sqrt(gx * gx + 1f + gz * gz);
                float dot = (-gx * L3.x + L3.y - gz * lz) * inv;
                float sunF = Mathf.Clamp01(dot / Mathf.Max(0.2f, L3.y));
                shade[k] = Mathf.Clamp(0.55f + 0.6f * dot, 0.55f, 1.2f);

                float wF = (float)System.Math.Exp(-System.Math.Pow((DFresh[k] - 50.0) / 100.0, 2)); if (DFresh[k] < 6f) wF = 0f;
                float rF = 1f - S3(8f, 90f, DRoad[k]);
                Suit[k] = (1f - S3(4f, 14f, s)) * (0.45f * rF + 0.25f * sunF + 0.30f * wF);

                double rel = h - sea, x = Xg(i), z = Xg(j), ex = x - RoadNet.SummitX, ez = z - RoadNet.SummitZ;
                Zone zn;
                if (h <= sea) zn = Zone.Sea;
                else if (DFresh[k] < 5f) zn = Zone.Water;
                else if (s >= WorldField.CliffSlopeDeg) zn = Zone.Cliff;
                else if (ex * ex + ez * ez < SummitR * SummitR && s < 16f) zn = Zone.Summit;
                else if (SD[k] < HarbourDist && rel < HarbourH && s < 9f) zn = Zone.Harbour;
                else if (Suit[k] > 0.28f && s < 10f && rel >= HarbourH) zn = Zone.Farm;
                else zn = Zone.Wild;
                Zones[k] = zn;
            }

        // hamlet seeds: best suitability on the mid slopes, near road + fresh water, well separated
        double span = RoadNet.SummitH - sea; if (span < 10) span = WorldField.MaxHeight - sea;
        var cand = new List<int>();
        for (int k = 0; k < n; k++)
        {
            if (Zones[k] != Zone.Farm || Suit[k] < 0.5f || DRoad[k] > 30f || DFresh[k] < 12f || DFresh[k] > 150f) continue;
            double rel = HG[k] - sea; if (rel < 0.18 * span || rel > 0.8 * span) continue; cand.Add(k);
        }
        cand.Sort((a, b) => { int c = Suit[b].CompareTo(Suit[a]); return c != 0 ? c : a.CompareTo(b); });
        foreach (int k in cand)
        {
            if (Hamlets.Count >= 4) break;
            var p = new Vector2((float)Xg(k % G), (float)Xg(k / G)); bool ok = true;
            foreach (var h in Hamlets) if ((h - p).sqrMagnitude < HamletSep * HamletSep) { ok = false; break; }
            if (ok) Hamlets.Add(p);
        }
        foreach (var h in Hamlets)
        {
            int i0 = Ix(h.x - HamletR), i1 = Ix(h.x + HamletR), j0 = Ix(h.y - HamletR), j1 = Ix(h.y + HamletR);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
                {
                    int k = j * G + i; double dx = Xg(i) - h.x, dz = Xg(j) - h.y;
                    if (dx * dx + dz * dz < HamletR * HamletR && (Zones[k] == Zone.Farm || Zones[k] == Zone.Wild) && SG[k] < 10f && DFresh[k] >= 5f) Zones[k] = Zone.Hamlet;
                }
        }

        //            zone          w   d  gap rMaxExtra max slope range
        Gen(Zone.Harbour, 9, 12, 15, 14, 14, 7f, 1.2f);
        Gen(Zone.Hamlet, 11, 11, 17, 14, 10, 8f, 1.5f);
        Gen(Zone.Farm, 18, 16, 60, 30, 8, 8f, 2.0f);
        Gen(Zone.Summit, 13, 13, 24, 18, 4, 8f, 1.5f);

        // PHASE 4.1 pickup note: Gen() above can under-produce relative to what a settlement needs
        // (e.g. Farm's wide gap=60 isolates lots so far apart that SettlementMap.cs can't cluster them
        // into villages; Summit's maxPer=4 caps it below a usable station size). This is the "relax or
        // extend lot placement to hit explicit targets" step the Phase 4 brief calls out. The retry is
        // ADDITIVE (Gen()'s own close-together dedupe guards against overlapping the lots already placed
        // above) and only fires per-zone if that zone's count is still short, so a healthy zone is untouched
        // and Phase 3.9's own Validate() rules (>=4/8/3/1 per zone) keep their existing headroom.
        RelaxIfShort(Zone.Harbour, 10, 9, 12, 15, 14, 24, 7f, 1.2f);
        RelaxIfShort(Zone.Farm, 14, 18, 16, 22, 14, 20, 8f, 2.0f);
        RelaxIfShort(Zone.Summit, 5, 13, 13, 14, 10, 10, 8f, 1.5f);
        Built = true; Validate();
    }

    static void NearestRoad(double x, double z, out double px, out double pz, out double d)
    {
        double best = 1e18; px = x; pz = z;
        for (int q = 0; q < rx.Count; q++) { double dx = rx[q] - x, dz = rz[q] - z, e = dx * dx + dz * dz; if (e < best) { best = e; px = rx[q]; pz = rz[q]; } }
        d = System.Math.Sqrt(best);
    }

    static void Gen(Zone zone, float w, float d, float gap, float rExtra, int maxPer, float maxSlope, float maxRange)
    {
        float rMin = d * 0.5f + 4.5f, rMax = rMin + rExtra; var cand = new List<int>();
        for (int k = 0; k < G * G; k++)
            if (Zones[k] == zone && DBridge[k] > 12f && DRoad[k] >= rMin && DRoad[k] <= rMax) cand.Add(k);
        cand.Sort((a, b) => { int c = Suit[b].CompareTo(Suit[a]); return c != 0 ? c : a.CompareTo(b); });
        var per = new Dictionary<int, int>();
        foreach (int k in cand)
        {
            double x = Xg(k % G), z = Xg(k / G); int grp = 0;
            if (zone == Zone.Hamlet) { float bd = 1e18f; for (int q = 0; q < Hamlets.Count; q++) { float e = (Hamlets[q] - new Vector2((float)x, (float)z)).sqrMagnitude; if (e < bd) { bd = e; grp = q; } } }
            int cnt; per.TryGetValue(grp, out cnt); if (cnt >= maxPer) continue;
            bool close = false;
            foreach (var o in Lots) { double dx = o.x - x, dz = o.z - z, g = o.zone == zone ? gap : 14; if (dx * dx + dz * dz < g * g) { close = true; break; } }
            if (close) continue;
            double px, pz, dr; NearestRoad(x, z, out px, out pz, out dr); if (dr < 1e-3) continue;
            double fx = (px - x) / dr, fz = (pz - z) / dr, hAvg;
            if (!Fit(x, z, fx, fz, w, d, maxSlope, maxRange, out hAvg)) continue;
            Lots.Add(new Lot { x = x, z = z, fx = fx, fz = fz, h = hAvg, w = w, d = d, dRoad = (float)dr, suit = Suit[k], zone = zone, group = grp });
            per[grp] = cnt + 1;
        }
    }

    // PHASE 4.1: if a zone came up short after Gen()'s normal pass, retry with a tighter gap (denser
    // packing) and a higher per-group cap, ADDITIVELY (existing lots are untouched; Gen()'s own
    // close-together check prevents new lots overlapping ones already placed). Only fires when short,
    // so a zone that already hit target is left exactly as the original Gen() pass produced it.
    static void RelaxIfShort(Zone zone, int target, float w, float d, float gap, float rExtra, int maxPer, float maxSlope, float maxRange)
    {
        int Count() { int c = 0; foreach (var l in Lots) if (l.zone == zone) c++; return c; }
        if (Count() >= target) return;
        // NEVER go below Validate()'s own overlap threshold (0.9 * max(w,d)) + a small margin, or the
        // denser retry pass places lots that Gen()'s same-zone `close` check allows but Validate() then
        // flags as overlapping (two different distance thresholds that only agreed at the original gap).
        float overlapSafe = Mathf.Max(w, d) * 0.95f;
        float tighterGap = Mathf.Max(gap * 0.6f, overlapSafe);
        Gen(zone, w, d, tighterGap, rExtra, maxPer, maxSlope, maxRange);
        if (Count() >= target) return;
        // still short: widen the road-distance search band and loosen slope/range a touch, last resort
        Gen(zone, w, d, Mathf.Max(tighterGap * 0.75f, overlapSafe), rExtra * 1.6f, maxPer, maxSlope + 2f, maxRange * 1.3f);
    }


    static bool Fit(double x, double z, double fx, double fz, float w, float d, float maxSlope, float maxRange, out double hAvg)
    {
        double ux = -fz, uz = fx, hmin = 1e9, hmax = -1e9, sum = 0; hAvg = 0; double sea = WorldField.SeaLevel;
        for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
            {
                double u = a * w * 0.5, v = b * d * 0.5, px = x + ux * u + fx * v, pz = z + uz * u + fz * v; int k = K(px, pz);
                var zn = Zones[k]; if (zn == Zone.Sea || zn == Zone.Water || zn == Zone.Cliff) return false;
                if (DRoad[k] < 3.5f || DFresh[k] < 5f || DBridge[k] < 8f || WorldField.SeaDist[k] < 3f || WorldField.SlopeGrid[k] > maxSlope) return false;
                double h = WorldField.Height(px, pz); if (h < sea + 0.35) return false;
                if (h < hmin) hmin = h; if (h > hmax) hmax = h; sum += h;
            }
        if (hmax - hmin > maxRange) return false; hAvg = sum / 9; return true;
    }

    public static string Validate()
    {
        var sb = new StringBuilder(); var fails = new List<string>(); int pass = 0;
        var area = new double[8]; foreach (var zn in Zones) area[(int)zn]++; double a2 = Cell * Cell;
        var cnt = new int[8]; double sSum = 0, rSum = 0;
        foreach (var l in Lots) { cnt[(int)l.zone]++; sSum += l.suit; rSum += l.dRoad; }
        var clusters = new HashSet<int>(); foreach (var l in Lots) if (l.zone == Zone.Hamlet) clusters.Add(l.group);
        int bad = 0, over = 0, far = 0;
        for (int i = 0; i < Lots.Count; i++)
        {
            var l = Lots[i]; int k = K(l.x, l.z);
            if (!WorldField.IsLand(l.x, l.z) || WorldField.IsCliff(l.x, l.z) || WorldField.SlopeDeg(l.x, l.z) > 10 || DFresh[k] < 5f || DBridge[k] < 8f) bad++;
            if (l.dRoad > 32f || l.dRoad < 3.5f) far++;
            for (int j = i + 1; j < Lots.Count; j++)
            { var o = Lots[j]; double dx = o.x - l.x, dz = o.z - l.z; if (dx * dx + dz * dz < System.Math.Pow(System.Math.Max(System.Math.Max(l.w, l.d), System.Math.Max(o.w, o.d)) * 0.9, 2)) over++; }
        }
        foreach (var z in Key) { if (area[(int)z] > 0) pass++; else fails.Add("zone " + z + " has no area"); }
        int[] min = { 4, 8, 3, 1 };
        for (int q = 0; q < 4; q++) { if (cnt[(int)Key[q]] >= min[q]) pass++; else fails.Add(Key[q] + " lots " + cnt[(int)Key[q]] + " < " + min[q]); }
        if (clusters.Count >= 2) pass++; else fails.Add("hamlet clusters " + clusters.Count + " < 2");
        if (Lots.Count >= 24) pass++; else fails.Add("total lots " + Lots.Count + " < 24");
        if (bad == 0) pass++; else fails.Add(bad + " lots on water/cliff/steep/bridge");
        if (over == 0) pass++; else fails.Add(over + " overlapping lot pairs");
        if (far == 0) pass++; else fails.Add(far + " lots too far from / on road");
        int nl = System.Math.Max(1, Lots.Count);
        sb.AppendLine("ZONES m2: Harbour " + (int)(area[5] * a2) + " | Hamlet " + (int)(area[6] * a2) + " (" + Hamlets.Count + " seeds) | Farm " + (int)(area[4] * a2) + " | Summit " + (int)(area[7] * a2) + " | Wild " + (int)(area[3] * a2) + " | Cliff " + (int)(area[2] * a2));
        sb.AppendLine("LOTS: Harbour " + cnt[5] + " | Hamlet " + cnt[6] + " (" + clusters.Count + " clusters) | Farm " + cnt[4] + " | Summit " + cnt[7] + " | total " + Lots.Count + " | mean suit " + (sSum / nl).ToString("F2") + " | mean road dist " + (rSum / nl).ToString("F1") + " m");
        sb.AppendLine("RULES: " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        legend = "zones: harbour " + cnt[5] + " hamlet " + cnt[6] + " farm " + cnt[4] + " summit " + cnt[7] + " lots | rules " + pass + "/" + (pass + fails.Count) + " (sea blue, water cyan, cliff brown, wild green, farm lime, harbour orange, hamlet red, summit violet)";
        return Report;
    }

    static Color Palette(Zone z)
    {
        switch (z)
        {
            case Zone.Sea: return new Color(0.1f, 0.25f, 0.5f);
            case Zone.Water: return new Color(0.25f, 0.75f, 0.95f);
            case Zone.Cliff: return new Color(0.45f, 0.32f, 0.3f);
            case Zone.Farm: return new Color(0.78f, 0.8f, 0.3f);
            case Zone.Harbour: return new Color(0.98f, 0.6f, 0.2f);
            case Zone.Hamlet: return new Color(0.92f, 0.25f, 0.35f);
            case Zone.Summit: return new Color(0.75f, 0.6f, 0.98f);
            default: return new Color(0.15f, 0.36f, 0.2f);
        }
    }

    public static Color ColorAt(int k)
    {
        if (!Built) Build();
        var c = Palette(Zones[k]) * (Zones[k] == Zone.Sea ? 1f : shade[k]); c.a = 1f; return c;
    }

    /// <summary>Overlays roads (grey), bridges (cyan) and lot markers (white with zone-coloured core) on the overlay pixels (top = +z).</summary>
    public static void Paint(Color[] px)
    {
        for (int j = 0; j < G; j++) for (int i = 0; i < G; i++)
            { int k = j * G + i; if (crossM[k]) px[(G - 1 - j) * G + i] = Color.cyan; else if (roadM[k]) px[(G - 1 - j) * G + i] = new Color(0.9f, 0.9f, 0.85f); }
        foreach (var l in Lots)
        {
            int ci = Ix(l.x), cj = Ix(l.z);
            for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++)
                {
                    int i = ci + di, j = cj + dj; if (i < 0 || j < 0 || i >= G || j >= G) continue;
                    px[(G - 1 - j) * G + i] = (System.Math.Abs(di) < 2 && System.Math.Abs(dj) < 2) ? Palette(l.zone) : Color.white;
                }
        }
    }

    public static void DrawGizmos()
    {
        if (!Built) return;
        foreach (var l in Lots)
        {
            Vector3 c = WorldField.ToWorld(l.x, l.z), f = WorldField.ToWorld(l.x + l.fx, l.z + l.fz) - c; f.y = 0;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
            Gizmos.matrix = Matrix4x4.TRS(c + Vector3.up * 1.2f, Quaternion.LookRotation(f), Vector3.one);
            var col = Palette(l.zone); Gizmos.color = new Color(col.r, col.g, col.b, 0.28f); Gizmos.DrawCube(Vector3.zero, new Vector3(l.w, 2.4f, l.d));
            Gizmos.color = col; Gizmos.DrawWireCube(Vector3.zero, new Vector3(l.w, 2.4f, l.d));
            Gizmos.color = Color.white; Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0, l.d * 0.5f + 2.5f));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

#if UNITY_EDITOR
    [MenuItem("Island/Phase 3.9 Zones + Validate")]
    static void Menu() { Build(); Debug.Log(Report); }
#endif
}
