using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Wishbound > Bake Interiors: builds every Interior_* scene's hand-laid design (InteriorDesigns) as plain
/// GameObjects inside the scene, and saves the generated meshes + materials as assets so the scenes are fully
/// editable and self-contained. Re-baking a scene replaces its contents (hand edits would be lost - bake once,
/// then edit).
/// </summary>
public static class InteriorBaker
{
    const string ScenesDir = "Assets/Scenes/Interiors";
    const string AssetsDir = "Assets/Art/Interiors/Generated";

    [MenuItem("Tools/Wishbound/Bake Interiors")]
    public static void BakeAll()
    {
        saved.Clear();
        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        System.IO.Directory.CreateDirectory(AssetsDir);
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { ScenesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sc = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            foreach (var root in sc.GetRootGameObjects())
            {
                var b = root.GetComponent<InteriorBuilder>(); if (!b) continue;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                b.Build();
                Persist(root, b.style.ToString());
            }
            EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
            if (sc != active) EditorSceneManager.CloseScene(sc, true);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[InteriorBaker] interiors baked");
    }

    // runtime-made meshes / materials -> assets (shared ones are saved once and re-used)
    static readonly Dictionary<Object, Object> saved = new Dictionary<Object, Object>();
    static void Persist(GameObject root, string tag)
    {
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh && !AssetDatabase.Contains(mf.sharedMesh)) mf.sharedMesh = (Mesh)Save(mf.sharedMesh, "mesh_" + mf.sharedMesh.name + "_" + MeshKey(mf.sharedMesh), ".asset");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] && !AssetDatabase.Contains(mats[i])) mats[i] = (Material)Save(mats[i], "mat_" + MatKey(mats[i]), ".mat");
            r.sharedMaterials = mats;
        }
    }
    static Object Save(Object o, string name, string ext)
    {
        if (saved.TryGetValue(o, out var s) && s) return s;
        string p = AssetsDir + "/" + Sanitize(name) + ext;
        var existing = AssetDatabase.LoadMainAssetAtPath(p);
        if (existing && existing.GetType() == o.GetType())
        {
            // re-bake: overwrite the asset in place (same path, same GUID) instead of piling up duplicates
            EditorUtility.CopySerialized(o, existing); EditorUtility.SetDirty(existing);
            saved[o] = existing; return existing;
        }
        AssetDatabase.CreateAsset(o, p);
        saved[o] = o; return o;
    }
    static string MeshKey(Mesh m)
    {
        unchecked { int h = m.vertexCount * 31 + m.triangles.Length; foreach (var v in m.vertices) h = h * 17 + v.GetHashCode(); return ((uint)h).ToString("x8"); }
    }
    static string MatKey(Material m)
    {
        var c = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white; var e = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
        return m.shader.name.Replace('/', '-') + "_" + ColorUtility.ToHtmlStringRGB(c) + "_" + ColorUtility.ToHtmlStringRGB(e * .25f) + "_" + (m.HasProperty("_BrushStrength") ? m.GetFloat("_BrushStrength").ToString("0.00") : "");
    }
    static string Sanitize(string s) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }
}
