using UnityEngine;
using UnityEngine.Rendering;
using static TerrainMath;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class IslandBuilder : MonoBehaviour
{
    public static IslandBuilder Instance;
    public Texture2D HeightTex { get; private set; }
    public float[] PathMap { get; private set; }

    static Color C(uint hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);

    void Awake()
    {
        Instance = this;
        Build();
    }

    [ContextMenu("Build Island")]
    public void Build()
    {
        Instance = this;
        PathMap = Roads.BuildPathMap();

        var pos = new Vector3[V * V];
        var h = new float[V * V];
        var tex = new Color[V * V];

        for (int j = 0; j < V; j++)
            for (int i = 0; i < V; i++)
            {
                int k = j * V + i;
                double wx = -E + i * S, wz = -E + j * S;
                float hv = Hf(wx, wz);
                h[k] = hv;
                pos[k] = U(wx, hv, wz);
                tex[k] = new Color((float)Cl(-16, 20, hv), 0, 0, 1);
            }

        var idx = new int[N * N * 6];
        int n = 0;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int k = j * V + i;
                idx[n++] = k; idx[n++] = k + V; idx[n++] = k + 1;
                idx[n++] = k + 1; idx[n++] = k + V; idx[n++] = k + V + 1;
            }
        if (MirrorZ) System.Array.Reverse(idx);

        var mesh = new Mesh { name = "IslandMesh", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = pos;
        mesh.triangles = idx;
        mesh.RecalculateNormals();
        var nrm = mesh.normals;

        mesh.colors = PaintVertices(pos, h, nrm);
        mesh.RecalculateBounds();
        var mf = GetComponent<MeshFilter>();
        if (mf) mf.sharedMesh = mesh;
        var mc = GetComponent<MeshCollider>();
        if (mc) mc.sharedMesh = mesh;

        HeightTex = new Texture2D(V, V, TextureFormat.RHalf, false, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "IslandHeight" };
        HeightTex.SetPixels(tex);
        HeightTex.Apply(false, false);
        Shader.SetGlobalTexture("_GhibliHeightMap", HeightTex);
    }

    Color[] PaintVertices(Vector3[] pos, float[] hArr, Vector3[] nrm)
    {
        Color cG1 = C(0x489626), cG2 = C(0x2d6816), cW = C(0xfbfbf6), cSt = C(0xd9d3c5), cMo = C(0x719c40),
              cSa = C(0xf0e6cb), cSb = C(0x278d9d), cDi = C(0xba9b6d), cDi2 = C(0x8a724d);
        Color[] fields = { C(0xa2c64e), C(0xd4c965), C(0x5fa838), C(0xbb8d54), C(0x72a537) };

        var cols = new Color[V * V];
        for (int k = 0; k < V * V; k++)
        {
            double x = pos[k].x, z = MirrorZ ? -pos[k].z : pos[k].z;
            double h = hArr[k];
            double ny = nrm[k].y;

            double mixG = Vn(x / 24, z / 24) * 0.65 + Vn(x * 0.15, z * 0.15) * 0.35;
            Color c = Color.Lerp(cG1, cG2, (float)mixG);

            if (h > 6 && ny > 0.85)
            {
                double fx = System.Math.Floor((x + 200) / 36), fz = System.Math.Floor((z + 200) / 30);
                double hs = System.Math.Sin(fx * 127.1 + fz * 311.7) * 43758.5453;
                double q = hs - System.Math.Floor(hs);
                if (q > 0.32)
                    c = Color.Lerp(c, fields[(int)System.Math.Floor(q * 13) % fields.Length], 0.78f);
            }

            double cliffW = 1.0 - Sm(0.58, 0.82, ny);
            if (cliffW > 0)
            {
                double strata = Sm(0.35, 0.65, 0.5 + 0.5 * System.Math.Sin(h * 1.5 + Vn(x * 0.05, z * 0.05) * 6));
                Color c2 = Color.Lerp(cW, cSt, (float)(strata * 0.75));
                double moss = Sm(0.55, 0.85, Vn(x * 0.1 + h * 0.03, z * 0.1 + h * 0.04)) * 0.45 * (h > 15 ? 1 : 0.3);
                c2 = Color.Lerp(c2, cMo, (float)moss);
                c = Color.Lerp(c, c2, (float)cliffW);
            }

            c = Color.Lerp(c, cSa, (float)(Sm(3.2, 1.1, h) * (1 - cliffW)));
            c = Color.Lerp(c, cSb, (float)Sm(0.8, -8.0, h));

            if (PathMap != null && PathMap[k] > 0 && h > 1.2)
            {
                Color dirt = Color.Lerp(cDi, cDi2, (float)Vn(x * 0.4, z * 0.4));
                c = Color.Lerp(c, dirt, (float)(PathMap[k] * (1 - cliffW * 0.75)));
            }
            c.a = 1;
            cols[k] = c;
        }
        return cols;
    }
}
