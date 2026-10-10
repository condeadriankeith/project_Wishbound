using System;
using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

public static class Scatter
{
    public static System.Random Rng = new System.Random(1234);
    public static double R() => Rng.NextDouble();
    public static void Reseed(int seed) { Rng = new System.Random(seed); }

    public static List<(double x, double z, double h)> Pick(int n, Func<double, double, double, bool> ok)
    {
        var list = new List<(double, double, double)>();
        for (int g = 0; list.Count < n && g < n * 80; g++)
        {
            double x = (R() * 2 - 1) * (E * 0.85), z = (R() * 2 - 1) * (E * 0.85);
            double h = H(x, z);
            if (ok(x, z, h)) list.Add((x, z, h));
        }
        return list;
    }

    public static Color Hex(uint hex)
    {
        return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);
    }
}

public static class Roads
{
    public static double Dz0 = -220;
    public static List<double[]> Points = new List<double[]>();

    static double[] PP(Tier t, double r, double a) =>
        new[] { t.cx + r * Math.Sin(a), t.cz - r * Math.Cos(a) };

    static void AddSeg(double[] a, double[] b)
    {
        double dx = b[0] - a[0], dz = b[1] - a[1];
        int n = Mathf.CeilToInt((float)(Math.Sqrt(dx * dx + dz * dz) / 1.8));
        n = Math.Max(n, 1);
        for (int i = 0; i <= n; i++)
            Points.Add(new[] { a[0] + dx * i / n, a[1] + dz * i / n });
    }

    public static float[] BuildPathMap()
    {
        Points.Clear();
        Dz0 = -220;
        int guard = 0;
        while (H(0, Dz0) > 0.6 && Dz0 > -360 && guard++ < 500) Dz0--;

        AddSeg(new[] { 0.0, Dz0 + 8 }, new[] { 15.0, -180.0 });
        AddSeg(new[] { 15.0, -180.0 }, PP(Tiers[0], Tiers[0].R + Tiers[0].W + 8, Tiers[0].th));

        for (int i = 0; i < Tiers.Length; i++)
        {
            var t = Tiers[i];
            AddSeg(PP(t, t.R + t.W + 6, t.th), PP(t, t.R - t.W - 6, t.th));
            if (i + 1 >= Tiers.Length) continue;
            var n = Tiers[i + 1];
            double d = ((n.th - t.th) % P2 + P2) % P2;
            double r0 = t.R - t.W - 6, r1 = n.R + n.W + 6;
            for (int k = 0; k <= 320; k++)
            {
                double u = k / 320.0, a = t.th + d * u;
                double cx = t.cx + (n.cx - t.cx) * u, cz = t.cz + (n.cz - t.cz) * u;
                double r = r0 + (r1 - r0) * u;
                Points.Add(new[] { cx + r * Math.Sin(a), cz - r * Math.Cos(a) });
            }
        }

        var pm = new float[V * V];
        foreach (var p in Points)
        {
            int ci = Mathf.RoundToInt((float)((p[0] + E) / S)), cj = Mathf.RoundToInt((float)((p[1] + E) / S));
            for (int j = cj - 4; j <= cj + 4; j++)
                for (int i = ci - 4; i <= ci + 4; i++)
                {
                    if (i < 0 || j < 0 || i >= V || j >= V) continue;
                    double dx = -E + i * S - p[0], dz = -E + j * S - p[1];
                    float w = (float)Sm(5.5, 2.5, Math.Sqrt(dx * dx + dz * dz));
                    if (w > pm[j * V + i]) pm[j * V + i] = w;
                }
        }
        return pm;
    }

    public static float At(float[] pm, double x, double z)
    {
        if (pm == null) return 0f;
        int i = Mathf.Clamp(Mathf.RoundToInt((float)((x + E) / S)), 0, N);
        int j = Mathf.Clamp(Mathf.RoundToInt((float)((z + E) / S)), 0, N);
        return pm[j * V + i];
    }
}
