using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static SettlementMap;

/// <summary>
/// Level of detail and streaming system for Phase 5.
/// Provides methods to determine appropriate LOD levels for building exteriors and interiors
/// based on distance from the player and settlement importance.
/// Integrates with SettlementMap and ArchitecturalKit for context-aware LOD decisions.
/// </summary>
public static class LODSystem
{
    // ---- configuration ----
    /// <summary>
    /// Distance within which full interiors are loaded. Beyond this, windows use a fake interior.
    /// </summary>
    public static float interiorLoadDistance = 25f;

    /// <summary>
    /// Distance thresholds for exterior LOD levels. Index 0 corresponds to highest LOD (LOD0).
    /// If distance exceeds threshold[i], LOD level increases to i+1.
    /// Array length defines number of LOD levels minus one (highest LOD is always available).
    /// Example: { 50f, 100f, 200f } means:
    ///   - Distance <= 50f: LOD0 (highest)
    ///   - 50f < distance <= 100f: LOD1
    ///   - 100f < distance <= 200f: LOD2
    ///   - distance > 200f: LOD3 (lowest)
    /// </summary>
    public static float[] exteriorLODDistances = new float[] { 50f, 100f, 200f, 500f };

    // ---- player tracking ----
    private static Transform _playerCamera;

    /// <summary>
    /// Sets the player camera transform for distance calculations.
    /// Call this once at runtime to configure the LOD system.
    /// </summary>
    /// <param name="cameraTransform">The player's camera transform (typically Camera.main.transform)</param>
    public static void SetPlayerCamera(Transform cameraTransform)
    {
        _playerCamera = cameraTransform;
    }

    /// <summary>
    /// Gets the player's current position from the configured camera.
    /// If no camera is set, attempts to use Camera.main.
    /// Returns Vector3.zero if no camera is found.
    /// </summary>
    public static Vector3 GetPlayerPosition()
    {
        if (_playerCamera != null)
            return _playerCamera.position;

        if (Camera.main != null)
            return Camera.main.transform.position;

        return Vector3.zero;
    }

    // ---- exterior LOD ----
    /// <summary>
    /// Calculates the appropriate exterior LOD level for a building based on distance from player.
    /// Returns 0 for highest LOD (LOD0), 1 for LOD1, etc.
    /// Higher numbers indicate lower detail levels.
    /// </summary>
    /// <param name="buildingPosition">The building's world position</param>
    /// <returns>LOD level index (0 = highest detail)</returns>
    public static int GetExteriorLODLevel(Vector3 buildingPosition)
    {
        Vector3 playerPos = GetPlayerPosition();
        if (playerPos == Vector3.zero)
            return 0; // default to highest LOD if player position unknown

        float distance = Vector3.Distance(buildingPosition, playerPos);

        // Find the appropriate LOD level based on distance thresholds
        int lodLevel = 0;
        for (int i = 0; i < exteriorLODDistances.Length; i++)
        {
            if (distance > exteriorLODDistances[i])
                lodLevel = i + 1;
            else
                break; // thresholds are sorted ascending, so we can break early
        }

        return lodLevel;
    }

    /// <summary>
    /// Calculates the appropriate exterior LOD level for a building, with optional settlement-based adjustment.
    /// Allows settlements with more lots or special significance to maintain higher LOD further away.
    /// </summary>
    /// <param name="buildingPosition">The building's world position</param>
    /// <param name="settlement">The settlement the building belongs to (for context-aware adjustment)</param>
    /// <returns>LOD level index (0 = highest detail)</returns>
    public static int GetExteriorLODLevel(Vector3 buildingPosition, Settlement settlement)
    {
        // Base LOD level from distance
        int baseLod = GetExteriorLODLevel(buildingPosition);

        // Optional: Adjust LOD based on settlement properties
        // Example: Larger settlements (more lots) might maintain higher LOD further away
        if (settlement.lotIdx != null && settlement.lotIdx.Count > 0)
        {
            // Reduce LOD level (increase detail) for larger settlements
            int lotCount = settlement.lotIdx.Count;
            if (lotCount > 20) // Large settlement
                baseLod = Mathf.Max(0, baseLod - 1);
            else if (lotCount > 10) // Medium settlement
                baseLod = Mathf.Max(0, baseLod - 0); // no change
            // Small settlements (lotCount <= 10) use base LOD
        }

        // Optional: Adjust based on settlement role (e.g., capitals or harbors maintain detail)
        // if (settlement.role == Zone.Harbour || settlement.role == Zone.Summit)
        //     baseLod = Mathf.Max(0, baseLod - 1);

        return baseLod;
    }

    // ---- interior streaming ----
    /// <summary>
    /// Determines whether a building should load its full interior based on distance from player.
    /// Returns true if within interiorLoadDistance, false otherwise (should use fake interior).
    /// </summary>
    /// <param name="buildingPosition">The building's world position</param>
    /// <returns>True if full interior should be loaded</returns>
    public static bool ShouldLoadFullInterior(Vector3 buildingPosition)
    {
        Vector3 playerPos = GetPlayerPosition();
        if (playerPos == Vector3.zero)
            return true; // default to loading interior if player position unknown (err on side of visibility)

        float distance = Vector3.Distance(buildingPosition, playerPos);
        return distance <= interiorLoadDistance;
    }

    /// <summary>
    /// Determines whether a building should load its full interior, with optional settlement-based adjustment.
    /// Allows important settlements to maintain full interiors further away.
    /// </summary>
    /// <param name="buildingPosition">The building's world position</param>
    /// <param name="settlement">The settlement the building belongs to (for context-aware adjustment)</param>
    /// <returns>True if full interior should be loaded</returns>
    public static bool ShouldLoadFullInterior(Vector3 buildingPosition, Settlement settlement)
    {
        bool baseShouldLoad = ShouldLoadFullInterior(buildingPosition);

        // Optional: Adjust based on settlement importance
        // Example: Harbor or summit settlements might load interiors further away
        if (settlement.role == Zone.Harbour || settlement.role == Zone.Summit)
        {
            // Increase effective interior load distance for important settlements
            float adjustedDistance = interiorLoadDistance * 1.5f;
            Vector3 playerPos = GetPlayerPosition();
            if (playerPos != Vector3.zero)
            {
                float distance = Vector3.Distance(buildingPosition, playerPos);
                return distance <= adjustedDistance;
            }
        }

        return baseShouldLoad;
    }

    // ---- validation ----
    /// <summary>
    /// Validates the LOD system configuration and returns a report.
    /// </summary>
    /// <returns>Validation report string</returns>
    public static string Validate()
    {
        var sb = new StringBuilder("[LODSystem 5.8]\n");
        int headerLength = sb.Length;

        if (interiorLoadDistance <= 0)
            sb.AppendLine("  WARNING: interiorLoadDistance should be positive.");

        if (exteriorLODDistances == null || exteriorLODDistances.Length == 0)
            sb.AppendLine("  WARNING: exteriorLODDistances array is empty or null.");
        else
        {
            for (int i = 0; i < exteriorLODDistances.Length; i++)
            {
                if (exteriorLODDistances[i] <= 0)
                    sb.AppendLine($"  WARNING: exteriorLODDistances[{i}] should be positive.");

                if (i > 0 && exteriorLODDistances[i] <= exteriorLODDistances[i-1])
                    sb.AppendLine($"  WARNING: exteriorLODDistances should be in ascending order. Issue at index {i}.");
            }
        }

        if (_playerCamera == null && Camera.main == null)
            sb.AppendLine("  WARNING: No player camera set. LOD system will use default position (0,0,0).");

        if (sb.Length == headerLength)
            sb.AppendLine("  LODSystem configuration is valid.");

        return sb.ToString();
    }
}