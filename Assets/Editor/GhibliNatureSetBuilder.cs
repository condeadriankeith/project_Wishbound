#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Assets/Resources/GhibliNatureSet.asset from the package
/// "3D set of stylized nature - GHIBLI style" (trees, shrubs, grass, flowers, rocks).
/// - Package materials are CLONED to Assets/Materials/GhibliNature with GPU instancing enabled
///   (package originals are never modified).
/// - Prefabs with several renderers / offset transforms are baked into ONE mesh (submesh per material)
///   saved in Assets/Models/GhibliNature.
/// Menu: Island/Ghibli/Build Nature Set  (safe to re-run).
/// </summary>
public static class GhibliNatureSetBuilder
{
    const string PkgRoot = "Assets/3D set of stylized nature - GHIBLI style/Art/";
    const string MatDir = "Assets/Materials/GhibliNature";
    const string MeshDir = "Assets/Models/GhibliNature";
    const string SetPath = "Assets/Resources/GhibliNatureSet.asset";

    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();
    static readonly StringBuilder log = new StringBuilder();

    [MenuItem("Island/Ghibli/Build Nature Set")]
    public static void BuildMenu() { Debug.Log(Build()); }

    public static string Build()
    {
        log.Length = 0; matCache.Clear();
        EnsureFolder(MatDir); EnsureFolder(MeshDir);
        var set = AssetDatabase.LoadAssetAtPath<GhibliNatureSet>(SetPath);
        if (set == null) { set = ScriptableObject.CreateInstance<GhibliNatureSet>(); AssetDatabase.CreateAsset(set, SetPath); }

        set.trees = Collect("TREES", n => n.StartsWith("Tree_") || n == "Big Tree");
        set.shrubs = Collect("SHRUBS", n => n.StartsWith("Shrubs_"));
        set.grass = Collect("GRASS", n => n.StartsWith("Gras_"));
        set.flowers = Collect("FLOWERS", n => n.StartsWith("Flower_"));
        set.rocks = Collect("ROCKS", n => n.StartsWith("Rock_"));

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return log.ToString();
    }

    static List<GhibliPiece> Collect(string label, System.Func<string, bool> filter)
    {
        log.AppendLine("== " + label);
        var res = new List<GhibliPiece>();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PkgRoot + "Prefabs" });
        var paths = new List<string>();
        foreach (var g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        paths.Sort(System.StringComparer.Ordinal);
        foreach (var p in paths)
        {
            string n = Path.GetFileNameWithoutExtension(p);
            if (!filter(n)) continue;
            var piece = MakePiece(p, n);
            if (piece != null) res.Add(piece);
        }
        return res;
    }

    static bool IsIdentity(Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (Mathf.Abs(m[r, c] - (r == c ? 1f : 0f)) > 1e-3f) return false;
        return true;
    }

    static GhibliPiece MakePiece(string prefabPath, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;
        var parts = new List<KeyValuePair<MeshFilter, MeshRenderer>>();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null && mf.sharedMesh != null) parts.Add(new KeyValuePair<MeshFilter, MeshRenderer>(mf, mr));
        }
        if (parts.Count == 0) { log.AppendLine(name + ": NO MESH, skipped"); return null; }

        Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;
        Mesh mesh; Material[] mats;
        bool direct = parts.Count == 1 && IsIdentity(rootInv * parts[0].Key.transform.localToWorldMatrix);
        if (direct)
        {
            mesh = parts[0].Key.sharedMesh;
            var src = parts[0].Value.sharedMaterials;
            mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) mats[i] = Clone(src[i]);
        }
        else
        {
            var uniq = new List<Material>();
            var groups = new List<List<CombineInstance>>();
            foreach (var kv in parts)
            {
                var m = kv.Key.sharedMesh; var sm = kv.Value.sharedMaterials;
                var rel = rootInv * kv.Key.transform.localToWorldMatrix;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var mat = sm[Mathf.Min(s, sm.Length - 1)];
                    int gi = uniq.IndexOf(mat);
                    if (gi < 0) { uniq.Add(mat); groups.Add(new List<CombineInstance>()); gi = uniq.Count - 1; }
                    groups[gi].Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = rel });
                }
            }
            var partInst = new CombineInstance[uniq.Count];
            for (int gi = 0; gi < uniq.Count; gi++)
            {
                var pm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                pm.CombineMeshes(groups[gi].ToArray(), true, true);
                partInst[gi] = new CombineInstance { mesh = pm, subMeshIndex = 0, transform = Matrix4x4.identity };
            }
            mesh = new Mesh { name = name + "_combined", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(partInst, false, false);
            mesh.RecalculateBounds();
            string mp = MeshDir + "/" + name + "_combined.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(mp) != null) AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mesh, mp);
            mats = new Material[uniq.Count];
            for (int i = 0; i < uniq.Count; i++) mats[i] = Clone(uniq[i]);
        }

        var b = mesh.bounds;
        var piece = new GhibliPiece { name = name, mesh = mesh, materials = mats, size = b.size, minY = b.min.y, maxY = b.max.y };
        var shaderNames = new List<string>();
        foreach (var m in mats) shaderNames.Add(m != null ? m.shader.name : "null");
        log.AppendLine(name + ": " + (direct ? "direct" : "combined(" + parts.Count + ")") + " size=" + b.size.ToString("F2") + " y[" + b.min.y.ToString("F2") + ".." + b.max.y.ToString("F2") + "] subs=" + mesh.subMeshCount + " mats=" + mats.Length + " [" + string.Join(", ", shaderNames) + "]");
        return piece;
    }

    static Material Clone(Material src)
    {
        if (src == null) return null;
        string sp = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(sp)) { src.enableInstancing = true; return src; }
        Material cached;
        if (matCache.TryGetValue(sp, out cached)) return cached;
        string dst = MatDir + "/" + src.name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(dst);
        if (existing != null) AssetDatabase.DeleteAsset(dst);
        AssetDatabase.CopyAsset(sp, dst);
        var clone = AssetDatabase.LoadAssetAtPath<Material>(dst);
        clone.enableInstancing = true;
        EditorUtility.SetDirty(clone);
        matCache[sp] = clone;
        return clone;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
