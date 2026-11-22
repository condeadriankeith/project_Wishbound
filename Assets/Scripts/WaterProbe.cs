using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Answers "is there river / lake water near here?" from the RiversAndLake mesh vertices (that mesh has no collider,
/// and sea level alone cannot see ponds that sit high up on the island). Built lazily once, then O(1) per query.
/// </summary>
public static class WaterProbe
{
    const float Cell = 4f;
    static Dictionary<long, List<Vector3>> grid;
    static bool built, missing;

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    static void Build()
    {
        built = true;
        var go = GameObject.Find("RiversAndLake");
        var mf = go ? go.GetComponent<MeshFilter>() : null;
        if (!mf || !mf.sharedMesh) { missing = true; return; }
        var tr = mf.transform; var vs = mf.sharedMesh.vertices;
        grid = new Dictionary<long, List<Vector3>>();
        for (int i = 0; i < vs.Length; i++)
        {
            Vector3 w = tr.TransformPoint(vs[i]);
            long k = Key(Mathf.FloorToInt(w.x / Cell), Mathf.FloorToInt(w.z / Cell));
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector3>(4);
            l.Add(w);
        }
    }

    /// <summary>True if any water vertex is within radius (horizontal) and within maxYDiff of p's height.</summary>
    public static bool Near(Vector3 p, float radius, float maxYDiff)
    {
        if (!built) Build();
        if (missing) return false;
        int r = Mathf.CeilToInt(radius / Cell);
        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
        float r2 = radius * radius;
        for (int x = cx - r; x <= cx + r; x++)
        for (int z = cz - r; z <= cz + r; z++)
        {
            if (!grid.TryGetValue(Key(x, z), out var l)) continue;
            for (int i = 0; i < l.Count; i++)
            {
                float dx = l[i].x - p.x, dz = l[i].z - p.z;
                if (dx * dx + dz * dz <= r2 && Mathf.Abs(l[i].y - p.y) <= maxYDiff) return true;
            }
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { built = false; missing = false; grid = null; }
}
