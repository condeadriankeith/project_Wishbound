using System;
using UnityEngine;

public static class TerrainMath
{
    public static bool MirrorZ = true;
    public const double E = 460;
    public const int N = 340;
    public const int V = N + 1;
    public const double S = 2 * E / N;
    public const double P2 = Math.PI * 2;
    public const double SPIRE_X = -175, SPIRE_Z = -135, SPIRE_RAD = 24, SPIRE_TOP_Y = 178;

    public static Vector3 U(double x, double y, double z) =>
        new Vector3((float)x, (float)y, (float)(MirrorZ ? -z : z));
    public static Vector3 U(Vector3 p) => U(p.x, p.y, p.z);
    public static float YawToUnity(double w) => (float)(-w * Mathf.Rad2Deg);

    public static double Cl(double a, double b, double x) => Math.Min(b, Math.Max(a, x));
    public static double Sm(double a, double b, double x)
    {
        double t = Cl(0, 1, (x - a) / (b - a));
        return t * t * (3 - 2 * t);
    }
    static double Hh(double x, double y)
    {
        double s = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453;
        return s - Math.Floor(s);
    }
    public static double Vn(double x, double y)
    {
        double i = Math.Floor(x), j = Math.Floor(y);
        double fx = x - i, fy = y - j;
        double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        return (Hh(i, j) * (1 - u) + Hh(i + 1, j) * u) * (1 - v) +
               (Hh(i, j + 1) * (1 - u) + Hh(i + 1, j + 1) * u) * v;
    }
    public static double Fbm(double x, double y) =>
        Vn(x, y) * 0.5 + Vn(x * 2.1, y * 2.1) * 0.28 + Vn(x * 4.3, y * 4.3) * 0.14 + Vn(x * 8.7, y * 8.7) * 0.08;
    public static double DA(double a, double b)
    {
        double d = Math.Abs(a - b) % P2;
        return d > Math.PI ? P2 - d : d;
    }
    public struct Tier { public double cx, cz, R, D, W, th, sweep; }  // W = ledge width, th = ramp start angle, sweep = ramp arc (rad)
    public static readonly Tier[] Tiers =
    {
        // Spiral: ramp t starts where ramp t-1 ends (th_t = th_{t-1} + sweep_{t-1} + ~0.1), all winding the same way.
        new Tier{ cx= 0, cz=15, R=285, D=75, W=58, th=0.35, sweep=4.40 },
        new Tier{ cx= 6, cz=12, R=205, D=85, W=48, th=4.85, sweep=5.00 },
        new Tier{ cx=12, cz=10, R=135, D=75, W=38, th=3.67, sweep=5.40 },
        new Tier{ cx=18, cz= 8, R= 75, D=60, W=34, th=2.89, sweep=5.80 },
    };
    static double Cs(double q) => q * q * q * (q * (q * 6 - 15) + 10);   // chalk-wall profile
    /// <summary>0..1 spiral progress: 0 at ramp start, rising over `sweep`, then 1 (full wall) until the radial end-wall.</summary>
    static double RampF(Tier t, double a)
    {
        const double w = 0.06;
        double p = ((a - t.th) % P2 + P2) % P2;
        if (p < t.sweep) { double u = p / t.sweep; return 0.92 * u + 0.08 * Sm(0, 1, u); }
        if (p < P2 - w) return 1;
        return 1 - Sm(0, 1, (p - (P2 - w)) / w);
    }
    public static double Re(Tier t, double a) =>
        t.R * (1 + 0.04 * Math.Sin(a * 3 + 0.7) + 0.025 * Math.Sin(a * 5 + 1.2)) + 3.2 * Math.Sin(a * 17 + t.R);
        // Beach lobes: push the shoreline outward. (cx,cz) lobe centre; radial = seaward axis, tangential = along the coast.
    public struct Lobe { public double cx, cz, ux, uz, rRad, rTan, peak; }
    public static readonly Lobe[] BeachLobes =
    {
        // seaward of the player's spot at src (164,-299): outward unit = (0.48,-0.88)
        // (blob lobe removed: the coast now widens through CoastBulge so land, beach and seabed move together)
    };
    static double LobeBlend(double x, double z, double h)
    {
        foreach (var l in BeachLobes)
        {
            double dx = x - l.cx, dz = z - l.cz;
            double dr = dx * l.ux + dz * l.uz, dt = -dx * l.uz + dz * l.ux;
            double t = Math.Sqrt((dr / l.rRad) * (dr / l.rRad) + (dt / l.rTan) * (dt / l.rTan));
            if (t > 1.3) continue;
            double target = -0.4 + (l.peak + 0.4) * (1 - Math.Pow(Math.Min(1, t), 1.6));   // gentle slope into the water
            double w = Sm(1.3, 0.8, t);                                                        // fade into the natural seabed
            if (target > h) h += (target - h) * w;
        }
        return h;
    }
    /// <summary>Widens the whole base (grass, beach, shelf) outward around bearing 0.55 rad (NE). Smooth, so no blobs or kinks.</summary>
    public static double CoastBulge(double a0)
    {
        double d = Math.Atan2(Math.Sin(a0 - 0.55), Math.Cos(a0 - 0.55));
        double w = Math.Abs(d) / 0.62;
        double b = w >= 1 ? 0 : 62 * Sm(1, 0, w);
        // Local extra widening (land + beach + shelf together) around bearing 0.118 (source x=45.8, z=-387): smooth, fades into the original coast
        double d2 = Math.Atan2(Math.Sin(a0 - 0.118), Math.Cos(a0 - 0.118));
        double w2 = Math.Abs(d2) / 0.24;
        if (w2 < 1) b += 36 * Sm(1, 0, w2);
        return b;
    }
    public static bool RiversOn = true;
    public static double H(double x, double z) { double h = HRaw(x, z); return RiversOn ? RiverCarve.Apply(x, z, h) : h; }
    public static double HRaw(double x, double z)
    {
        double r0 = Math.Sqrt(x * x + z * z);
        double a0 = Math.Atan2(x, -z);
        double R0 = 360 + 24 * Math.Sin(a0 * 2 + 1.4) + 16 * Math.Sin(a0 * 3 + 2.1) + 9 * Math.Sin(a0 * 7) + CoastBulge(a0);
        double h = -6 - 16 * Sm(R0 + 25, R0 + 65, r0) + 12 * Sm(R0 + 20, R0 - 50, r0)
                   + 6 * Sm(350, 240, Math.Sqrt(x * x + (z - 40) * (z - 40)));
        double ledgeMax = 0;
        foreach (var t in Tiers)
        {
            double dx = x - t.cx, dz = z - t.cz;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            double a = Math.Atan2(dx, -dz);
            double Ro = Re(t, a), Ri = Ro - t.W, rw = 9.0;
            double f = RampF(t, a);
            double wo = Cs(Cl(0, 1, (Ro + rw - dist) / (2 * rw)));
            double wi = Cs(Cl(0, 1, (Ri + rw - dist) / (2 * rw)));
            // outer wall rises with f, inner wall falls with f: the ledge between them climbs the mountain
            h += t.D * (f * wo + (1 - f) * wi);
            double lm = Sm(Ri - 4, Ri + 4, dist) * (1 - Sm(Ro - 4, Ro + 4, dist));
            if (lm > ledgeMax) ledgeMax = lm;
        }
        double townDist = Math.Sqrt((x - 25) * (x - 25) + (z + 35) * (z + 35));
        // (old 12 m village dip removed: it bent the spiral ramps; settlements will flatten their own lots)
        double sd = Math.Sqrt((x - SPIRE_X) * (x - SPIRE_X) + (z - SPIRE_Z) * (z - SPIRE_Z));
        if (sd < SPIRE_RAD + 20)
        {
            double sF = Sm(SPIRE_RAD + 12, SPIRE_RAD - 3, sd);
            double pillar = SPIRE_TOP_Y + (Vn(x * 0.15, z * 0.15) - 0.5) * 4;
            h = Math.Max(h, pillar * sF);
        }
        h = LobeBlend(x, z, h);
        double micro = (Fbm(x * 0.035 + 5.2, z * 0.035) - 0.5) * 5.5 + (Vn(x * 0.12, z * 0.12) - 0.5) * 1.5;
        h += micro * Sm(2.5, 12, h) * (1 - 0.85 * ledgeMax);   // keep ramps smooth and readable
        return h;
    }
    public static float Hf(double x, double z) => (float)H(x, z);
    /// <summary>Height of the RENDERED island mesh: exact triangle-plane sampling over IslandBuilder.HGrid
    /// (which includes cliff smoothing), using the same diagonal as IslandBuilder's index buffer, so this
    /// matches visible pixels bit-for-bit. Anything placed on the terrain (trees, grass, rocks, water) must
    /// use this, not Hf: Hf is the analytic field and can differ by metres near smoothed/carved relief.</summary>
    public static float Hm(double x, double z)
    {
        var g = IslandBuilder.HGrid;
        if (g == null) return Hf(x, z);
        int n = V;
        double fx = (x + E) / S, fz = (z + E) / S;
        int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
        if (i < 0 || j < 0 || i >= n - 1 || j >= n - 1) return Hf(x, z);
        float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = g[j * n + i], b = g[j * n + i + 1], c = g[(j + 1) * n + i], d = g[(j + 1) * n + i + 1];
        // mesh triangles are (a,b,c) and (b,d,c): diagonal b-c, i.e. the tx+tz=1 line
        if (tx + tz <= 1f) return a + (b - a) * tx + (c - a) * tz;
        return d + (c - d) * (1f - tx) + (b - d) * (1f - tz);
    }
    public static Vector3 NV(double x, double z)
    {
        const double e = 1.4;
        return new Vector3(
            (float)(-(H(x + e, z) - H(x - e, z)) / (2 * e)), 1f,
            (float)(-(H(x, z + e) - H(x, z - e)) / (2 * e))).normalized;
    }
}
