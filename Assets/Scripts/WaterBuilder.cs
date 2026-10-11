using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Phase 3.4: builds river ribbons, lake disc, waterfall mist and a few demo logs at Play.</summary>
public static class WaterBuilder
{
    public static bool SpawnDemoLogs = true;
    static GameObject root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Application.isPlaying || root != null || UnityEngine.Object.FindObjectOfType<IslandBuilder>() == null) return;
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline; if (rp != null) { var f = rp.GetType().GetField("m_RequireDepthTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); if (f != null) f.SetValue(rp, true); }   // water shader needs the depth texture
        Build();
    }

    public static void Build()
    {
        TerrainMath.H(0, 0); // forces RiverCarve.Build
        WaterField.Clear();
        root = new GameObject("IslandWater");
        var V = new List<Vector3>(); var UV = new List<Vector2>(); var C = new List<Color>(); var T = new List<int>();
        RiverCarve.Channel longest = null; var UV2 = new List<Vector2>();

        RiverWater.Build(V, UV, UV2, C, T, out longest);

        if (FlowNet.LakeCell >= 0)
        {
            float R = (float)FlowNet.LakeR + 3f, lx = (float)FlowNet.LakeX, lz = (float)FlowNet.LakeZ, lv = (float)FlowNet.LakeLevel;
            WaterField.SetLake(lx, lz, R - 3f, lv);
            { var lr = new System.Random(77); for (int q = 0; q < 110; q++) { double an = lr.NextDouble() * 6.2832, d = (R - 3f) * (0.97 + lr.NextDouble() * 0.16); RiverRocks.Add(lx + Math.Cos(an) * d, lz + Math.Sin(an) * d, (float)(0.12 + 0.45 * Math.Pow(lr.NextDouble(), 2.2)), lr); } }
            int rings = 6, segs = 48, b0 = V.Count;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    float d = R * r / rings, an = s * Mathf.PI * 2f / segs;
                    V.Add(TerrainMath.U(lx + Mathf.Cos(an) * d, lv, lz + Mathf.Sin(an) * d)); UV.Add(Vector2.zero); UV2.Add(Vector2.one);
                    C.Add(new Color(0, 1, Mathf.Clamp01((d - 0.7f * R) / (0.3f * R)), 0));
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int s2 = (s + 1) % segs, i0 = b0 + r * segs + s, i1 = b0 + r * segs + s2, i2 = b0 + (r + 1) * segs + s, i3 = b0 + (r + 1) * segs + s2;
                    T.Add(i0); T.Add(i2); T.Add(i1); T.Add(i1); T.Add(i2); T.Add(i3);
                }
        }

        var mesh = new Mesh { name = "IslandWaterMesh", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(V); mesh.SetUVs(0, UV); mesh.SetUVs(1, UV2); mesh.SetColors(C); mesh.SetTriangles(T, 0); mesh.RecalculateBounds();
        var go = new GameObject("RiversAndLake"); go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Island/GhibliRiver"); if (sh == null) sh = Shader.Find("Sprites/Default");
        mr.sharedMaterial = new Material(sh) { renderQueue = 3000 };
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;

        RiverRocks.Build(root.transform);
        if (SpawnDemoLogs && longest != null) Logs(longest);
    }

    static Texture2D soft;
    static Texture2D Soft()
    {
        if (soft) return soft;
        soft = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Mathf.Clamp01(1f - Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 15.5f); soft.SetPixel(x, y, new Color(1, 1, 1, d * d)); }
        soft.Apply(); return soft;
    }

    public static void Mist(double x, double y, double z, float w, float drop)
    {
        var g = new GameObject("WaterfallMist"); g.transform.SetParent(root.transform, false);
        g.transform.position = TerrainMath.U(x, y + 1, z);
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop();
        var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 3.5f + drop * 0.05f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startColor = new Color(1, 1, 1, 0.35f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.05f;
        var em = ps.emission; em.rateOverTime = 10 + Mathf.Min(drop, 40) * 0.4f;
        var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 35; sp.radius = Mathf.Max(1f, w);
        sp.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.45f, 0.25f), new GradientAlphaKey(0, 1) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var pr = g.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft() };
        ps.Play();
    }

    static void Logs(RiverCarve.Channel c)
    {
        int M = c.x.Length, m = M / 3;
        for (int i = 0; i < 4; i++)
        {
            int k = Mathf.Min(M - 1, m + i * 4);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = "DemoLog"; g.transform.SetParent(root.transform, false);
            g.transform.localScale = new Vector3(0.5f, 1.3f, 0.5f);
            g.transform.position = TerrainMath.U(c.x[k], c.bed[k] + c.depth[k] + 1.0, c.z[k]);
            g.transform.rotation = Quaternion.Euler(0, i * 37f, 90);
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 40; rb.angularDamping = 0.5f;
            g.AddComponent<Buoyant>();
            g.GetComponent<Renderer>().sharedMaterial.color = new Color(0.45f, 0.3f, 0.18f);
        }
    }
}
