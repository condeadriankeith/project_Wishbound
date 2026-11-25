using UnityEngine;

/// <summary>
/// The star character's look: glowing plasma body (Wishbound/StarOrb), a soft billboard halo
/// (Wishbound/StarHalo), and three particle layers -
///   Sparkle shell : tight twinkling sparks orbiting the body (local space)
///   Wisp trail    : cyan/magenta streamers left behind when moving (world space, emitted by distance)
///   Star motes    : a few slow drifting motes around the star (world space)
/// plus a breathing light synced to the body pulse.
///
/// cometBlend (0..1) turns the same star into the falling comet used by the opening sequence:
/// brighter, bigger halo, long dense trail.
/// Lives on PlasmaOrb/PlasmaOrbVisual next to AmberOrbBody (which springs the body mesh).
/// </summary>
[DefaultExecutionOrder(60)]
public class StarOrbFX : MonoBehaviour
{
    [Header("Assets")]
    public Material bodyMaterial;     // Assets/Art/Star/StarOrbBody.mat
    public Material haloMaterial;     // Assets/Art/Star/StarHalo.mat
    public Material sparkMaterial;    // Assets/Art/Comet/CometEmber.mat (additive soft dot)

    [Header("Look")]
    public float haloSize = 1.35f;
    public float pulseSpeed = 1.6f;
    [Range(0, 1)] public float pulseDepth = .5f;
    public Light glowLight;
    public float lightIntensity = 2.2f;
    [Tooltip("The star is a light source: it lights the toon world around it and casts soft shadows.")]
    public float lightRange = 10f;
    public bool castShadows = false;   // point-light shadows are costly; the toon bands carry the light
    [Range(0, 1)] public float cometBlend;
    public bool hideLegacyVFX = true;

    [Header("State (driven by StarRadiance)")]
    public float radianceScale = 1f;
    public float trailScale = 1f;
    public float RiderLighting { get; set; }
    public float RideWake { get; set; }
    public float FragmentPower { get; set; }
    public float particleScale = 1f;
    [Range(0, 1)] public float matte;       // Roots: solid matte ivory
    [Range(0, 1)] public float prismatic;   // Tether: high-contrast prismatic shimmer

    [Header("Particles")]
    public Gradient sparkColors;
    public Gradient trailColors;

    Transform body, halo;
    ParticleSystem sparks, trail, motes;
    MaterialPropertyBlock mpb;
    Renderer bodyRenderer, haloRenderer;
    static readonly int PulseId = Shader.PropertyToID("_Pulse"), IntensityId = Shader.PropertyToID("_Intensity"), RimId = Shader.PropertyToID("_RimIntensity"), BandAmountId = Shader.PropertyToID("_BandAmount"), MidId = Shader.PropertyToID("_MidColor"), CoreId = Shader.PropertyToID("_CoreColor"), RimAId = Shader.PropertyToID("_RimA"), RimBId = Shader.PropertyToID("_RimB");
    float phase;

    void Reset() { DefaultGradients(); }

    void DefaultGradients()
    {
        sparkColors = new Gradient();
        sparkColors.SetKeys(new[] { new GradientColorKey(new Color(1f, .82f, .45f), 0), new GradientColorKey(new Color(.45f, .95f, 1f), .5f), new GradientColorKey(new Color(1f, .5f, .95f), 1) },
                            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        trailColors = new Gradient();
        trailColors.SetKeys(new[] { new GradientColorKey(new Color(1f, .85f, .55f), 0), new GradientColorKey(new Color(.4f, .9f, 1f), .35f), new GradientColorKey(new Color(.95f, .4f, .95f), 1) },
                            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.9f, .08f), new GradientAlphaKey(.35f, .6f), new GradientAlphaKey(0, 1) });
    }

    void Awake()
    {
        if (sparkColors == null || sparkColors.colorKeys.Length < 2) DefaultGradients();
        mpb = new MaterialPropertyBlock();
        phase = Random.value * 10;
        body = transform.Find("AmberStarBody");
        if (body) { bodyRenderer = body.GetComponent<Renderer>(); if (bodyMaterial && bodyRenderer) bodyRenderer.sharedMaterial = bodyMaterial; if (bodyRenderer) bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        if (!glowLight) { var l = transform.Find("OrbLight"); if (l) glowLight = l.GetComponent<Light>(); }
        if (glowLight) { glowLight.type = LightType.Point; glowLight.color = new Color(1f, .85f, .62f); glowLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None; glowLight.shadowNearPlane = .6f; }
        if (hideLegacyVFX) { var v = transform.Find("StarSwarmVFX"); if (v) v.gameObject.SetActive(false); }
        var legacy = GetComponent<PlasmaOrbVisual>(); if (legacy) legacy.enabled = false;   // its bob + flicker fought the body spring
        BuildHalo();
        sparks = BuildSparks(); trail = BuildTrail(); motes = BuildMotes();
    }

    void BuildHalo()
    {
        if (!haloMaterial) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "StarHalo";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(body ? body : transform, false);
        halo = go.transform;
        haloRenderer = go.GetComponent<Renderer>(); haloRenderer.sharedMaterial = haloMaterial;
        haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; haloRenderer.receiveShadows = false;
    }

    ParticleSystem NewSystem(string name, bool world)
    {
        var go = new GameObject(name); go.transform.SetParent(body ? body : transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local; m.playOnAwake = true; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = sparkMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    ParticleSystem BuildSparks()
    {
        var ps = NewSystem("Sparkle shell", false);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.9f, 1.8f); m.startSpeed = 0; m.maxParticles = 120;
        m.startSize = new ParticleSystem.MinMaxCurve(.035f * particleScale, .08f * particleScale); m.startColor = new ParticleSystem.MinMaxGradient(sparkColors) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission; em.rateOverTime = 45;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .62f * particleScale; sh.radiusThickness = .15f;
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
        v.orbitalX = new ParticleSystem.MinMaxCurve(-.6f, .6f); v.orbitalY = new ParticleSystem.MinMaxCurve(1.8f, 3.2f); v.orbitalZ = new ParticleSystem.MinMaxCurve(-.4f, .4f);
        v.radial = new ParticleSystem.MinMaxCurve(.05f, .2f);
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        var twinkle = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.15f, 1), new Keyframe(.4f, .45f), new Keyframe(.6f, 1), new Keyframe(1, 0));
        sz.size = new ParticleSystem.MinMaxCurve(1, twinkle);
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(.1f, .75f);
        ps.Play(); return ps;
    }

    ParticleSystem BuildTrail()
    {
        var ps = NewSystem("Wisp trail", true);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.3f); m.startSpeed = 0; m.maxParticles = 600;
        m.startSize = new ParticleSystem.MinMaxCurve(.14f, .32f); m.startColor = Color.white;
        var em = ps.emission; em.rateOverTime = 6; em.rateOverDistance = 16;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .3f * particleScale;
        var noise = ps.noise; noise.enabled = true; noise.strength = .45f; noise.frequency = 1.2f; noise.scrollSpeed = .6f; noise.damping = true;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(trailColors);
        ps.Play(); return ps;
    }

    ParticleSystem BuildMotes()
    {
        var ps = NewSystem("Star motes", true);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(3, 5); m.startSpeed = new ParticleSystem.MinMaxCurve(.03f, .12f); m.maxParticles = 40;
        m.startSize = new ParticleSystem.MinMaxCurve(.04f * particleScale, .07f * particleScale); m.startColor = new ParticleSystem.MinMaxGradient(sparkColors) { mode = ParticleSystemGradientMode.RandomColor };
        m.gravityModifier = -.01f;
        var em = ps.emission; em.rateOverTime = 4;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.3f * particleScale;
        var noise = ps.noise; noise.enabled = true; noise.strength = .2f; noise.frequency = .4f;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(.2f, .7f);
        ps.Play(); return ps;
    }

    static ParticleSystem.MinMaxGradient FadeInOut(float a, float b)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, a), new GradientAlphaKey(1, b), new GradientAlphaKey(0, 1) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    void LateUpdate()
    {
        float t = Time.time * pulseSpeed + phase;
        // a soft double-beat "breath": slow swell with a gentle secondary flutter
        float pulse = (Mathf.Sin(t) * .5f + .5f) * .8f + (Mathf.Sin(t * 2.7f + 1.3f) * .5f + .5f) * .2f;
        pulse *= pulseDepth;
        float comet = Mathf.SmoothStep(0, 1, cometBlend);
        float riderExposure = Mathf.Lerp(1f, .66f, RiderLighting);
        float lightExposure = Mathf.Lerp(1f, .14f, RiderLighting);

        if (bodyRenderer)
        {
            bodyRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(PulseId, pulse * (1 - matte * .8f)); mpb.SetFloat(IntensityId, (1 + comet * 1.6f) * Mathf.Lerp(1, .42f, matte) * Mathf.Max(.05f, radianceScale) * riderExposure);
            mpb.SetFloat(RimId, Mathf.Lerp(.55f, .2f, matte) * (1 + prismatic * .8f) * Mathf.Max(.2f, radianceScale) * riderExposure);
            mpb.SetFloat(BandAmountId, Mathf.Lerp(.55f, .05f, matte));
            mpb.SetColor(MidId, Color.Lerp(new Color(1.05f, .42f, .08f), new Color(.82f, .78f, .7f), matte));
            mpb.SetColor(CoreId, Color.Lerp(new Color(1.9f, 1.45f, .85f), new Color(.95f, .92f, .85f), matte));
            if (prismatic > .01f) { float h = Mathf.Repeat(Time.time * .35f, 1); mpb.SetColor(RimAId, Color.Lerp(new Color(.2f, 1.5f, 2.1f), Color.HSVToRGB(h, .7f, 1) * 2.2f, prismatic)); mpb.SetColor(RimBId, Color.Lerp(new Color(1.8f, .3f, 1.6f), Color.HSVToRGB(Mathf.Repeat(h + .4f, 1), .7f, 1) * 2.2f, prismatic)); }
            bodyRenderer.SetPropertyBlock(mpb);
        }
        if (halo)
        {
            float parentScale = halo.parent ? Mathf.Max(.01f, halo.parent.lossyScale.x) : 1;
            float s = haloSize * (1 + FragmentPower * .085f) * (1 + pulse * .12f) * Mathf.Lerp(1, 3.2f, comet) / parentScale;
            halo.localScale = new Vector3(s, s, s);
            haloRenderer.GetPropertyBlock(mpb); mpb.SetFloat(IntensityId, (.42f + pulse * .18f) * Mathf.Lerp(1, 4.5f, comet) * riderExposure); haloRenderer.SetPropertyBlock(mpb);
        }
        if (glowLight) { glowLight.intensity = lightIntensity * (.85f + pulse * .3f) * Mathf.Lerp(1, 3, comet) * Mathf.Max(.05f, radianceScale) * lightExposure; glowLight.color = Color.Lerp(new Color(1f, .85f, .62f), new Color(1f, .93f, .82f), matte); glowLight.range = Mathf.Lerp(lightRange, 22, comet); }
        if (trail)
        {
            float wake = Mathf.Lerp(1f, 2.25f, RideWake) * (1f + FragmentPower * .22f);
            var em = trail.emission; em.rateOverDistance = Mathf.Lerp(16, 70, comet) * trailScale * wake; em.rateOverTime = Mathf.Lerp(6, 60, comet) * trailScale;
            var m = trail.main; m.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Lerp(.7f, 1.6f, comet) * trailScale, Mathf.Lerp(1.3f, 3f, comet) * trailScale * Mathf.Lerp(1f, 1.25f, RideWake)); m.gravityModifier = matte * .35f;
            m.startSize = new ParticleSystem.MinMaxCurve(Mathf.Lerp(.14f, .5f, comet) * particleScale, Mathf.Lerp(.32f, 1.2f, comet) * particleScale);
        }
    }
}
