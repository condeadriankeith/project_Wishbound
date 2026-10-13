using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Architectural kit system for Phase 5 - generates a library of architectural pieces
/// that can be snapped to a grid and tagged by style for building generation.
/// </summary>
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

        // Check piece count
        if (Pieces.Count < 60 || Pieces.Count > 100)
        {
            Report += $"Piece count {Pieces.Count} is outside valid range (60-100). ";
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

        // Determinism check: generate kit twice and compare hashes
        int h1 = Hash;
        bool builtBackup = Built;
        Built = false;
        Build(); // Rebuild to test determinism
        bool det = h1 == Hash;
        Built = builtBackup; // Restore original state

        if (!det)
        {
            Report += "Determinism check failed: generating kit twice yields different hashes. ";
        }

        if (string.IsNullOrEmpty(Report))
        {
            Report = $"ArchitecturalKit validation passed. Generated {Pieces.Count} pieces.";
        }
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

    // Piece generation methods
    private static void GeneratePlinths()
    {
        // Plinths: 8 varieties (2-6 heights, 2 styles each)
        string[] types = { "PlinthShort", "PlinthMedium", "PlinthTall" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                // Vary height from 1 to 3 meters
                float height = 1.0f + (variant % 3) * 1.0f;
                CreatePiece($"{type}-{style}-{variant}", type, style,
                    new Vector3(1.0f, height, 1.0f),
                    new Vector3(0.5f, 0f, 0.5f), // Snap at bottom center
                    false,
                    new Vector3(0.5f, height * 0.5f, 0.5f));
                variant++;
            }
        }
        // Add more variations to reach 8 total
        for (int i = 0; i < 2; i++)
        {
            CreatePiece($"PlinthWide-{ArchitecturalStyle.Rustic}-{i}", "PlinthWide", ArchitecturalStyle.Rustic,
                new Vector3(2.0f, 1.5f, 2.0f),
                new Vector3(1.0f, 0f, 1.0f),
                false,
                new Vector3(1.0f, 0.75f, 1.0f));
        }
    }

    private static void GenerateQuoins()
    {
        // Quoins: 8 varieties (corner stones, 2 patterns × 4 styles)
        string[] patterns = { "QuoinSquare", "QuoinRounded" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined, ArchitecturalStyle.Ornate, ArchitecturalStyle.Vernacular };

        int variant = 0;
        foreach (var pattern in patterns)
        {
            foreach (var style in styles)
            {
                CreatePiece($"{pattern}-{style}-{variant}", pattern, style,
                    new Vector3(0.5f, 2.0f, 0.5f),
                    new Vector3(0.25f, 1.0f, 0.25f), // Snap at bottom corner
                    false,
                    new Vector3(0.25f, 1.0f, 0.25f));
                variant++;
            }
        }
    }

    private static void GenerateSills()
    {
        // Sills: 10 varieties (window sills, 5 projections × 2 styles)
        float[] projections = { 0.15f, 0.2f, 0.25f, 0.3f, 0.35f };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var projection in projections)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Sill-{projection}m-{style}-{variant}", "Sill", style,
                    new Vector3(projection * 2 + 0.1f, 0.05f, 0.1f),
                    new Vector3(0f, 0.025f, 0f), // Snap at bottom center
                    false,
                    new Vector3((projection * 2 + 0.1f) * 0.5f, 0.025f, 0.05f));
                variant++;
            }
        }
    }

    private static void GenerateLintels()
    {
        // Lintels: 10 varieties (headers, 5 types × 2 styles)
        string[] types = { "LintelFlat", "LintelArch", "LintelStone", "LintelWood", "LintelSteel" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Lintel-{type}-{style}-{variant}", type, style,
                    new Vector3(2.0f, 0.2f, 0.2f),
                    new Vector3(1.0f, 0.1f, 0.1f), // Snap at center
                    false,
                    new Vector3(1.0f, 0.1f, 0.1f));
                variant++;
            }
        }
    }

    private static void GenerateShutters()
    {
        // Shutters: 12 varieties (6 types × 2 styles)
        string[] types = { "ShutterPanel", "ShutterLouvered", "ShutterBoard", "ShutterScrolled", "ShutterMetal", "ShutterVinyl" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Shutter-{type}-{style}-{variant}", type, style,
                    new Vector3(0.05f, 1.8f, 0.02f),
                    new Vector3(0.025f, 0.9f, 0.01f), // Snap at center left
                    false,
                    new Vector3(0.025f, 0.9f, 0.01f));
                variant++;
            }
        }
    }

    private static void GenerateArchedDoors()
    {
        // Arched Doors: 8 varieties (4 spans × 2 styles)
        float[] spans = { 1.0f, 1.5f, 2.0f, 2.5f };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var span in spans)
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
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

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
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

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
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

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
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

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
        // Chimney Caps: 6 varieties (3 styles × 2 variations)
        string[] stylesStr = { "ChimneyCapFlat", "ChimneyCapSloped", "ChimneyCapDecorative" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var styleStr in stylesStr)
        {
            foreach (var style in styles)
            {
                CreatePiece($"ChimneyCap-{styleStr}-{style}-{variant}", styleStr, style,
                    new Vector3(1.0f, 0.3f, 1.0f),
                    new Vector3(0.5f, 0.15f, 0.5f), // Snap at bottom center
                    false,
                    new Vector3(0.5f, 0.15f, 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateParapetBands()
    {
        // Parapet Bands: 8 varieties (4 types × 2 styles)
        string[] types = { "ParapetPlain", "ParapetCrenelated", "ParapetArched", "ParapetPanelled" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Parapet-{type}-{style}-{variant}", type, style,
                    new Vector3(2.0f, 0.5f, 0.2f),
                    new Vector3(1.0f, 0.25f, 0.1f), // Snap at bottom center
                    false,
                    new Vector3(1.0f, 0.25f, 0.1f));
                variant++;
            }
        }
    }

    private static void GenerateDrainpipes()
    {
        // Drainpipes: 4 varieties (2 shapes × 2 styles)
        string[] shapes = { "DrainpipeRound", "DrainpipeSquare" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var shape in shapes)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Drainpipe-{shape}-{style}-{variant}", shape, style,
                    new Vector3(0.15f, 2.5f, 0.15f),
                    new Vector3(0.075f, 0f, 0.075f), // Snap at bottom center
                    false,
                    new Vector3(0.075f, 1.25f, 0.075f));
                variant++;
            }
        }
    }

    private static void GenerateDormers()
    {
        // Dormers: 6 varieties (3 types × 2 styles)
        string[] types = { "DormerGabled", "DormerHipped", "DormerShed" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var type in types)
        {
            foreach (var style in styles)
            {
                CreatePiece($"Dormer-{type}-{style}-{variant}", type, style,
                    new Vector3(1.5f, 1.0f, 1.0f),
                    new Vector3(0.75f, 0.5f, 0.5f), // Snap at bottom front center
                    false,
                    new Vector3(0.75f, 0.5f, 0.5f));
                variant++;
            }
        }
    }

    private static void GenerateSteppedGables()
    {
        // Stepped Gables: 4 varieties (2 patterns × 2 styles)
        string[] patterns = { "GableStepped2", "GableStepped3" }; // 2-step or 3-step
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

        int variant = 0;
        foreach (var pattern in patterns)
        {
            foreach (var style in styles)
            {
                CreatePiece($"SteppedGable-{pattern}-{style}-{variant}", pattern, style,
                    new Vector3(2.0f, 1.5f, 0.2f),
                    new Vector3(1.0f, 0.75f, 0.1f), // Snap at bottom center
                    false,
                    new Vector3(1.0f, 0.75f, 0.1f));
                variant++;
            }
        }
    }

    private static void GenerateTrimSheets()
    {
        // Trim Sheets: 9 varieties (corner, frieze, cornice, etc. × styles)
        string[] types = { "TrimCorner", "TrimFrieze", "TrimCornice", "TrimBase", "TrimChairRail", "TrimPictureRail", "TrimPanel", "TrimWindow", "TrimDoor" };
        ArchitecturalStyle[] styles = { ArchitecturalStyle.Rustic, ArchitecturalStyle.Refined };

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
}