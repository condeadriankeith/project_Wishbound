using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-safe catalogue of the "3D set of stylized nature - GHIBLI style" pieces we use
/// (trees, shrubs, grass, flowers, rocks). Built by Assets/Editor/GhibliNatureSetBuilder.cs
/// (menu: Island/Ghibli/Build Nature Set) and loaded with Resources.Load("GhibliNatureSet").
/// Materials are instancing-enabled clones of the package materials (package assets stay untouched).
/// See Assets/Docs/GHIBLI_NATURE_INTEGRATION.md
/// </summary>
[System.Serializable]
public class GhibliPiece
{
    public string name;
    public Mesh mesh;
    public Material[] materials;   // one per submesh
    public Vector3 size;           // mesh bounds size (m)
    public float minY, maxY;       // mesh bounds extent in Y (origin = prefab pivot)
    public float MaxXZ { get { return Mathf.Max(size.x, size.z); } }
}

[CreateAssetMenu(fileName = "GhibliNatureSet", menuName = "Island/Ghibli Nature Set")]
public class GhibliNatureSet : ScriptableObject
{
    public List<GhibliPiece> trees = new List<GhibliPiece>();
    public List<GhibliPiece> shrubs = new List<GhibliPiece>();
    public List<GhibliPiece> grass = new List<GhibliPiece>();
    public List<GhibliPiece> flowers = new List<GhibliPiece>();
    public List<GhibliPiece> rocks = new List<GhibliPiece>();

    static GhibliNatureSet cached;
    public static GhibliNatureSet Load()
    {
        if (cached == null) cached = Resources.Load<GhibliNatureSet>("GhibliNatureSet");
        return cached;
    }

    public static GhibliPiece Find(List<GhibliPiece> list, string pieceName)
    {
        if (list == null) return null;
        foreach (var p in list) if (p != null && p.name == pieceName) return p;
        return null;
    }
}
