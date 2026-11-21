using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Circles (world XZ) where the scattered grass and flowers must not grow - courtyards, plazas, interiors' doorsteps.
/// Register in Awake (before the vegetation scatters in Start). Included in the grass cache signature.
/// </summary>
public static class GrassExclusions
{
    static readonly List<Vector3> zones = new List<Vector3>();   // x, z (world), radius
    public static void Add(Vector3 worldCentre, float radius) { zones.Add(new Vector3(worldCentre.x, worldCentre.z, radius)); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] public static void Clear() => zones.Clear();
    /// <summary>Test in terrain source coordinates (world z is mirrored).</summary>
    public static bool Contains(double sx, double sz)
    {
        float wx = (float)sx, wz = TerrainMath.MirrorZ ? (float)-sz : (float)sz;
        for (int i = 0; i < zones.Count; i++) { float dx = wx - zones[i].x, dz = wz - zones[i].y; if (dx * dx + dz * dz < zones[i].z * zones[i].z) return true; }
        return false;
    }
    public static long Hash() { long h = zones.Count; foreach (var z in zones) h = h * 31 + (long)(z.x * 10) * 7 + (long)(z.y * 10) * 13 + (long)(z.z * 10); return h; }
}
