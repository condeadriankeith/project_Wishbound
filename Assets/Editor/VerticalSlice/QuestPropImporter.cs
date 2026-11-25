using System.IO;
using UnityEditor;
using UnityEngine;

public static class QuestPropImporter
{
    const string Source = "Assets/WishboundVerticalSlice/Art/Models/Quest/";
    const string Output = "Assets/Resources/VerticalSlice/";

    [MenuItem("Wishbound/Vertical Slice/Import Quest Props")]
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        var shader = Shader.Find("Ghibli/ToonLit");
        if (!shader) throw new System.InvalidOperationException("Ghibli/ToonLit shader unavailable");
        foreach (string file in Directory.GetFiles(Source, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
            string name = Path.GetFileNameWithoutExtension(file);
            string path = Output + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(file));
            material.SetColor("_Color", Color.white);
            material.SetFloat("_BrushStrength", name == "Crystal" ? .06f : .14f);
            material.SetFloat("_RimStrength", name == "Crystal" ? .25f : .12f);
            material.SetColor("_EmissionColor", name == "Crystal" ? new Color(.035f,.09f,.12f) : Color.black);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }
        foreach (string file in Directory.GetFiles(Source, "*.fbx"))
        {
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(file);
            modelImporter.isReadable = true;
            modelImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            modelImporter.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = Path.GetFileNameWithoutExtension(file);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var assigned = renderer.sharedMaterials;
                for (int i = 0; i < assigned.Length; i++)
                {
                    if (!assigned[i]) continue;
                    string name = assigned[i].name.Split('.')[0];
                    var replacement = AssetDatabase.LoadAssetAtPath<Material>(Output + name + ".mat");
                    if (replacement) assigned[i] = replacement;
                }
                renderer.sharedMaterials = assigned;
            }
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new System.InvalidOperationException("No renderers: " + file);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            if (root.name.StartsWith("ShadowStone") || root.name == "FerryPier" || root.name == "ObservatoryAltar")
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.center = root.transform.InverseTransformPoint(bounds.center);
                collider.size = bounds.size;
                if (root.name == "FerryPier") { collider.center = new Vector3(0,.43f,0); collider.size = new Vector3(2.35f,.12f,3.2f); }
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.025f, renderers) });
            lod.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, Output + root.name + ".prefab");
            Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[VerticalSlice] Quest prop prefabs imported; geometry LOD reduction remains pending.");
    }
}
