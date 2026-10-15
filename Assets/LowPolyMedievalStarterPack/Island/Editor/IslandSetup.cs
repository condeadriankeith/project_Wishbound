#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// Scaffolding for §3/§5: creates the params asset, Terrain + TerrainData,
/// and 4 procedural TerrainLayers (Grass, Chalk, Sand, Seabed) in guide order.
public static class IslandSetup
{
    [MenuItem("Tools/Island/0. Setup Scene Assets")]
    public static void Setup()
    {
        IslandParams p = IslandGenerator.FindParams();
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<IslandParams>();
            p.heightmapRes = 1025;   // iterate fast; raise to 4097 for final
            AssetDatabase.CreateAsset(p, "Assets/Island/Terrain/IslandParams.asset");
        }

        Terrain terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null)
        {
            TerrainData td = new TerrainData();
            td.heightmapResolution = 1025;
            td.size = new Vector3(p.terrainSize, p.terrainHeight, p.terrainSize);
            AssetDatabase.CreateAsset(td, "Assets/Island/Terrain/ChalkTerrain.asset");
            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "ChalkIsland";
            terrain = go.GetComponent<Terrain>();
        }

        EnsureLayers(terrain);

        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 1;
        terrain.basemapDistance = 2000;
        terrain.detailObjectDistance = 120;
        terrain.treeDistance = 500;
        terrain.treeBillboardDistance = 150;

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        Debug.Log("Island setup done: params + terrain + 4 layers assigned.");
    }

    static void EnsureLayers(Terrain terrain)
    {
        string[] names = { "Grass", "Chalk", "Sand", "Seabed" };
        Color[] cols = {
            new Color(0x7D / 255f, 0xB5 / 255f, 0x44 / 255f),
            new Color(0xF4 / 255f, 0xF1 / 255f, 0xEA / 255f),
            new Color(0xD9 / 255f, 0xC9 / 255f, 0xA0 / 255f),
            new Color(0x6F / 255f, 0xA3 / 255f, 0x8B / 255f),
        };
        float[] tiles = { 5f, 14f, 4f, 8f };
        var layers = new TerrainLayer[4];
        for (int i = 0; i < 4; i++)
        {
            string texPath = "Assets/Island/Textures/T_" + names[i] + ".asset";
            string layPath = "Assets/Island/Terrain/Layer_" + names[i] + ".terrainlayer";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = MakeTexture(cols[i], i);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(tiles[i], tiles[i]);
            layer.smoothness = i == 0 ? 0.1f : 0.05f;
            EditorUtility.SetDirty(layer);
            layers[i] = layer;
        }
        terrain.terrainData.terrainLayers = layers;
        EditorUtility.SetDirty(terrain.terrainData);
    }

    static Texture2D MakeTexture(Color c, int kind)
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGB24, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        var rng = new System.Random(1000 + kind);
        float[] colHash = new float[S];
        for (int x = 0; x < S; x++) colHash[x] = (float)rng.NextDouble();
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = (float)rng.NextDouble() - 0.5f;
                float v = 1f + n * 0.10f;
                if (kind == 1)   // chalk: faint vertical streaks + grey-pink mottling
                    v *= 1f + (colHash[x] - 0.5f) * 0.10f + ((x * 7 + y * 13) % 5 == 0 ? -0.03f : 0f);
                if (kind == 0)   // grass: fine painterly grain (no tiling pattern)
                    v *= 1f + (n * 0.5f + (colHash[(x * 13 + y * 7) % S] - 0.5f) * 0.5f) * 0.12f;
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(c.r * v + (kind == 1 && (x + y) % 11 == 0 ? 0.02f : 0f)),
                    Mathf.Clamp01(c.g * v),
                    Mathf.Clamp01(c.b * v + (kind == 1 && (x * 3 + y) % 13 == 0 ? 0.015f : 0f))));
            }
        tex.Apply(true);
        return tex;
    }
}
#endif
