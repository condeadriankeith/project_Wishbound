using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Cinematic look: bloom, tonemapping, grade, vignette, soft DoF, atmospheric fog, sun god-ray shafts, floating dust motes.
public class IslandCinematic : MonoBehaviour
{
    public float fogDensity = 0.004f;
    public Color fogColor = new Color(0.74f, 0.80f, 0.86f);
    public float bloomIntensity = 1.1f;
    public float shaftStrength = 0.06f;
    Transform root, cam;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        var c = Camera.main;
        if (c == null) yield break;
        cam = c.transform;

        var d = c.GetUniversalAdditionalCameraData();
        d.renderPostProcessing = true;
        d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // ---- post processing ----
        var vgo = new GameObject("CinematicVolume");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 10;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        vol.sharedProfile = p;

        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.72f); bloom.tint.Override(new Color(1f, 0.93f, 0.82f));
        bloom.highQualityFiltering.Override(true);

        var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.25f); ca.contrast.Override(14f); ca.saturation.Override(16f);
        ca.colorFilter.Override(new Color(1f, 0.96f, 0.9f));

        var vg = p.Add<Vignette>(true);
        vg.intensity.Override(0.3f); vg.smoothness.Override(0.45f);


        var chroma = p.Add<ChromaticAberration>(true); chroma.intensity.Override(0.2f);
        var grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f);

        // ---- atmosphere ----
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogColor = fogColor;

        Light sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsOfType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
        Vector3 sunDir = sun != null ? sun.transform.forward : new Vector3(0.4f, -0.8f, 0.3f).normalized;
        if (sun != null) { sun.color = Color.Lerp(sun.color, new Color(1f, 0.88f, 0.7f), 0.5f); sun.shadows = LightShadows.Soft; }

        root = new GameObject("Atmosphere").transform;
        var tex = SoftTex();
        Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Mobile/Particles/Additive");
        var mat = new Material(sh); mat.mainTexture = tex;
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);

        // god-ray shafts: long stretched soft billboards aligned with the sun direction
        var sg = new GameObject("Shafts"); sg.transform.SetParent(root, false); sg.transform.localPosition = new Vector3(0, 28f, 0);
        var ps = sg.AddComponent<ParticleSystem>();
        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = true;
        m.startLifetime = 14f; m.startSpeed = 0f; m.maxParticles = 28;
        m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        m.startRotation = 0f;
        m.startColor = new Color(1f, 0.88f, 0.62f, shaftStrength);
        var em = ps.emission; em.rateOverTime = 2f;
        var sh1 = ps.shape; sh1.shapeType = ParticleSystemShapeType.Box; sh1.scale = new Vector3(90f, 1f, 90f);
        var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
        vo.x = sunDir.x * 0.05f; vo.y = sunDir.y * 0.05f; vo.z = sunDir.z * 0.05f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 14f; r.velocityScale = 0f;
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();

        // dust motes / pollen
        var dg = new GameObject("Dust"); dg.transform.SetParent(root, false);
        var dp = dg.AddComponent<ParticleSystem>();
        var dm = dp.main; dm.simulationSpace = ParticleSystemSimulationSpace.World;
        dm.startLifetime = 9f; dm.startSpeed = 0.15f; dm.maxParticles = 350;
        dm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        dm.startColor = new Color(1f, 0.95f, 0.8f, 0.55f);
        var de = dp.emission; de.rateOverTime = 40f;
        var ds = dp.shape; ds.shapeType = ParticleSystemShapeType.Box; ds.scale = new Vector3(45f, 14f, 45f);
        var dn = dp.noise; dn.enabled = true; dn.strength = 0.4f; dn.frequency = 0.3f;
        var dc = dp.colorOverLifetime; dc.enabled = true; dc.color = g;
        var dr = dp.GetComponent<ParticleSystemRenderer>(); dr.sharedMaterial = mat; dr.shadowCastingMode = ShadowCastingMode.Off;
        dp.Play();
    }

    static Texture2D SoftTex()
    {
        int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a = a * a * (3f - 2f * a);
            t.SetPixel(x, y, new Color(a, a, a, a));
        }
        t.Apply(); return t;
    }

    void LateUpdate()
    {
        if (root != null && cam != null) root.position = cam.position;
    }
}
