using UnityEngine;

/// <summary>
/// Ground-height queries that look at the terrain only.
/// A raw Physics.Raycast against the whole scene also hits the tree canopy capsules (each tree is one tall capsule),
/// houses and props, so anything that "follows the ground" that way ends up riding up trees. This asks the island mesh
/// and the crater sand patch directly with Collider.Raycast: no physics-scene query, no canopy hits.
/// </summary>
public static class TerrainProbe
{
    /// <summary>Below this height the ground is sea / wet sand the guides should not lead across.</summary>
    public const float SeaLevel = .9f;

    static Collider island, craterPatch;
    static int lastLookup = -999;

    static void Refresh()
    {
        if (island && craterPatch) return;
        if (Time.frameCount - lastLookup < 60) return;      // while something is missing, retry about once a second - never per call
        lastLookup = Time.frameCount;
        if (!island) { var ib = IslandBuilder.Instance; if (ib) island = ib.GetComponent<MeshCollider>(); }
        if (!craterPatch)
        {
            var c = BeachCometCrater.Instance;
            if (c) { var t = c.transform.Find("Crater sand (detail)"); if (t) craterPatch = t.GetComponent<Collider>(); }
        }
    }

    /// <summary>True if the collider is part of the terrain surface (island mesh or crater patch).</summary>
    public static bool IsTerrain(Collider c) { Refresh(); return c && (c == island || c == craterPatch); }

    /// <summary>Terrain surface height under (x, z). False if there is no terrain there (off the island).</summary>
    public static bool TryGetGroundY(float x, float z, out float y)
    {
        Refresh();
        y = float.NegativeInfinity;
        var ray = new Ray(new Vector3(x, 1200f, z), Vector3.down);
        RaycastHit h;
        // the crater patch lies on top of the sunk island mesh, so take whichever surface is higher
        if (craterPatch && craterPatch.enabled && craterPatch.Raycast(ray, out h, 1400f)) y = h.point.y;
        if (island && island.enabled && island.Raycast(ray, out h, 1400f) && h.point.y > y) y = h.point.y;
        return y > float.NegativeInfinity;
    }

    public static float GroundY(float x, float z, float fallback) { return TryGetGroundY(x, z, out var y) ? y : fallback; }
    public static float GroundY(Vector3 p, float fallback) { return GroundY(p.x, p.z, fallback); }
}
