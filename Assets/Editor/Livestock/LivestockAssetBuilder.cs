using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Turns the Blender livestock FBX files into toon prefabs and audits every custom model.
// Materials are rebuilt as Ghibli/ToonLit using the colour each Blender material carried.
public static class LivestockAssetBuilder
{
    public const string ToonShader = "Ghibli/ToonLit";
    public const string ModelDir = "Assets/Art/Animals/Models";
    public const string AnimalMatDir = "Assets/Art/Animals/Materials";
    public const string PrefabDir = "Assets/Art/Animals/Prefabs";
    public const string ToonMatDir = "Assets/Art/Materials/Toon";
    public const string QuestPrefabDir = "Assets/Resources/VerticalSlice";
    public const string QuestModelDir = "Assets/WishboundVerticalSlice/Art/Models/Quest";
    static readonly string[] Species = { "Hen", "Cat", "Cow", "Sheep", "CowPen" };

    [MenuItem("Wishbound/Livestock/Build Prefabs")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(AnimalMatDir);
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(ToonMatDir);
        AssetDatabase.Refresh();
        foreach (var s in Species) BuildSpecies(s);
        foreach (var file in Directory.GetFiles(QuestPrefabDir, "*.prefab")) RetoonPrefab(file.Replace('\\', '/'));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Livestock] built " + Species.Length + " animal prefabs and checked quest prefabs");
    }

    static void BuildSpecies(string species)
    {
        string fbx = ModelDir + "/" + species + ".fbx";
        var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (importer == null) { Debug.LogError("[Livestock] missing " + fbx); return; }
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var inst = Object.Instantiate(src);
        inst.name = species;
        var bounds = new Bounds(inst.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = ToonFor(mats[i], AnimalMatDir);
            r.sharedMaterials = mats;
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        }
        string prefab = PrefabDir + "/" + species + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(inst, prefab);
        Object.DestroyImmediate(inst);
        Debug.Log("[Livestock] " + species + " size=" + bounds.size.ToString("F2") + " -> " + prefab);
    }

    // Returns the toon material for a source material, creating it once per name.
    static Material ToonFor(Material src, string dir)
    {
        string name = src != null ? src.name : "Default";
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find(ToonShader)) { name = name };
        Color c = Color.white;
        if (src != null)
        {
            if (src.HasProperty("_BaseColor")) c = src.GetColor("_BaseColor");
            else if (src.HasProperty("_Color")) c = src.GetColor("_Color");
        }
        m.SetColor("_Color", c);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void RetoonPrefab(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        bool changed = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].shader != null && mats[i].shader.name == ToonShader) continue;
                mats[i] = ToonFor(mats[i], ToonMatDir);
                changed = true;
            }
            r.sharedMaterials = mats;
        }
        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Positive when triangle winding faces outward (closed mesh). Negative means inside-out.
    static double SignedVolume(Mesh m)
    {
        var v = m.vertices;
        var t = m.triangles;
        double s = 0;
        for (int i = 0; i < t.Length; i += 3)
            s += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
        return s / 6.0;
    }

    [MenuItem("Wishbound/Livestock/Audit Custom Models")]
    public static void Audit()
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(ModelDir, "*.fbx")) AuditAsset(file.Replace('\\', '/'), lines);
        foreach (var file in Directory.GetFiles(QuestModelDir, "*.fbx")) AuditAsset(file.Replace('\\', '/'), lines);
        foreach (var file in Directory.GetFiles(QuestPrefabDir, "*.prefab")) AuditAsset(file.Replace('\\', '/'), lines);
        Directory.CreateDirectory("Captures/VerticalSlice");
        File.WriteAllLines("Captures/VerticalSlice/model-audit.txt", lines);
        Debug.Log("[Livestock] audit wrote " + lines.Count + " entries");
    }

    static void AuditAsset(string path, List<string> lines)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (root == null) { lines.Add(path + " :: could not load"); return; }
        int meshes = 0, inward = 0, unreadable = 0, nonToon = 0, skinned = 0;
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = f.sharedMesh;
            if (m == null) continue;
            meshes++;
            if (!m.isReadable) { unreadable++; continue; }
            if (SignedVolume(m) < 0) inward++;
        }
        skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name != ToonShader) nonToon++;
        lines.Add(path + " :: meshes=" + meshes + " inward=" + inward + " unreadable=" + unreadable + " skinned=" + skinned + " nonToonMaterials=" + nonToon);
    }
}
