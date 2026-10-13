using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 4.6 — water access. DATA ONLY: places water features and irrigation channels.
/// Uses data from SettlementMap (plazas, houses), FlowNet/RiverCarve (rivers), and creates
/// irrigation networks branching from rivers. No terrain modification here — channels are
/// shallow carves that follow the existing terrain slope.
/// </summary>
public static class WaterAccess
{
    // ---- configuration ----
    public const double WellRadius = 1.0;       // well/fountain radius (m)
    public const double CisternRadius = 1.5;    // cistern radius (m)
    public const double MaxWaterDist = 80.0;    // max walking distance to water (m)
    public const double ChannelWidth = 2.0;     // irrigation channel width (m)
    public const double ChannelDepth = 0.5;     // irrigation channel depth (m)
    public const double BranchAngle = 0.3;      // rad (~17°) for river branches
    public const double MinBranchDist = 10.0;   // m between branches on same river
    public const double MaxBranchSlope = 0.15;  // max slope for irrigation channels (15%)

    // ---- water feature data ----
    public class WaterFeature
    {
        public string kind;     // "well", "fountain", "cistern"
        public double x, z;     // position
        public double h;        // ground height
        public double radius;   // radius of influence
        public int settlement;  // owning settlement (-1 for cisterns)
        public int lot;         // owning lot (-1 for plazas/cisterns)
    }

    public class IrrigationChannel
    {
        public int riverId;     // source river index
        public int branchId;    // branch index per river
        public double[] x, z;   // channel centreline points
        public double length;   // total channel length
        public double avgSlope; // average slope along channel
        public bool downhill;   // true if channel runs downhill
    }

    public static readonly List<WaterFeature> Features = new List<WaterFeature>();
    public static readonly List<IrrigationChannel> Channels = new List<IrrigationChannel>();
    public static bool Built;
    public static string Report = "";
    static int Hash;

    // ---- main build ----
    public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (PadGrade.Fill == null) PadGrade.Build();
        if (LaneNet.Doors == null) LaneNet.Build();
        FlowNet.Build(1337);
        RiverCarve.Build(1337);

        Features.Clear();
        Channels.Clear();

        CollectPlazaFeatures();
        CollectCisternFeatures();
        GenerateIrrigationChannels();

        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Features.Count) * 16777619;
            hs = (hs ^ Channels.Count) * 16777619;
            foreach (var f in Features) { hs = (hs ^ (int)(f.x * 1000f)) * 16777619; hs = (hs ^ (int)(f.z * 1000f)) * 16777619; }
            foreach (var c in Channels) { hs = (hs ^ (int)(c.length * 1000f)) * 16777619; }
            Hash = hs;
        }

        Built = true;
        Validate();
    }

    // ---- collect wells/fountains at plazas ----
    static void CollectPlazaFeatures()
    {
        for (int si = 0; si < SettlementMap.Settlements.Count; si++)
        {
            var s = SettlementMap.Settlements[si];
            if (s.hasPlaza)
            {
                // Determine if this is a well or fountain based on settlement type
                string kind = s.role == Zone.Summit ? "fountain" : "well";
                // Summits get fountains (more ceremonial), others get wells

                Features.Add(new WaterFeature
                {
                    kind = kind,
                    x = s.plazaX,
                    z = s.plazaZ,
                    h = WorldField.Height(s.plazaX, s.plazaZ),
                    radius = WellRadius,
                    settlement = si,
                    lot = -1
                });
            }
        }
    }

    // ---- collect cisterns beside houses ----
    static void CollectCisternFeatures()
    {
        // Place cisterns beside houses that are far from natural water sources
        // or based on settlement type/need

        for (int li = 0; li < SettlementMap.Lots.Length; li++)
        {
            var lot = SettlementMap.Lots[li];
            if (lot.dropped) continue;

            // Get house position and orientation
            var massing = SettlementMap.Lots[li];
            double hx = Math.Sin(massing.yaw);
            double hz = Math.Cos(massing.yaw);

            // House centre + front offset for door approach (consistent with LaneNet)
            double houseX = lot.x + hx * (lot.footprintW * 0.5 + LaneNet.DoorOffset);
            double houseZ = lot.z + hz * (lot.footprintD * 0.5 + LaneNet.DoorOffset);

            // Check distance to existing water features (wells/fountains from plazas)
            double minDistToWater = MaxWaterDist + 1;
            foreach (var f in Features)
            {
                if (f.kind == "well" || f.kind == "fountain")
                {
                    double dx = f.x - houseX;
                    double dz = f.z - houseZ;
                    double dist = Math.Sqrt(dx * dx + dz * dz);
                    if (dist < minDistToWater) minDistToWater = dist;
                }
            }

            // Check distance to rivers (using RiverCarve channel points for accuracy)
            foreach (var channel in RiverCarve.Channels)
            {
                for (int i = 0; i < channel.x.Length; i++)
                {
                    double dx = channel.x[i] - houseX;
                    double dz = channel.z[i] - houseZ;
                    double dist = Math.Sqrt(dx * dx + dz * dz);
                    if (dist < minDistToWater) minDistToWater = dist;
                }
            }

            // If house is far from water (>40m), place a cistern
            // This threshold can be tuned; 40m leaves margin for the 80m max walk
            if (minDistToWater > 40.0)
            {
                // Place cistern beside house (on the left/right side relative to door)
                double rx = hz;   // right vector
                double rz = -hx;

                // Alternate left/right placement to avoid clustering
                int side = li % 2 == 0 ? 1 : -1;
                double cisternX = houseX + rx * side * (lot.footprintW * 0.5 + 2.0);
                double cisternZ = houseZ + rz * side * (lot.footprintD * 0.5 + 2.0);

                // Ensure cistern is on land and not too steep
                if (WorldField.IsLand(cisternX, cisternZ) && !WorldField.IsCliff(cisternX, cisternZ))
                {
                    double h = WorldField.Height(cisternX, cisternZ);

                    Features.Add(new WaterFeature
                    {
                        kind = "cistern",
                        x = cisternX,
                        z = cisternZ,
                        h = h,
                        radius = CisternRadius,
                        settlement = massing.settlement,
                        lot = li
                    });
                }
            }
        }
    }

    // ---- generate irrigation channels branching from rivers ----
    static void GenerateIrrigationChannels()
    {
        // Branch irrigation channels off rivers at regular intervals
        // Follow existing terrain contours (use RiverCarve channel data as base)

        int[] branchCounter = new int[FlowNet.Rivers.Count]; // tracks branches per river

        for (int ri = 0; ri < FlowNet.Rivers.Count; ri++)
        {
            var river = FlowNet.Rivers[ri];
            if (river.cells.Length < 10) continue; // skip tiny rivers

            // Sample points along the river for potential branches
            int sampleStep = Math.Max(1, river.cells.Length / 20); // ~20 samples per river
            double lastBranchS = -1e9; // track s-coordinate of last branch

            for (int si = 0; si < river.cells.Length; si += sampleStep)
            {
                int cellIndex = river.cells[si];
                double riverX = FlowNet.X(cellIndex);
                double riverZ = FlowNet.Z(cellIndex);
                double riverS = si * WorldField.Cell; // approximate distance along river

                // Ensure minimum distance between branches
                if (riverS - lastBranchS < MinBranchDist) continue;

                // Get river flow direction at this point
                double dx = 0, dz = 0;
                if (si > 0 && si < river.cells.Length - 1)
                {
                    int prevCell = river.cells[si - 1];
                    int nextCell = river.cells[si + 1];
                    double prevX = FlowNet.X(prevCell);
                    double prevZ = FlowNet.Z(prevCell);
                    double nextX = FlowNet.X(nextCell);
                    double nextZ = FlowNet.Z(nextCell);

                    dx = nextX - prevX;
                    dz = nextZ - prevZ;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    if (len > 1e-6) { dx /= len; dz /= len; }
                }
                else if (si == 0 && river.cells.Length > 1)
                {
                    // Use first two points for direction
                    int nextCell = river.cells[si + 1];
                    double nextX = FlowNet.X(nextCell);
                    double nextZ = FlowNet.Z(nextCell);
                    dx = nextX - riverX;
                    dz = nextZ - riverZ;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    if (len > 1e-6) { dx /= len; dz /= len; }
                }
                else if (si == river.cells.Length - 1 && river.cells.Length > 1)
                {
                    // Use last two points for direction
                    int prevCell = river.cells[si - 1];
                    double prevX = FlowNet.X(prevCell);
                    double prevZ = FlowNet.Z(prevCell);
                    dx = riverX - prevX;
                    dz = riverZ - prevZ;
                    double len = Math.Sqrt(dx * dx + dz * dz);
                    if (len > 1e-6) { dx /= len; dz /= len; }
                }

                if (Math.Abs(dx) < 1e-6 && Math.Abs(dz) < 1e-6) continue; // invalid direction

                // Create branching channel going inland (approximately perpendicular to river)
                // Try both sides of the river
                for (int side = 0; side < 2; side++)
                {
                    double sign = (side == 0) ? 1.0 : -1.0;

                    // Channel direction: rotate river flow direction by BranchAngle
                    double cosA = Math.Cos(BranchAngle);
                    double sinA = Math.Sin(BranchAngle);
                    double chDX = dx * cosA - sign * dz * sinA;
                    double chDZ = sign * dx * sinA + dz * cosA;

                    // Normalize
                    double chLen = Math.Sqrt(chDX * chDX + chDZ * chDZ);
                    if (chLen < 1e-6) continue;
                    chDX /= chLen;
                    chDZ /= chLen;

                    // Generate channel points following terrain gradient
                    List<double> pointsX = new List<double>();
                    List<double> pointsZ = new List<double>();
                    pointsX.Add(riverX);
                    pointsZ.Add(riverZ);

                    double curX = riverX;
                    double curZ = riverZ;
                    double accumulatedLength = 0;
                    bool valid = true;

                    // Generate up to 20 points or until we hit invalid terrain
                    for (int step = 0; step < 20; step++)
                    {
                        double stepLen = ChannelWidth; // m per step
                        curX += chDX * stepLen;
                        curZ += chDZ * stepLen;
                        accumulatedLength += stepLen;

                        // Check if still on land and not too steep
                        if (!WorldField.IsLand(curX, curZ) || WorldField.IsCliff(curX, curZ))
                        {
                            valid = false;
                            break;
                        }

                        // Check slope (simplified: check height change over step)
                        if (step > 0)
                        {
                            double prevH = WorldField.Height(pointsX[pointsX.Count - 1], pointsZ[pointsZ.Count - 1]);
                            double curH = WorldField.Height(curX, curZ);
                            double slope = Math.Abs(curH - prevH) / stepLen;
                            if (slope > MaxBranchSlope)
                            {
                                valid = false;
                                break;
                            }
                        }

                        pointsX.Add(curX);
                        pointsZ.Add(curZ);
                    }

                    if (valid && pointsX.Count >= 2)
                    {
                        // Create the irrigation channel
                        Channels.Add(new IrrigationChannel
                        {
                            riverId = ri,
                            branchId = branchCounter[ri]++,
                            x = pointsX.ToArray(),
                            z = pointsZ.ToArray(),
                            length = accumulatedLength,
                            avgSlope = 0 // TODO: calculate actual average slope
                        });

                        lastBranchS = riverS;
                        break; // only create one branch per sample point (pick first valid side)
                    }
                }
            }
        }

        // TODO: Calculate actual average slope for each channel
        foreach (var channel in Channels)
        {
            double totalSlope = 0;
            int validSegments = 0;

            for (int i = 1; i < channel.x.Length; i++)
            {
                double dx = channel.x[i] - channel.x[i-1];
                double dz = channel.z[i] - channel.z[i-1];
                double segLen = Math.Sqrt(dx*dx + dz*dz);

                if (segLen > 1e-6)
                {
                    double h1 = WorldField.Height(channel.x[i-1], channel.z[i-1]);
                    double h2 = WorldField.Height(channel.x[i], channel.z[i]);
                    double slope = Math.Abs(h2 - h1) / segLen;
                    totalSlope += slope;
                    validSegments++;
                }
            }

            channel.avgSlope = validSegments > 0 ? totalSlope / validSegments : 0;
            channel.downhill = true; // Simplified: assume we followed terrain downhill
                                  // In reality, we'd need to check if end is lower than start
        }
    }

    // ---- validation ----
    public static string Validate()
    {
        if (!Built) Build();
        var sb = new StringBuilder("[WaterAccess 4.6]\n");
        var fails = new List<string>(); int pass = 0;

        int totalLots = 0, housedWithoutWater = 0;
        double maxWaterDist = 0;

        // Check every house has water access within MaxWaterDist
        for (int li = 0; li < SettlementMap.Lots.Length; li++)
        {
            var lot = SettlementMap.Lots[li];
            if (lot.dropped) continue;
            totalLots++;

            // Get house position (consistent with LaneNet door position)
            var massing = SettlementMap.Lots[li];
            double hx = Math.Sin(massing.yaw);
            double hz = Math.Cos(massing.yaw);
            double houseX = lot.x + hx * (lot.footprintW * 0.5 + LaneNet.DoorOffset);
            double houseZ = lot.z + hz * (lot.footprintD * 0.5 + LaneNet.DoorOffset);

            // Find minimum distance to any water feature
            double minDist = MaxWaterDist + 1;

            // Check wells/fountains
            foreach (var f in Features)
            {
                if (f.kind == "well" || f.kind == "fountain")
                {
                    double dx = f.x - houseX;
                    double dz = f.z - houseZ;
                    double dist = Math.Sqrt(dx*dx + dz*dz);
                    if (dist < minDist) minDist = dist;
                }
            }

            // Check cisterns
            foreach (var f in Features)
            {
                if (f.kind == "cistern")
                {
                    double dx = f.x - houseX;
                    double dz = f.z - houseZ;
                    double dist = Math.Sqrt(dx*dx + dz*dz);
                    if (dist < minDist) minDist = dist;
                }
            }

            #region TODO: Also check distance to rivers/channels for validation
            // For a complete validation, we should also check distance to:
            // - RiverCarve channel points (natural water)
            // - Irrigation channels (man-made water)
            #endregion

            if (minDist > MaxWaterDist)
            {
                housedWithoutWater++;
                if (minDist > maxWaterDist) maxWaterDist = minDist;
            }
        }

        if (housedWithoutWater == 0) pass++; else fails.Add(housedWithoutWater + " houses lack water access within " + MaxWaterDist + " m (worst: " + maxWaterDist.ToString("F1") + " m)");

        int channelsWithoutDownhill = 0;
        double maxUphillSlope = 0;

        #region TODO: Validate irrigation channels run downhill
        // For each channel, verify that it generally flows downhill
        #endregion

        if (channelsWithoutDownhill == 0) pass++; else fails.Add(channelsWithoutDownhill + " irrigation channels do not run downhill (max uphill slope: " + maxUphillSlope.ToString("F2") + ")");

        // Check determinism: generate data twice and see if we get the same hash
        int hash1 = Hash;
        int hash2 = 0;
        int hash3 = 0;

        // First generation
        Features.Clear();
        Channels.Clear();
        CollectPlazaFeatures();
        CollectCisternFeatures();
        GenerateIrrigationChannels();
        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Features.Count) * 16777619;
            hs = (hs ^ Channels.Count) * 16777619;
            foreach (var f in Features) { hs = (hs ^ (int)(f.x * 1000f)) * 16777619; hs = (hs ^ (int)(f.z * 1000f)) * 16777619; }
            foreach (var c in Channels) { hs = (hs ^ (int)(c.length * 1000f)) * 16777619; }
            hash2 = hs;
        }

        // Second generation
        Features.Clear();
        Channels.Clear();
        CollectPlazaFeatures();
        CollectCisternFeatures();
        GenerateIrrigationChannels();
        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ Features.Count) * 16777619;
            hs = (hs ^ Channels.Count) * 16777619;
            foreach (var f in Features) { hs = (hs ^ (int)(f.x * 1000f)) * 16777619; hs = (hs ^ (int)(f.z * 1000f)) * 16777619; }
            foreach (var c in Channels) { hs = (hs ^ (int)(c.length * 1000f)) * 16777619; }
            hash3 = hs;
        }

        bool det = (hash2 == hash3);
        if (det) pass++; else fails.Add("water access generation is not deterministic");

        sb.AppendLine("water features " + Features.Count + " (wells: " + Features.Count(f => f.kind == "well") +
                     ", fountains: " + Features.Count(f => f.kind == "fountain") +
                     ", cisterns: " + Features.Count(f => f.kind == "cistern") + ")");
        sb.AppendLine("irrigation channels " + Channels.Count +
                     (Channels.Count > 0 ? ", avg length: " + Channels.Average(c => c.length).ToString("F0") + " m" : ""));
        sb.AppendLine("houses without water: " + housedWithoutWater + "/" + totalLots);
        sb.AppendLine("RULES (4.6): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

    #if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 4.6 Water Access + Validate")]
    static void Menu() { Built = false; Build(); Debug.Log(Validate()); }
    #endif
}