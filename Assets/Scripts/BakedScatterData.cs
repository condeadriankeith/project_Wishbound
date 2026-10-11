using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Runtime data containers for baked scatter data.
/// These are used by IslandStylizedGrass and IslandStylizedTrees at runtime.
/// </summary>

[System.Serializable]
public class GrassChunkData
{
    public Matrix4x4[] matrices;
    public int count;
    public Bounds bounds;
}

[CreateAssetMenu(fileName = "GrassScatterData", menuName = "Island/Grass Scatter Data")]
public class GrassScatterData : ScriptableObject
{
    public List<GrassChunkData> chunks = new();
    public int TotalBlades()
    {
        int t = 0;
        foreach (var c in chunks) t += c.count;
        return t;
    }
}

[System.Serializable]
public class TreeChunkData
{
    public Matrix4x4[] matrices;
    public int count;
    public Bounds bounds;
}

[System.Serializable]
public class TreeVariantData
{
    public string meshPath;
    public Material leafMaterial;
    public List<TreeChunkData> chunks = new();
}

[CreateAssetMenu(fileName = "TreeScatterData", menuName = "Island/Tree Scatter Data")]
public class TreeScatterData : ScriptableObject
{
    public List<TreeVariantData> variants = new();
}