using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// PHASE 5.9 — Building validation and reporting. Validates that generated buildings
/// properly integrate with settlement layouts, architectural kits, and infrastructure systems.
/// </summary>
public static class BuildingValidator
{
    // ---- state ----
    public static bool Built;
    public static string Report = "";

    /// <summary>
    /// Validates the current state of buildings and their integration with other systems.
    /// Should be called after BuildingSystem.Build() and related system builds.
    /// </summary>
    public static string Validate()
    {
        // Ensure dependencies are built
        if (!SettlementMap.Built) SettlementMap.Build();
        if (!ArchitecturalKit.Built) ArchitecturalKit.Build();
        if (!BuildingSystem.Built) BuildingSystem.Build();
        if (LaneNet.Doors == null) LaneNet.Build();
        if (!WaterAccess.Built) WaterAccess.Build();
        if (!FarmPlots.Built) FarmPlots.Build();

        var sb = new StringBuilder("[BuildingValidator 5.9]\n");
        var fails = new List<string>();
        int pass = 0;

        // Validation counters
        int buildingsTotal = BuildingSystem.Buildings.Count;
        int buildingsOutsideSettlements = 0;
        int buildingsTooCloseToPlazas = 0;
        int buildingsTooCloseToAnchors = 0;
        int buildingsWithoutProperDoorAccess = 0;
        int buildingsUsingInvalidArchitecturalPieces = 0;
        int buildingsMissingRequiredExteriorFeatures = 0;
        int buildingsWithInadequateYards = 0;
        int buildingsWithIncorrectRoofTypes = 0;
        int buildingsWithInsufficientWindowArea = 0;

        // Validate each building
        foreach (var building in BuildingSystem.Buildings)
        {
            bool buildingFailed = false;

            // Check 1: Building is within a valid settlement
            if (building.settlement < 0 || building.settlement >= SettlementMap.Settlements.Count)
            {
                buildingsOutsideSettlements++;
                buildingFailed = true;
            }
            else
            {
                var settlement = SettlementMap.Settlements[building.settlement];
                var buildingPos = new Vector2((float)building.position.x, (float)building.position.z);

                // Check 2: Building is not too close to plaza (if settlement has one)
                if (settlement.hasPlaza)
                {
                    var plazaPos = new Vector2((float)settlement.plazaX, (float)settlement.plazaZ);
                    float distToPlaza = Vector2.Distance(buildingPos, plazaPos);
                    if (distToPlaza < settlement.plazaR + 5.0f) // 5m buffer from plaza edge
                    {
                        buildingsTooCloseToPlazas++;
                        buildingFailed = true;
                    }
                }

                // Check 3: Building is not too close to anchors (well, chapel, monument, etc.)
                foreach (var anchor in settlement.anchors)
                {
                    var anchorPos = new Vector2((float)anchor.x, (float)anchor.z);
                    float distToAnchor = Vector2.Distance(buildingPos, anchorPos);
                    float minDist = 3.0f; // 3m minimum distance from anchors
                    if (anchor.kind == "tower") minDist = 8.0f; // towers need more space
                    if (distToAnchor < minDist)
                    {
                        buildingsTooCloseToAnchors++;
                        buildingFailed = true;
                        break;
                    }
                }
            }

            // Check 4: Building has proper door access to lane/road network
            var laneDoor = LaneNet.Doors[building.lot];
            if (!laneDoor.ok)
            {
                buildingsWithoutProperDoorAccess++;
                buildingFailed = true;
            }

            // Check 5: Building uses valid architectural pieces from ArchitecturalKit
            if (building.exterior.architecturalDetails != null)
            {
                foreach (var detail in building.exterior.architecturalDetails)
                {
                    bool pieceValid = false;
                    foreach (var kitPiece in ArchitecturalKit.Pieces)
                    {
                        if (detail.id == kitPiece.id &&
                            detail.type == kitPiece.type &&
                            detail.style == kitPiece.style)
                        {
                            pieceValid = true;
                            break;
                        }
                    }
                    if (!pieceValid)
                    {
                        buildingsUsingInvalidArchitecturalPieces++;
                        buildingFailed = true;
                        break;
                    }
                }
            }

            // Check 6: Building has required exterior features based on household type
            bool hasRequiredFeatures = true;
            switch (building.householdType)
            {
                case "farmer":
                    if (!building.exterior.hasWoodStacks && !building.exterior.hasBarrels)
                        hasRequiredFeatures = false;
                    break;
                case "fisher":
                    if (!building.exterior.hasLanterns)
                        hasRequiredFeatures = false;
                    break;
                case "shopkeeper":
                    if (!building.exterior.hasShopSign)
                        hasRequiredFeatures = false;
                    break;
            }
            if (!hasRequiredFeatures)
            {
                buildingsMissingRequiredExteriorFeatures++;
                buildingFailed = true;
            }

            // Check 7: Building has adequate yard space
            if (!building.yard.hasYard && !building.yard.hasStoop)
            {
                buildingsWithInadequateYards++;
                buildingFailed = true;
            }

            // Check 8: Building has appropriate roof type for settlement/zone
            if (building.settlement >= 0 && building.settlement < SettlementMap.Settlements.Count)
            {
                var settlement = SettlementMap.Settlements[building.settlement];
                bool roofAppropriate = true;
                switch (settlement.role)
                {
                    case Zone.Harbour:
                        // Harbour buildings should have practical roofs
                        if (building.roof.type == BuildingSystem.BuildingRoof.RoofType.SteppedGable)
                            roofAppropriate = false; // too ornate for harbour
                        break;
                    case Zone.Summit:
                        // Summit buildings should withstand weather
                        if (building.roof.type == BuildingSystem.BuildingRoof.RoofType.FlatParapet)
                            roofAppropriate = false; // flat roofs bad for summit weather
                        break;
                }
                if (!roofAppropriate)
                {
                    buildingsWithIncorrectRoofTypes++;
                    buildingFailed = true;
                }
            }

            // Check 9: Building has sufficient window area for habitable rooms
            float totalWindowArea = 0f;
            float totalFloorArea = 0f;
            foreach (var floor in building.floorPlan)
            {
                foreach (var room in floor.rooms)
                {
                    bool isHabitable = room.roomType == "living" || room.roomType == "kitchen" ||
                                     room.roomType == "bedroom";
                    if (isHabitable)
                    {
                        totalFloorArea += room.size.x * room.size.y;
                        foreach (var opening in building.exterior.openings)
                        {
                            if (opening.type == BuildingSystem.Opening.OpeningType.Window &&
                                IsOpeningInRoom(opening, room, building))
                            {
                                totalWindowArea += opening.size.x * opening.size.y;
                            }
                        }
                    }
                }
            }
            if (totalFloorArea > 0 && totalWindowArea / totalFloorArea < 0.1f) // Minimum 10% window-to-floor ratio
            {
                buildingsWithInsufficientWindowArea++;
                buildingFailed = true;
            }

            if (buildingFailed)
                fails.Add($"Building {building.id} ({building.householdType} in settlement {building.settlement}) failed validation");
        }

        // Tally passes
        if (buildingsOutsideSettlements == 0) pass++;
        if (buildingsTooCloseToPlazas == 0) pass++;
        if (buildingsTooCloseToAnchors == 0) pass++;
        if (buildingsWithoutProperDoorAccess == 0) pass++;
        if (buildingsUsingInvalidArchitecturalPieces == 0) pass++;
        if (buildingsMissingRequiredExteriorFeatures == 0) pass++;
        if (buildingsWithInadequateYards == 0) pass++;
        if (buildingsWithIncorrectRoofTypes == 0) pass++;
        if (buildingsWithInsufficientWindowArea == 0) pass++;

        // Build report
        sb.AppendLine("buildings validated: " + buildingsTotal);
        sb.AppendLine("buildings outside settlements: " + buildingsOutsideSettlements + "/" + buildingsTotal);
        sb.AppendLine("buildings too close to plazas: " + buildingsTooCloseToPlazas + "/" + buildingsTotal);
        sb.AppendLine("buildings too close to anchors: " + buildingsTooCloseToAnchors + "/" + buildingsTotal);
        sb.AppendLine("buildings without proper door access: " + buildingsWithoutProperDoorAccess + "/" + buildingsTotal);
        sb.AppendLine("buildings using invalid architectural pieces: " + buildingsUsingInvalidArchitecturalPieces + "/" + buildingsTotal);
        sb.AppendLine("buildings missing required exterior features: " + buildingsMissingRequiredExteriorFeatures + "/" + buildingsTotal);
        sb.AppendLine("buildings with inadequate yards: " + buildingsWithInadequateYards + "/" + buildingsTotal);
        sb.AppendLine("buildings with incorrect roof types: " + buildingsWithIncorrectRoofTypes + "/" + buildingsTotal);
        sb.AppendLine("buildings with insufficient window area: " + buildingsWithInsufficientWindowArea + "/" + buildingsTotal);
        sb.AppendLine("RULES (5.9): " + pass + " pass / " + fails.Count + " fail");
        foreach (var f in fails) sb.AppendLine("  FAIL: " + f);

        Report = sb.ToString();
        Built = true;
        return Report;
    }

    // Helper methods
    private static bool IsOpeningInRoom(BuildingSystem.Opening opening, BuildingSystem.Room room, BuildingSystem.Building building)
    {
        // Simplified check - see if opening position is within room bounds
        float roomLeft = room.position.x;
        float roomRight = room.position.x + room.size.x;
        float roomBottom = room.position.y;
        float roomTop = room.position.y + room.size.y;

        float openingX = opening.position.x;
        float openingY = opening.position.y;

        // Adjust for building rotation (simplified - assuming opening is already in local coordinates)
        return openingX >= roomLeft && openingX <= roomRight &&
               openingY >= roomBottom && openingY <= roomTop;
    }

    #if UNITY_EDITOR
    [UnityEditor.MenuItem("Island/Phase 5.9 Building Validation")]
    static void Menu()
    {
        Built = false;
        Debug.Log(Validate());
    }
    #endif
}