using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PHASE 5 — floor plan generation system. Creates room layouts for buildings based on household type and footprint.
/// Follows the same pattern as other Phase systems.
/// </summary>
public static class FloorPlanGenerator
{
    /// <summary>
    /// Generates a list of rooms for a given floor based on household type and footprint.
    /// </summary>
    /// <param name="householdType">The household type (e.g., "farmer", "fisher", "shopkeeper").</param>
    /// <param name="floorIndex">The floor index (0=ground, 1=first, etc.).</param>
    /// <param name="footprint">The building footprint (width, depth) in meters.</param>
    /// <returns>A list of rooms for the specified floor.</returns>
    public static List<BuildingSystem.Room> GenerateRoomsForFloor(string householdType, int floorIndex, Vector2 footprint)
    {
        // Nominal footprint size used in the room designs (16x16 meters)
        const float nominalWidth = 16f;
        const float nominalDepth = 16f;

        // Avoid division by zero
        if (footprint.x <= 0f || footprint.y <= 0f)
            footprint = new Vector2(nominalWidth, nominalDepth);

        float scaleX = footprint.x / nominalWidth;
        float scaleZ = footprint.y / nominalDepth;

        List<BuildingSystem.Room> nominalRooms = new List<BuildingSystem.Room>();

        switch (householdType)
        {
            case "farmer":
                nominalRooms = GenerateFarmerRoomsNominal(floorIndex);
                break;
            case "fisher":
                nominalRooms = GenerateFisherRoomsNominal(floorIndex);
                break;
            case "shopkeeper":
                nominalRooms = GenerateShopkeeperRoomsNominal(floorIndex);
                break;
            default:
                nominalRooms = GenerateFarmerRoomsNominal(floorIndex);
                break;
        }

        // Scale rooms to actual footprint
        List<BuildingSystem.Room> scaledRooms = new List<BuildingSystem.Room>();
        foreach (var room in nominalRooms)
        {
            BuildingSystem.Room scaledRoom = new BuildingSystem.Room
            {
                roomType = room.roomType,
                position = new Vector2(
                    room.position.x * scaleX,
                    room.position.y * scaleZ
                ),
                size = new Vector2(
                    room.size.x * scaleX,
                    room.size.y * scaleZ
                ),
                doorCount = room.doorCount,
                windowCount = room.windowCount,
                hasHearth = room.hasHearth
            };
            scaledRooms.Add(scaledRoom);
        }

        return scaledRooms;
    }

    // Nominal room generation methods (designed for 16x16 footprint)

    private static List<BuildingSystem.Room> GenerateFarmerRoomsNominal(int floorIndex)
    {
        List<BuildingSystem.Room> rooms = new List<BuildingSystem.Room>();

        if (floorIndex == 0) // ground floor
        {
            // Entry or tool room
            rooms.Add(new BuildingSystem.Room { roomType = "entry", position = new Vector2(1f, 1f), size = new Vector2(2f, 2f), doorCount = 1, windowCount = 1, hasHearth = false });
            // Kitchen and hearth
            rooms.Add(new BuildingSystem.Room { roomType = "kitchen", position = new Vector2(4f, 1f), size = new Vector2(3f, 3f), doorCount = 1, windowCount = 2, hasHearth = true });
            // Living room
            rooms.Add(new BuildingSystem.Room { roomType = "living", position = new Vector2(8f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 2, hasHearth = false });
            // Pantry/storage
            rooms.Add(new BuildingSystem.Room { roomType = "pantry", position = new Vector2(13f, 1f), size = new Vector2(2f, 2f), doorCount = 1, windowCount = 0, hasHearth = false });
            // Byre/storage (for animals)
            rooms.Add(new BuildingSystem.Room { roomType = "byre", position = new Vector2(1f, 5f), size = new Vector2(6f, 4f), doorCount = 1, windowCount = 0, hasHearth = false });
        }
        else // upper floors - sleeping rooms or lofts
        {
            // Simple layout: sleeping rooms
            rooms.Add(new BuildingSystem.Room { roomType = "bedroom", position = new Vector2(1f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
            rooms.Add(new BuildingSystem.Room { roomType = "bedroom", position = new Vector2(6f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
            rooms.Add(new BuildingSystem.Room { roomType = "loft", position = new Vector2(11f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
        }

        return rooms;
    }

    private static List<BuildingSystem.Room> GenerateFisherRoomsNominal(int floorIndex)
    {
        List<BuildingSystem.Room> rooms = new List<BuildingSystem.Room>();

        if (floorIndex == 0) // ground floor
        {
            // Net room (for fishing nets)
            rooms.Add(new BuildingSystem.Room { roomType = "net", position = new Vector2(1f, 1f), size = new Vector2(3f, 4f), doorCount = 2, windowCount = 1, hasHearth = false });
            // Kitchen and hearth
            rooms.Add(new BuildingSystem.Room { roomType = "kitchen", position = new Vector2(5f, 1f), size = new Vector2(3f, 3f), doorCount = 1, windowCount = 2, hasHearth = true });
            // Living room
            rooms.Add(new BuildingSystem.Room { roomType = "living", position = new Vector2(9f, 1f), size = new Vector2(3f, 3f), doorCount = 1, windowCount = 2, hasHearth = false });
            // Storage
            rooms.Add(new BuildingSystem.Room { roomType = "storage", position = new Vector2(13f, 1f), size = new Vector2(2f, 2f), doorCount = 1, windowCount = 0, hasHearth = false });
        }
        else // upper floors
        {
            // Sleeping rooms
            rooms.Add(new BuildingSystem.Room { roomType = "bedroom", position = new Vector2(1f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
            rooms.Add(new BuildingSystem.Room { roomType = "bedroom", position = new Vector2(6f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
        }

        return rooms;
    }

    private static List<BuildingSystem.Room> GenerateShopkeeperRoomsNominal(int floorIndex)
    {
        List<BuildingSystem.Room> rooms = new List<BuildingSystem.Room>();

        if (floorIndex == 0) // ground floor
        {
            // Shopfront
            rooms.Add(new BuildingSystem.Room { roomType = "shop", position = new Vector2(1f, 1f), size = new Vector2(5f, 4f), doorCount = 2, windowCount = 4, hasHearth = false });
            // Kitchen and hearth (behind shop)
            rooms.Add(new BuildingSystem.Room { roomType = "kitchen", position = new Vector2(7f, 1f), size = new Vector2(3f, 3f), doorCount = 1, windowCount = 2, hasHearth = true });
            // Living/storage
            rooms.Add(new BuildingSystem.Room { roomType = "living", position = new Vector2(11f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
        }
        else // upper floors - living quarters
        {
            // Living room
            rooms.Add(new BuildingSystem.Room { roomType = "living", position = new Vector2(1f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 2, hasHearth = false });
            // Kitchen
            rooms.Add(new BuildingSystem.Room { roomType = "kitchen", position = new Vector2(6f, 1f), size = new Vector2(3f, 3f), doorCount = 1, windowCount = 1, hasHearth = true });
            // Bedrooms
            rooms.Add(new BuildingSystem.Room { roomType = "bedroom", position = new Vector2(10f, 1f), size = new Vector2(4f, 3f), doorCount = 1, windowCount = 1, hasHearth = false });
        }

        return rooms;
    }
}