using System;
using UnityEngine;

/// <summary>
/// Phase 3.7 — the terrain-flattening term composed into TerrainMath.H (like RiverCarve).
/// Holds a signed adjustment raster (metres to ADD to the natural carved height) plus it is the lazy
/// hook that builds the whole road grading on the first H() call. While building it returns the height
/// unchanged, so the grading pass can sample the natural terrain without recursing.
/// </summary>
public static class RoadGrade
{
    public const double Res = 1.0;          // grading raster resolution (m)
    const double E = TerrainMath.E;

    public static float[] Fill;             // signed metres to add to natural carved H (bilinear)
    public static float[] Mask;             // 1 inside the running corridor -> 0 at the outer shoulder
    public static int NR;
    public static int Hash;
    static bool building;

    public static double Apply(double x, double z, double h)
    {
        if (Fill == null) { if (building) return h; Build(1337); }
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return h;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Fill[k] + (Fill[k + 1] - Fill[k]) * tx, b = Fill[k + NR] + (Fill[k + NR + 1] - Fill[k + NR]) * tx;
        return h + (a + (b - a) * tz);
    }

    /// <summary>1 on the graded road bench, fading to 0 across the shoulder. IslandBuilder protects these
    /// cells from cliff-smoothing so the flattening (and the road's no-clip guarantee) survives to the mesh.</summary>
    public static double MaskAt(double x, double z)
    {
        if (Mask == null) return 0;
        double fx = (x + E) / Res, fz = (z + E) / Res;
        if (fx < 0 || fz < 0 || fx >= NR - 1 || fz >= NR - 1) return 0;
        int i = (int)fx, j = (int)fz, k = j * NR + i; float tx = (float)(fx - i), tz = (float)(fz - j);
        float a = Mask[k] + (Mask[k + 1] - Mask[k]) * tx, b = Mask[k + NR] + (Mask[k + NR + 1] - Mask[k + NR]) * tx;
        return a + (b - a) * tz;
    }

    public static void Build(int seed = 1337)
    {
        building = true;
        try { RoadBuild.Build(seed); }
        finally { building = false; }
    }
}
