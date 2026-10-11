using UnityEngine;
using static TerrainMath;

public enum SurfaceType : byte { Water, Sand, Grass, Field, Dirt, DirtPath, Scree, CliffWall }

/// <summary>
/// One place that decides what the ground IS. Terrain painting (IslandBuilder), grass planting
/// (IslandStylizedGrass), and later roads / lots / props all ask this, so colours and plants always agree.
/// Source (x, z) coordinates, like TerrainMath.
/// </summary>
public static class Surface
{
    public static double CliffWeight(double ny) => 1.0 - Sm(0.58, 0.82, ny);            // 0 gentle .. 1 chalk wall
    public static double SandWeight(double h, double cliffW) => Sm(3.2, 1.1, h) * (1 - cliffW);

    /// <summary>-1 = no field here; else palette index (3 = bare brown dirt, others = crops / meadow patches).</summary>
    public static int FieldIndex(double x, double z, double h, double ny)
    {
        if (!(h > 6 && ny > 0.85)) return -1;
        double fx = System.Math.Floor((x + 200) / 36), fz = System.Math.Floor((z + 200) / 30);
        double hs = System.Math.Sin(fx * 127.1 + fz * 311.7) * 43758.5453, q = hs - System.Math.Floor(hs);
        return q > 0.32 ? (int)System.Math.Floor(q * 13) % 5 : -1;
    }

    public static double PathAt(double x, double z)
    {
        var ib = IslandBuilder.Instance; var pm = ib != null ? ib.PathMap : null;
        if (pm == null) return 0;
        int vi = Mathf.Clamp(Mathf.RoundToInt((float)((x + E) / S)), 0, N);
        int vj = Mathf.Clamp(Mathf.RoundToInt((float)((z + E) / S)), 0, N);
        return pm[vj * V + vi];
    }

    public static SurfaceType Classify(double x, double z, double h, double ny, double path)
    {
        if (h <= 0.3) return SurfaceType.Water;
        double cw = CliffWeight(ny);
        if (cw > 0.5) return SurfaceType.CliffWall;            // ny < ~0.70
        if (ny < 0.85) return SurfaceType.Scree;               // steep but not a wall: rock / scree, no plants
        if (path > 0.5 && h > 1.2) return SurfaceType.DirtPath;
        if (SandWeight(h, cw) > 0.5) return SurfaceType.Sand;
        int f = FieldIndex(x, z, h, ny);
        if (f == 3) return SurfaceType.Dirt;
        if (f >= 0) return SurfaceType.Field;
        return SurfaceType.Grass;
    }

    public static SurfaceType At(double x, double z) => Classify(x, z, H(x, z), NV(x, z).y, PathAt(x, z));

    /// <summary>0..1 planting probability. Zero on anything that is not living ground; feathers at sand, steep ground and paths.</summary>
    public static double GrassDensity(SurfaceType t, double h, double ny, double path)
    {
        double d;
        if (t == SurfaceType.Grass) d = 1; else if (t == SurfaceType.Field) d = 0.35; else return 0;
        d *= Sm(0.80, 0.88, ny);          // thin out toward steep ground
        d *= Sm(2.8, 4.5, h);             // feather in above the sand line (dune grass)
        d *= 1 - path;                    // thin along paths
        return d;
    }

    public static bool GrassAllowed(double x, double z) { double h = H(x, z), ny = NV(x, z).y, p = PathAt(x, z); return GrassDensity(Classify(x, z, h, ny, p), h, ny, p) > 0; }
}
