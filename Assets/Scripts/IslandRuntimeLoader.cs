using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Loads pre-baked island data at runtime instead of generating everything.
/// Add this to a GameObject in the scene to use baked data.
/// </summary>
public class IslandRuntimeLoader : MonoBehaviour
{
    [Header("Baked Data References")]
    public Mesh bakedIslandMesh;
    public Texture2D bakedHeightMap;
    public Texture2D bakedPathMap;
    public Texture2D bakedTerrainColor;
    public GrassScatterData grassScatterData;
    public TreeScatterData treeScatterData;

    [Header("Settings")]
    public bool useBakedData = true;
    public bool autoLoadOnStart = true;

    [SerializeField] bool dataLoaded = false;

    void Awake()
    {
        if (autoLoadOnStart && useBakedData)
            LoadBakedData();
    }

    public void LoadBakedData()
    {
        if (dataLoaded) return;

        var ib = IslandBuilder.Instance;
        if (ib == null) return;

        // Load island mesh
        if (bakedIslandMesh != null)
        {
            var mf = ib.GetComponent<MeshFilter>();
            var mc = ib.GetComponent<MeshCollider>();
            mf.sharedMesh = bakedIslandMesh;
            if (mc) mc.sharedMesh = bakedIslandMesh;
            var isr = ib.GetComponent<MeshRenderer>();
            if (isr != null && (isr.sharedMaterial == null || isr.sharedMaterial.shader == null || !isr.sharedMaterial.shader.isSupported))
            {
                var tsh = Shader.Find("Ghibli/ToonLit"); if (tsh == null) tsh = Shader.Find("Universal Render Pipeline/Lit");
                var tmat = new Material(tsh);
                tmat.SetColor("_Color", Color.white); tmat.SetColor("_InstColor", Color.white);
                tmat.SetFloat("_UseVertexColor", 1);
                isr.sharedMaterial = tmat;
            }

            // Set global heightmap for ocean shader
            if (bakedHeightMap != null)
            {
                ib.HeightTex = bakedHeightMap;
                Shader.SetGlobalTexture("_GhibliHeightMap", bakedHeightMap);
            }

            // Set pathmap
            if (bakedPathMap != null)
            {
                ib.PathMap = TextureToFloatArray(bakedPathMap);
            }

            // Update island world with loaded mesh bounds
            var iw = FindFirstObjectByType<IslandWorld>();
            if (iw != null) iw.OnIslandLoaded(bakedIslandMesh);
        }

        // Load grass scatter data
        if (grassScatterData != null)
        {
            var grassSystem = FindFirstObjectByType<IslandStylizedGrass>();
            if (grassSystem != null)
            {
                grassSystem.LoadBakedData(grassScatterData, bakedTerrainColor);
            }
        }

        // Load tree scatter data
        if (treeScatterData != null)
        {
            var treeSystem = FindFirstObjectByType<IslandStylizedTrees>();
            if (treeSystem != null)
            {
                treeSystem.LoadBakedData(treeScatterData);
            }
        }

        dataLoaded = true;
        Debug.Log("[IslandRuntimeLoader] Baked data loaded successfully!");
    }

    float[] TextureToFloatArray(Texture2D tex)
    {
        var pixels = tex.GetPixels();
        var result = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            result[i] = pixels[i].r;
        return result;
    }
}