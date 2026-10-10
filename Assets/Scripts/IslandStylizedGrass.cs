using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Applies the practiced StylizedGrass look (blade mesh + Shader Graphs/StylizedGrass)
/// across the Island mesh terrain. Bakes island vertex colors into a terrain-color
/// texture (same sampling contract as the test scene's top-down camera feed) and
/// GPU-instances grass blades wherever grass grows. Attach to any GameObject in
/// the Island scene; requires IslandBuilder in the scene.
/// </summary>
public class IslandStylizedGrass : MonoBehaviour
{
    [Header("Source assets (from StylizedGrass_TestScene practice)")]
    public Mesh grassMesh;
    public Material sourceMaterial;

    [Header("Coverage")]
    public int bladeCount = 400000;
    public int seed = 777;
    public float minHeight = 1.5f;
    public float maxHeight = 330f;
    public float minSlopeY = 0.35f;
    public float minWidth = 1.8f, maxWidth = 2.8f;
    public float minBladeHeight = 1.6f, maxBladeHeight = 2.8f;

    [Header("Terrain-color bake")]
    public int colorTexSize = 512;

    Material grassMat;
    readonly List<Matrix4x4[]> chunks = new();
    readonly List<int> counts = new();
    readonly List<Bounds> bnds = new();
    readonly Plane[] planes = new Plane[6];

    void Start()
    {
        var ib = IslandBuilder.Instance;
        if (ib == null) { Debug.LogError("[IslandStylizedGrass] No IslandBuilder in scene."); return; }
        var mf = ib.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[IslandStylizedGrass] Island mesh not built yet."); return; }
        if (grassMesh == null || sourceMaterial == null) { Debug.LogError("[IslandStylizedGrass] Assign grassMesh + sourceMaterial."); return; }

        // Clone the practiced material and retarget it to island space (920 m domain).
        grassMat = new Material(sourceMaterial);
        grassMat.name = "IslandStylizedGrass";
        grassMat.enableInstancing = true;
        grassMat.SetFloat("_TerrainSize", 920f);
        grassMat.SetFloat("_TerrainOffset", 0.5f);
        grassMat.SetTexture("_TerrainColorTex", BakeColorTexture(mf.sharedMesh));
        // Lift the practiced shadow tint: golden-hour side-light plus the 0.29 shadow
        // color crushes shaded blades to near-black. Keeps the cool shadow hue.
        grassMat.SetColor("_ShadowColor", new Color(0.45f, 0.45f, 0.62f, 1f));

        Scatter(mf.sharedMesh);
        Debug.Log($"[IslandStylizedGrass] {Count()} blades planted.");
    }

    Texture2D BakeColorTexture(Mesh mesh)
    {
        int T = colorTexSize;
        var cols = mesh.colors;
        // Linear (non-sRGB) texture: island vertex colors are gamma-authored values used
        // raw by the terrain shader, so sample them raw for a matching grass tint.
        var tex = new Texture2D(T, T, TextureFormat.RGB24, true, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[T * T];
        for (int j = 0; j < T; j++)
            for (int i = 0; i < T; i++)
            {
                double wx = (i / (double)(T - 1) - 0.5) * 920.0;
                // Unity world Z is negated vs source Z (MirrorZ), while the shader samples
                // the texture with Unity world coords: bake the row flipped so lookups land
                // on the true location instead of its Z-mirror.
                double wz = (0.5 - j / (double)(T - 1)) * 920.0;
                // Island mesh is a regular grid: direct vertex lookup, no search.
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

    void Scatter(Mesh mesh)
    {
        var cols = mesh.colors;
        var rng = new System.Random(seed);
        var mats = new List<Matrix4x4>(bladeCount);
        int guard = 0;
        while (mats.Count < bladeCount && guard++ < bladeCount * 60)
        {
            double x = (rng.NextDouble() * 2 - 1) * E;
            double z = (rng.NextDouble() * 2 - 1) * E;
            float h = Hf(x, z);
            if (h < minHeight || h > maxHeight) continue;
            int vi = Mathf.Clamp(Mathf.RoundToInt((float)((x + E) / S)), 0, N);
            int vj = Mathf.Clamp(Mathf.RoundToInt((float)((z + E) / S)), 0, N);
            Color c = cols[vj * V + vi];
            // green-dominant paint only: meadows, fields, moss. Rejects chalk, sand,
            // reef, dirt (whitish transition blends fail the margin test too)
            if (!(c.g > c.b + 0.04f)) continue;
            if (NV(x, z).y < minSlopeY) continue;
            float w = minWidth + (float)rng.NextDouble() * (maxWidth - minWidth);
            float bh = minBladeHeight + (float)rng.NextDouble() * (maxBladeHeight - minBladeHeight);
            var rot = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            mats.Add(Matrix4x4.TRS(U(x, h - 0.05, z), rot, new Vector3(w, bh, w)));
        }
        // spatial cells so each draw batch has tight bounds and can be frustum-culled (identical blades, nothing removed)
        var cellMap = new Dictionary<long, List<Matrix4x4>>();
        foreach (var m in mats)
        {
            Vector4 p = m.GetColumn(3);
            long k = ((long)Mathf.FloorToInt(p.x / 64f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 64f);
            List<Matrix4x4> lst;
            if (!cellMap.TryGetValue(k, out lst)) { lst = new List<Matrix4x4>(); cellMap[k] = lst; }
            lst.Add(m);
        }
        foreach (var lst in cellMap.Values)
            for (int i = 0; i < lst.Count; i += 1023)
            {
                int n = Mathf.Min(1023, lst.Count - i);
                var arr = lst.GetRange(i, n).ToArray();
                Vector3 mn = arr[0].GetColumn(3), mx = mn;
                for (int j = 1; j < n; j++) { Vector3 q = arr[j].GetColumn(3); mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q); }
                chunks.Add(arr); counts.Add(n);
                bnds.Add(new Bounds((mn + mx) * 0.5f, (mx - mn) + Vector3.one * 8f));
            }
    }

    int Count() { int t = 0; foreach (var c in counts) t += c; return t; }

    void Update()
    {
        if (grassMat == null || chunks.Count == 0) return;
        var cam = Camera.main; if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        for (int i = 0; i < chunks.Count; i++)
        {
            if (!GeometryUtility.TestPlanesAABB(planes, bnds[i])) continue;
            Graphics.DrawMeshInstanced(grassMesh, 0, grassMat, chunks[i], counts[i],
                null, UnityEngine.Rendering.ShadowCastingMode.Off, true);
        }
    }
}
