#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class IslandGenerator
{
    public static IslandParams FindParams()
    {
        var g = AssetDatabase.FindAssets("t:IslandParams");
        return g.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<IslandParams>(AssetDatabase.GUIDToAssetPath(g[0]));
    }

    [MenuItem("Tools/Island/1. Generate Terrain")]
    public static void Generate()
    {
        var p = FindParams();
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (p == null || terrain == null) { Debug.LogError("Need an IslandParams asset and a Terrain in the scene."); return; }

        var td = terrain.terrainData;
        td.heightmapResolution = p.heightmapRes;
        td.size = new Vector3(p.terrainSize, p.terrainHeight, p.terrainSize);
        int n = td.heightmapResolution;
        float step = p.terrainSize / (n - 1);
        float half = p.terrainSize * 0.5f;
        var field = new IslandField(p);
        var hm = new float[n, n];                      // [z, x]

        EditorUtility.DisplayProgressBar("Island", "Sampling heightfield…", 0.1f);
        Parallel.For(0, n, zi =>
        {
            float z = zi * step - half;
            for (int xi = 0; xi < n; xi++)
                hm[zi, xi] = math.saturate((field.Height(xi * step - half, z) + p.seabedDepth) / p.terrainHeight);
        });
        td.SetHeights(0, 0, hm);
        terrain.transform.position = new Vector3(-half, -p.seabedDepth, -half);   // island centred on world (0,0)

        EditorUtility.DisplayProgressBar("Island", "Painting layers…", 0.7f);
        PaintSplat(td, hm, p);
        EditorUtility.ClearProgressBar();
        EditorUtility.SetDirty(td);
        Debug.Log("Island generated.");
    }

    // Terrain layers must be assigned in this order: 0 Grass, 1 Chalk, 2 Sand, 3 Seabed
    static void PaintSplat(TerrainData td, float[,] hm, IslandParams p)
    {
        if (td.terrainLayers == null || td.terrainLayers.Length < 4)
        { Debug.LogWarning("Assign 4 Terrain Layers (Grass, Chalk, Sand, Seabed), then run Generate again."); return; }

        const int ar = 2048;
        td.alphamapResolution = ar;
        int n = hm.GetLength(0);
        float cell = p.terrainSize / (n - 1), half = p.terrainSize * 0.5f;
        var am = new float[ar, ar, 4];

        Parallel.For(0, ar, zi =>
        {
            int hz = Mathf.Clamp(Mathf.RoundToInt(zi * (n - 1f) / (ar - 1f)), 1, n - 2);
            for (int xi = 0; xi < ar; xi++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt(xi * (n - 1f) / (ar - 1f)), 1, n - 2);
                float h  = hm[hz, hx] * p.terrainHeight - p.seabedDepth;
                float dx = (hm[hz, hx + 1] - hm[hz, hx - 1]) * p.terrainHeight / (2f * cell);
                float dz = (hm[hz + 1, hx] - hm[hz - 1, hx]) * p.terrainHeight / (2f * cell);
                float slope = math.degrees(math.atan(math.sqrt(dx * dx + dz * dz)));

                float2 w = new float2(hx * cell - half, hz * cell - half);
                float patch = math.saturate(noise.snoise(w / 9f) * 1.6f);       // grass tufts clinging to the chalk

                float chalk = math.smoothstep(50f, 66f, slope);
                chalk *= 1f - 0.65f * patch * (1f - math.smoothstep(58f, 70f, slope));
                float seabed = math.smoothstep(-0.5f, -2.5f, h);
                float sand = (1f - math.smoothstep(0.8f, 2.2f, h)) * (1f - seabed) * (1f - chalk);
                float grass = math.max(0f, 1f - chalk - seabed - sand);
                float sum = chalk + seabed + sand + grass + 1e-5f;
                am[zi, xi, 0] = grass / sum; am[zi, xi, 1] = chalk / sum;
                am[zi, xi, 2] = sand / sum;  am[zi, xi, 3] = seabed / sum;
            }
        });
        td.SetAlphamaps(0, 0, am);
    }

    [MenuItem("Tools/Island/2. Scatter Shrubs (optional)")]
    public static void ScatterShrubs()
    {
        var p = FindParams(); var terrain = Object.FindFirstObjectByType<Terrain>(); var td = terrain.terrainData;
        if (td.treePrototypes.Length == 0) { Debug.LogError("Add a shrub prefab as a Tree prototype first."); return; }
        var f = new IslandField(p);
        var rng = new System.Random(p.seed + 7);
        var list = new List<TreeInstance>();
        float half = p.terrainSize * 0.5f;
        for (int i = 0; i < 80000 && list.Count < 6000; i++)
        {
            float wx = ((float)rng.NextDouble() * 2f - 1f) * half;
            float wz = ((float)rng.NextDouble() * 2f - 1f) * half;
            float h = f.Height(wx, wz, out float ramp);
            if (h < 6f || ramp > 0.05f) continue;                     // not on beach, not on a ramp ledge
            float u = (wx + half) / p.terrainSize, v = (wz + half) / p.terrainSize;
            if (td.GetSteepness(u, v) > 28f) continue;
            list.Add(new TreeInstance
            {
                prototypeIndex = rng.Next(td.treePrototypes.Length),
                position = new Vector3(u, 0, v),
                widthScale = 0.8f + (float)rng.NextDouble() * 0.7f,
                heightScale = 0.8f + (float)rng.NextDouble() * 0.7f,
                color = Color.white, lightmapColor = Color.white,
                rotation = (float)rng.NextDouble() * 6.283f
            });
        }
        td.SetTreeInstances(list.ToArray(), true);
        Debug.Log($"Placed {list.Count} shrubs.");
    }

    [MenuItem("Tools/Island/3. Fill Grass Detail (optional)")]
    public static void FillGrass()
    {
        var terrain = Object.FindFirstObjectByType<Terrain>(); var td = terrain.terrainData;
        if (td.detailPrototypes.Length == 0) { Debug.LogError("Add a grass Detail prototype first."); return; }
        const int res = 2048;
        td.SetDetailResolution(res, 32);
        float[,,] am = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
        int aw = td.alphamapWidth;
        var d = new int[res, res];
        var rng = new System.Random(1);
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float g = am[z * aw / res, x * aw / res, 0];
                d[z, x] = (g > 0.6f && rng.NextDouble() < 0.55) ? 6 : 0;
            }
        td.SetDetailLayer(0, 0, 0, d);
    }
}
#endif
