using System.Collections.Generic;
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

    // --- RoadBuild awareness -------------------------------------------------------------------------
    // PathAt()/PathMap above come from the OLDER settlement-road system (Roads.BuildPathMap, baked once at
    // terrain build time) and never learned about RoadBuild's later, real graded roads (the ones with bridges,
    // grading, etc). Grass was planting right through those roads because of that gap. This rebuilds a cheap
    // flat list of road centreline segments + half-widths from RoadBuild once per terrain build and answers
    // 0..1 'on the road, fading at the shoulder' the same way PathAt does, so callers can blend both sources.
    struct RoadSeg { public double x0, z0, x1, z1, halfW; }
    static List<RoadSeg> roadSegs;
    static int roadSegsBuiltHash = int.MinValue;

    static void BuildRoadSegs()
    {
        roadSegs = new List<RoadSeg>();
        foreach (var r in RoadBuild.Roads)
            for (int i = 0; i < r.x.Length - 1; i++)
                roadSegs.Add(new RoadSeg { x0 = r.x[i], z0 = r.z[i], x1 = r.x[i + 1], z1 = r.z[i + 1], halfW = r.halfW[i] + Shoulder });
        roadSegsBuiltHash = RoadBuild.Hash;
    }

    // Extra feather beyond the graded running surface so grass doesn't plant in a hard line right at the edge.
    const double Shoulder = 1.5;

    /// <summary>0 clear of any RoadBuild road, 1 on/inside its running surface, feathering out across the
    /// shoulder in between. Independent of the legacy PathAt() above; callers that want both should combine
    /// them (e.g. Mathf.Max) rather than replace one with the other, since some decorative paths only exist
    /// in the legacy system.</summary>
    public static double RoadPathAt(double x, double z)
    {
        if (roadSegs == null || roadSegsBuiltHash != RoadBuild.Hash) BuildRoadSegs();
        double best = 0;
        foreach (var seg in roadSegs)
        {
            double vx = seg.x1 - seg.x0, vz = seg.z1 - seg.z0, l2 = vx * vx + vz * vz;
            double t = l2 > 1e-9 ? ((x - seg.x0) * vx + (z - seg.z0) * vz) / l2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double dx = x - (seg.x0 + t * vx), dz = z - (seg.z0 + t * vz);
            double d = System.Math.Sqrt(dx * dx + dz * dz);
            // full road halfW counts as fully "on path"; fades to 0 over one more Shoulder width beyond that
            double v = 1.0 - Sm(seg.halfW - Shoulder, seg.halfW + Shoulder, d);
            if (v > best) best = v;
        }
        return best;
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
        if (t == SurfaceType.Grass) d = 1; else if (t == SurfaceType.Field) d = 0.45; else return 0;
        d *= Sm(0.78, 0.88, ny);          // thin out toward steep ground (slightly wider band = fuller coverage)
        d *= Sm(2.4, 4.0, h);             // feather in above the sand line (dune grass), starts a touch sooner
        d *= 1 - path;                    // thin along the legacy settlement-road path map
        return d;
    }

    /// <summary>Like GrassDensity, but also thins out along RoadBuild's real graded roads (bridges, grading,
    /// etc - a separate system from the legacy PathAt map above). IslandStylizedGrass should call this instead
    /// of GrassDensity directly so grass doesn't grow through visible roads.</summary>
    public static double GrassDensityAt(double x, double z, SurfaceType t, double h, double ny, double path)
    {
        double d = GrassDensity(t, h, ny, path);
        if (d <= 0) return 0;
        d *= 1 - RoadPathAt(x, z);
        return d;
    }

    /// <summary>0..1 river/lake wetness at a point (see RiverCarve.WetAt): feathers grass out approaching the
    /// water's edge instead of letting it plant right up to (or past) the visible waterline. The height-based
    /// Water classification only trips at h&lt;=0.3, but the wet band painted on the banks extends several
    /// metres beyond that, so grass needs its own check here or it reads as growing in the river/lake.</summary>
    public static double WetSuppression(double x, double z)
    {
        double wet = RiverCarve.WetAt(x, z);
        return 1.0 - Sm(0.15, 0.55, wet);   // fully clear by wet=0.15, fully suppressed by wet=0.55
    }

    public static bool GrassAllowed(double x, double z) { double h = H(x, z), ny = NV(x, z).y, p = PathAt(x, z); return GrassDensity(Classify(x, z, h, ny, p), h, ny, p) > 0; }
}
