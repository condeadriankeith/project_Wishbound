using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.IO;
using static TerrainMath;

public class IslandBaker : EditorWindow
{
    [MenuItem("Tools/Island/Bake Island Data")]
    public static void BakeIsland()
    {
        var ib = FindFirstObjectByType<IslandBuilder>();
        if (ib == null)
        {
            Debug.LogError("[IslandBaker] No IslandBuilder in scene.");
            return;
        }

        // Use Resources folder for runtime loading
        string savePath = "Assets/Resources/BakedIslandData";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(savePath))
            AssetDatabase.CreateFolder("Assets/Resources", "BakedIslandData");

        // Build island (generates mesh, heightmap, pathmap)
        ib.Build();

        var mf = ib.GetComponent<MeshFilter>();
        var mesh = mf.sharedMesh;

        // Save Island Mesh
        string meshPath = $"{savePath}/IslandMesh.asset";
        var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existingMesh != null)
        {
            EditorUtility.CopySerialized(mesh, existingMesh);
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.CreateAsset(Object.Instantiate(mesh), meshPath);
        }

        // Save Heightmap Texture
        string heightPath = $"{savePath}/IslandHeightMap.asset";
        var existingHeight = AssetDatabase.LoadAssetAtPath<Texture2D>(heightPath);
        if (existingHeight != null)
        {
            EditorUtility.CopySerialized(ib.HeightTex, existingHeight);
            AssetDatabase.SaveAssets();
        }
        else
        {
            var heightCopy = Object.Instantiate(ib.HeightTex);
            AssetDatabase.CreateAsset(heightCopy, heightPath);
        }

        // Save PathMap as Texture2D (for shader access)
        string pathMapPath = $"{savePath}/IslandPathMap.asset";
        var pathMapTex = CreatePathMapTexture(ib.PathMap);
        var existingPathMap = AssetDatabase.LoadAssetAtPath<Texture2D>(pathMapPath);
        if (existingPathMap != null)
        {
            EditorUtility.CopySerialized(pathMapTex, existingPathMap);
            AssetDatabase.SaveAssets();
            DestroyImmediate(pathMapTex);
        }
        else
        {
            AssetDatabase.CreateAsset(pathMapTex, pathMapPath);
        }

        // Save Terrain Color Texture (baked from vertex colors)
        string colorPath = $"{savePath}/IslandTerrainColor.asset";
        var grassSystem = FindFirstObjectByType<IslandStylizedGrass>();
        Texture2D colorTex = null;
        if (grassSystem != null)
        {
            colorTex = BakeTerrainColorTexture(mesh, grassSystem.colorTexSize);
            var existingColor = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            if (existingColor != null)
            {
                EditorUtility.CopySerialized(colorTex, existingColor);
                AssetDatabase.SaveAssets();
            }
            else
            {
                AssetDatabase.CreateAsset(colorTex, colorPath);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandBaker] Baked island data to {savePath}");
        EditorUtility.RevealInFinder(Path.GetFullPath(savePath));
    }

    [MenuItem("Tools/Island/Bake Grass Scatter Data")]
    public static void BakeGrassScatter()
    {
        var grassSystem = FindFirstObjectByType<IslandStylizedGrass>();
        if (grassSystem == null)
        {
            Debug.LogError("[IslandBaker] No IslandStylizedGrass in scene.");
            return;
        }

        var ib = IslandBuilder.Instance;
        if (ib == null || ib.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            Debug.LogError("[IslandBaker] Island not built yet. Run 'Bake Island Data' first.");
            return;
        }

        // Use Resources folder for runtime loading
        string savePath = "Assets/Resources/BakedIslandData";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(savePath))
            AssetDatabase.CreateFolder("Assets/Resources", "BakedIslandData");

        // Initialize grass system (scatters blades)
        var initMethod = typeof(IslandStylizedGrass).GetMethod("Init", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        initMethod.Invoke(grassSystem, null);

        // Access private fields via reflection
        var chunksField = typeof(IslandStylizedGrass).GetField("chunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var countsField = typeof(IslandStylizedGrass).GetField("counts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bndsField = typeof(IslandStylizedGrass).GetField("bnds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var chunks = (List<Matrix4x4[]>)chunksField.GetValue(grassSystem);
        var counts = (List<int>)countsField.GetValue(grassSystem);
        var bnds = (List<Bounds>)bndsField.GetValue(grassSystem);

        if (chunks == null || chunks.Count == 0)
        {
            Debug.LogError("[IslandBaker] No grass chunks generated.");
            return;
        }

        // Save scatter data as ScriptableObject
        var scatterData = ScriptableObject.CreateInstance<GrassScatterData>();
        scatterData.chunks = new List<GrassChunkData>();
        for (int i = 0; i < chunks.Count; i++)
        {
            scatterData.chunks.Add(new GrassChunkData
            {
                matrices = chunks[i],
                count = counts[i],
                bounds = bnds[i]
            });
        }

        string assetPath = $"{savePath}/GrassScatterData.asset";
        var existing = AssetDatabase.LoadAssetAtPath<GrassScatterData>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(scatterData, existing);
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.CreateAsset(scatterData, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandBaker] Baked {scatterData.TotalBlades()} grass blades in {chunks.Count} chunks to {assetPath}");
    }

    [MenuItem("Tools/Island/Bake Tree Scatter Data")]
    public static void BakeTreeScatter()
    {
        var treeSystem = FindFirstObjectByType<IslandStylizedTrees>();
        if (treeSystem == null)
        {
            Debug.LogError("[IslandBaker] No IslandStylizedTrees in scene.");
            return;
        }

        var ib = IslandBuilder.Instance;
        if (ib == null || ib.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            Debug.LogError("[IslandBaker] Island not built yet. Run 'Bake Island Data' first.");
            return;
        }

        // Use Resources folder for runtime loading
        string savePath = "Assets/Resources/BakedIslandData";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(savePath))
            AssetDatabase.CreateFolder("Assets/Resources", "BakedIslandData");

        // Initialize tree system
        var startMethod = typeof(IslandStylizedTrees).GetMethod("StartRoutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var enumerator = (System.Collections.IEnumerator)startMethod.Invoke(treeSystem, null);
        while (enumerator.MoveNext()) { } // Run coroutine to completion

        // Access private fields
        var varsField = typeof(IslandStylizedTrees).GetField("vars", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var vars = (object[])varsField.GetValue(treeSystem);

        var scatterData = ScriptableObject.CreateInstance<TreeScatterData>();
        scatterData.variants = new List<TreeVariantData>();

        foreach (var v in vars)
        {
            var chunksField = v.GetType().GetField("chunks", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var countsField = v.GetType().GetField("counts", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var bndsField = v.GetType().GetField("bnds", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var meshField = v.GetType().GetField("mesh", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var leafMatField = v.GetType().GetField("leaf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            var chunks = (List<Matrix4x4[]>)chunksField.GetValue(v);
            var counts = (List<int>)countsField.GetValue(v);
            var bnds = (List<Bounds>)bndsField.GetValue(v);
            var mesh = (Mesh)meshField.GetValue(v);
            var leafMat = (Material)leafMatField.GetValue(v);

            // Save tree meshes
            string meshName = $"TreeMesh_{scatterData.variants.Count}";
            string meshPath = $"{savePath}/{meshName}.asset";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(mesh, existingMesh);
            }
            else
            {
                AssetDatabase.CreateAsset(Object.Instantiate(mesh), meshPath);
            }

            // Resources.Load uses path relative to Resources folder
            string resourcesPath = $"BakedIslandData/{meshName}";

            var variantData = new TreeVariantData
            {
                meshPath = resourcesPath,
                leafMaterial = leafMat,
                chunks = new List<TreeChunkData>()
            };

            for (int i = 0; i < chunks.Count; i++)
            {
                variantData.chunks.Add(new TreeChunkData
                {
                    matrices = chunks[i],
                    count = counts[i],
                    bounds = bnds[i]
                });
            }

            scatterData.variants.Add(variantData);
        }

        string assetPath = $"{savePath}/TreeScatterData.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TreeScatterData>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(scatterData, existing);
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.CreateAsset(scatterData, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IslandBaker] Baked tree scatter data to {assetPath}");
    }

    [MenuItem("Tools/Island/Bake All")]
    public static void BakeAll()
    {
        BakeIsland();
        BakeGrassScatter();
        BakeTreeScatter();
        Debug.Log("[IslandBaker] All baking complete!");
    }

    static Texture2D CreatePathMapTexture(float[] pathMap)
    {
        int V = Mathf.RoundToInt(Mathf.Sqrt(pathMap.Length));
        var tex = new Texture2D(V, V, TextureFormat.RHalf, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "IslandPathMap"
        };
        var pixels = new Color[V * V];
        for (int i = 0; i < V * V; i++)
            pixels[i] = new Color(pathMap[i], 0, 0, 1);
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    static Texture2D BakeTerrainColorTexture(Mesh mesh, int texSize)
    {
        int T = texSize;
        var cols = mesh.colors;
        var tex = new Texture2D(T, T, TextureFormat.RGB24, true, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[T * T];
        for (int j = 0; j < T; j++)
            for (int i = 0; i < T; i++)
            {
                double wx = (i / (double)(T - 1) - 0.5) * 920.0;
                double wz = (0.5 - j / (double)(T - 1)) * 920.0;
                int vi = Mathf.Clamp(Mathf.RoundToInt((float)((wx + E) / S)), 0, N);
                int vj = Mathf.Clamp(Mathf.RoundToInt((float)((wz + E) / S)), 0, N);
                Color c = cols[vj * V + vi];
                c.a = 1f;
                px[j * T + i] = c;
            }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }
}

// Data containers for baked scatter data
[System.Serializable]
public class GrassChunkData
{
    public Matrix4x4[] matrices;
    public int count;
    public Bounds bounds;
}

[System.Serializable]
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

[System.Serializable]
public class TreeScatterData : ScriptableObject
{
    public List<TreeVariantData> variants = new();
}