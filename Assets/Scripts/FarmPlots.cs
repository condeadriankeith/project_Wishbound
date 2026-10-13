using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 4.7 — farm plots. DATA ONLY: generates contour-aligned farm plots on suitable land,
/// assigns each to a household and crop type. No terrain modification here.
/// </summary>
public static class FarmPlots
{
    // ---- configuration ----
    public const double MaxSlope = 0.06;        // 6% slope limit for farming
    public const double MinPlotArea = 50.0;     // minimum plot area (m²)
    public const double MaxPlotArea = 500.0;    // maximum plot area (m²)
    public const double HouseWalkDist = 250.0;  // max walking distance from house to plot (m)
    public const double PlotSeparation = 5.0;   // minimum separation between plots (m)
    public const double KitchenGardenSize = 10.0; // kitchen garden area per household (m²)

    // ---- crop types ----
    public enum CropType { Wheat, Vines, Orchards, Pasture, Paddies, KitchenGarden }

    // ---- plot data ----
    public class FarmPlot
    {
        public int id;
        public List<Vector2> vertices;   // polygon vertices in xz space (clockwise)
        public double area;              // plot area (m²)
        public double avgSlope;          // average slope over plot
        public double aspect;            // average aspect (0=north, 0.5=east, 1=south, 1.5=west, 2=north)
        public double elevation;         // average elevation (m)
        public double distToHarbour;     // distance to harbour zone (m)
        public bool flatByChannel;       // true if flat ground near irrigation channel
        public CropType cropType;        // assigned crop type
        public int householdLot;         // owning house lot index (-1 for kitchen gardens)
        public bool isKitchenGarden;     // true if this is a household kitchen garden
        public int settlement;           // owning settlement index
    }

    public static readonly List<FarmPlot> Plots = new List<FarmPlot>();
    public static bool Built;
    public static string Report = "";
    static int Hash;

    // ---- main build ----
    public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (PadGrade.Fill == null) PadGrade.Build();
        if (LaneNet.Doors == null) LaneNet.Build();
        if (!WaterAccess.Built) WaterAccess.Build();
        FlowNet.Build(1337);
        RiverCarve.Build(1337);

        Plots.Clear();

        GenerateFarmLand();
        AssignPlotsToHouseholds();
        AssignCropTypes();
        AddKitchenGardens();

        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Plots.Count) * 16777619;
            foreach (var p in Plots)
            {
                hs = (hs ^ p.id) * 16777619;
                hs = (hs ^ (int)(p.area * 1000f)) * 16777619;
                foreach (var v in p.vertices)
                {
                    hs = (hs ^ (int)(v.x * 1000f)) * 16777619;
                    hs = (hs ^ (int)(v.y * 1000f)) * 16777619;
                }
            }
            Hash = hs;
        }

        Built = true;
        Validate();
    }

    // ---- generate candidate farm land and create plots ----
    static void GenerateFarmLand()
    {
        // Sample points across the map to find valid farm land
        const double sampleStep = 5.0; // m - balance between detail and performance
        int g = WorldField.G;
        double cell = WorldField.Cell;
        double worldSize = g * cell;

        // Use a grid of sample points
        List<Vector2> farmPoints = new List<Vector2>();
        for (int zi = 0; zi < g; zi += Mathf.CeilToInt((float)(sampleStep / cell)))
        {
            for (int xi = 0; xi < g; xi += Mathf.CeilToInt((float)(sampleStep / cell)))
            {
                double x = -E + xi * cell;
                double z = -E + zi * cell;

                if (IsValidFarmLand(x, z))
                {
                    farmPoints.Add(new Vector2((float)x, (float)z));
                }
            }
        }

        // Cluster nearby points to form plot seeds
        const double clusterRadius = 10.0; // m
        bool[] used = new bool[farmPoints.Count];

        for (int i = 0; i < farmPoints.Count; i++)
        {
            if (used[i]) continue;

            // Start a new plot seed
            List<Vector2> cluster = new List<Vector2> { farmPoints[i] };
            used[i] = true;

            // Find all nearby points
            for (int j = i + 1; j < farmPoints.Count; j++)
            {
                if (used[j]) continue;
                double dx = farmPoints[i].x - farmPoints[j].x;
                double dz = farmPoints[i].y - farmPoints[j].y;
                if (dx*dx + dz*dz < clusterRadius*clusterRadius)
                {
                    cluster.Add(farmPoints[j]);
                    used[j] = true;
                }
            }

            // If we have enough points, create a plot from this cluster
            if (cluster.Count >= 3)
            {
                CreatePlotFromPoints(cluster);
            }
        }
    }

    static bool IsValidFarmLand(double x, double z)
    {
        // Check basic terrain validity
        if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z)) return false;

        // Check slope
        int g = WorldField.G;
        double cell = WorldField.Cell;
        double sx = (WorldField.HeightGrid[((int)Math.Round((x + cell + E) / cell)) * g + ((int)Math.Round((z + E) / cell))] -
                     WorldField.HeightGrid[((int)Math.Round((x - cell + E) / cell)) * g + ((int)Math.Round((z + E) / cell))]) / (2 * cell);
        double sz = (WorldField.HeightGrid[((int)Math.Round((x + E) / cell)) * g + ((int)Math.Round((z + cell + E) / cell))] -
                     WorldField.HeightGrid[((int)Math.Round((x + E) / cell)) * g + ((int)Math.Round((z - cell + E) / cell))]) / (2 * cell);
        double slope = Math.Sqrt(sx*sx + sz*sz);
        if (slope > MaxSlope) return false;

        // Check distance to roads
        if (RoadGrade.MaskAt(x, z) > 0.1) return false;

        // Check distance to existing lots (from SettlementMap)
        foreach (var lot in SettlementMap.Lots)
        {
            if (lot.dropped) continue;
            double dx = lot.x - x;
            double dz = lot.z - z;
            double dist = Math.Sqrt(dx*dx + dz*dz);
            if (dist < lot.footprintW * 0.6) return false; // use footprint radius
        }

        // Check distance to water (rivers, lakes, seas)
        FlowNet.Build(1337); // ensure built
        {
            int gx = WorldField.G;
            int cellIx = Mathf.Clamp(Mathf.RoundToInt((float)((x + E) / WorldField.Cell)), 0, gx - 1);
            int cellIz = Mathf.Clamp(Mathf.RoundToInt((float)((z + E) / WorldField.Cell)), 0, gx - 1);
            int k = cellIz * gx + cellIx;
            if (FlowNet.Sea[k] || FlowNet.InLake[k]) return false;
        }

        // Additional water check from RiverCarve wetness
        if (RiverCarve.WetAt(x, z) > 0.1) return false;

        return true;
    }

    static void CreatePlotFromPoints(List<Vector2> seedPoints)
    {
        // Compute centroid of seed points
        double cx = 0, cz = 0;
        foreach (var p in seedPoints) { cx += p.x; cz += p.y; }
        cx /= seedPoints.Count;
        cz /= seedPoints.Count;

        // Compute average properties over the seed area
        double totalSlope = 0, totalElev = 0, totalWeight = 0;
        double harboursX = 0, harboursZ = 0, harbourCount = 0;
        bool flatByChannel = false;
        int g = WorldField.G;
        double cell = WorldField.Cell;

        foreach (var p in seedPoints)
        {
            double h = WorldField.Height(p.x, p.y);
            double areaWeight = 1.0; // could weight by contour length etc.
            totalElev += h * areaWeight;

            // Slope
            double sx = (WorldField.HeightGrid[((int)Math.Round((p.x + cell + E) / cell)) * g + ((int)Math.Round((p.y + E) / cell))] -
                         WorldField.HeightGrid[((int)Math.Round((p.x - cell + E) / cell)) * g + ((int)Math.Round((p.y + E) / cell))]) / (2 * cell);
            double sz = (WorldField.HeightGrid[((int)Math.Round((p.x + E) / cell)) * g + ((int)Math.Round((p.y + cell + E) / cell))] -
                         WorldField.HeightGrid[((int)Math.Round((p.x + E) / cell)) * g + ((int)Math.Round((p.y - cell + E) / cell))]) / (2 * cell);
            totalSlope += Math.Sqrt(sx*sx + sz*sz) * areaWeight;

            // Check proximity to harbour (for orchards)
            foreach (var lot in SettlementMap.Lots)
            {
                if (lot.dropped) continue;
                if (lot.footprintW > 0 && lot.footprintD > 0) // only check lots with valid footprints
                {
                    // Find the settlement for this lot to get its zone
                    int settlementIdx = lot.settlement;
                    if (settlementIdx >= 0 && settlementIdx < SettlementMap.Settlements.Count)
                    {
                        var settlement = SettlementMap.Settlements[settlementIdx];
                        if (settlement.role == Zone.Harbour)
                        {
                            double dx = lot.x - p.x;
                            double dz = lot.z - p.y;
                            harboursX += lot.x * areaWeight;
                            harboursZ += lot.z * areaWeight;
                            harbourCount += areaWeight;
                        }
                    }
                }
            }

            // Check if flat ground near irrigation channel (for paddies)
            foreach (var channel in WaterAccess.Channels)
            {
                for (int i = 0; i < channel.x.Length; i++)
                {
                    double dx = channel.x[i] - p.x;
                    double dz = channel.z[i] - p.y;
                    double dist = Math.Sqrt(dx*dx + dz*dz);
                    if (dist < 5.0) // near channel
                    {
                        double h1 = WorldField.Height(channel.x[i], channel.z[i]);
                        double h2 = WorldField.Height(p.x, p.y);
                        if (Math.Abs(h2 - h1) < 0.2) // flat
                        {
                            flatByChannel = true;
                        }
                    }
                }
            }

            totalWeight += areaWeight;
        }

        if (totalWeight <= 0) return;

        double avgElev = totalElev / totalWeight;
        double avgSlope = totalSlope / totalWeight;
        double harbourCX = harbourCount > 0 ? harboursX / harbourCount : 0;
        double harbourCZ = harbourCount > 0 ? harboursZ / harbourCount : 0;
        double distToHarbour = harbourCount > 0 ?
            Math.Sqrt(Math.Pow(cx - harbourCX, 2) + Math.Pow(cz - harbourCZ, 2)) :
            double.MaxValue;

        // Generate plot polygon - simple approach: create a rounded rectangle
        // oriented roughly perpendicular to average slope aspect (contour-aligned)
        double plotSize = Math.Sqrt(Math.Min(Math.Max(MinPlotArea,
                         avgSlope < 0.02 ? MaxPlotArea : MinPlotArea + (avgSlope / MaxSlope) * (MaxPlotArea - MinPlotArea)),
                         MaxPlotArea));

        // Aspect: direction of steepest slope (we'll align plot perpendicular to this for contour alignment)
        double gradX = (WorldField.HeightGrid[((int)Math.Round((cx + cell + E) / cell)) * g + ((int)Math.Round((cz + E) / cell))] -
                        WorldField.HeightGrid[((int)Math.Round((cx - cell + E) / cell)) * g + ((int)Math.Round((cz + E) / cell))]) / (2 * cell);
        double gradZ = (WorldField.HeightGrid[((int)Math.Round((cx + E) / cell)) * g + ((int)Math.Round((cz + cell + E) / cell))] -
                        WorldField.HeightGrid[((int)Math.Round((cx + E) / cell)) * g + ((int)Math.Round((cz - cell + E) / cell))]) / (2 * cell);
        double gradLen = Math.Sqrt(gradX*gradX + gradZ*gradZ);
        double aspect = 0.5; // default north-facing
        if (gradLen > 1e-6)
        {
            // Aspect: 0=north, 0.5=east, 1=south, 1.5=west (clockwise from north)
            double angle = Math.Atan2(gradX, gradZ); // angle from north clockwise
            aspect = (angle / Math.PI) * 2.0; // convert to 0-2 range
            if (aspect < 0) aspect += 2.0;
            if (aspect >= 2.0) aspect -= 2.0;
        }

        // Create plot vertices (rounded rectangle aligned to contour)
        int numVertices = 8; // enough to approximate rounded shape
        List<Vector2> vertices = new List<Vector2>();
        double radius = plotSize / 2.0;

        for (int i = 0; i < numVertices; i++)
        {
            double angle = (2 * Math.PI * i) / numVertices;
            // Stretch perpendicular to slope direction for contour alignment
            double slopeAngle = Math.Atan2(gradZ, gradX); // angle of slope vector
            double contourAngle = slopeAngle + Math.PI/2; // perpendicular to slope

            double vx = radius * Math.Cos(angle + contourAngle);
            double vz = radius * Math.Sin(angle + contourAngle);

            // Add some noise for "wavy" appearance
            double noise = 0.1 * radius * (Math.Sin(i * 0.7) + Math.Cos(i * 1.3));
            vx += noise * Math.Cos(angle);
            vz += noise * Math.Sin(angle);

            vertices.Add(new Vector2((float)(cx + vx), (float)(cz + vz)));
        }

        // Validate the plot
        if (IsValidPlot(vertices))
        {
            Plots.Add(new FarmPlot
            {
                id = Plots.Count,
                vertices = vertices,
                area = CalculatePolygonArea(vertices),
                avgSlope = avgSlope,
                aspect = aspect,
                elevation = avgElev,
                distToHarbour = distToHarbour,
                flatByChannel = flatByChannel,
                cropType = CropType.Wheat, // temporary, will be assigned later
                householdLot = -1,
                isKitchenGarden = false,
                settlement = FindSettlementForPoint(cx, cz)
            });
        }
    }

    static bool IsValidPlot(List<Vector2> vertices)
    {
        // Check area
        double area = CalculatePolygonArea(vertices);
        if (area < MinPlotArea || area > MaxPlotArea) return false;

        // Check for self-intersection (simple check)
        if (IsSelfIntersecting(vertices)) return false;

        // Check distance to other plots
        foreach (var other in Plots)
        {
            if (PolygonsTooClose(vertices, other.vertices, PlotSeparation)) return false;
        }

        // Check overlap with forbidden areas (roads, water, lots, cliffs)
        // Sample points along edges and interior
        for (int edge = 0; edge < vertices.Count; edge++)
        {
            Vector2 v1 = vertices[edge];
            Vector2 v2 = vertices[(edge + 1) % vertices.Count];

            // Sample edge
            for (int t = 0; t <= 10; t++)
            {
                double f = t / 10.0;
                double x = v1.x + f * (v2.x - v1.x);
                double z = v1.y + f * (v2.y - v1.y);

                if (!IsPositionValidForPlot(x, z)) return false;
            }

            // Sample interior (centroid + some inward points)
            if (edge == 0)
            {
                double centroidX = 0, centroidZ = 0;
                foreach (var v in vertices) { centroidX += v.x; centroidZ += v.y; }
                centroidX /= vertices.Count;
                centroidZ /= vertices.Count;

                if (!IsPositionValidForPlot(centroidX, centroidZ)) return false;

                // Sample points inward from centroid toward each vertex
                foreach (var v in vertices)
                {
                    double dx = v.x - centroidX;
                    double dz = v.y - centroidZ;
                    double len = Math.Sqrt(dx*dx + dz*dz);
                    if (len > 1e-6)
                    {
                        double ix = centroidX + 0.3 * dx/len;
                        double iz = centroidZ + 0.3 * dz/len;
                        if (!IsPositionValidForPlot(ix, iz)) return false;
                    }
                }
            }
        }

        return true;
    }

    static bool IsPositionValidForPlot(double x, double z)
    {
        // Not water or cliff
        if (!WorldField.IsLand(x, z) || WorldField.IsCliff(x, z)) return false;

        // Not too steep
        int g = WorldField.G;
        double cell = WorldField.Cell;
        double sx = (WorldField.HeightGrid[((int)Math.Round((x + cell + E) / cell)) * g + ((int)Math.Round((z + E) / cell))] -
                     WorldField.HeightGrid[((int)Math.Round((x - cell + E) / cell)) * g + ((int)Math.Round((z + E) / cell))]) / (2 * cell);
        double sz = (WorldField.HeightGrid[((int)Math.Round((x + E) / cell)) * g + ((int)Math.Round((z + cell + E) / cell))] -
                     WorldField.HeightGrid[((int)Math.Round((x + E) / cell)) * g + ((int)Math.Round((z - cell + E) / cell))]) / (2 * cell);
        if (Math.Sqrt(sx*sx + sz*sz) > MaxSlope) return false;

        // Not on road
        if (RoadGrade.MaskAt(x, z) > 0.1) return false;

        // Not too close to existing lots
        foreach (var lot in SettlementMap.Lots)
        {
            if (lot.dropped) continue;
            if (lot.footprintW > 0 && lot.footprintD > 0) // only check lots with valid footprints
            {
                // Find the settlement for this lot to get its zone
                int settlementIdx = lot.settlement;
                if (settlementIdx >= 0 && settlementIdx < SettlementMap.Settlements.Count)
                {
                    var settlement = SettlementMap.Settlements[settlementIdx];
                    // Note: original code checked lot.footprintW * 0.5, but we don't have zone/y/lot on Massing
                    // Keeping the original distance check logic
                    double dx = lot.x - x;
                    double dz = lot.z - z;
                    double dist = Math.Sqrt(dx*dx + dz*dz);
                    if (dist < lot.footprintW * 0.5) return false;
                }
            }
        }

        // Not in water bodies
        FlowNet.Build(1337);
        {
            int gx = WorldField.G;
            int cellIx = Mathf.Clamp(Mathf.RoundToInt((float)((x + E) / WorldField.Cell)), 0, gx - 1);
            int cellIz = Mathf.Clamp(Mathf.RoundToInt((float)((z + E) / WorldField.Cell)), 0, gx - 1);
            int k = cellIz * gx + cellIx;
            if (FlowNet.Sea[k] || FlowNet.InLake[k] || RiverCarve.WetAt(x, z) > 0.1) return false;
        }

        return true;
    }

    static double CalculatePolygonArea(List<Vector2> vertices)
    {
        double area = 0;
        int j = vertices.Count - 1;
        for (int i = 0; i < vertices.Count; i++)
        {
            area += (vertices[j].x + vertices[i].x) * (vertices[j].y - vertices[i].y);
            j = i;
        }
        return Math.Abs(area) / 2.0;
    }

    static bool IsSelfIntersecting(List<Vector2> vertices)
    {
        // Simple check: no non-adjacent edges intersect
        for (int i = 0; i < vertices.Count; i++)
        {
            for (int j = i + 2; j < vertices.Count; j++)
            {
                if (j == vertices.Count - 1 && i == 0) continue; // first and last are adjacent
                if (LinesIntersect(vertices[i], vertices[(i+1)%vertices.Count],
                                 vertices[j], vertices[(j+1)%vertices.Count]))
                    return true;
            }
        }
        return false;
    }

    static bool LinesIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        // Check if line segments a1a2 and b1b2 intersect
        double denom = (b2.y - b1.y) * (a2.x - a1.x) - (b2.x - b1.x) * (a2.y - a1.y);
        if (Math.Abs(denom) < 1e-10) return false; // parallel

        double ua = ((b2.x - b1.x) * (a1.y - b1.y) - (b2.y - b1.y) * (a1.x - b1.x)) / denom;
        double ub = ((a2.x - a1.x) * (a1.y - b1.y) - (a2.y - a1.y) * (a1.x - b1.x)) / denom;

        return ua >= 0 && ua <= 1 && ub >= 0 && ub <= 1;
    }

    static bool PolygonsTooClose(List<Vector2> poly1, List<Vector2> poly2, double minDist)
    {
        // Check minimum distance between two polygons
        foreach (var v1 in poly1)
        {
            foreach (var v2 in poly2)
            {
                double dx = v1.x - v2.x;
                double dz = v1.y - v2.y;
                if (Math.Sqrt(dx*dx + dz*dz) < minDist) return true;
            }
        }
        return false;
    }

    static int FindSettlementForPoint(double x, double z)
    {
        for (int si = 0; si < SettlementMap.Settlements.Count; si++)
        {
            var s = SettlementMap.Settlements[si];
            // Use settlement center instead of first lot
            double dx = s.cx - x;
            double dz = s.cz - z;
            if (Math.Sqrt(dx*dx + dz*dz) < s.radius) return si;
        }
        return -1;
    }

    // ---- assign plots to nearest households ----
    static void AssignPlotsToHouseholds()
    {
        foreach (var plot in Plots)
        {
            if (plot.isKitchenGarden) continue; // skip kitchen gardens for now

            // Find nearest house (lot) within walking distance
            int bestLot = -1;
            double bestDist = HouseWalkDist + 1;

            // Use plot centroid for distance calculation
            double plotCX = 0, plotCZ = 0;
            foreach (var v in plot.vertices) { plotCX += v.x; plotCZ += v.y; }
            plotCX /= plot.vertices.Count;
            plotCZ /= plot.vertices.Count;

            for (int li = 0; li < SettlementMap.Lots.Length; li++)
            {
                var lot = SettlementMap.Lots[li];
                if (lot.dropped) continue;

                // Approximate walking distance - use straight line for now
                // TODO: proper path distance would use LaneNet for routed distance
                double dx = lot.x - plotCX;
                double dz = lot.z - plotCZ;
                double dist = Math.Sqrt(dx*dx + dz*dz);

                // Apply rough path factor (paths are rarely straight)
                double pathDist = dist * 1.3; // 30% extra for path winding

                if (pathDist < bestDist && pathDist <= HouseWalkDist)
                {
                    bestDist = pathDist;
                    bestLot = li;
                }
            }

            plot.householdLot = bestLot;
            if (bestLot >= 0)
            {
                plot.settlement = SettlementMap.Lots[bestLot].settlement;
            }

            // If no house found nearby, assign to nearest settlement anyway
            if (bestLot < 0)
            {
                plot.settlement = FindSettlementForPoint(plotCX, plotCZ);
            }
        }
    }

    // ---- assign crop types based on land characteristics ----
    static void AssignCropTypes()
    {
        foreach (var plot in Plots)
        {
            if (plot.isKitchenGarden)
            {
                plot.cropType = CropType.KitchenGarden;
                continue;
            }

            // Determine crop type based on land characteristics
            bool isSouthFacing = plot.aspect >= 0.75 && plot.aspect < 1.25; // roughly south-facing (0.75-1.25 of 0-2 range)
            bool isNearHarbour = plot.distToHarbour < 100.0; // within 100m of harbour
            bool isHighElevation = plot.elevation > WorldField.MaxHeight * 0.7; // upper 30% of elevation range
            bool isFlatByChannel = plot.flatByChannel;

            // Priority-based assignment
            if (isFlatByChannel)
            {
                plot.cropType = CropType.Paddies; // wet paddies
            }
            else if (isNearHarbour)
            {
                plot.cropType = CropType.Orchards; // orchards near harbour
            }
            else if (isHighElevation)
            {
                plot.cropType = CropType.Pasture; // pasture high up
            }
            else if (isSouthFacing)
            {
                plot.cropType = CropType.Vines; // vines on south-facing slopes
            }
            else
            {
                plot.cropType = CropType.Wheat; // wheat/default for dry shelves
            }
        }
    }

    // ---- add kitchen gardens for each household ----
    static void AddKitchenGardens()
    {
        // Track which households already have kitchen gardens assigned
        HashSet<int> householdsWithKG = new HashSet<int>();

        // First, assign kitchen gardens to households that have a main plot
        foreach (var plot in Plots)
        {
            if (!plot.isKitchenGarden && plot.householdLot >= 0)
            {
                int household = plot.householdLot;
                if (!householdsWithKG.Contains(household))
                {
                    // Create kitchen garden near the house
                    CreateKitchenGardenForHousehold(household);
                    householdsWithKG.Add(household);
                }
            }
        }

        // Ensure all households get a kitchen garden (even those without main plots)
        for (int i = 0; i < SettlementMap.Lots.Length; i++)
        {
            var lot = SettlementMap.Lots[i];
            if (lot.dropped) continue;
            int household = i; // lot index IS the household index in this context
            if (!householdsWithKG.Contains(household))
            {
                CreateKitchenGardenForHousehold(household);
                householdsWithKG.Add(household);
            }
        }
    }

    static void CreateKitchenGardenForHousehold(int householdLot)
    {
        var lot = SettlementMap.Lots[householdLot];
        if (lot.dropped) return;

        // Get house position and orientation
        var massing = SettlementMap.Lots[householdLot];
        double hx = Math.Sin(massing.yaw);
        double hz = Math.Cos(massing.yaw);

        // Place kitchen garden near house (back/side yard)
        double offsetDist = lot.footprintD * 0.5 + 2.0; // behind the house
        double gardenX = lot.x - hz * offsetDist; // behind (negative forward)
        double gardenZ = lot.z + hx * offsetDist; // slightly to the right

        // Create small circular/rounded plot
        double gardenRadius = Math.Sqrt(KitchenGardenSize / Math.PI);
        int numVertices = 6;
        List<Vector2> vertices = new List<Vector2>();

        for (int i = 0; i < numVertices; i++)
        {
            double angle = (2 * Math.PI * i) / numVertices;
            double vx = gardenRadius * Math.Cos(angle);
            double vz = gardenRadius * Math.Sin(angle);

            // Add slight irregularity
            double noise = 0.2 * gardenRadius * (Math.Sin(i * 0.5) + Math.Cos(i * 0.8));
            vx += noise * Math.Cos(angle);
            vz += noise * Math.Sin(angle);

            vertices.Add(new Vector2((float)(gardenX + vx), (float)(gardenZ + vz)));
        }

        if (IsValidPlot(vertices))
        {
            Plots.Add(new FarmPlot
            {
                id = Plots.Count,
                vertices = vertices,
                area = CalculatePolygonArea(vertices),
                avgSlope = 0, // will be calculated properly if needed
                aspect = 0,
                elevation = WorldField.Height(lot.x, lot.z),
                distToHarbour = 0,
                flatByChannel = false,
                cropType = CropType.KitchenGarden,
                householdLot = householdLot,
                isKitchenGarden = true,
                settlement = lot.settlement
            });
        }
    }

    // ---- validation ----
    public static string Validate()
    {
        if (!Built) Build();
        var sb = new StringBuilder("[FarmPlots 4.7]\n");
        var fails = new List<string>(); int pass = 0;

        int totalPlots = Plots.Count;
        int plotsWithoutHouse = 0;
        int invalidPlots = 0;
        double maxWalkDist = 0;
        int[] cropCounts = new int[Enum.GetValues(typeof(CropType)).Length];

        // Validate each plot
        foreach (var plot in Plots)
        {
            cropCounts[(int)plot.cropType]++;

            if (!IsValidPlot(plot.vertices))
            {
                invalidPlots++;
                continue;
            }

            // Check household assignment (skip kitchen gardens for this check)
            if (!plot.isKitchenGarden)
            {
                if (plot.householdLot < 0 || plot.householdLot >= SettlementMap.Lots.Length)
                {
                    plotsWithoutHouse++;
                }
                else
                {
                    var lot = SettlementMap.Lots[plot.householdLot];
                    if (lot.dropped) plotsWithoutHouse++;
                    else
                    {
                        // Calculate walking distance (approximate - should use LaneNet for proper path)
                        double plotCX = 0, plotCZ = 0;
                        foreach (var v in plot.vertices) { plotCX += v.x; plotCZ += v.y; }
                        plotCX /= plot.vertices.Count;
                        plotCZ /= plot.vertices.Count;

                        var houseMassing = SettlementMap.Lots[plot.householdLot];
                        double hx = Math.Sin(houseMassing.yaw);
                        double hz = Math.Cos(houseMassing.yaw);
                        double houseX = lot.x + hx * (lot.footprintW * 0.5 + LaneNet.DoorOffset);
                        double houseZ = lot.z + hz * (lot.footprintD * 0.5 + LaneNet.DoorOffset);

                        double dx = houseX - plotCX;
                        double dz = houseZ - plotCZ;
                        double straightDist = Math.Sqrt(dx*dx + dz*dz);
                        double pathDist = straightDist * 1.3; // approximate path factor

                        if (pathDist > maxWalkDist) maxWalkDist = pathDist;

                        if (pathDist > HouseWalkDist)
                        {
                            plotsWithoutHouse++;
                        }
                    }
                }
            }
        }

        if (invalidPlots == 0) pass++; else fails.Add(invalidPlots + " invalid plots (self-intersecting, too small/large, or overlap forbidden areas)");
        if (plotsWithoutHouse == 0) pass++; else fails.Add(plotsWithoutHouse + " plots lack household within " + HouseWalkDist + " m walking distance (worst: " + maxWalkDist.ToString("F1") + " m)");

        // Check crop type distribution makes sense
        int orchardCount = cropCounts[(int)CropType.Orchards];
        int pastureCount = cropCounts[(int)CropType.Pasture];
        int paddyCount = cropCounts[(int)CropType.Paddies];
        int vineCount = cropCounts[(int)CropType.Vines];
        int wheatCount = cropCounts[(int)CropType.Wheat];
        int kgCount = cropCounts[(int)CropType.KitchenGarden];

        // Basic sanity checks
        if (paddyCount == 0 && WaterAccess.Features.Count(f => f.kind == "cistern" || f.kind == "well" || f.kind == "fountain") > 0)
            fails.Add("No wet paddies found despite irrigation/water presence");

        // Check determinism: generate farm plots twice and see if we get the same hash
        int hash1 = Hash;
        int hash2 = 0;
        int hash3 = 0;

        // First generation
        Plots.Clear();
        GenerateFarmLand();
        AssignPlotsToHouseholds();
        AssignCropTypes();
        AddKitchenGardens();
        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Plots.Count) * 16777619;
            foreach (var p in Plots)
            {
                hs = (hs ^ p.id) * 16777619;
                hs = (hs ^ (int)(p.area * 1000f)) * 16777619;
                foreach (var v in p.vertices)
                {
                    hs = (hs ^ (int)(v.x * 1000f)) * 16777619;
                    hs = (hs ^ (int)(v.y * 1000f)) * 16777619;
                }
            }
            hash2 = hs;
        }

        // Second generation
        Plots.Clear();
        GenerateFarmLand();
        AssignPlotsToHouseholds();
        AssignCropTypes();
        AddKitchenGardens();
        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Plots.Count) * 16777619;
            foreach (var p in Plots)
            {
                hs = (hs ^ p.id) * 16777619;
                hs = (hs ^ (int)(p.area * 1000f)) * 16777619;
                foreach (var v in p.vertices)
                {
                    hs = (hs ^ (int)(v.x * 1000f)) * 16777619;
                    hs = (hs ^ (int)(v.y * 1000f)) * 16777619;
                }
            }
            hash3 = hs;
        }

        bool det = (hash2 == hash3);
        if (det) pass++; else fails.Add("farm plot generation is not deterministic");

        sb.AppendLine("total plots " + totalPlots);
        sb.AppendLine("  wheat: " + wheatCount + ", vines: " + vineCount + ", orchards: " + orchardCount);
        sb.AppendLine("  pasture: " + pastureCount + ", paddies: " + paddyCount + ", kitchen gardens: " + kgCount);
        sb.AppendLine("plots without valid house access: " + plotsWithoutHouse + "/" + (totalPlots - kgCount));
        sb.AppendLine("max walk dist to house: " + maxWalkDist.ToString("F1") + " m");
        sb.AppendLine("RULES (4.7): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

    #if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 4.7 Farm Plots + Validate")]
    static void Menu() { Built = false; Build(); Debug.Log(Validate()); }
    #endif
}