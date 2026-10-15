using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mediterranean/Santorini-style architectural kit for Phase 5 housing.
/// Provides a modular kit of components for creating organic, clustered buildings
/// with whitewashed walls, terracotta roofs, and blue accents.
/// </summary>
public static class MediterraneanArchitecturalKit
{
    public enum WallHeight { Short = 2, Medium = 3, Tall = 4 } // meters
    public enum RoofType { FlatTerracotta, Pitched }
    public enum DoorState { Open, Closed }

    /// <summary>
    /// A modular building component that can be snapped together to create buildings.
    /// </summary>
    [Serializable]
    public class ModularPiece
    {
        public string id;              // Unique identifier
        public string type;            // Piece type (wall, corner, door, window, roof, stair, arch, chimney)
        public Vector3 size;           // Dimensions (x, y, z) in meters
        public Vector3 snapPoint;      // Primary snap point for grid alignment (local to piece origin)
        public bool isInterior;        // Whether this piece is primarily interior (affects material)
        public Material wallMaterial;  // Material for wall sections (whitewashed)
        public Material roofMaterial;  // Material for roof sections (terracotta)
        public Material accentMaterial; // Material for accents (blue doors/shutters)
        public GameObject prefab;      // Optional prefab for complex pieces
    }

    // ---- State ----
    public static readonly List<ModularPiece> Pieces = new List<ModularPiece>();
    public static bool Built;
    public static string Report = "";
    static int Hash;

    // ---- Material Settings ----
    // These would typically be assigned in the Unity editor or loaded from assets
    public static Material DefaultWallMaterial => GetOrCreateWallMaterial();
    public static Material DefaultRoofMaterial => GetOrCreateRoofMaterial();
    public static Material DefaultAccentMaterial => GetOrCreateAccentMaterial();

    // ---- Main Build ----
    /// <summary>
    /// Builds the Mediterranean architectural kit with all required components.
    /// </summary>
    public static void Build()
    {
        Clear();

        GenerateWallSegments();
        GenerateWallCorners();
        GenerateDoorFrames();
        GenerateWindowFrames();
        GenerateRoofCaps();
        GenerateStairModules();
        GenerateArchwayModules();
        GenerateChimneyModules();

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

static void GenerateAllPiecesWithoutValidation()
    {
        GenerateWallSegments();
        GenerateWallCorners();
        GenerateDoorFrames();
        GenerateWindowFrames();
        GenerateRoofCaps();
        GenerateStairModules();
        GenerateArchwayModules();
        GenerateChimneyModules();
    }


    /// <summary>
    /// Validates the architectural kit to ensure it meets Mediterranean style requirements.
    /// </summary>
    public static string Validate()
    {
        if (!Built)
        {
            Build(); // Build() runs Validate() itself and fills Report
            return Report;
        }

        var sb = new System.Text.StringBuilder("[MediterraneanArchitecturalKit]\n");
        var fails = new System.Collections.Generic.List<string>();
        int pass = 0;

        // Check that we have all required component types
        bool hasWallSegments = false;
        bool hasWallCorners = false;
        bool hasDoorFrames = false;
        bool hasWindowFrames = false;
        bool hasRoofCaps = false;
        bool hasStairModules = false;
        bool hasArchwayModules = false;
        bool hasChimneyModules = false;

        foreach (var piece in Pieces)
        {
            switch (piece.type.ToLower())
            {
                case "wall": hasWallSegments = true; break;
                case "corner": hasWallCorners = true; break;
                case "door": hasDoorFrames = true; break;
                case "window": hasWindowFrames = true; break;
                case "roof": hasRoofCaps = true; break;
                case "stair": hasStairModules = true; break;
                case "arch": hasArchwayModules = true; break;
                case "chimney": hasChimneyModules = true; break;
            }
        }

        if (!hasWallSegments) fails.Add("Missing wall segments");
        if (!hasWallCorners) fails.Add("Missing wall corners");
        if (!hasDoorFrames) fails.Add("Missing door frames");
        if (!hasWindowFrames) fails.Add("Missing window frames");
        if (!hasRoofCaps) fails.Add("Missing roof caps");
        if (!hasStairModules) fails.Add("Missing stair modules");
        if (!hasArchwayModules) fails.Add("Missing archway modules");
        if (!hasChimneyModules) fails.Add("Missing chimney modules");

        // Check proportions: walls should be thick (0.5-0.8m), doors/windows small relative to wall
        int thickWallCount = 0;
        int smallOpeningCount = 0;
        int totalWallPieces = 0;
        int totalOpeningPieces = 0;

        foreach (var piece in Pieces)
        {
            if (piece.type.ToLower() == "wall")
            {
                totalWallPieces++;
                // Check if wall thickness (x or z dimension) is in the 0.5-0.8m range
                if ((piece.size.x >= 0.5f && piece.size.x <= 0.8f) ||
                    (piece.size.z >= 0.5f && piece.size.z <= 0.8f))
                {
                    thickWallCount++;
                }
            }
            else if (piece.type.ToLower() == "door" || piece.type.ToLower() == "window")
            {
                totalOpeningPieces++;
                // Check if opening is small relative to typical wall thickness (0.65m avg)
                float wallThicknessReference = 0.65f;
                float openingSize = Mathf.Max(piece.size.x, piece.size.z); // width of opening
                if (openingSize <= wallThicknessReference * 0.5f) // door/window should be <= half wall thickness
                {
                    smallOpeningCount++;
                }
            }
        }

        if (totalWallPieces > 0 && (float)thickWallCount / totalWallPieces < 0.8f)
        {
            fails.Add($"Insufficient thick walls: {thickWallCount}/{totalWallPieces} (need >=80%)");
        }

        if (totalOpeningPieces > 0 && (float)smallOpeningCount / totalOpeningPieces < 0.7f)
        {
            fails.Add($"Insufficient small openings: {smallOpeningCount}/{totalOpeningPieces} (need >=70%)");
        }

        // Determinism check
        int h1 = Hash;
        var snapshot = new List<ModularPiece>(Pieces);
        Pieces.Clear();
        GenerateAllPiecesWithoutValidation();
        int h2 = GenerateHash();
        Pieces.Clear();
        Pieces.AddRange(snapshot);
        bool det = h1 == h2;

        if (!det)
        {
            fails.Add("Determinism check failed: generating kit twice yields different hashes");
        }

        if (fails.Count == 0)
        {
            sb.AppendLine($"Validation passed. Generated {Pieces.Count} modular pieces.");
            sb.AppendLine($"  Wall segments: {CountPiecesByType("wall")}");
            sb.AppendLine($"  Wall corners: {CountPiecesByType("corner")}");
            sb.AppendLine($"  Door frames: {CountPiecesByType("door")}");
            sb.AppendLine($"  Window frames: {CountPiecesByType("window")}");
            sb.AppendLine($"  Roof caps: {CountPiecesByType("roof")}");
            sb.AppendLine($"  Stair modules: {CountPiecesByType("stair")}");
            sb.AppendLine($"  Archway modules: {CountPiecesByType("arch")}");
            sb.AppendLine($"  Chimney modules: {CountPiecesByType("chimney")}");
            pass++;
        }
        else
        {
            foreach (var f in fails)
            {
                sb.AppendLine($"  FAIL: {f}");
            }
        }

        Report = sb.ToString();
        return Report;
    }

    // ---- Piece Generation Methods ----
    static void GenerateWallSegments()
    {
        // Wall segments: 3 heights × 2 lengths (standard and half)
        float[] heights = { 2f, 3f, 4f }; // Short, Medium, Tall
        float[] lengths = { 2f, 1f };     // Standard and half segments

        int variant = 0;
        foreach (float height in heights)
        {
            foreach (float length in lengths)
            {
                // Wall segment: thickness 0.65m (good for Mediterranean thick walls), length, height
                CreatePiece(
                    $"Wall_L{length}_H{height}_V{variant}",
                    "Wall",
                    new Vector3(length, height, 0.65f), // x=length, y=height, z=thickness
                    new Vector3(length * 0.5f, 0f, 0.325f), // Snap at bottom center, offset for thickness
                    false,
                    DefaultWallMaterial,
                    null, // roof material not applicable
                    DefaultAccentMaterial,
                    null // prefab
                );
                variant++;
            }
        }
    }

    static void GenerateWallCorners()
    {
        // Wall corners: inner and outer corners for each height
        float[] heights = { 2f, 3f, 4f };
        bool[] cornerTypes = { true, false }; // true = outer, false = inner

        int variant = 0;
        foreach (float height in heights)
        {
            foreach (bool isOuter in cornerTypes)
            {
                // Corner piece: two walls meeting at 90 degrees, each 1m long, 0.65m thick
                float legLength = 1f;
                CreatePiece(
                    $"Corner_{(isOuter ? "Outer" : "Inner")}_H{height}_V{variant}",
                    "Corner",
                    new Vector3(legLength, height, 0.65f), // x=leg length, y=height, z=thickness
                    new Vector3(0f, 0f, 0.325f), // Snap at bottom inside corner
                    false,
                    DefaultWallMaterial,
                    null,
                    DefaultAccentMaterial,
                    null
                );
                variant++;
            }
        }
    }

    static void GenerateDoorFrames()
    {
        // Door frames: blue accent, openable
        float[] widths = { 0.8f, 0.9f, 1.0f }; // Door widths
        float height = 2.2f; // Door height
        float wallThickness = 0.65f;

        int variant = 0;
        foreach (float width in widths)
        {
            CreatePiece(
                $"Door_W{width}_H{height}_V{variant}",
                "Door",
                new Vector3(width, height, wallThickness),
                new Vector3(0f, 0f, wallThickness * 0.5f), // Snap at bottom left (interior side)
                false,
                DefaultWallMaterial, // frame uses wall material
                null,
                DefaultAccentMaterial, // door itself is blue
                null
            );
            variant++;
        }
    }

    static void GenerateWindowFrames()
    {
        // Window frames: small, deep-set, with shutters (blue accent)
        float[] widths = { 0.6f, 0.8f, 1.0f };
        float[] heights = { 1.0f, 1.2f, 1.4f };
        float wallThickness = 0.65f;
        float recessDepth = 0.2f; // How deep the window is set into the wall

        int variant = 0;
        foreach (float width in widths)
        {
            foreach (float height in heights)
            {
                CreatePiece(
                    $"Window_W{width}_H{height}_V{variant}",
                    "Window",
                    new Vector3(width, height, wallThickness + recessDepth), // Includes recess
                    new Vector3(recessDepth * 0.5f, 0f, (wallThickness + recessDepth) * 0.5f), // Snap at bottom left of recess
                    false,
                    DefaultWallMaterial,
                    null,
                    DefaultAccentMaterial, // shutters are blue
                    null
                );
                variant++;
            }
        }
    }

    static void GenerateRoofCaps()
    {
        // Roof caps: flat terracotta slab and pitched variant
        float[] lengths = { 2f, 3f, 4f };
        float[] widths = { 2f, 3f, 4f };
        float thickness = 0.15f; // Roof thickness

        int variant = 0;
        foreach (float length in lengths)
        {
            foreach (float width in widths)
            {
                // Flat terracotta roof slab
                CreatePiece(
                    $"RoofFlat_L{length}_W{width}_V{variant}",
                    "Roof",
                    new Vector3(length, thickness, width),
                    new Vector3(length * 0.5f, thickness * 0.5f, width * 0.5f), // Snap at bottom center
                    false,
                    null, // wall material not applicable
                    DefaultRoofMaterial,
                    null,
                    null
                );
                variant++;

                // Pitched roof variant (simple triangle prism)
                CreatePiece(
                    $"RoofPitched_L{length}_W{width}_V{variant}",
                    "Roof",
                    new Vector3(length, 0.5f, width), // height is the pitch height
                    new Vector3(length * 0.5f, 0.25f, width * 0.5f), // Snap at bottom center
                    false,
                    null,
                    DefaultRoofMaterial,
                    null,
                    null
                );
                variant++;
            }
        }
    }

static void GenerateStairModules()
    {
        // Exterior stair modules: straight, L-shaped, switchback
        float width = 1.0f; // Stair width
        float rise = 0.2f;  // Rise per step
        float run = 0.25f;  // Run per step
        int variant = 0;

        // Straight stairs: 3, 4, 5 steps
        for (int steps = 3; steps <= 5; steps++)
        {
            float height = steps * rise;
            float depth = steps * run;
            CreatePiece(
                $"StairStraight_S{steps}_V{variant}",
                "Stair",
                new Vector3(width, height, depth),
                new Vector3(0f, 0f, 0f), // Snap at bottom left
                false,
                DefaultWallMaterial,
                null,
                DefaultAccentMaterial,
                null
            );
            variant++;
        }

        // L-stairs: 2x2 steps
        CreatePiece(
            $"StairL_V{variant}",
            "Stair",
            new Vector3(width * 2, 2 * rise, 2 * run),
            new Vector3(0f, 0f, 0f),
            false,
            DefaultWallMaterial,
            null,
            DefaultAccentMaterial,
            null
        );
        variant++;

        // Switchback stairs: 2x3 steps
        CreatePiece(
            $"StairSwitchback_V{variant}",
            "Stair",
            new Vector3(width * 2, 3 * rise, 2 * run),
            new Vector3(0f, 0f, 0f),
            false,
            DefaultWallMaterial,
            null,
            DefaultAccentMaterial,
            null
        );
        variant++;
    }

    static void GenerateArchwayModules()
    {
        // Archway/passage modules for gaps between fused buildings
        float[] widths = { 1.5f, 2.0f, 2.5f };
        float height = 2.5f;
        float wallThickness = 0.65f;

        int variant = 0;
        foreach (float width in widths)
        {
            CreatePiece(
                $"Archway_W{width}_H{height}_V{variant}",
                "Arch",
                new Vector3(width, height, wallThickness * 2), // Two walls with gap
                new Vector3(width * 0.5f, 0f, wallThickness * 0.5f), // Snap at bottom center
                false,
                DefaultWallMaterial,
                null,
                DefaultAccentMaterial,
                null
            );
            variant++;
        }
    }

    static void GenerateChimneyModules()
    {
        // Chimney/tower cap modules
        float[] sizes = { 0.5f, 0.75f, 1.0f }; // Chimney width/depth
        float height = 1.0f; // Chimney height above roof

        int variant = 0;
        foreach (float size in sizes)
        {
            CreatePiece(
                $"Chimney_S{size}_H{height}_V{variant}",
                "Chimney",
                new Vector3(size, height, size),
                new Vector3(size * 0.5f, 0f, size * 0.5f), // Snap at bottom center
                false,
                DefaultWallMaterial,
                null,
                DefaultAccentMaterial,
                null
            );
            variant++;
        }
    }

    // ---- Helper Methods ----
    static int CountPiecesByType(string type)
    {
        int count = 0;
        foreach (var piece in Pieces)
        {
            if (piece.type.ToLower() == type.ToLower())
                count++;
        }
        return count;
    }

    static void CreatePiece(string id, string type, Vector3 size, Vector3 snapPoint,
                           bool isInterior, Material wallMaterial, Material roofMaterial,
                           Material accentMaterial, GameObject prefab)
    {
        Pieces.Add(new ModularPiece
        {
            id = id,
            type = type,
            size = size,
            snapPoint = snapPoint,
            isInterior = isInterior,
            wallMaterial = wallMaterial,
            roofMaterial = roofMaterial,
            accentMaterial = accentMaterial,
            prefab = prefab
        });
    }

    static int GenerateHash()
    {
        int hash = 17;
        foreach (var piece in Pieces)
        {
            hash = hash * 31 + piece.id.GetHashCode();
            hash = hash * 31 + piece.type.GetHashCode();
            hash = hash * 31 + piece.size.GetHashCode();
            hash = hash * 31 + piece.snapPoint.GetHashCode();
            hash = hash * 31 + piece.isInterior.GetHashCode();
            hash = hash * 31 + (piece.wallMaterial != null ? piece.wallMaterial.GetHashCode() : 0);
            hash = hash * 31 + (piece.roofMaterial != null ? piece.roofMaterial.GetHashCode() : 0);
            hash = hash * 31 + (piece.accentMaterial != null ? piece.accentMaterial.GetHashCode() : 0);
            hash = hash * 31 + (piece.prefab != null ? piece.prefab.GetHashCode() : 0);
        }
        return hash;
    }

    static Material GetOrCreateWallMaterial()
    {
        // In a real implementation, this would load or create a material
        // For now, we'll return null and expect materials to be set in editor
        return null;
    }

    static Material GetOrCreateRoofMaterial()
    {
        return null;
    }

    static Material GetOrCreateAccentMaterial()
    {
        return null;
    }

    // ---- Debugging ----
    public static string RebuildAndValidate()
    {
        Built = false;
        Build();
        return Validate();
    }
}