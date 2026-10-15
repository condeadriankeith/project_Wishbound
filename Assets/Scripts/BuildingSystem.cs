using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// PHASE 5 — Building system. Assembles complete building data from the core generator
/// (BuildingGenerator), floor plans, furnishing, yards and lane doors. Other Phase 5 systems
/// (ExteriorDetailSystem, BuildingValidator, ...) consume BuildingSystem.Buildings.
///
/// Local building space: origin at the footprint's corner, x = width, y = depth ("z" in world).
/// Local -> world uses the same rotation as Quaternion.Euler(0, yaw, 0).
/// Walls: 0 = y=0 side, 1 = x=W side, 2 = y=D side, 3 = x=0 side.
/// </summary>
public static class BuildingSystem
{
    // ---- constants ----
    public const float FloorHeight = 3.0f;
    public const float DoorWidth = 1.0f;
    public const float DoorHeight = 2.1f;
    const float ExteriorWallTolerance = 1.5f; // room within this distance of a wall counts as exterior

    // ---- building data ----
    public class Building
    {
        public int id;
        public int settlement;
        public string householdType;
        public Vector3 position;      // building position in world space
        public float yaw;             // building orientation in radians
        public int lot;               // owning lot index
        public int floors = 1;        // number of floors (1-3)
        public Vector2 footprint;     // width (x), depth (y)
        public Exterior exterior = new Exterior();
        public Interior interior = new Interior();
        public Yard yard = new Yard();
        public Roof roof = new Roof();
        public List<Floor> floorPlan = new List<Floor>();

        [Serializable]
        public class Exterior
        {
            public List<ArchitecturalDetail> architecturalDetails = new List<ArchitecturalDetail>();
            public bool hasWoodStacks;
            public bool hasBarrels;
            public bool hasLanterns;
            public bool hasShopSign;
            public List<Opening> openings = new List<Opening>(); // BuildingSystem.Opening, building-local coords
        }

        [Serializable]
        public class Interior
        {
            public bool hasHearthGlow;
            public bool hasLanternLight;
            public string floorMaterial = "timber";
        }

        [Serializable]
        public class ArchitecturalDetail
        {
            public string id;
            public string type;
            public ArchitecturalKit.ArchitecturalStyle style;
        }

        [Serializable]
        public class Yard
        {
            public bool hasYard;
            public bool hasStoop;
        }

        [Serializable]
        public class Roof
        {
            public BuildingRoof.RoofType type;
        }

        [Serializable]
        public class Floor
        {
            public int floorIndex;
            public float y; // height of this floor
            public List<Room> rooms = new List<Room>();
        }
    }

    // ---- enums ----
    public class BuildingRoof
    {
        public enum RoofType { GabledTerracotta, FlatParapet, SteppedGable }
    }

    // Exposed nested classes for use with 'using static BuildingSystem'
    public class Room
    {
        public string roomType;   // "living", "kitchen", "bedroom", etc.
        public Vector2 position;  // x, y local to building footprint (corner origin)
        public Vector2 size;      // width, depth
        public int doorCount;
        public int windowCount;
        public bool hasHearth;
        public List<Furniture> furniture = new List<Furniture>();
    }

    public class Furniture
    {
        public string furnitureType; // e.g., "bed", "table", "hearth", "shelf"
        public Vector2 position;     // local position in the room
        public Vector2 size;         // size of the furniture
        public bool isBuiltIn;       // whether the furniture is built-in/mounted
    }

    public class Opening
    {
        public enum OpeningType { Window, Door }

        public OpeningType type;
        public Vector2 position;   // building-local (corner origin): x along width, y along depth
        public Vector2 size;       // width, height
        public float revealDepth;  // depth of reveal (for doors/windows)
        public bool isExternal;    // true if the opening is on an exterior wall
        public float baseHeight;   // height of the opening's base above ground level (floor y + sill)
    }

    // ---- state ----
    public static readonly List<Building> Buildings = new List<Building>();
    public static bool Built;
    public static string Report = "";
    public static int Hash;

    // ---- main build ----
    /// <summary>
    /// Assembles building data. Dependencies: SettlementMap, ArchitecturalKit, BuildingGenerator,
    /// LaneNet (doors) and YardSystem.
    /// </summary>
public static void Build()
    {
        if (!SettlementMap.Built) SettlementMap.Build();
        if (!ArchitecturalKit.Built) ArchitecturalKit.Build();
        if (!BuildingGenerator.Built) BuildingGenerator.Build();
        if (!LaneNet.Built) LaneNet.Build();
        if (!YardSystem.Built) YardSystem.Build();

        Buildings.Clear();
        Generate(Buildings);
        Hash = ComputeHash(Buildings);
        Built = true;
        Validate();
    }

    /// <summary>Clears all building data and resets the build state.</summary>
    public static void Clear()
    {
        Buildings.Clear();
        Built = false;
        Report = "";
        Hash = 0;
    }

static void Generate(List<Building> target)
    {
        target.Clear();

        foreach (var bd in BuildingGenerator.Buildings)
        {
            var b = new Building
            {
                id = target.Count, // id == index into the list
                lot = bd.lot,
                settlement = bd.settlement,
                householdType = bd.householdType,
                position = bd.position,
                yaw = bd.yaw,
                floors = Mathf.Clamp(bd.floors, 1, 3),
                footprint = bd.footprint
            };

            // floor plans + furniture
            for (int f = 0; f < b.floors; f++)
            {
                var floor = new Building.Floor { floorIndex = f, y = f * FloorHeight };
                floor.rooms = FloorPlanGenerator.GenerateRoomsForFloor(b.householdType, f, b.footprint);
                foreach (var room in floor.rooms)
                    room.furniture = FurnishingSystem.GenerateFurnitureForRoom(room, b.householdType);
                b.floorPlan.Add(floor);
            }

            // yard (from YardSystem, parallel to lots)
            if (YardSystem.Lots != null && b.lot >= 0 && b.lot < YardSystem.Lots.Length)
            {
                var y = YardSystem.Lots[b.lot];
                b.yard.hasYard = y.hasYard;
                b.yard.hasStoop = y.hasStoop;
            }
            else
            {
                b.yard.hasYard = true;
            }

            b.roof.type = ChooseRoof(b);
            AssignExteriorFeatures(b);
            FurnishingSystem.SetBuildingLighting(b);
            GenerateOpenings(b);

            target.Add(b);
        }
    }

    static BuildingRoof.RoofType ChooseRoof(Building b)
    {
        var rand = new System.Random(b.id * 7919 + 17);
        Zone role = Zone.Farm;
        if (b.settlement >= 0 && b.settlement < SettlementMap.Settlements.Count)
            role = SettlementMap.Settlements[b.settlement].role;

        switch (role)
        {
            case Zone.Harbour: // practical roofs, no ornate stepped gables
                return rand.Next(2) == 0 ? BuildingRoof.RoofType.GabledTerracotta : BuildingRoof.RoofType.FlatParapet;
            case Zone.Summit:  // weather-proof roofs, no flat roofs
                return rand.Next(2) == 0 ? BuildingRoof.RoofType.GabledTerracotta : BuildingRoof.RoofType.SteppedGable;
            default:
                switch (rand.Next(3))
                {
                    case 0: return BuildingRoof.RoofType.GabledTerracotta;
                    case 1: return BuildingRoof.RoofType.FlatParapet;
                    default: return BuildingRoof.RoofType.SteppedGable;
                }
        }
    }

    static void AssignExteriorFeatures(Building b)
    {
        var rand = new System.Random(b.id * 104729 + 31);
        switch (b.householdType)
        {
            case "farmer":
                b.exterior.hasWoodStacks = true;
                b.exterior.hasBarrels = rand.NextDouble() > 0.5;
                break;
            case "fisher":
                b.exterior.hasWoodStacks = true;
                b.exterior.hasLanterns = true;
                break;
            case "shopkeeper":
                b.exterior.hasShopSign = true;
                break;
        }
        if (!b.exterior.hasLanterns)
            b.exterior.hasLanterns = rand.NextDouble() > 0.3;
    }

    static void GenerateOpenings(Building b)
    {
        var list = b.exterior.openings;
        list.Clear();

        // entry door from the lane network
        if (LaneNet.Doors != null && b.lot >= 0 && b.lot < LaneNet.Doors.Length)
        {
            var d = LaneNet.Doors[b.lot];
            if (d.ok)
            {
                Vector2 local = WorldToLocal(b, (float)d.x, (float)d.z);
                float s;
                int wall = NearestWall(b, local, out s);
                list.Add(new Opening
                {
                    type = Opening.OpeningType.Door,
                    position = PointOnWall(b, wall, s),
                    size = new Vector2(DoorWidth, DoorHeight),
                    revealDepth = 0.2f,
                    baseHeight = 0f,
                    isExternal = true
                });
            }
        }

        // windows from room requirements
        foreach (var floor in b.floorPlan)
        {
            foreach (var room in floor.rooms)
            {
                if (room.windowCount <= 0) continue;
                bool external = IsRoomOnExteriorWall(b, room, floor.floorIndex);
                float width = Mathf.Clamp(room.size.x * 0.25f, 0.6f, 1.6f);
                float height = Mathf.Clamp(room.size.y * 0.45f, 1.0f, 1.8f);
                for (int w = 0; w < room.windowCount; w++)
                {
                    float px = room.position.x + room.size.x * (w + 1) / (float)(room.windowCount + 1);
                    float py = room.position.y + room.size.y * 0.1f;
                    list.Add(new Opening
                    {
                        type = Opening.OpeningType.Window,
                        position = new Vector2(px, py),
                        size = new Vector2(width, height),
                        revealDepth = 0.15f,
                        baseHeight = floor.y + 1.0f,
                        isExternal = external
                    });
                }
            }
        }
    }

    // ---- geometry helpers (shared with ExteriorDetailSystem) ----

    /// <summary>True if the room lies within a wall-thickness tolerance of an exterior wall.</summary>
    public static bool IsRoomOnExteriorWall(Building b, Room room, int floorIndex)
    {
        float t = ExteriorWallTolerance;
        return room.position.x <= t ||
               room.position.y <= t ||
               b.footprint.x - (room.position.x + room.size.x) <= t ||
               b.footprint.y - (room.position.y + room.size.y) <= t;
    }

    public static float WallLength(Building b, int wall)
    {
        return (wall % 2 == 0) ? b.footprint.x : b.footprint.y;
    }

    /// <summary>Nearest wall to a building-local point; s is the distance along that wall.</summary>
    public static int NearestWall(Building b, Vector2 p, out float s)
    {
        float w = b.footprint.x, d = b.footprint.y;
        int wall = 0; float best = p.y;
        if (w - p.x < best) { best = w - p.x; wall = 1; }
        if (d - p.y < best) { best = d - p.y; wall = 2; }
        if (p.x < best) { best = p.x; wall = 3; }
        s = Mathf.Clamp((wall % 2 == 0) ? p.x : p.y, 0f, WallLength(b, wall));
        return wall;
    }

    public static Vector2 PointOnWall(Building b, int wall, float s)
    {
        switch (wall)
        {
            case 0: return new Vector2(s, 0f);
            case 1: return new Vector2(b.footprint.x, s);
            case 2: return new Vector2(s, b.footprint.y);
            default: return new Vector2(0f, s);
        }
    }

    public static Vector2 WallNormal(int wall)
    {
        switch (wall)
        {
            case 0: return new Vector2(0f, -1f);
            case 1: return new Vector2(1f, 0f);
            case 2: return new Vector2(0f, 1f);
            default: return new Vector2(-1f, 0f);
        }
    }

    /// <summary>Local yaw (degrees) that makes a piece face outward from the given wall.</summary>
    public static float WallYawDeg(int wall)
    {
        switch (wall)
        {
            case 0: return 180f;
            case 1: return 90f;
            case 2: return 0f;
            default: return 270f;
        }
    }

    /// <summary>Building-local (corner origin) to world, matching Quaternion.Euler(0, yaw, 0).</summary>
    public static Vector3 LocalToWorld(Building b, Vector2 local, float height = 0f)
    {
        float cx = local.x - b.footprint.x * 0.5f;
        float cz = local.y - b.footprint.y * 0.5f;
        float cos = Mathf.Cos(b.yaw), sin = Mathf.Sin(b.yaw);
        float wx = cx * cos + cz * sin;
        float wz = -cx * sin + cz * cos;
        return new Vector3(b.position.x + wx, b.position.y + height, b.position.z + wz);
    }

    /// <summary>World (x,z) to building-local (corner origin). Inverse of LocalToWorld.</summary>
    public static Vector2 WorldToLocal(Building b, float worldX, float worldZ)
    {
        float dx = worldX - b.position.x;
        float dz = worldZ - b.position.z;
        float cos = Mathf.Cos(b.yaw), sin = Mathf.Sin(b.yaw);
        float lx = dx * cos - dz * sin;
        float lz = dx * sin + dz * cos;
        return new Vector2(lx + b.footprint.x * 0.5f, lz + b.footprint.y * 0.5f);
    }

    // ---- hash ----
static int ComputeHash(List<Building> list)
    {
        unchecked
        {
            int hs = (int)2166136261u;
            hs = (hs ^ list.Count) * 16777619;
            foreach (var b in list)
            {
                hs = (hs ^ b.id) * 16777619;
                hs = (hs ^ b.floors) * 16777619;
                hs = (hs ^ (int)b.roof.type) * 16777619;
                hs = (hs ^ (int)(b.position.x * 1000f)) * 16777619;
                hs = (hs ^ (int)(b.position.z * 1000f)) * 16777619;
                hs = (hs ^ b.exterior.openings.Count) * 16777619;
                int rooms = 0;
                foreach (var f in b.floorPlan) rooms += f.rooms.Count;
                hs = (hs ^ rooms) * 16777619;
            }
            return hs;
        }
    }

    /// <summary>
    /// Validates the building data (structure + determinism). Detailed integration checks
    /// live in BuildingValidator.cs.
    /// </summary>
    public static string Validate()
    {
        if (!Built)
        {
            Report = "[BuildingSystem] Not built.";
            return Report;
        }

        var sb = new StringBuilder("[BuildingSystem 5.0]\n");
        int pass = 0;
        var fails = new List<string>();

        int total = Buildings.Count;
        int nulls = 0, badIds = 0, missingParts = 0, badFloors = 0, badFootprint = 0, noDoor = 0, badFloorPlan = 0;

        for (int i = 0; i < Buildings.Count; i++)
        {
            var b = Buildings[i];
            if (b == null) { nulls++; continue; }
            if (b.id != i) badIds++;
            if (b.exterior == null || b.interior == null || b.yard == null || b.roof == null || b.floorPlan == null)
            { missingParts++; continue; }
            if (b.floors < 1 || b.floors > 3) badFloors++;
            if (b.footprint.x <= 0.1f || b.footprint.y <= 0.1f) badFootprint++;
            if (b.floorPlan.Count != b.floors) badFloorPlan++;

            bool hasDoor = false;
            foreach (var o in b.exterior.openings)
                if (o.type == Opening.OpeningType.Door) { hasDoor = true; break; }
            if (!hasDoor) noDoor++;
        }

        if (nulls == 0) pass++; else fails.Add(nulls + " null buildings");
        if (badIds == 0) pass++; else fails.Add(badIds + " buildings whose id != list index");
        if (missingParts == 0) pass++; else fails.Add(missingParts + " buildings missing exterior/interior/yard/roof/floor plan");
        if (badFloors == 0) pass++; else fails.Add(badFloors + " buildings with floor count outside 1-3");
        if (badFootprint == 0) pass++; else fails.Add(badFootprint + " buildings with degenerate footprint");
        if (badFloorPlan == 0) pass++; else fails.Add(badFloorPlan + " buildings whose floor plan count != floors");
        if (noDoor == 0) pass++; else fails.Add(noDoor + " buildings without an entry door");

        // determinism: regenerate from the same inputs and compare hashes
        // determinism: regenerate into a scratch list (does not touch Buildings) and compare hashes
        var scratch = new List<Building>();
        Generate(scratch);
        int hash2 = ComputeHash(scratch);
        if (Hash == hash2) pass++; else fails.Add("building assembly is not deterministic");

        sb.AppendLine("buildings: " + total);
        sb.AppendLine("null: " + nulls + " | bad ids: " + badIds + " | missing parts: " + missingParts);
        sb.AppendLine("bad floors: " + badFloors + " | bad footprint: " + badFootprint + " | floor plan mismatch: " + badFloorPlan);
        sb.AppendLine("without entry door: " + noDoor + "/" + total);
        sb.AppendLine("RULES (BuildingSystem): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);

        Report = sb.ToString();
        return Report;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Building System Validate")]
    static void Menu()
    {
        Built = false;
        Build();
        Debug.Log(Report);
    }
#endif
}
