using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Wishing-star comet (redesign v2). A white-hot nucleus with a soft tinted halo and an anamorphic star flare
/// (long horizontal streak + short vertical cross, cyan-to-magenta fringe), a long straight electric-blue ion tail
/// with peeling prismatic filaments, a broad curved dust tail, drifting embers, and shed diamond sparkles.
/// When the descent reaches Progress 0.42 (the moment the intro fractures the shard) it fires a one-shot BURST:
/// full-screen additive flash, iridescent shockwave ring + starburst rays left hanging where it happened, a head pulse,
/// and a fan of small cyan wish-fragment streaks that trail beside the hero comet down to the impact.
/// The whole thing warms from blue-white to gold/magenta as it nears the earth, matching the amber star born at impact.
/// Tails are sampled by DISTANCE (not time), so they stay long when the comet eases in or out.
/// Shader modes (Resources/WishCelestial): 5 nucleus, 6 ion tail, 7 dust tail, 3 soft glow, 4 sky,
/// 8 anamorphic star flare, 9 diamond sparkle, 10 prismatic burst (ring + rays + flash; _Param = progress).
/// Public API is unchanged: Create(parent), Progress (0..1), Atmosphere, ionLength, dustLength. New: Burst().
/// </summary>
public sealed class WishCometVisual : MonoBehaviour
{
    public float Progress;
    public float Atmosphere = 1;
    public float ionLength = 170f, dustLength = 135f;
    [Tooltip("Seconds the shockwave ring / starburst takes to expand and fade.")] public float burstDuration = 1.25f;
    [Tooltip("Progress (0..1) at which the burst fires automatically, if Burst() has not been called.")] public float autoBurstAt = .42f;

    const int Samples = 72;
    const float Spacing = 1.6f;
    const int MaxRaw = 420;

    readonly List<Vector3> history = new List<Vector3>(256);   // newest last
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly Vector3[] raw = new Vector3[MaxRaw];
    readonly float[] cum = new float[MaxRaw];
    readonly Vector3[] pts = new Vector3[Samples];
    readonly Vector3[] tmp = new Vector3[Samples];

    Transform sky, nucleus, halo, bloom, flare, burstQuad;
    Material skyMat, nucleusMat, haloMat, bloomMat, flareMat, burstMat, ionCoreMat, ionGlowMat, dustAMat, dustBMat;
    LineRenderer ionCore, ionGlow, dustA, dustB;
    readonly List<LineRenderer> filaments = new List<LineRenderer>();
    readonly List<Material> filamentMats = new List<Material>();

    // secondary "wish fragment" streaks: parallel cyan lines fanned out beside and behind the hero comet
    sealed class Fragment { public LineRenderer line; public Material mat; public Vector3[] buf; public int kStart; public Vector2 offset; }
    readonly List<Fragment> fragments = new List<Fragment>();
    static readonly Vector2[] FragOffsets = { new Vector2(-9f, 6f), new Vector2(11f, -7f), new Vector2(-17f, -3f), new Vector2(19f, 4f), new Vector2(-26f, 9f), new Vector2(6f, -14f) };
    static readonly float[] FragLag = { .05f, .12f, .20f, .28f, .38f, .48f };       // fraction of the path skipped at the head -> shorter, trailing streaks
    static readonly float[] FragWidth = { .9f, .75f, .7f, .6f, .55f, .5f };

    float born, sideSign = 1;
    float burstStart = -100f; bool burstFired; Vector3 burstPos;
    Vector3 dir, lastDirPoint, sideVec, camRight = Vector3.right, camUp = Vector3.up;
    bool haveDir;

    public static WishCometVisual Create(Transform parent)
    {
        var g = new GameObject("Wishing Star - comet VFX");
        g.transform.SetParent(parent, false);
        return g.AddComponent<WishCometVisual>();
    }

    public void PrimeFlight(Vector3 direction)
    {
        dir = direction.normalized; haveDir = true; lastDirPoint = transform.position;
        born = Time.time - 3f;
        history.Clear();
        for (int i=200;i>=0;i--) history.Add(transform.position-dir*i*Spacing);
    }

    /// <summary>Fires the one-shot prismatic burst (flash, shockwave ring, starburst, fragment streaks). Safe to call once.</summary>
    public void Burst()
    {
        if (burstFired) return;
        burstFired = true; burstStart = Time.time; burstPos = transform.position;
    }

    Material MaterialFor(float mode, Color color)
    {
        var shader = Resources.Load<Shader>("WishCelestial");
        if (!shader) { Debug.LogError("Missing Resources/WishCelestial shader"); enabled = false; return null; }
        var m = new Material(shader) { name = "Wish comet " + mode };
        m.SetFloat("_Mode", mode); m.SetColor("_Tint", color); materials.Add(m); return m;
    }

    Transform Quad(string label, Material mat, Transform parent)
    {
        var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = label };
        mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return go.transform;
    }

    static AnimationCurve Curve(params float[] tv)
    {
        var k = new Keyframe[tv.Length / 2];
        for (int i = 0; i < k.Length; i++) k[i] = new Keyframe(tv[2 * i], tv[2 * i + 1]);
        return new AnimationCurve(k);
    }

    LineRenderer MakeLine(string label, Material mat, float width, AnimationCurve curve)
    {
        var go = new GameObject(label); go.transform.SetParent(transform, false);
        var l = go.AddComponent<LineRenderer>();
        l.useWorldSpace = true; l.positionCount = 0; l.sharedMaterial = mat;
        l.widthMultiplier = width; l.widthCurve = curve;
        l.textureMode = LineTextureMode.Stretch; l.alignment = LineAlignment.View;
        l.numCornerVertices = 0; l.numCapVertices = 0;
        l.shadowCastingMode = ShadowCastingMode.Off; l.receiveShadows = false;
        l.lightProbeUsage = LightProbeUsage.Off; l.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return l;
    }

    void MakeEmbers(string label, Color tint, float size0, float size1, float rate, float radius, float life0, float life1)
    {
        var go = new GameObject(label); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1); main.startSpeed = new ParticleSystem.MinMaxCurve(.1f, .7f);
        main.startSize = new ParticleSystem.MinMaxCurve(size0, size1); main.maxParticles = 500;
        var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
        var nz = ps.noise; nz.enabled = true; nz.strength = .35f; nz.frequency = .12f;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(0, 0, .08f, 1, .7f, .7f, 1, 0));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = MaterialFor(3, tint); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }

    // Prismatic diamond twinkles shed along the flight path (shader mode 9, hue from the particle colour).
    void MakeDiamonds()
    {
        var go = new GameObject("Diamond sparkles"); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 3.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(.9f, 2.8f); main.maxParticles = 700;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var rainbow = new Gradient();
        rainbow.SetKeys(new[] { new GradientColorKey(new Color(.5f, .95f, 1f), 0), new GradientColorKey(new Color(.75f, .6f, 1f), .3f), new GradientColorKey(new Color(1f, .55f, .9f), .6f), new GradientColorKey(new Color(1f, .85f, .6f), 1) },
                        new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        main.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = 1.7f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 5.5f;
        var nz = ps.noise; nz.enabled = true; nz.strength = .4f; nz.frequency = .1f;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(0, 0, .12f, 1, .3f, .35f, .45f, .9f, .7f, .3f, 1, 0));   // twinkle
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-.8f, .8f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .1f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = MaterialFor(9, Color.white); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }

    static Color FilamentColor(int i, float a)
    {
        switch (i % 3)
        {
            case 0: return new Color(.4f, .8f, 1f, a);        // cyan
            case 1: return new Color(.55f, .5f, 1f, a * .9f);  // violet
            default: return new Color(1f, .5f, .9f, a * .8f);  // magenta
        }
    }

    void Awake()
    {
        born = Time.time;
        if (!Resources.Load<Shader>("WishCelestial")) { Debug.LogError("Missing Resources/WishCelestial shader"); enabled = false; return; }

        // distant blue cloud horizon (unchanged)
        skyMat = MaterialFor(4, Color.white);
        skyMat.SetFloat("_DstBlend", 10); skyMat.renderQueue = 3000;
        sky = Quad("Cinematic blue cloud horizon", skyMat, null);
        sky.localScale = new Vector3(2600, 1600, 1);

        // head: wide bloom, soft halo, anamorphic star flare, small white-hot nucleus
        bloomMat = MaterialFor(3, new Color(.1f, .35f, .9f, .18f));
        haloMat = MaterialFor(3, new Color(.14f, .55f, 1f, .5f));
        flareMat = MaterialFor(8, new Color(.65f, .9f, 1f, .4f));
        nucleusMat = MaterialFor(5, new Color(.72f, .88f, 1f, 1f));
        bloom = Quad("Comet bloom", bloomMat, transform);
        halo = Quad("Comet halo", haloMat, transform);
        flare = Quad("Comet star flare", flareMat, transform);
        nucleus = Quad("Comet nucleus", nucleusMat, transform);

        // one-shot burst: prismatic shockwave ring + starburst rays + flash (hidden until Burst())
        burstMat = MaterialFor(10, new Color(1f, 1f, 1f, .9f));
        burstMat.SetFloat("_Param", 0f);
        burstQuad = Quad("Comet burst", burstMat, transform);
        burstQuad.gameObject.SetActive(false);

        // tails (u runs 0 at the tail end -> 1 at the head)
        dustBMat = MaterialFor(7, new Color(.6f, .35f, 1f, .18f));
        dustAMat = MaterialFor(7, new Color(1f, .68f, .3f, .35f));
        ionGlowMat = MaterialFor(6, new Color(.15f, .5f, 1f, .4f));
        ionCoreMat = MaterialFor(6, new Color(.45f, .85f, 1f, .95f));
        dustB = MakeLine("Dust tail haze", dustBMat, 24f, Curve(0, 1, .5f, .62f, .9f, .2f, 1, .06f));
        dustA = MakeLine("Dust tail", dustAMat, 14f, Curve(0, 1, .5f, .62f, .9f, .2f, 1, .06f));
        ionGlow = MakeLine("Ion tail glow", ionGlowMat, 3.2f, Curve(0, .5f, .7f, .7f, 1, .5f));
        ionCore = MakeLine("Ion tail core", ionCoreMat, .55f, Curve(0, .25f, .6f, .45f, .95f, .8f, 1, .35f));
        for (int i = 0; i < 5; i++)
        {
            var m = MaterialFor(6, FilamentColor(i, .5f));
            filamentMats.Add(m);
            filaments.Add(MakeLine("Ion filament " + i, m, .35f, Curve(0, 0, .5f, .6f, 1, .15f)));
        }

        // wish fragments: small cyan streaks that fan out behind the hero comet after the burst
        for (int i = 0; i < FragOffsets.Length; i++)
        {
            var f = new Fragment { offset = FragOffsets[i] };
            f.kStart = Mathf.Clamp(Mathf.RoundToInt(Samples * FragLag[i]), 0, Samples - 4);
            f.buf = new Vector3[Samples - f.kStart];
            f.mat = MaterialFor(6, i % 2 == 0 ? new Color(.35f, .85f, 1f, 0f) : new Color(.5f, .7f, 1f, 0f));
            f.line = MakeLine("Wish fragment " + i, f.mat, FragWidth[i], Curve(0, 0, .6f, .65f, 1, .4f));
            f.line.enabled = false;
            fragments.Add(f);
        }

        // embers that hang in the sky: gold sparks and cool motes (parallax sells the speed)
        MakeEmbers("Gold embers", new Color(1f, .72f, .3f, .7f), .3f, 1f, 1.1f, 4.5f, 5f, 9f);
        MakeEmbers("Cool motes", new Color(.5f, .85f, 1f, .5f), .25f, .8f, .8f, 6f, 5f, 8f);
        MakeDiamonds();
    }

    // Samples the flight path behind the head into Samples points (0 = head, Samples-1 = tail end). Returns the length.
    float BuildPath(float length)
    {
        Vector3 head = transform.position;
        int n = 0; raw[n++] = head;
        float acc = 0; Vector3 prev = head;
        for (int i = history.Count - 1; i >= 0 && n < MaxRaw - 2; i--)
        {
            Vector3 p = history[i];
            float d = Vector3.Distance(prev, p);
            if (d < .001f) continue;
            if (acc + d >= length) { prev = prev + (p - prev) * ((length - acc) / d); raw[n++] = prev; acc = length; break; }
            acc += d; raw[n++] = p; prev = p;
        }
        if (acc < length - .01f)
        {
            // history shorter than the tail: continue straight back along the flight line
            Vector3 back = n >= 2 ? raw[n - 1] - raw[n - 2] : -dir;
            back = back.sqrMagnitude > 1e-6f ? back.normalized : -dir;
            raw[n++] = prev + back * (length - acc);
        }
        if (n < 2) return 0;
        cum[0] = 0;
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(raw[i - 1], raw[i]);
        float total = cum[n - 1];
        if (total < .5f) return 0;
        int seg = 1;
        for (int k = 0; k < Samples; k++)
        {
            float target = total * k / (Samples - 1);
            while (seg < n - 1 && cum[seg] < target) seg++;
            float span = cum[seg] - cum[seg - 1];
            float u = span > 1e-5f ? Mathf.Clamp01((target - cum[seg - 1]) / span) : 0;
            pts[k] = Vector3.Lerp(raw[seg - 1], raw[seg], u);
        }
        return total;
    }

    Vector3 Offset(int kind, float s, float len, float amp, int idx)
    {
        switch (kind)
        {
            case 1: return sideVec * (s * s * len * amp);   // dust tail: curves away behind the head
            case 2:                                          // filaments peel off the ion tail, bounded and coherent
                float ph = idx * 1.9f + .7f, t = Time.time;
                return (camRight * Mathf.Sin(ph + s * 7f - t * 1.5f) + camUp * (Mathf.Cos(ph * 1.3f + s * 5f - t * 1.1f) * .7f)) * (s * amp);
            case 3: return sideVec * (Mathf.Sin(s * 5f - Time.time * 1.2f) * s * amp);   // ion tail: gentle sway
            default: return Vector3.zero;
        }
    }

    void Apply(LineRenderer l, float total, int kind, float amp, int idx)
    {
        if (total <= 0) { l.positionCount = 0; return; }
        for (int j = 0; j < Samples; j++)
        {
            int k = Samples - 1 - j;                    // line runs tail (0) -> head
            float s = k / (float)(Samples - 1);         // 0 at head, 1 at tail end
            tmp[j] = pts[k] + Offset(kind, s, total, amp, idx);
        }
        if (l.positionCount != Samples) l.positionCount = Samples;
        l.SetPositions(tmp);
    }

    // Fragment streak: same flight path, shifted sideways in screen space (fans out toward the tail), head lagging behind the comet.
    void ApplyFragment(Fragment f, float total, int idx)
    {
        int cnt = Samples - f.kStart;
        if (total <= 0 || cnt < 2) { f.line.positionCount = 0; return; }
        Vector3 off = camRight * f.offset.x + camUp * f.offset.y;
        for (int j = 0; j < cnt; j++)
        {
            int k = Samples - 1 - j;
            float s = k / (float)(Samples - 1);
            Vector3 sway = (camRight * Mathf.Sin(idx * 2.1f + s * 6f - Time.time * 1.3f) + camUp * Mathf.Cos(idx * 1.7f + s * 5f - Time.time * 1f)) * (s * .9f);
            f.buf[j] = pts[k] + off * (.55f + s * .9f) + sway;
        }
        if (f.line.positionCount != cnt) f.line.positionCount = cnt;
        f.line.SetPositions(f.buf);
    }

    static void Tint(Material m, float r, float g, float b, float a) { m.SetColor("_Tint", new Color(r, g, b, a)); }

    void LateUpdate()
    {
        var cam = Camera.main; if (!cam) return;
        var ct = cam.transform;
        camRight = ct.right; camUp = ct.up;

        sky.SetPositionAndRotation(ct.position + ct.forward * 900, ct.rotation);
        skyMat.SetFloat("_Visibility", Mathf.SmoothStep(0, 1, (Time.time - born) / 1.5f) * Atmosphere);

        Vector3 head = transform.position;
        // direction of travel (starts as the authored screen heading: toward lower right)
        if (!haveDir) { dir = (camRight * .8f - camUp * .45f).normalized; lastDirPoint = head; haveDir = true; }
        if ((head - lastDirPoint).sqrMagnitude >= .0025f) { dir = Vector3.Slerp(dir, (head - lastDirPoint).normalized, .35f); lastDirPoint = head; }

        // path history by distance
        if (history.Count == 0 || (head - history[history.Count - 1]).sqrMagnitude >= Spacing * Spacing)
        {
            if (history.Count > 0 && (head - history[history.Count - 1]).sqrMagnitude > 3600f) history.Clear();
            history.Add(head);
            if (history.Count > 240) history.RemoveAt(0);
        }

        Vector3 side = Vector3.Cross(dir, ct.forward);
        if (side.sqrMagnitude > .0001f)
        {
            side.Normalize();
            float d = Vector3.Dot(side, camUp);
            if (d > .25f) sideSign = -1; else if (d < -.25f) sideSign = 1;   // dust curves toward screen-down, without flicker
            sideVec = side * sideSign;
        }

        if (!burstFired && Progress >= autoBurstAt) Burst();
        float since = burstFired ? Time.time - burstStart : 0f;
        float bt = burstFired ? since / Mathf.Max(.1f, burstDuration) : 2f;
        float flash = burstFired ? Mathf.Exp(-since * 4.2f) : 0f;            // head pulse right after the burst
        float fragFade = burstFired ? Mathf.SmoothStep(0, 1, since / .9f) : 0f;

        float grow = 1f; // Flight is already established before the shot is revealed.
        float reveal = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .85f, Progress));   // blue-white -> gold/magenta

        float ion = BuildPath(ionLength * grow);
        Apply(ionGlow, ion, 3, .6f, 0); Apply(ionCore, ion, 3, .6f, 0);
        for (int i = 0; i < filaments.Count; i++) Apply(filaments[i], ion, 2, 1.2f + i * .8f, i);
        float dust = BuildPath(dustLength * grow);
        Apply(dustA, dust, 1, .2f, 0); Apply(dustB, dust, 1, .27f, 0);
        float fragTotal = BuildPath(ionLength * .62f * grow);
        for (int i = 0; i < fragments.Count; i++)
        {
            var f = fragments[i];
            f.line.enabled = fragFade > .01f;
            if (!f.line.enabled) continue;
            Tint(f.mat, i % 2 == 0 ? .35f : .5f, i % 2 == 0 ? .85f : .7f, 1f, .85f * fragFade * Mathf.Lerp(1f, .7f, reveal));
            ApplyFragment(f, fragTotal, i);
        }

        // colour: ion tail stays cool and dims; dust tail warms and brightens; haze goes violet -> magenta; head turns gold
        Tint(nucleusMat, Mathf.Lerp(.72f, 1f, reveal), Mathf.Lerp(.88f, .82f, reveal), Mathf.Lerp(1f, .5f, reveal), 1f);
        Tint(haloMat, Mathf.Lerp(.14f, 1f, reveal), Mathf.Lerp(.55f, .58f, reveal), Mathf.Lerp(1f, .14f, reveal), .5f * (1f + flash * .5f));
        Tint(bloomMat, Mathf.Lerp(.1f, 1f, reveal), Mathf.Lerp(.35f, .5f, reveal), Mathf.Lerp(.9f, .1f, reveal), Mathf.Lerp(.18f, .16f, reveal) * (1f + flash));
        Tint(ionCoreMat, Mathf.Lerp(.45f, .7f, reveal), Mathf.Lerp(.85f, .9f, reveal), 1f, Mathf.Lerp(.95f, .65f, reveal));
        Tint(ionGlowMat, .15f, .5f, 1f, Mathf.Lerp(.4f, .24f, reveal));
        Tint(dustAMat, 1f, Mathf.Lerp(.7f, .55f, reveal), Mathf.Lerp(.35f, .15f, reveal), Mathf.Lerp(.34f, .72f, reveal));
        Tint(dustBMat, Mathf.Lerp(.6f, 1f, reveal), Mathf.Lerp(.35f, .4f, reveal), Mathf.Lerp(1f, .5f, reveal), Mathf.Lerp(.18f, .3f, reveal));
        for (int i = 0; i < filamentMats.Count; i++) { var c = FilamentColor(i, Mathf.Lerp(.5f, .3f, reveal)); Tint(filamentMats[i], c.r, c.g, c.b, c.a); }
        // star flare: cyan-white early, gold-white late, with a kick on the burst
        Tint(flareMat, Mathf.Lerp(.65f, 1f, reveal), Mathf.Lerp(.9f, .78f, reveal), Mathf.Lerp(1f, .55f, reveal), (.38f * Mathf.Clamp01(grow * 1.4f) + flash * .5f));

        // head billboards (sizes are world units, independent of the parent's scale)
        float inv = 1f / Mathf.Max(.01f, transform.lossyScale.x);
        float pulse = 1f + .035f * Mathf.Sin(Time.time * 5.2f) + .02f * Mathf.Sin(Time.time * 11.3f + 1f);
        nucleus.rotation = halo.rotation = bloom.rotation = flare.rotation = ct.rotation;
        nucleus.localScale = Vector3.one * (4.2f * pulse * (1f + flash * .6f) * inv);
        halo.localScale = Vector3.one * ((16f + 2.5f * Mathf.Sin(Progress * Mathf.PI)) * pulse * (1f + flash * .5f) * inv);
        bloom.localScale = Vector3.one * (46f * inv);
        flare.localScale = new Vector3(150f * pulse * (1f + flash * .7f) * inv, 62f * pulse * (1f + flash * .5f) * inv, 1f);

        // burst: shockwave ring + starburst rays stay where the burst happened while the comet carries on
        bool showBurst = burstFired && bt < 1f;
        if (burstQuad.gameObject.activeSelf != showBurst) burstQuad.gameObject.SetActive(showBurst);
        if (showBurst)
        {
            burstQuad.SetPositionAndRotation(burstPos, ct.rotation);
            burstQuad.localScale = Vector3.one * (210f * inv);
            burstMat.SetFloat("_Param", bt);
            Tint(burstMat, 1f, 1f, 1f, .9f);
        }
    }

    void OnDestroy()
    {
        if (sky) Destroy(sky.gameObject);
        foreach (var mesh in ownedMeshes) if (mesh) Destroy(mesh);
        foreach (var m in materials) if (m) Destroy(m);
    }
}
