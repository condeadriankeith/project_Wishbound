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
    public struct Tier { public double cx, cz, R, D, W, th; }
    public static readonly Tier[] Tiers =
    {
        new Tier{ cx= 0, cz=40, R=240, D=75, W=42, th=0.35 },
        new Tier{ cx=10, cz=35, R=165, D=85, W=36, th=4.10 },
        new Tier{ cx=25, cz=30, R= 98, D=80, W=26, th=1.85 },
        new Tier{ cx=35, cz=25, R= 44, D=95, W=16, th=5.40 },
    };
    public static double Re(Tier t, double a) =>
        t.R * (1 + 0.05 * Math.Sin(a * 3 + t.th) + 0.025 * Math.Sin(a * 5 + 1.2)) + 3.2 * Math.Sin(a * 17 + t.R);
    public static double H(double x, double z)
    {
        double r0 = Math.Sqrt(x * x + z * z);
        double a0 = Math.Atan2(x, -z);
        double R0 = 360 + 24 * Math.Sin(a0 * 2 + 1.4) + 16 * Math.Sin(a0 * 3 + 2.1) + 9 * Math.Sin(a0 * 7);
        double h = -6 - 16 * Sm(R0 + 25, R0 + 65, r0) + 12 * Sm(R0 + 20, R0 - 50, r0)
                   + 6 * Sm(350, 240, Math.Sqrt(x * x + (z - 40) * (z - 40)));
        foreach (var t in Tiers)
        {
            double dx = x - t.cx, dz = z - t.cz;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            double a = Math.Atan2(dx, -dz);
            double d = DA(a, t.th);
            double rampW = 4.5 + t.W * Math.Exp(-(d * d) / 0.22);
            double rB = Re(t, a);
            double q = Cl(0, 1, (rB + rampW - dist) / (2 * rampW));
            double cliff = Math.Pow(q, 3.2) * 0.45 + Sm(0.15, 0.9, q) * 0.55;
            h += t.D * cliff;
        }
        double townDist = Math.Sqrt((x - 25) * (x - 25) + (z + 35) * (z + 35));
        h -= 12 * Sm(120, 20, townDist);
        double sd = Math.Sqrt((x - SPIRE_X) * (x - SPIRE_X) + (z - SPIRE_Z) * (z - SPIRE_Z));
        if (sd < SPIRE_RAD + 20)
        {
            double sF = Sm(SPIRE_RAD + 12, SPIRE_RAD - 3, sd);
            double pillar = SPIRE_TOP_Y + (Vn(x * 0.15, z * 0.15) - 0.5) * 4;
            h = Math.Max(h, pillar * sF);
        }
        double micro = (Fbm(x * 0.035 + 5.2, z * 0.035) - 0.5) * 5.5 + (Vn(x * 0.12, z * 0.12) - 0.5) * 1.5;
        h += micro * Sm(2.5, 12, h);
        return h;
    }
    public static float Hf(double x, double z) => (float)H(x, z);
    public static Vector3 NV(double x, double z)
    {
        const double e = 1.4;
        return new Vector3(
            (float)(-(H(x + e, z) - H(x - e, z)) / (2 * e)), 1f,
            (float)(-(H(x, z + e) - H(x, z - e)) / (2 * e))).normalized;
    }
}
