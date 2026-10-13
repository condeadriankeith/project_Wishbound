using UnityEngine;
using UnityEngine.Rendering;
using static TerrainMath;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class IslandBuilder : MonoBehaviour
{
    public static IslandBuilder Instance;
    public Texture2D HeightTex { get; set; }
    public float[] PathMap { get; set; }

    static Color C(uint hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);

    [SerializeField, HideInInspector] bool wasBuilt; public static float[] HGrid;   // serialized: survives a script reload during Play

    void Awake()
    {
        Instance = this;
        Build();
        wasBuilt = true;
    }

    // A script reload while playing wipes statics + runtime data (PathMap, Instance, height texture): rebuild so nothing goes missing.
    void OnEnable()
    {
        if (Application.isPlaying && wasBuilt && PathMap == null) Build();
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

        // cliffSmooth: relax steep cells so cliff faces are rounded, not stair-stepped.
        // A carved river channel is a local MINIMUM (the lowest cells around), so ordinary steep-cell
        // smoothing would RAISE the valley floor back up and blur its banks - erasing the very carve that
        // RiverWater drapes its ribbon over. That is why the carve used to disagree with the flow. Protect
        // the whole channel + graded-bank corridor (WetAt > 0.25) so the valley survives to the mesh intact;
        // terrain just outside the mask still smooths, blending the rim naturally into the raw ground.
        var protect = new bool[V * V];
        for (int j = 0; j < V; j++) for (int i = 0; i < V; i++)
            protect[j * V + i] = RiverCarve.WetAt(-E + i * S, -E + j * S) > 0.25f || RoadGrade.MaskAt(-E + i * S, -E + j * S) > 0.5f || PadGrade.MaskAt(-E + i * S, -E + j * S) > 0.5f;
var h2 = new float[V * V];
for (int pass = 0; pass < 3; pass++)
{
 System.Array.Copy(h, h2, h.Length);
 for (int j = 1; j < V - 1; j++) for (int i = 1; i < V - 1; i++)
 {
  int k = j * V + i; float c0 = h[k]; if (c0 < 2f || protect[k]) continue;   // never smooth a carved cell
  float n1 = h[k - 1], n2 = h[k + 1], n3 = h[k - V], n4 = h[k + V];
  float mx = Mathf.Max(Mathf.Max(n1, n2), Mathf.Max(n3, n4)), mn = Mathf.Min(Mathf.Min(n1, n2), Mathf.Min(n3, n4));
  float st = Mathf.Clamp01((mx - mn - 1.5f) / 4f); if (st <= 0) continue;
  float avg = (n1 + n2 + n3 + n4) * 0.125f + (h[k - V - 1] + h[k - V + 1] + h[k + V - 1] + h[k + V + 1]) * 0.0625f + c0 * 0.25f;
  h2[k] = Mathf.Lerp(c0, avg, 0.75f * st);
 }
 System.Array.Copy(h2, h, h.Length);
}
for (int j = 0; j < V; j++) for (int i = 0; i < V; i++) { int k = j * V + i; double wx = -E + i * S, wz = -E + j * S; pos[k] = U(wx, h[k], wz); tex[k] = new Color((float)Cl(-16, 20, h[k]), 0, 0, 1); }
HGrid = h;
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

            double wet = RiverCarve.WetAt(x, z);   // 1 in the channel -> 0 a few metres past the banks
            int fi = Surface.FieldIndex(x, z, h, ny);
            if (fi >= 0 && wet < 0.4) c = Color.Lerp(c, fields[fi], 0.78f);

            double cliffW = Surface.CliffWeight(ny);
            if (cliffW > 0)
            {
                double strata = Sm(0.35, 0.65, 0.5 + 0.5 * System.Math.Sin(h * 1.5 + Vn(x * 0.05, z * 0.05) * 6));
                Color c2 = Color.Lerp(cW, cSt, (float)(strata * 0.75));
                double moss = Sm(0.55, 0.85, Vn(x * 0.1 + h * 0.03, z * 0.1 + h * 0.04)) * 0.45 * (h > 15 ? 1 : 0.3);
                c2 = Color.Lerp(c2, cMo, (float)moss);
                // Smooth cliff transition using smoothstep for anti-aliased edges
                float smoothCliffW = Mathf.SmoothStep(0f, 1f, (float)cliffW);
                c = Color.Lerp(c, c2, smoothCliffW);
            }

            // Smooth shoreline transition - blend sand into water/grass near water level
            float sandW = (float)Surface.SandWeight(h, cliffW);
            float shoreBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 3.0f, (float)h));
            float smoothSandW = Mathf.Lerp(sandW, sandW * shoreBlend, shoreBlend);
            c = Color.Lerp(c, cSa, smoothSandW);
            
            c = Color.Lerp(c, cSb, (float)Sm(0.8, -8.0, h));

            if (PathMap != null && PathMap[k] > 0 && h > 1.2)
            {
                Color dirt = Color.Lerp(cDi, cDi2, (float)Vn(x * 0.4, z * 0.4));
                // Smooth path transition near cliffs
                float pathBlend = Mathf.Lerp(1f, 0.25f, (float)cliffW);
                c = Color.Lerp(c, dirt, (float)(PathMap[k] * pathBlend));
            }
            if (wet > 0.01)
            {
                // damp bank: dark wet stone showing through grass/sand, fords stay wet
                Color wetRock = new Color(0.40f, 0.36f, 0.29f);
                c = Color.Lerp(c, wetRock, (float)(wet * 0.55));
                float dk = 1f - (float)(wet * 0.18);
                c.r *= dk; c.g *= dk; c.b *= dk;
            }
            c.a = 1;
            cols[k] = c;
        }
        return cols;
    }
}
