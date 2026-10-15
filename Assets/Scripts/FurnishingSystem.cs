using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static BuildingSystem; // To reuse Building, Room, Furniture, etc.

/// <summary>
/// Furnishing system for Phase 5 - generates furniture sets for rooms and handles lighting.
/// Integrates with SettlementMap and ArchitecturalKit to provide appropriate furniture
/// based on household type and style.
/// </summary>
public static class FurnishingSystem
{
    // Furniture piece definition (kit of parts)
    public class FurniturePiece
    {
        public string id;              // Unique identifier (type-style-variant format)
        public string type;            // Piece type (bed, table, hearth, shelf, tool, etc.)
        public string style;           // Style classification (Rustic, Refined, etc.)
        public Vector2 size;           // Dimensions (x, z) in meters
        public bool isBuiltIn;         // Whether furniture is built-in/mounted
        public Vector2 originOffset;   // Offset from pivot to origin for snapping (typically half-size)
    }

    // Static data
    public static readonly List<FurniturePiece> Pieces = new List<FurniturePiece>();
    public static bool Built;
    public static string Report = "";
    static int Hash;

    // Main build method
    public static void Build()
    {
        Clear();

        // Generate all furniture pieces
        GenerateBeds();
        GenerateTables();
        GenerateHearths();
        GenerateShelves();
        GenerateTools();
        GenerateChests();
        GenerateWardrobes();
        GenerateBenches();
        GenerateWorkbenches();
        GenerateToolRacks();

        // Set hash for determinism testing
        Hash = GenerateHash();

        Built = true;
        Validate();
    }

    // Clear all data
    public static void Clear()
    {
        Pieces.Clear();
        Built = false;
        Report = "";
        Hash = 0;
    }

    // Validate the furniture kit
public static string Validate()
    {
        if (!Built)
        {
            Build(); // Build() runs Validate() itself and fills Report
            return Report;
        }

        var report = new StringBuilder();

        // main (standalone) pieces
        int mainPieceCount = 0;
        foreach (var piece in Pieces)
            if (!piece.isBuiltIn) mainPieceCount++;
        if (mainPieceCount < 10)
            report.Append("Main piece count " + mainPieceCount + " is low (consider adding more varieties). ");

        // duplicate IDs
        var idSet = new HashSet<string>();
        foreach (var piece in Pieces)
        {
            if (!string.IsNullOrEmpty(piece.id) && !idSet.Add(piece.id))
                report.Append("Duplicate piece ID found: " + piece.id + ". ");
        }

        // per-piece checks + style counts
        var styleCounts = new Dictionary<string, int>();
        foreach (var name in StyleNames) styleCounts[name] = 0;
        foreach (var piece in Pieces)
        {
            if (piece.size.x < 0.2f || piece.size.y < 0.2f)
                report.Append("Piece " + piece.id + " has invalid dimensions: " + piece.size + ". ");

            if (styleCounts.ContainsKey(piece.style)) styleCounts[piece.style]++;
            else report.Append("Piece " + piece.id + " has unknown style: " + piece.style + ". ");
        }

        // style distribution: no style < 10% or > 50% of total
        int totalPieces = Pieces.Count;
        if (totalPieces > 0)
        {
            foreach (var name in StyleNames)
            {
                float pct = (float)styleCounts[name] / totalPieces;
                if (pct < 0.1f || pct > 0.5f + 0.001f)
                    report.Append("Style distribution unbalanced: " + name + "=" + pct.ToString("P1") + ". ");
            }
        }

        // determinism: regenerate into the list and compare hashes, then restore the original pieces
        int h1 = Hash;
        var snapshot = new List<FurniturePiece>(Pieces);
        Pieces.Clear();
        GenerateAllPiecesWithoutValidation();
        int h2 = GenerateHash();
        Pieces.Clear();
        Pieces.AddRange(snapshot);
        if (h1 != h2)
            report.Append("Determinism check failed: generating kit twice yields different hashes. ");

        Report = report.Length == 0
            ? "FurnishingSystem validation passed. Generated " + mainPieceCount + " main furniture pieces."
            : report.ToString().Trim();
        return Report;
    }

    // Generate all pieces without validation (for determinism check)
    private static void GenerateAllPiecesWithoutValidation()
    {
        GenerateBeds();
        GenerateTables();
        GenerateHearths();
        GenerateShelves();
        GenerateTools();
        GenerateChests();
        GenerateWardrobes();
        GenerateBenches();
        GenerateWorkbenches();
        GenerateToolRacks();
    }

    // Generate a hash for determinism testing
    private static int GenerateHash()
    {
        int hash = 17;
        foreach (var piece in Pieces)
        {
            hash = hash * 31 + piece.id.GetHashCode();
            hash = hash * 31 + piece.type.GetHashCode();
            hash = hash * 31 + piece.style.GetHashCode();
            hash = hash * 31 + piece.size.GetHashCode();
            hash = hash * 31 + piece.isBuiltIn.GetHashCode();
            hash = hash * 31 + piece.originOffset.GetHashCode();
        }
        return hash;
    }

    // Piece generation methods
    private static void GenerateBeds()
    {
        // Beds: 6 varieties (3 sizes × 2 styles)
        string[] sizes = { "Single", "Double", "Queen" };
        string[] styles = StylePair(0);

        int variant = 0;
        foreach (var size in sizes)
        {
            foreach (var style in styles)
            {
                float width = 0.9f; // Standard door width
                float length = size switch
                {
                    "Single" => 1.9f,
                    "Double" => 1.9f,
                    "Queen" => 2.0f,
                    _ => 1.9f
                };

                CreatePiece($"{size}-Bed-{style}-{variant}", "Bed", style,
                    new Vector2(width, length),
                    false,
                    new Vector2(width * 0.5f, length * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateTables()
    {
        // Tables: 8 varieties (4 types × 2 styles)
        string[] types = { "Dining", "Coffee", "Side", "Workbench" };
        string[] styles = StylePair(1);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "Dining" => new Vector2(1.5f, 0.9f),
                    "Coffee" => new Vector2(0.8f, 0.8f),
                    "Side" => new Vector2(0.5f, 0.5f),
                    "Workbench" => new Vector2(1.2f, 0.6f),
                    _ => new Vector2(1.0f, 1.0f)
                };

                CreatePiece($"{type}-Table-{style}-{variant}", "Table", style,
                    size,
                    false,
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateHearths()
    {
        // Hearths: 4 varieties (2 types × 2 styles)
        string[] types = { "Open", "Enclosed" };
        string[] styles = StylePair(2);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "Open" => new Vector2(1.0f, 0.6f),
                    "Enclosed" => new Vector2(1.2f, 0.8f),
                    _ => new Vector2(1.0f, 0.6f)
                };

                CreatePiece($"{type}-Hearth-{style}-{variant}", "Hearth", style,
                    size,
                    true, // Hearths are typically built-in
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateShelves()
    {
        // Shelves: 8 varieties (4 types × 2 styles)
        string[] types = { "Wall", "Floor", "Corner", "Display" };
        string[] styles = StylePair(3);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "Wall" => new Vector2(1.2f, 0.2f),
                    "Floor" => new Vector2(0.6f, 0.6f),
                    "Corner" => new Vector2(0.8f, 0.8f),
                    "Display" => new Vector2(1.0f, 0.4f),
                    _ => new Vector2(1.0f, 1.0f)
                };

                CreatePiece($"{type}-Shelf-{style}-{variant}", "Shelf", style,
                    size,
                    true, // Shelves are often built-in
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateTools()
    {
        // Tool furniture: 4 varieties (2 types × 2 styles)
        string[] types = { "WallRack", "FloorStand" };
        string[] styles = StylePair(4);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "WallRack" => new Vector2(1.0f, 0.2f),
                    "FloorStand" => new Vector2(0.6f, 1.2f),
                    _ => new Vector2(1.0f, 1.0f)
                };

                CreatePiece($"{type}-Tool-{style}-{variant}", "Tool", style,
                    size,
                    false,
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateChests()
    {
        // Chests: 4 varieties (2 sizes × 2 styles)
        string[] sizes = { "Small", "Large" };
        string[] styles = StylePair(5);

        int variant = 0;
        foreach (var size in sizes)
        {
            foreach (var style in styles)
            {
                Vector2 sizeVec = size switch
                {
                    "Small" => new Vector2(0.6f, 0.4f),
                    "Large" => new Vector2(1.0f, 0.6f),
                    _ => new Vector2(0.8f, 0.5f)
                };

                CreatePiece($"{size}-Chest-{style}-{variant}", "Chest", style,
                    sizeVec,
                    false,
                    new Vector2(sizeVec.x * 0.5f, sizeVec.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateWardrobes()
    {
        // Wardrobes: 4 varieties (2 types × 2 styles)
        string[] types = { "Single", "Double" };
        string[] styles = StylePair(6);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "Single" => new Vector2(0.6f, 1.8f),
                    "Double" => new Vector2(1.2f, 1.8f),
                    _ => new Vector2(0.9f, 1.8f)
                };

                CreatePiece($"{type}-Wardrobe-{style}-{variant}", "Wardrobe", style,
                    size,
                    false,
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateBenches()
    {
        // Benches: 4 varieties (2 types × 2 styles)
        string[] types = { "Indoor", "Garden" };
        string[] styles = StylePair(7);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                Vector2 size = type switch
                {
                    "Indoor" => new Vector2(1.2f, 0.4f),
                    "Garden" => new Vector2(1.5f, 0.5f),
                    _ => new Vector2(1.0f, 0.5f)
                };

                CreatePiece($"{type}-Bench-{style}-{variant}", "Bench", style,
                    size,
                    false,
                    new Vector2(size.x * 0.5f, size.y * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateWorkbenches()
    {
        // Workbenches: 2 varieties (1 type × 2 styles) - already covered in Tables as Workbench
        // But we can add specific workbench varieties if needed
        // For now, we rely on the GenerateTables method for workbenches
    }

    private static void GenerateToolRacks()
    {
        // Tool racks: 2 varieties (1 type × 2 styles) - already covered in Tools as WallRack
        // For now, we rely on the GenerateTools method for tool racks
    }

    // Helper method to create a furniture piece and add it to the collection
    private static void CreatePiece(string id, string type, string style, Vector2 size, bool isBuiltIn, Vector2 originOffset)
    {
        Pieces.Add(new FurniturePiece
        {
            id = id,
            type = type,
            style = style,
            size = size,
            isBuiltIn = isBuiltIn,
            originOffset = originOffset
        });
    }

    // Generate furniture for a room based on room type and household type
    public static List<Furniture> GenerateFurnitureForRoom(Room room, string householdType)
    {
        var furniture = new List<Furniture>();

        // Select appropriate furniture pieces based on room type and household type
        switch (room.roomType)
        {
            case "kitchen":
                furniture.AddRange(GenerateKitchenFurniture(room, householdType));
                break;
            case "living":
                furniture.AddRange(GenerateLivingFurniture(room, householdType));
                break;
            case "bedroom":
                furniture.AddRange(GenerateBedroomFurniture(room, householdType));
                break;
            case "entry":
                furniture.AddRange(GenerateEntryFurniture(room, householdType));
                break;
            case "pantry":
            case "storage":
                furniture.AddRange(GenerateStorageFurniture(room, householdType));
                break;
            case "byre":
                furniture.AddRange(GenerateByreFurniture(room, householdType));
                break;
            case "net":
                furniture.AddRange(GenerateNetFurniture(room, householdType));
                break;
            case "shop":
                furniture.AddRange(GenerateShopFurniture(room, householdType));
                break;
            case "tool":
                furniture.AddRange(GenerateToolFurniture(room, householdType));
                break;
            default:
                // Fallback: generate some basic furniture
                furniture.AddRange(GenerateGenericFurniture(room, householdType));
                break;
        }

        return furniture;
    }

    // Kitchen furniture generation
    private static List<Furniture> GenerateKitchenFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Hearth/fireplace (almost always in kitchen)
        furn.Add(new Furniture { furnitureType = "hearth", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.1f), size = new Vector2(1f, 0.5f), isBuiltIn = true });

        // Table
        furn.Add(new Furniture { furnitureType = "table", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(1.2f, 0.8f), isBuiltIn = false });

        // Shelves
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.2f), isBuiltIn = true });

        // Optional: chest for storage
        if (householdType == "farmer" || householdType == "fisher")
        {
            furn.Add(new Furniture { furnitureType = "chest", position = new Vector2(0.7f, room.size.y * 0.2f), size = new Vector2(0.6f, 0.5f), isBuiltIn = false });
        }

        return furn;
    }

    // Living room furniture generation
    private static List<Furniture> GenerateLivingFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Table (coffee or dining depending on room size)
        bool isLargeRoom = room.size.x > 4f && room.size.y > 3f;
        furn.Add(new Furniture { furnitureType = isLargeRoom ? "table" : "table",
            position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f),
            size = isLargeRoom ? new Vector2(1.5f, 1.0f) : new Vector2(1.0f, 0.8f),
            isBuiltIn = false });

        // Seating (benches/chairs)
        int seatCount = Mathf.FloorToInt(room.size.x / 2.0f);
        for (int i = 0; i < seatCount; i++)
        {
            float x = 0.3f + (i * (room.size.x - 0.6f) / Mathf.Max(seatCount - 1, 1));
            furn.Add(new Furniture { furnitureType = "seat", position = new Vector2(x, room.size.y * 0.3f), size = new Vector2(0.5f, 0.4f), isBuiltIn = false });
        }

        // Shelves/cupboards
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.2f), isBuiltIn = true });

        return furn;
    }

    // Bedroom furniture generation
    private static List<Furniture> GenerateBedroomFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Bed
        string bedSize = householdType == "shopkeeper" ? "Single" : "Double"; // Shopkeepers might have smaller beds
        float bedWidth = 0.9f;
        float bedLength = bedSize switch
        {
            "Single" => 1.9f,
            "Double" => 1.9f,
            _ => 1.9f
        };
        furn.Add(new Furniture { furnitureType = "bed", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(bedWidth, bedLength), isBuiltIn = false });

        // Chest/storage
        furn.Add(new Furniture { furnitureType = "chest", position = new Vector2(room.size.x * 0.2f, room.size.y * 0.2f), size = new Vector2(0.6f, 0.5f), isBuiltIn = false });

        // Wardrobe (if room is large enough)
        if (room.size.x > 3f && room.size.y > 3f)
        {
            furn.Add(new Furniture { furnitureType = "wardrobe", position = new Vector2(room.size.x * 0.7f, room.size.y * 0.2f), size = new Vector2(0.6f, 1.8f), isBuiltIn = false });
        }

        return furn;
    }

    // Entry furniture generation
    private static List<Furniture> GenerateEntryFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Bench or hook rack
        furn.Add(new Furniture { furnitureType = "bench", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.2f), size = new Vector2(0.8f, 0.4f), isBuiltIn = false });

        // Shelf for keys/etc
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.1f), isBuiltIn = true });

        return furn;
    }

    // Storage/pantry furniture generation
    private static List<Furniture> GenerateStorageFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Shelves/storage units
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(room.size.x * 0.8f, room.size.y * 0.8f), isBuiltIn = false });

        // Optional: chest for valuable storage
        if (householdType == "shopkeeper")
        {
            furn.Add(new Furniture { furnitureType = "chest", position = new Vector2(0.7f, room.size.y * 0.2f), size = new Vector2(0.6f, 0.5f), isBuiltIn = false });
        }

        return furn;
    }

    // Byre (animal shelter) furniture generation
    private static List<Furniture> GenerateByreFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Byres typically have minimal furniture: maybe a feed rack or tool hook
        furn.Add(new Furniture { furnitureType = "toolrack", position = new Vector2(0.5f, room.size.y * 0.5f), size = new Vector2(room.size.x * 0.6f, 0.3f), isBuiltIn = true });

        return furn;
    }

    // Net room (fishing nets) furniture generation
    private static List<Furniture> GenerateNetFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Net storage and repair tools
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.2f), isBuiltIn = true });
        furn.Add(new Furniture { furnitureType = "toolrack", position = new Vector2(0.7f, room.size.y * 0.2f), size = new Vector2(room.size.x * 0.6f, 0.3f), isBuiltIn = true });

        return furn;
    }

    // Shop furniture generation
    private static List<Furniture> GenerateShopFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Counter/display
        furn.Add(new Furniture { furnitureType = "table", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(2.0f, 0.8f), isBuiltIn = false });

        // Shelves for goods
        furn.Add(new Furniture { furnitureType = "shelf", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.3f), isBuiltIn = true });

        // Optional: chest for money/valuables
        furn.Add(new Furniture { furnitureType = "chest", position = new Vector2(0.8f, room.size.y * 0.2f), size = new Vector2(0.6f, 0.5f), isBuiltIn = false });

        return furn;
    }

    // Tool room furniture generation
    private static List<Furniture> GenerateToolFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Workbench
        furn.Add(new Furniture { furnitureType = "workbench", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(1.2f, 0.6f), isBuiltIn = false });

        // Tool rack
        furn.Add(new Furniture { furnitureType = "toolrack", position = new Vector2(0.2f, room.size.y * 0.7f), size = new Vector2(room.size.x * 0.6f, 0.3f), isBuiltIn = true });

        // Optional: chest for small parts
        furn.Add(new Furniture { furnitureType = "chest", position = new Vector2(0.7f, room.size.y * 0.2f), size = new Vector2(0.6f, 0.5f), isBuiltIn = false });

        return furn;
    }

    // Generic furniture generation (fallback)
    private static List<Furniture> GenerateGenericFurniture(Room room, string householdType)
    {
        var furn = new List<Furniture>();

        // Add a basic table and chair
        furn.Add(new Furniture { furnitureType = "table", position = new Vector2(room.size.x * 0.5f, room.size.y * 0.5f), size = new Vector2(1.0f, 0.8f), isBuiltIn = false });
        furn.Add(new Furniture { furnitureType = "seat", position = new Vector2(room.size.x * 0.3f, room.size.y * 0.3f), size = new Vector2(0.5f, 0.4f), isBuiltIn = false });

        return furn;
    }

    // Set lighting properties for a building based on its rooms and features
    public static void SetBuildingLighting(Building building)
    {
        if (building == null || building.interior == null)
            return;

        // Hearth glow: present if any room has a hearth
        bool hasHearthGlow = false;
        foreach (var floor in building.floorPlan)
        {
            foreach (var room in floor.rooms)
            {
                if (room.hasHearth)
                {
                    hasHearthGlow = true;
                    break;
                }
            }
            if (hasHearthGlow) break;
        }
        building.interior.hasHearthGlow = hasHearthGlow;

        // Lantern light: based on exterior lanterns (from BuildingSystem) or interior lanterns
        // We'll reuse the exterior hasLanterns property if available, otherwise default to false
        building.interior.hasLanternLight = building.exterior.hasLanterns;

        // Floor material: determine based on room types (already set in BuildingSystem, but we can override or refine)
        // For now, we leave it to BuildingSystem's GenerateFloorMaterial method
        // But we can adjust based on room types if needed
        // Example: kitchens and bathrooms might be tiled, bedrooms timber, etc.
        // We'll compute a predominant floor material for the building
        building.interior.floorMaterial = DeterminePredominantFloorMaterial(building);
    }

    // Determine the predominant floor material for a building based on room types
    private static string DeterminePredominantFloorMaterial(Building building)
    {
        // Count room types that favor certain materials
        int tileCount = 0;   // kitchens, bathrooms
        int timberCount = 0; // living rooms, bedrooms
        int stoneCount = 0;  // entries, storage
        int dirtCount = 0;   // utility rooms, byres, net rooms

        foreach (var floor in building.floorPlan)
        {
            foreach (var room in floor.rooms)
            {
                switch (room.roomType)
                {
                    case "kitchen":
                    case "pantry": // pantries might be tile or stone
                        tileCount++;
                        break;
                    case "living":
                    case "bedroom":
                        timberCount++;
                        break;
                    case "entry":
                    case "storage":
                        stoneCount++;
                        break;
                    case "byre":
                    case "net":
                    case "tool":
                        dirtCount++;
                        break;
                    default:
                        // Default to timber for unknown rooms
                        timberCount++;
                        break;
                }
            }
        }

        // Determine the material with the highest count
        int maxCount = Mathf.Max(tileCount, Mathf.Max(timberCount, Mathf.Max(stoneCount, dirtCount)));
        if (maxCount == tileCount) return "tiled";
        if (maxCount == timberCount) return "timber";
        if (maxCount == stoneCount) return "stone";
        return "dirt";
    }

    #if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 5.6 Furnishing System + Validate")]
    static void Menu() { Built = false; Build(); Debug.Log(Validate()); }
    #endif


static readonly string[] StyleNames = { "Rustic", "Refined", "Ornate", "Vernacular", "Minimalist" };

    // Each category uses two neighbouring styles, rotating by category so every style stays in the 10-50% band.
    static string[] StylePair(int category)
    {
        return new[] { StyleNames[category % StyleNames.Length], StyleNames[(category + 1) % StyleNames.Length] };
    }
}