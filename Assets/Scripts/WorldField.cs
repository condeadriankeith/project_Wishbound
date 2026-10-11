using System;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Single query layer for all world systems (rivers, roads, zoning, props).
/// Works in SOURCE coordinates (x, z) like TerrainMath.H; Unity world z = -z (MirrorZ).
/// Swap the terrain later by changing only Height().
/// </summary>
public static class WorldField
{
    public const double SeaLevel = 0.0;
    public const double CliffSlopeDeg = 38.0;   // above = cliff (no roads/lots/grass)
    public const double FlatSlopeDeg = 6.0;     // below = buildable
    public const int G = 256;                   // coarse cache grid
    public static readonly double Cell = 2 * E / (G - 1);

    public static float[] HeightGrid, SlopeGrid, SeaDist;   // SeaDist: metres inland, 0 in sea
    public static float MaxHeight, MaxSeaDist;
    static bool built;

    public static double Height(double x, double z) => H(x, z);
    public static bool IsLand(double x, double z) => H(x, z) > SeaLevel;
    public static double SlopeDeg(double x, double z) =>
        Math.Acos(Math.Min(1.0, NV(x, z).y)) * (180.0 / Math.PI);
    public static bool IsCliff(double x, double z) => SlopeDeg(x, z) > CliffSlopeDeg;
    public static bool IsFlat(double x, double z) => SlopeDeg(x, z) < FlatSlopeDeg;
    /// <summary>0 = gentle, 1 = full cliff (smooth ramp 28..38 deg).</summary>
    public static double CliffMask(double x, double z) => Sm(CliffSlopeDeg - 10, CliffSlopeDeg, SlopeDeg(x, z));

    public static double DistToSea(double x, double z)
    {
        Build();
        double fi = (x + E) / Cell, fj = (z + E) / Cell;
        if (fi < 0 || fj < 0 || fi > G - 1 || fj > G - 1) return 0;
        int i = Math.Min(G - 2, (int)fi), j = Math.Min(G - 2, (int)fj);
        float tx = (float)(fi - i), tz = (float)(fj - j);
        float a = SeaDist[j * G + i], b = SeaDist[j * G + i + 1];
        float c = SeaDist[(j + 1) * G + i], d = SeaDist[(j + 1) * G + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    public static void Build()
    {
        if (built) return;
        bool riv = RiversOn; RiversOn = false;   // grids stay RAW (flow network depends on them)
        int n = G * G;
        HeightGrid = new float[n]; SlopeGrid = new float[n]; SeaDist = new float[n];
        MaxHeight = 0;
        for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                double x = -E + i * Cell, z = -E + j * Cell;
                float h = (float)H(x, z);
                int k = j * G + i;
                HeightGrid[k] = h;
                SlopeGrid[k] = (float)SlopeDeg(x, z);
                if (h > MaxHeight) MaxHeight = h;
                SeaDist[k] = h > SeaLevel ? 1e9f : 0f;
            }
        // two-pass chamfer distance transform (metres)
        float w1 = (float)Cell, w2 = w1 * 1.41421356f;
        for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                int k = j * G + i; float v = SeaDist[k];
                if (i > 0) v = Mathf.Min(v, SeaDist[k - 1] + w1);
                if (j > 0) { v = Mathf.Min(v, SeaDist[k - G] + w1);
                    if (i > 0) v = Mathf.Min(v, SeaDist[k - G - 1] + w2);
                    if (i < G - 1) v = Mathf.Min(v, SeaDist[k - G + 1] + w2); }
                SeaDist[k] = v;
            }
        MaxSeaDist = 0;
        for (int j = G - 1; j >= 0; j--)
            for (int i = G - 1; i >= 0; i--)
            {
                int k = j * G + i; float v = SeaDist[k];
                if (i < G - 1) v = Mathf.Min(v, SeaDist[k + 1] + w1);
                if (j < G - 1) { v = Mathf.Min(v, SeaDist[k + G] + w1);
                    if (i < G - 1) v = Mathf.Min(v, SeaDist[k + G + 1] + w2);
                    if (i > 0) v = Mathf.Min(v, SeaDist[k + G - 1] + w2); }
                // domain border counts as sea
                if (i == 0 || j == 0 || i == G - 1 || j == G - 1) v = 0;
                SeaDist[k] = v; if (v < 1e8f && v > MaxSeaDist) MaxSeaDist = v;
            }
        built = true; RiversOn = riv;
    }

    public static string SelfTest()
    {
        Build();
        int land = 0, cliff = 0, flat = 0; float maxSlope = 0, minH = 1e9f;
        for (int k = 0; k < G * G; k++)
        {
            minH = Mathf.Min(minH, HeightGrid[k]);
            if (HeightGrid[k] <= SeaLevel) continue;
            land++;
            if (SlopeGrid[k] > CliffSlopeDeg) cliff++;
            if (SlopeGrid[k] < FlatSlopeDeg) flat++;
            maxSlope = Mathf.Max(maxSlope, SlopeGrid[k]);
        }
        bool det = H(12.3, -45.6) == H(12.3, -45.6) && Math.Abs(DistToSea(0, 0) - DistToSea(0, 0)) < 1e-9;
        int mid = G / 2 * G + G / 2;
        bool nodeOk = Math.Abs(DistToSea(-E + (G / 2) * Cell, -E + (G / 2) * Cell) - SeaDist[mid]) < 0.01;
        return $"[WorldField] H {minH:F1}..{MaxHeight:F1} m | land {100f * land / (G * G):F1}% | cliff {100f * cliff / Mathf.Max(1, land):F1}% of land | flat<{FlatSlopeDeg}deg {100f * flat / Mathf.Max(1, land):F1}% | maxSlope {maxSlope:F0}deg | maxSeaDist {MaxSeaDist:F0} m | deterministic {det} | gridMatch {nodeOk}";
    }

    // Source -> Unity world and back
    public static Vector3 ToWorld(double x, double z) => U(x, H(x, z), z);
    public static void FromWorld(Vector3 p, out double x, out double z) { x = p.x; z = MirrorZ ? -p.z : p.z; }
}
