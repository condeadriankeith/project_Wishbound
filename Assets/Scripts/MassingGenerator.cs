using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// PHASE 5.2 — stacked volumes on terraced pads. Creates simple stacked cube masses
/// for each lot based on SettlementMap massing data, placed on the current terrain
/// (including pad flattening from PadGrade.cs). Integrates with SettlementMap for
/// lot data and ArchitecturalKit by ensuring both systems are built before generation.
/// </summary>
public static class MassingGenerator
{
    // ---- massing data ----
    public class MassingVolume
    {
        public GameObject root;           // root object for this lot's stacked volume
        public List<GameObject> floors;   // individual floor cubes (bottom to top)
        public int lotIndex;              // originating lot index in ZoneMap.Lots
        public int settlementIndex;       // owning settlement index
        public Vector3 position;          // base position (x, terrain y, z)
        public float yaw;                 // orientation (radians)
        public int floorCount;            // number of floors
        public Vector2 footprint;         // footprint (width, depth)
    }

    // ---- state ----
    public static readonly List<MassingVolume> Volumes = new List<MassingVolume>();
    public static bool Built;
    public static string Report = "";
    public static int Hash;

    // ---- configuration ----
    const float DefaultFloorHeight = 3.0f; // meters per floor
    const string MassingParentName = "MassingGenerator_Volumes";

    // ---- main build ----
    /// <summary>
    /// Generates stacked volume masses for all lots based on SettlementMap data.
    /// Dependencies: SettlementMap and ArchitecturalKit must be built.
    /// Should be called after PadGrade.Build() so terrain includes pad flattening.
    /// </summary>
    public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (!ArchitecturalKit.Built) ArchitecturalKit.Build();

        Clear();

        GameObject parent = new GameObject(MassingParentName);
        parent.transform.position = Vector3.zero;
        parent.transform.rotation = Quaternion.identity;

        var block = new MaterialPropertyBlock();

        for (int li = 0; li < SettlementMap.Lots.Length; li++)
        {
            var massing = SettlementMap.Lots[li];
            if (massing.dropped) continue; // skip duplicate lots

            // terrain height at lot position (uses current terrain, including pads)
            float terrainY = (float)H((float)massing.x, (float)massing.z);
            Vector3 basePos = new Vector3((float)massing.x, terrainY, (float)massing.z);

            GameObject lotRoot = new GameObject("Lot_" + li + "_Massing");
            lotRoot.transform.SetParent(parent.transform, false);
            lotRoot.transform.position = basePos;
            lotRoot.transform.rotation = Quaternion.Euler(0f, massing.yaw * Mathf.Rad2Deg, 0f);

            var floors = new List<GameObject>();
            for (int floorIdx = 0; floorIdx < massing.floors; floorIdx++)
            {
                float floorY = floorIdx * DefaultFloorHeight;
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Floor_" + floorIdx;
                floor.transform.SetParent(lotRoot.transform, false);
                floor.transform.localPosition = new Vector3(0f, floorY + DefaultFloorHeight * 0.5f, 0f);
                floor.transform.localScale = new Vector3(massing.footprintW, DefaultFloorHeight, massing.footprintD);

                // property block: no material instances (renderer.material leaks in edit mode)
                Color c = GetFloorColor(massing.settlement, floorIdx, massing.floors);
                block.SetColor("_Color", c);
                block.SetColor("_BaseColor", c);
                floor.GetComponent<Renderer>().SetPropertyBlock(block);
                floors.Add(floor);
            }

            Volumes.Add(new MassingVolume
            {
                root = lotRoot,
                floors = floors,
                lotIndex = li,
                settlementIndex = massing.settlement,
                position = basePos,
                yaw = massing.yaw,
                floorCount = massing.floors,
                footprint = new Vector2(massing.footprintW, massing.footprintD)
            });
        }

        Hash = VolumeHash();
        Built = true;
        Validate();
    }

    /// <summary>Destroys generated volumes and resets the build state.</summary>
    public static void Clear()
    {
        GameObject parent = GameObject.Find(MassingParentName);
        if (parent != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(parent);
            else UnityEngine.Object.DestroyImmediate(parent);
        }

        Volumes.Clear();
        Built = false;
        Report = "";
        Hash = 0;
    }

    /// <summary>
    /// Validates the generated volumes: structure, and that they match the source lot data
    /// (deterministic hash of the volumes vs. a hash recomputed from SettlementMap).
    /// </summary>
    public static string Validate()
    {
        if (!Built)
        {
            Build(); // Build() runs Validate() itself and fills Report
            return Report;
        }

        var sb = new StringBuilder("[MassingGenerator 5.2]\n");
        var fails = new List<string>();
        int pass = 0;

        int totalVolumes = Volumes.Count;
        int totalFloors = 0;
        int volumesMissingRoot = 0;
        int volumesMissingFloors = 0;
        int volumesWithZeroFloors = 0;
        int floorsMissingRenderer = 0;
        int floorCountMismatch = 0;

        foreach (var vol in Volumes)
        {
            if (vol.root == null) volumesMissingRoot++;
            if (vol.floors == null)
            {
                volumesMissingFloors++;
                continue;
            }

            totalFloors += vol.floors.Count;
            if (vol.floors.Count == 0) volumesWithZeroFloors++;
            if (vol.floors.Count != vol.floorCount) floorCountMismatch++;
            foreach (var floor in vol.floors)
            {
                if (floor == null || floor.GetComponent<Renderer>() == null)
                    floorsMissingRenderer++;
            }
        }

        if (volumesMissingRoot == 0) pass++; else fails.Add(volumesMissingRoot + " volumes missing root object");
        if (volumesMissingFloors == 0 && volumesWithZeroFloors == 0) pass++;
        else fails.Add(volumesMissingFloors + " volumes missing floor list, " + volumesWithZeroFloors + " volumes have zero floors");
        if (floorsMissingRenderer == 0) pass++; else fails.Add(floorsMissingRenderer + " floors missing renderer");
        if (floorCountMismatch == 0) pass++; else fails.Add(floorCountMismatch + " volumes whose floor objects != floor count");

        // determinism / source consistency: the volumes must hash identically to the source lot data
        int sourceHash = SourceHash();
        if (VolumeHash() == sourceHash && Hash == sourceHash) pass++;
        else fails.Add("massing volumes do not match source lot data (not deterministic)");

        float avg = totalVolumes > 0 ? totalFloors / (float)totalVolumes : 0f;
        sb.AppendLine("volumes " + totalVolumes + " | total floors " + totalFloors + " | avg floors per volume " + avg.ToString("F1"));
        sb.AppendLine("volumes missing root: " + volumesMissingRoot + "/" + totalVolumes);
        sb.AppendLine("volumes missing floors: " + volumesMissingFloors + "/" + totalVolumes);
        sb.AppendLine("volumes with zero floors: " + volumesWithZeroFloors + "/" + totalVolumes);
        sb.AppendLine("RULES (5.2): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);
        Report = sb.ToString();
        return Report;
    }

    // ---- helpers ----
    static Color GetFloorColor(int settlementIndex, int floorIndex, int totalFloors)
    {
        float hue = (settlementIndex * 0.1f) % 1.0f; // different hue per settlement
        float t = floorIndex / (float)Mathf.Max(1, totalFloors - 1);
        float saturation = 0.6f + t * 0.3f;          // more saturated higher up
        float value = 0.6f + t * 0.3f;               // brighter higher up
        return Color.HSVToRGB(hue, saturation, value);
    }

    static int Mix(int hs, int lot, int settlement, Vector3 pos, float yaw, int floors, float w, float d)
    {
        unchecked
        {
            hs = (hs ^ lot) * 16777619;
            hs = (hs ^ settlement) * 16777619;
            hs = (hs ^ (int)(pos.x * 100f)) * 16777619;
            hs = (hs ^ (int)(pos.y * 100f)) * 16777619;
            hs = (hs ^ (int)(pos.z * 100f)) * 16777619;
            hs = (hs ^ (int)(yaw * 1000f)) * 16777619;
            hs = (hs ^ floors) * 16777619;
            hs = (hs ^ (int)(w * 100f)) * 16777619;
            hs = (hs ^ (int)(d * 100f)) * 16777619;
            return hs;
        }
    }

    // hash of the generated volumes
    static int VolumeHash()
    {
        unchecked
        {
            int hs = (int)2166136261u;
            foreach (var v in Volumes)
                hs = Mix(hs, v.lotIndex, v.settlementIndex, v.position, v.yaw, v.floorCount, v.footprint.x, v.footprint.y);
            return hs;
        }
    }

    // hash recomputed straight from SettlementMap + terrain (what the volumes should be)
    static int SourceHash()
    {
        unchecked
        {
            int hs = (int)2166136261u;
            for (int li = 0; li < SettlementMap.Lots.Length; li++)
            {
                var m = SettlementMap.Lots[li];
                if (m.dropped) continue;
                float y = (float)H((float)m.x, (float)m.z);
                hs = Mix(hs, li, m.settlement, new Vector3((float)m.x, y, (float)m.z), m.yaw, m.floors, m.footprintW, m.footprintD);
            }
            return hs;
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 5.2 Massing Generator + Validate")]
    static void Menu()
    {
        Built = false;
        Build();
        Debug.Log(Report);
    }
#endif
}
