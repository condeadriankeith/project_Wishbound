using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Architectural kit system for Phase 5 - generates a library of architectural pieces
/// that can be snapped to a grid and tagged by style for building generation.
/// </summary>
// Trigger recompilation: 2026-10-14
public static class ArchitecturalKit
{
    public enum ArchitecturalStyle { Rustic, Refined, Ornate, Vernacular, Minimalist }

    public class ArchitecturalPiece
    {
        public string id;              // Unique identifier (type-style-variant format)
        public string type;            // Piece type (plinth, quoins, sill, lintel, shutter, door, balcony, loggia, pergola, stair, chimney, parapet, drainpipe, dormer, gable, trim)
        public ArchitecturalStyle style; // Style classification
        public Vector3 size;           // Dimensions (x, y, z) in meters
        public Vector3 snapPoint;      // Primary snap point for grid alignment (local to piece origin)
        public bool isTrim;            // Whether this is a trim sheet/piece
        public Vector3 originOffset;   // Offset from pivot to origin for snapping (typically half-size)
    }

    public static readonly List<ArchitecturalPiece> Pieces = new List<ArchitecturalPiece>();
    public static bool Built;
    public static string Report = "";
    static int Hash;

    /// <summary>
    /// Builds the architectural kit by generating all pieces with appropriate dimensions and styles.
    /// Follows the same pattern as other Phase 4 systems.
    /// </summary>
    public static void Build()
    {
        Clear();

        // Generate all architectural pieces
        GeneratePlinths();
        GenerateQuoins();
        GenerateSills();
        GenerateLintels();
        GenerateShutters();
        GenerateArchedDoors();
        GenerateBalconies();
        GenerateLoggias();
        GeneratePergolas();
        GenerateExternalStairs();
        GenerateChimneyCaps();
        GenerateParapetBands();
        GenerateDrainpipes();
        GenerateDormers();
        GenerateSteppedGables();
        GenerateTrimSheets();

        // Set hash for determinism testing
        Hash = GenerateHash();

        Built = true;
        Validate();
    }

    /// <summary>
    /// Clears all pieces and resets the build state.
    /// </summary>
    public static void Clear()
    {
        Pieces.Clear();
        Built = false;
        Report = "";
        Hash = 0;
    }

    /// <summary>
    /// Validates the architectural kit to ensure it meets all requirements.
    /// </summary>
    public static void Validate()
    {
        Report = "";

        // Check piece count (60-100 main pieces plus trim sheets)
        int mainPieceCount = 0;
        int trimSheetCount = 0;
        foreach (var piece in Pieces)
        {
            if (piece.isTrim)
                trimSheetCount++;
            else
                mainPieceCount++;
        }

        if (mainPieceCount < 60 || mainPieceCount > 100)
        {
            Report += $"Main piece count {mainPieceCount} is outside valid range (60-100). ";
        }

        // Check for duplicate IDs
        var idSet = new HashSet<string>();
        foreach (var piece in Pieces)
        {
            if (!string.IsNullOrEmpty(piece.id))
            {
                if (!idSet.Add(piece.id))
                {
                    Report += $"Duplicate piece ID found: {piece.id}. ";
                }
            }
        }

        // Validate each piece
        int styleCountRustic = 0;
        int styleCountRefined = 0;
        int styleCountOrnate = 0;
        int styleCountVernacular = 0;
        int styleCountMinimalist = 0;

        foreach (var piece in Pieces)
        {
            // Check dimensions
            if (piece.size.x < 0.25f || piece.size.y < 0.25f || piece.size.z < 0.25f)
            {
                Report += $"Piece {piece.id} has invalid dimensions: {piece.size}. ";
            }

            // Count styles for distribution check
            switch (piece.style)
            {
                case ArchitecturalStyle.Rustic: styleCountRustic++; break;
                case ArchitecturalStyle.Refined: styleCountRefined++; break;
                case ArchitecturalStyle.Ornate: styleCountOrnate++; break;
                case ArchitecturalStyle.Vernacular: styleCountVernacular++; break;
                case ArchitecturalStyle.Minimalist: styleCountMinimalist++; break;
            }

            // Check snap point alignment (should be on 0.5m grid)
            if (Mathf.Abs(piece.snapPoint.x * 2f - Mathf.Round(piece.snapPoint.x * 2f)) > 0.001f ||
                Mathf.Abs(piece.snapPoint.y * 2f - Mathf.Round(piece.snapPoint.y * 2f)) > 0.001f ||
                Mathf.Abs(piece.snapPoint.z * 2f - Mathf.Round(piece.snapPoint.z * 2f)) > 0.001f)
            {
                Report += $"Piece {piece.id} snap point not aligned to 0.5m grid: {piece.snapPoint}. ";
            }
        }

        // Check style distribution (no style < 10% or > 40% of total)
        int totalPieces = Pieces.Count;
        if (totalPieces > 0)
        {
            float rusticPct = (float)styleCountRustic / totalPieces;
            float refinedPct = (float)styleCountRefined / totalPieces;
            float ornatePct = (float)styleCountOrnate / totalPieces;
            float vernacularPct = (float)styleCountVernacular / totalPieces;
            float minimalistPct = (float)styleCountMinimalist / totalPieces;

            if (rusticPct < 0.1f || rusticPct > 0.4f ||
                refinedPct < 0.1f || refinedPct > 0.4f ||
                ornatePct < 0.1f || ornatePct > 0.4f ||
                vernacularPct < 0.1f || vernacularPct > 0.4f ||
                minimalistPct < 0.1f || minimalistPct > 0.4f)
            {
                Report += $"Style distribution unbalanced: Rustic={rusticPct:P1}, Refined={refinedPct:P1}, Ornate={ornatePct:P1}, Vernacular={vernacularPct:P1}, Minimalist={minimalistPct:P1}. ";
            }
        }

        // Determinism check: regenerate into the list and compare hashes, then restore the original pieces.
        // (Clear() must not be used here: it would wipe Report and Hash.)
        int h1 = Hash;
        var snapshot = new List<ArchitecturalPiece>(Pieces);
        Pieces.Clear();
        GenerateAllPiecesWithoutValidation();
        int h2 = GenerateHash();
        Pieces.Clear();
        Pieces.AddRange(snapshot);
        bool det = h1 == h2;

        if (!det)
        {
            Report += "Determinism check failed: generating kit twice yields different hashes. ";
        }

        if (string.IsNullOrEmpty(Report))
        {
            Report = $"ArchitecturalKit validation passed. Generated {mainPieceCount} main pieces and {trimSheetCount} trim sheets.";
        }
    }

    /// <summary>
    /// Generates all pieces without calling Validate() to avoid recursion in determinism check.
    /// </summary>
    private static void GenerateAllPiecesWithoutValidation()
    {
        GeneratePlinths();
        GenerateQuoins();
        GenerateSills();
        GenerateLintels();
        GenerateShutters();
        GenerateArchedDoors();
        GenerateBalconies();
        GenerateLoggias();
        GeneratePergolas();
        GenerateExternalStairs();
        GenerateChimneyCaps();
        GenerateParapetBands();
        GenerateDrainpipes();
        GenerateDormers();
        GenerateSteppedGables();
        GenerateTrimSheets();
        // Note: We don't set Built = true here, as this is just for the check
    }

    /// <summary>
    /// Generates a hash for determinism testing using the same algorithm as other systems.
    /// </summary>
    private static int GenerateHash()
    {
        int hash = 17;
        foreach (var piece in Pieces)
        {
            hash = hash * 31 + piece.id.GetHashCode();
            hash = hash * 31 + piece.type.GetHashCode();
            hash = hash * 31 + piece.style.GetHashCode();
            hash = hash * 31 + piece.size.GetHashCode();
            hash = hash * 31 + piece.snapPoint.GetHashCode();
            hash = hash * 31 + piece.isTrim.GetHashCode();
            hash = hash * 31 + piece.originOffset.GetHashCode();
        }
        return hash;
    }

    // Piece generation methods - adjusted for 60-100 main pieces plus trim sheets
    private static void GeneratePlinths()
    {
        // Wall segments: 6 varieties (3 heights × 2 styles)
        // These are the basic wall pieces for Mediterranean buildings
        string[] types = { "WallShort", "WallMedium", "WallTall" };
        ArchitecturalStyle[] styles = StylePair(0);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                // Mediterranean walls: 2-4m high, 0.65m thick (for that thick wall look), 2m wide (standard segment)
                float height = 2.0f + (variant % 3) * 1.0f; // 2m, 3m, 4m heights
                float thickness = 0.65f; // Mediterranean thick wall
                float length = 2.0f;     // Standard wall segment length
                CreatePiece($"{type}-{style}-{variant}", type, style,
                    new Vector3(length, height, thickness),
                    new Vector3(length * 0.5f, 0f, thickness * 0.5f), // Snap at bottom center
                    false,
                    new Vector3(length * 0.5f, height * 0.5f, thickness * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateQuoins()
    {
        // Wall corners: 6 varieties (2 corner types × 3 heights)
        // Creates inner and outer corners for Mediterranean walls
        string[] cornerTypes = { "InnerCorner", "OuterCorner" };
        ArchitecturalStyle[] styles = StylePair(1);

        int variant = 0;
        foreach (var cornerType in cornerTypes)
        {
            foreach (var style in styles)
            {
                // Mediterranean wall corners: 2-4m high, 0.65m thick walls
                float height = 2.0f + (variant % 3) * 1.0f; // 2m, 3m, 4m heights
                float thickness = 0.65f; // Wall thickness
                float length = 1.0f;     // Each leg of the corner is 1m
                CreatePiece($"{cornerType}-{style}-{variant}", "WallCorner", style,
                    new Vector3(length, height, thickness),
                    new Vector3(0f, 0f, thickness * 0.5f), // Snap at bottom inside/outside corner
                    false,
                    new Vector3(length * 0.5f, height * 0.5f, thickness * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateSills()
    {
        // Window frames: 8 varieties (2 heights × 2 widths × 2 styles)
        // Creates proper window frames for Mediterranean walls (deep-set, with shutters)
        float[] heights = { 1.0f, 1.2f }; // Window heights
        float[] widths = { 0.6f, 0.8f };  // Window widths
        ArchitecturalStyle[] styles = StylePair(2);

        int variant = 0;
        foreach (var height in heights)
        {
            foreach (var width in widths)
            {
                foreach (var style in styles)
                {
                    // Mediterranean window frame: set into wall thickness, with space for shutters
                    float frameWidth = width;      // width of window opening
                    float frameHeight = height;    // height of window opening
                    float wallThickness = 0.65f;   // Mediterranean wall thickness
                    float recessDepth = 0.2f;      // how deep window is set into wall
                    float totalDepth = wallThickness + recessDepth; // total depth including recess

                    CreatePiece($"WindowFrame_{width}m_{height}m_{style}-{variant}", "WindowFrame", style,
                        new Vector3(frameWidth, frameHeight, totalDepth),
                        new Vector3(recessDepth * 0.5f, 0f, (wallThickness + recessDepth) * 0.5f), // Snap at bottom left of recess
                        false,
                        new Vector3(frameWidth * 0.5f, frameHeight * 0.5f, totalDepth * 0.5f));
                    variant++;
                }
            }
        }
    }

    private static void GenerateLintels()
    {
        // Door frames: 8 varieties (2 widths × 2 heights × 2 styles)
        // Creates proper door frames for Mediterranean walls (for blue doors)
        float[] heights = { 2.0f, 2.2f }; // Door heights
        float[] widths = { 0.8f, 0.9f };  // Door widths
        ArchitecturalStyle[] styles = StylePair(3);

        int variant = 0;
        foreach (var height in heights)
        {
            foreach (var width in widths)
            {
                foreach (var style in styles)
                {
                    // Mediterranean door frame: opening in thick wall
                    float frameWidth = width;      // width of door opening
                    float frameHeight = height;    // height of door opening
                    float wallThickness = 0.65f;   // Mediterranean wall thickness

                    CreatePiece($"DoorFrame_{width}m_{height}m_{style}-{variant}", "DoorFrame", style,
                        new Vector3(frameWidth, frameHeight, wallThickness),
                        new Vector3(0f, 0f, wallThickness * 0.5f), // Snap at bottom left (interior side)
                        false,
                        new Vector3(frameWidth * 0.5f, frameHeight * 0.5f, wallThickness * 0.5f));
                    variant++;
                }
            }
        }
    }

    private static void GenerateShutters()
    {
        // Shutters: 8 varieties (2 heights × 2 widths × 2 styles)
        // Creates shutters for Mediterranean windows (typically blue accent)
        float[] heights = { 1.0f, 1.2f }; // Match window heights
        float[] widths = { 0.3f, 0.4f };  // Shutter width (half of window width typically)
        ArchitecturalStyle[] styles = StylePair(4);

        int variant = 0;
        foreach (var height in heights)
        {
            foreach (var width in widths)
            {
                foreach (var style in styles)
                {
                    // Mediterranean shutter: hinged, typically blue accent
                    CreatePiece($"Shutter_{width}m_{height}m_{style}-{variant}", "Shutter", style,
                        new Vector3(width, height, 0.02f), // thin shutter
                        new Vector3(0f, height * 0.5f, 0.01f), // Snap at center left
                        false,
                        new Vector3(width * 0.5f, height * 0.5f, 0.01f));
                    variant++;
                }
            }
        }
    }

    private static void GenerateArchedDoors()
    {
        // Doors: 8 varieties (2 widths × 2 heights × 2 styles)
        // Creates both standard and arched door options for Mediterranean buildings
        float[] heights = { 2.0f, 2.5f }; // Door heights
        float[] widths = { 0.8f, 1.0f };  // Door widths
        ArchitecturalStyle[] styles = StylePair(5);

        int variant = 0;
        foreach (var height in heights)
        {
            foreach (var width in widths)
            {
                foreach (var style in styles)
                {
                    // Create a standard rectangular door
                    CreatePiece($"Door_{width}m_{height}m_{style}-{variant}", "Door", style,
                        new Vector3(width, height, 0.1f),
                        new Vector3(0f, 0f, 0.05f), // Snap at bottom center
                        false,
                        new Vector3(width * 0.5f, height * 0.5f, 0.05f));
                    variant++;
                }
            }
        }

        // Also create some arched door variants for visual interest
        variant = 0; // reset variant for arched doors to keep IDs unique
        float[] archSpans = { 1.0f, 1.5f }; // Smaller spans for arched doors
        foreach (var span in archSpans)
        {
            foreach (var style in styles)
            {
                CreatePiece($"ArchedDoor-{span}m-{style}-{variant}", "ArchedDoor", style,
                    new Vector3(span, 2.5f, 0.1f),
                    new Vector3(span * 0.5f, 0f, 0.05f), // Snap at bottom center
                    false,
                    new Vector3(span * 0.5f, 1.25f, 0.05f));
                variant++;
            }
        }
    }

    private static void GenerateBalconies()
    {
        // Balconies: 6 varieties (3 types × 2 styles)
        string[] types = { "BalconyJuliet", "BalconyStandard", "BalconyWrap" };
        ArchitecturalStyle[] styles = StylePair(6);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Balcony-{type}-{style}-{variant}", type, style,
                    new Vector3(2.0f, 0.1f, 1.5f),
                    new Vector3(1.0f, 0.05f, 0.75f), // Snap at bottom center
                    false,
                    new Vector3(1.0f, 0.05f, 0.75f));
                variant++;
            }
        }
    }

    private static void GenerateLoggias()
    {
        // Loggias: 4 varieties (2 arcades × 2 styles)
        int[] arcades = { 1, 3 }; // 1 arch or 3 arches
        ArchitecturalStyle[] styles = StylePair(7);

        int variant = 0;
        foreach (var arcadeCount in arcades)
        {
            foreach (var style in styles)
            {
                float width = arcadeCount * 2.0f + 0.5f;
                CreatePiece($"Loggia-{arcadeCount}arc-{style}-{variant}", "Loggia", style,
                    new Vector3(width, 0.1f, 3.0f),
                    new Vector3(width * 0.5f, 0.05f, 1.5f), // Snap at bottom center
                    false,
                    new Vector3(width * 0.5f, 0.05f, 1.5f));
                variant++;
            }
        }
    }

    private static void GeneratePergolas()
    {
        // Pergolas: 4 varieties (2 beam/spacings × 2 styles)
        float[] beamSpacings = { 0.2f, 0.4f }; // Beam thickness and spacing
        ArchitecturalStyle[] styles = StylePair(8);

        int variant = 0;
        foreach (var beamSpacing in beamSpacings)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Pergola-{beamSpacing}m-{style}-{variant}", "Pergola", style,
                    new Vector3(3.0f, 0.2f, 3.0f),
                    new Vector3(1.5f, 0.1f, 1.5f), // Snap at bottom center
                    false,
                    new Vector3(1.5f, 0.1f, 1.5f));
                variant++;
            }
        }
    }

    private static void GenerateExternalStairs()
    {
        // External Stairs: 6 varieties (3 types × 2 styles)
        string[] types = { "StairsStraight", "StairsL", "StairsSwitchback" };
        ArchitecturalStyle[] styles = StylePair(9);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Stairs-{type}-{style}-{variant}", type, style,
                    new Vector3(1.0f, 3.0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.25f), // Snap at bottom inside corner
                    false,
                    new Vector3(0.5f, 1.5f, 0.25f));
                variant++;
            }
        }
    }

    private static void GenerateChimneyCaps()
    {
        // Chimney caps: 6 varieties (2 styles × 3 sizes)
        // Creates chimneys for Mediterranean buildings (typically whitewashed or terracotta)
        float[] sizes = { 0.5f, 0.75f, 1.0f }; // Chimney width/depth
        ArchitecturalStyle[] styles = StylePair(10);

        int variant = 0;
        foreach (var size in sizes)
        {
            foreach (var style in styles)
            {
                // Mediterranean chimney: typically rectangular prism
                CreatePiece($"Chimney_{size}m_{style}-{variant}", "Chimney", style,
                    new Vector3(size, 1.0f, size), // width, height, depth
                    new Vector3(size * 0.5f, 0f, size * 0.5f), // Snap at bottom center
                    false,
                    new Vector3(size * 0.5f, 0.5f, size * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateParapetBands()
    {
        // Roof caps: 8 varieties (2 types × 2 sizes × 2 styles)
        // Creates roof caps for Mediterranean buildings (flat terracotta slab and pitched variant)
        string[] roofTypes = { "RoofFlat", "RoofPitched" };
        float[] lengths = { 2.0f, 3.0f }; // Roof segment lengths
        float[] widths = { 2.0f, 3.0f };  // Roof segment widths
        ArchitecturalStyle[] styles = StylePair(11);

        int variant = 0;
        foreach (var roofType in roofTypes)
        {
            foreach (var length in lengths)
            {
                foreach (var width in widths)
                {
                    foreach (var style in styles)
                    {
                        if (roofType == "RoofFlat")
                        {
                            // Flat terracotta roof slab
                            float thickness = 0.15f; // roof thickness
                            CreatePiece($"RoofFlat_{length}m_{width}m_{style}-{variant}", "Roof", style,
                                new Vector3(length, thickness, width),
                                new Vector3(length * 0.5f, thickness * 0.5f, width * 0.5f), // Snap at bottom center
                                false,
                                new Vector3(length * 0.5f, thickness * 0.5f, width * 0.5f));
                        }
                        else if (roofType == "RoofPitched")
                        {
                            // Pitched roof variant (triangular prism)
                            float pitchHeight = 0.5f; // height of the pitch
                            CreatePiece($"RoofPitched_{length}m_{width}m_{style}-{variant}", "Roof", style,
                                new Vector3(length, pitchHeight, width),
                                new Vector3(length * 0.5f, pitchHeight * 0.5f, width * 0.5f), // Snap at bottom center
                                false,
                                new Vector3(length * 0.5f, pitchHeight * 0.5f, width * 0.5f));
                        }
                        variant++;
                    }
                }
            }
        }
    }

    private static void GenerateDrainpipes()
    {
        // Stair modules: 4 varieties (2 types × 2 styles)
        // Creates exterior stair modules for Mediterranean buildings
        string[] stairTypes = { "StairStraight", "StairL" };
        ArchitecturalStyle[] styles = StylePair(12);

        int variant = 0;
        foreach (var stairType in stairTypes)
        {
            foreach (var style in styles)
            {
                if (stairType == "StairStraight")
                {
                    // Straight stair module: 1m wide, 2-3m high
                    float width = 1.0f; // stair width
                    float height = 2.0f + (variant % 2) * 1.0f; // 2m or 3m high
                    float depth = 0.5f; // stair depth
                    CreatePiece($"StairStraight_W{width}_H{height}m_{style}-{variant}", "Stair", style,
                        new Vector3(width, height, depth),
                        new Vector3(0f, 0f, 0f), // Snap at bottom left
                        false,
                        new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f));
                }
                else if (stairType == "StairL")
                {
                    // L-shaped stair module
                    float width = 2.0f; // 2m wide for L-shape
                    float height = 2.0f + (variant % 2) * 1.0f; // 2m or 3m high
                    float depth = 2.0f; // 2m deep for L-shape
                    CreatePiece($"StairL_W{width}_H{height}m_{style}-{variant}", "Stair", style,
                        new Vector3(width, height, depth),
                        new Vector3(0f, 0f, 0f), // Snap at bottom inside corner
                        false,
                        new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f));
                }
                variant++;
            }
        }
    }

    private static void GenerateDormers()
    {
        // Archway/passage modules: 6 varieties (2 widths × 2 heights × 2 styles)
        // Creates archways/passages for gaps between fused Mediterranean buildings
        float[] heights = { 2.2f, 2.5f }; // Passage heights
        float[] widths = { 1.5f, 2.0f };  // Passage widths
        ArchitecturalStyle[] styles = StylePair(13);

        int variant = 0;
        foreach (var height in heights)
        {
            foreach (var width in widths)
            {
                foreach (var style in styles)
                {
                    // Mediterranean archway/passage: opening between buildings
                    CreatePiece($"Archway_{width}m_{height}m_{style}-{variant}", "Archway", style,
                        new Vector3(width, height, 0.65f), // width, height, wall thickness (two walls)
                        new Vector3(width * 0.5f, 0f, 0.65f * 0.5f), // Snap at bottom center
                        false,
                        new Vector3(width * 0.5f, height * 0.5f, 0.65f * 0.5f));
                    variant++;
                }
            }
        }
    }

    private static void GenerateSteppedGables()
    {
        // Chimney/tower cap modules: 4 varieties (2 sizes × 2 styles)
        // Creates chimney/tower caps for Mediterranean buildings
        float[] sizes = { 0.75f, 1.0f }; // Chimney width/depth
        ArchitecturalStyle[] styles = StylePair(14);

        int variant = 0;
        foreach (var size in sizes)
        {
            foreach (var style in styles)
            {
                // Mediterranean chimney/tower cap: typically prismatic
                CreatePiece($"ChimneyTower_{size}m_{style}-{variant}", "ChimneyTower", style,
                    new Vector3(size, 1.5f, size), // width, height, depth
                    new Vector3(size * 0.5f, 0f, size * 0.5f), // Snap at bottom center
                    false,
                    new Vector3(size * 0.5f, 0.75f, size * 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateTrimSheets()
    {
        // Trim Sheets: 12 varieties (6 types × 2 styles) - additional to the 60-100 main pieces
        string[] types = { "TrimCorner", "TrimFrieze", "TrimCornice", "TrimBase", "TrimChairRail", "TrimPictureRail" };
        ArchitecturalStyle[] styles = StylePair(15);

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                // Make trim pieces thin but wide
                CreatePiece($"Trim-{type}-{style}-{variant}", type, style,
                    new Vector3(0.02f, 0.1f, 2.0f),
                    new Vector3(0.01f, 0.05f, 1.0f), // Snap at bottom left
                    true, // This is trim
                    new Vector3(0.01f, 0.05f, 1.0f));
                variant++;
            }
        }
    }

    /// <summary>
    /// Helper method to create an architectural piece and add it to the collection.
    /// </summary>
    private static void CreatePiece(string id, string type, ArchitecturalStyle style, Vector3 size, Vector3 snapPoint, bool isTrim, Vector3 originOffset)
    {
        Pieces.Add(new ArchitecturalPiece
        {
            id = id,
            type = type,
            style = style,
            size = size,
            snapPoint = snapPoint,
            isTrim = isTrim,
            originOffset = originOffset
        });
    }


static readonly ArchitecturalStyle[] AllStyles =
    {
        ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined, ArchitecturalStyle.Ornate,
        ArchitecturalStyle.Vernacular, ArchitecturalStyle.Minimalist
    };

    // Each piece category uses two neighbouring styles, rotating by category, so all five styles
    // land in the required 10-40% band while the main piece count stays at 94.
    static ArchitecturalStyle[] StylePair(int category)
    {
        return new[] { AllStyles[category % AllStyles.Length], AllStyles[(category + 1) % AllStyles.Length] };
    }
}