using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// PHASE 5.7 — yard and outdoor spaces system. Generates yard features for each lot
/// based on settlement context and lot characteristics. Integrates with SettlementMap
/// and WaterAccess and provides data for building generation and scene population.
/// </summary>
public static class YardSystem
{
    /// <summary>Yard data for a lot, parallel to SettlementMap.Lots.</summary>
    public struct YardData
    {
        public bool hasStoop;             // front steps/stoop
        public bool hasYard;              // yard area (vs. zero-lot-line)
        public bool hasFence;             // fence boundary
        public bool hasGarden;            // garden area
        public bool hasWell;              // well or cistern
        public bool hasShadeTree;         // shade tree in yard
        public bool hasPrivy;             // privy (only with a well)
        public bool hasDungHeap;          // dung heap (farming households with a yard)
        public bool hasPatio;             // paved patio area
        public bool hasWorkshop;          // outdoor workshop/shed
        public float gardenSize;          // garden area size (fraction of yard)
        public int treeCount;             // number of shade/fruit trees
        public Vector3[] fencePosts;      // positions of fence posts (if hasFence)
    }

    // Parallel array to SettlementMap.Lots, one YardData per lot index
    public static YardData[] Lots;
    public static bool Built;
    public static string Report = "";
    public static int Hash;

    const float PostSpacing = 2.5f;

    /// <summary>Generates yard data for all lots. Depends on SettlementMap and WaterAccess.</summary>
    public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (!WaterAccess.Built) WaterAccess.Build();

        Lots = Generate();
        Hash = ComputeHash(Lots);
        Built = true;
        Validate();
    }

    /// <summary>Clears all yard data and resets the build state.</summary>
    public static void Clear()
    {
        Lots = null;
        Built = false;
        Report = "";
        Hash = 0;
    }

    // Pure generation: returns a fresh array and touches no static state.
    static YardData[] Generate()
    {
        var lots = new YardData[SettlementMap.Lots.Length];

        for (int li = 0; li < SettlementMap.Lots.Length; li++)
        {
            var massing = SettlementMap.Lots[li];
            if (massing.dropped) continue; // skip duplicate lots

            int settlementIndex = massing.settlement;
            if (settlementIndex < 0 || settlementIndex >= SettlementMap.Settlements.Count)
                continue;

            var settlement = SettlementMap.Settlements[settlementIndex];
            var lot = ZoneMap.Lots[li]; // original lot data (position, suitability)
            lots[li] = MakeYard(li, settlement, lot);
        }

        for (int li = 0; li < lots.Length; li++)
        {
            if (!lots[li].hasFence) continue;
            var massing = SettlementMap.Lots[li];
            if (massing.dropped) continue;
            lots[li].fencePosts = MakeFencePosts(massing);
        }

        return lots;
    }

    static bool IsFarmingLot(SettlementMap.Settlement settlement, ZoneMap.Lot lotData)
    {
        return settlement.role == Zone.Farm ||
               (settlement.role == Zone.Hamlet && lotData.suit > 0.5f);
    }

    static YardData MakeYard(int lotIndex, SettlementMap.Settlement settlement, ZoneMap.Lot lotData)
    {
        var yard = new YardData();
        var rand = new System.Random(lotIndex * 1337 ^ settlement.id * 7); // deterministic per lot

        yard.hasYard = rand.NextDouble() > 0.1;     // 90%
        yard.hasStoop = rand.NextDouble() > 0.2;    // 80%
        yard.hasFence = rand.NextDouble() > 0.3;    // 70%

        yard.hasGarden = rand.NextDouble() > 0.2;   // 80%
        double gardenRoll = rand.NextDouble();      // always consume so later rolls stay stable
        if (yard.hasGarden)
            yard.gardenSize = (float)(gardenRoll * 0.5 + 0.2); // 20-70% of yard

        yard.hasWell = CheckWaterAccess(lotData.x, lotData.z);

        double treeRoll = rand.NextDouble();
        int treeCountRoll = rand.Next(1, 4);
        yard.hasShadeTree = yard.hasYard && treeRoll > 0.4; // 60% if has yard
        if (yard.hasShadeTree)
            yard.treeCount = treeCountRoll;

        yard.hasPrivy = yard.hasWell && rand.NextDouble() > 0.5; // only with water access

        double dungRoll = rand.NextDouble();
        yard.hasDungHeap = IsFarmingLot(settlement, lotData) && yard.hasYard && dungRoll > 0.6; // 40%

        bool refined = settlement.role == Zone.Harbour || settlement.role == Zone.Summit;
        yard.hasPatio = rand.NextDouble() > (refined ? 0.3 : 0.5);

        bool working = settlement.role == Zone.Farm || settlement.role == Zone.Harbour;
        yard.hasWorkshop = rand.NextDouble() > (working ? 0.5 : 0.7);

        // every lot needs some outdoor access
        if (!yard.hasYard && !yard.hasStoop)
            yard.hasStoop = true;

        return yard;
    }

    /// <summary>True if a lot is within reach of a well/cistern feature from WaterAccess.</summary>
    static bool CheckWaterAccess(double lotX, double lotZ)
    {
        if (!WaterAccess.Built)
            return false;

        foreach (var feature in WaterAccess.Features)
        {
            double dx = feature.x - lotX;
            double dz = feature.z - lotZ;
            if (Math.Sqrt(dx * dx + dz * dz) <= WaterAccess.MaxWaterDist)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Fence posts around the lot footprint, evenly spaced per edge. Corner posts are shared
    /// between edges (each edge emits its start point but not its end point).
    /// </summary>
    static Vector3[] MakeFencePosts(SettlementMap.Massing massing)
    {
        float hw = massing.footprintW * 0.5f;
        float hd = massing.footprintD * 0.5f;
        float cx = (float)massing.x;
        float cz = (float)massing.z;

        var local = new Vector2[]
        {
            new Vector2(-hw, -hd),
            new Vector2( hw, -hd),
            new Vector2( hw,  hd),
            new Vector2(-hw,  hd)
        };

        // same rotation as Quaternion.Euler(0, yaw, 0), used by MassingGenerator
        float cos = Mathf.Cos(massing.yaw);
        float sin = Mathf.Sin(massing.yaw);
        var corners = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float wx = cx + (local[i].x * cos + local[i].y * sin);
            float wz = cz + (-local[i].x * sin + local[i].y * cos);
            corners[i] = new Vector3(wx, 0f, wz);
        }

        var posts = new List<Vector3>();
        for (int edge = 0; edge < 4; edge++)
        {
            Vector3 start = corners[edge];
            Vector3 end = corners[(edge + 1) % 4];
            float edgeLength = (end - start).magnitude;
            int segments = Mathf.Max(2, Mathf.FloorToInt(edgeLength / PostSpacing));
            for (int i = 0; i < segments; i++) // i == segments is the next edge's start
                posts.Add(Vector3.Lerp(start, end, i / (float)segments));
        }
        return posts.ToArray();
    }

    static int ComputeHash(YardData[] lots)
    {
        unchecked
        {
            int hs = (int)2166136261u;
            if (lots == null) return hs;
            hs = (hs ^ lots.Length) * 16777619;
            foreach (var y in lots)
            {
                int flags = (y.hasStoop ? 1 : 0) | (y.hasYard ? 2 : 0) | (y.hasFence ? 4 : 0) | (y.hasGarden ? 8 : 0) |
                            (y.hasWell ? 16 : 0) | (y.hasShadeTree ? 32 : 0) | (y.hasPrivy ? 64 : 0) |
                            (y.hasDungHeap ? 128 : 0) | (y.hasPatio ? 256 : 0) | (y.hasWorkshop ? 512 : 0);
                hs = (hs ^ flags) * 16777619;
                hs = (hs ^ (int)(y.gardenSize * 1000f)) * 16777619;
                hs = (hs ^ y.treeCount) * 16777619;
                int posts = y.fencePosts == null ? 0 : y.fencePosts.Length;
                hs = (hs ^ posts) * 16777619;
                if (posts > 0)
                {
                    hs = (hs ^ (int)(y.fencePosts[0].x * 100f)) * 16777619;
                    hs = (hs ^ (int)(y.fencePosts[posts - 1].z * 100f)) * 16777619;
                }
            }
            return hs;
        }
    }

    /// <summary>Validates the yard system against the Phase 5.7 requirements.</summary>
    public static string Validate()
    {
        if (!Built)
        {
            Build(); // Build() runs Validate() itself and fills Report
            return Report;
        }

        var sb = new StringBuilder("[YardSystem 5.7]\n");
        var fails = new List<string>();
        int pass = 0;

        int totalLots = SettlementMap.Lots.Length;
        int activeLots = 0;
        int lotsWithoutYard = 0;
        int wellWithoutAccess = 0;
        int invalidFence = 0;
        int privyWithoutWell = 0;
        int invalidDungHeap = 0;

        for (int li = 0; li < Lots.Length; li++)
        {
            var massing = SettlementMap.Lots[li];
            if (massing.dropped) continue;
            activeLots++;

            var yard = Lots[li];

            // every lot should have some outdoor space
            if (!yard.hasYard && !yard.hasStoop)
                lotsWithoutYard++;

            // a well must actually be near a water feature
            if (yard.hasWell && !CheckWaterAccess(ZoneMap.Lots[li].x, ZoneMap.Lots[li].z))
                wellWithoutAccess++;

            // fence flag and posts must agree
            if (yard.hasFence && (yard.fencePosts == null || yard.fencePosts.Length < 4))
                invalidFence++;

            // privy only exists with a well
            if (yard.hasPrivy && !yard.hasWell)
                privyWithoutWell++;

            // dung heap only on farming lots that have a yard
            bool farmingLot = false;
            if (massing.settlement >= 0 && massing.settlement < SettlementMap.Settlements.Count)
                farmingLot = IsFarmingLot(SettlementMap.Settlements[massing.settlement], ZoneMap.Lots[li]);
            if (yard.hasDungHeap && (!farmingLot || !yard.hasYard))
                invalidDungHeap++;
        }

        if (lotsWithoutYard == 0) pass++; else fails.Add(lotsWithoutYard + " lots lack both yard and stoop");
        if (wellWithoutAccess == 0) pass++; else fails.Add(wellWithoutAccess + " lots have a well but no water access");
        if (invalidFence == 0) pass++; else fails.Add(invalidFence + " lots have a fence but too few fence posts");
        if (privyWithoutWell == 0) pass++; else fails.Add(privyWithoutWell + " lots have a privy but no well");
        if (invalidDungHeap == 0) pass++; else fails.Add(invalidDungHeap + " lots have a dung heap but are not farming lots with a yard");

        // determinism: regenerate into a fresh array and compare hashes
        int hash2 = ComputeHash(Generate());
        if (Hash == hash2) pass++; else fails.Add("yard generation is not deterministic");

        sb.AppendLine("lots " + totalLots + " (active " + activeLots + ")");
        sb.AppendLine("lots without yard/stoop: " + lotsWithoutYard + "/" + activeLots);
        sb.AppendLine("wells without water access: " + wellWithoutAccess + "/" + activeLots);
        sb.AppendLine("invalid fences: " + invalidFence + "/" + activeLots);
        sb.AppendLine("privies without well: " + privyWithoutWell + "/" + activeLots);
        sb.AppendLine("invalid dung heaps: " + invalidDungHeap + "/" + activeLots);
        sb.AppendLine("RULES (5.7): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 5.7 Yard System + Validate")]
    static void Menu()
    {
        Built = false;
        Build();
        Debug.Log(Report);
    }
#endif
}
