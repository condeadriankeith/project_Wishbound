using System.IO;
using UnityEditor;
using UnityEngine;

public static class VillageAnimalImporter
{
    [MenuItem("Wishbound/Animals/Rebuild Prefabs")]
    public static void Build()
    {
        const string output = "Assets/Resources/VillageAnimals/";
        Directory.CreateDirectory(output);
        AssetDatabase.Refresh();
        var shader = Shader.Find("Ghibli/ToonLit");
        if (!shader || !shader.isSupported) throw new System.InvalidOperationException("Animal toon shader unavailable.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(output + "AnimalToon.mat");
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, output + "AnimalToon.mat");
        }
        material.SetFloat("_UseVertexColor", 1);
        material.SetFloat("_BrushStrength", .08f);
        material.SetFloat("_RimStrength", .13f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);

        foreach (string species in new[] { "Hen", "Cat", "Cow", "Sheep" })
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Animals/AnimatedModels/" + species + ".fbx");
            if (!source) throw new System.InvalidOperationException("Missing model " + species);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (Transform pivot in model.transform)
                {
                    int suffix = pivot.name.IndexOf('.');
                    if (suffix >= 0) pivot.name = pivot.name.Substring(0, suffix);
                }
                var renderers = model.GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                        throw new System.InvalidOperationException("Missing vertex colors on " + mesh.name);
                    renderer.sharedMaterial = material;
                    bounds.Encapsulate(renderer.bounds);
                }
                var hips = model.transform.Find("Body");
                var head = model.transform.Find("Head");
                if (!hips || !head || !model.transform.Find("Tail") || bounds.size.y < .45f || bounds.size.y > 2.2f)
                    throw new System.InvalidOperationException("Unexpected animal pivots or scale for " + species + ": " + bounds);
                model.name = species;
                PrefabUtility.SaveAsPrefabAsset(model, output + species + ".prefab");
                Debug.Log("[VillageAnimals] " + species + " " + bounds.size + ", " + renderers.Length + " renderers");
            }
            finally { Object.DestroyImmediate(model); }
        }
        AssetDatabase.SaveAssets();
    }
}
