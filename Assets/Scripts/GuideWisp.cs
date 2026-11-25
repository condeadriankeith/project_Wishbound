using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A small will-o'-the-wisp that teaches the way without any HUD. It appears a moment after the Star awakens and floats
/// a few metres ahead of it toward the current goal, bobbing, darting forward and glancing back; if the player falls
/// behind it waits and pulses brighter. Goals, in order:
///   1. the child hiding behind a tree   (until they hold hands)
///   2. the weathered cottage window      (until the choice is made)
/// When the Star reaches the goal the wisp drifts into it and fades; with no goal left it goes away entirely.
///
/// Navigation: the wisp hovers a fixed height above the TERRAIN (TerrainProbe - never the tree canopy colliders, which is
/// what used to haul it up into the trees). Each ~0.3 s it fans out a few headings from the star, discards any that
/// cross water, a steep bank or a tree trunk, and leads along the clearest one closest to the goal. While flying it is
/// also pushed gently away from trunks, so it curves round them instead of through them.
/// </summary>
public class GuideWisp : MonoBehaviour
{
    public Material haloMaterial;      // Assets/Art/Star/StarHalo.mat
    public Material sparkMaterial;     // Assets/Art/Comet/CometEmber.mat
    public float leadDistance = 5.5f, waitDistance = 10f, arriveRadius = 3.5f, height = 1.3f, appearDelay = 3f, maxSpeed = 11f;
    public Color colour = WispPreset.Colour;
    [Tooltip("Clear space kept between the wisp's path and a tree trunk (metres).")] public float trunkClearance = 1.2f;
    [Tooltip("Steepest bank (rise over run) the wisp will lead the star up.")] public float maxGradient = .9f;

    // ---------------------------------------------------------------- visuals
    class Layer
    {
        public Transform t; public Renderer r;
        public float scale, intensity, falloff, rays, orbit, orbitSpeed, tilt, phase;
        public Color inner, outer;
    }
    readonly List<Layer> layers = new List<Layer>();
    Transform star, wisp; Light glow; ParticleSystem sparks; TrailRenderer trail;
    MaterialPropertyBlock mpb;

    // ---------------------------------------------------------------- state
    Vector3 pos, vel, planned, lastGoal;
    float alpha, sinceIntro = -1, darting, groundY, nextPlan, nextLookup, lastSide, phase;
    bool blocked;
    CottageChoiceScene cottage;

    // trees (static after the island is built): flat arrays so a query is a plain loop
    float[] tx, tz, tr; int treeCount; bool treesScanned; float nextTreeScan;

    static readonly float[] Fan = { 0, 20, -20, 40, -40, 60, -60, 80, -80, 105, -105 };

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
        mpb = new MaterialPropertyBlock(); phase = Random.value * 6.28f;
        wisp = new GameObject("Wisp").transform; wisp.SetParent(transform, false);

        if (haloMaterial)
        {
            // soft wide halo, warm body, white-hot core, then three motes circling it
            foreach (var L in WispPreset.Layers) AddLayer(L.name, L.scale, L.inner, L.outer, L.falloff, L.rays, L.intensity, L.orbit, L.orbitSpeed, L.tilt);
        }

        glow = wisp.gameObject.AddComponent<Light>(); glow.type = LightType.Point; glow.color = colour; glow.range = 5f; glow.intensity = 0; glow.shadows = LightShadows.None;

        // a thin comet tail so its motion reads even at a glance
        trail = wisp.gameObject.AddComponent<TrailRenderer>();
        trail.time = 1.1f; trail.minVertexDistance = .08f; trail.alignment = LineAlignment.View; trail.numCapVertices = 4;
        trail.widthCurve = new AnimationCurve(new Keyframe(0, .2f), new Keyframe(.35f, .12f), new Keyframe(1, 0));
        var tg = new Gradient();
        tg.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .8f), 0), new GradientColorKey(new Color(1f, .75f, .35f), .45f), new GradientColorKey(new Color(.5f, .6f, 1f), 1) },
                   new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.35f, .5f), new GradientAlphaKey(0, 1) });
        trail.colorGradient = tg;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false;
        if (sparkMaterial) trail.sharedMaterial = sparkMaterial;

        var sg = new GameObject("Sparks"); sg.transform.SetParent(wisp, false);
        sparks = sg.AddComponent<ParticleSystem>(); sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = sparks.main; m.useUnscaledTime = MenuMode; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .3f);
        m.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f); m.startColor = new ParticleSystem.MinMaxGradient(colour * 2f, new Color(.6f, .95f, 1f) * 2f); m.maxParticles = 120; m.gravityModifier = -.05f;
        var em = sparks.emission; em.rateOverTime = 0; em.rateOverDistance = 7;
        var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .12f;
        var col = sparks.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = g;
        if (sparkMaterial) sg.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMaterial;
        if (MenuMode)
        {
            trail.emitting = false; trail.enabled = false;
        }
        sparks.Play();
        wisp.gameObject.SetActive(false);
    }

    void AddLayer(string name, float scale, Color inner, Color outer, float falloff, float rays, float intensity, float orbit = 0, float orbitSpeed = 0, float tilt = 0)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = name;
        q.transform.SetParent(wisp, false); q.transform.localScale = Vector3.one * scale;
        var r = q.GetComponent<Renderer>(); r.sharedMaterial = haloMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        layers.Add(new Layer { t = q.transform, r = r, scale = scale, inner = inner, outer = outer, falloff = falloff, rays = rays, intensity = intensity, orbit = orbit, orbitSpeed = orbitSpeed, tilt = tilt, phase = Random.value * 6.28f });
    }

    // ---------------------------------------------------------------- goals
    bool Goal(out Vector3 goal)
    {
        goal = default;
        var journey = MountainJourney.Instance;
        if (journey && journey.Ready && journey.Current != MountainJourney.Step.Complete)
        {
            goal = journey.GuideDestination;
            return !journey.Busy;
        }
        var ce = ChildEncounter.Instance;
        if (ce && ce.Child && ce.Child.gameObject.activeInHierarchy && !ce.HasMet) { goal = ce.Child.transform.position; return true; }
        if (!cottage && Time.time > nextLookup) { cottage = FindFirstObjectByType<CottageChoiceScene>(); nextLookup = Time.time + 2f; }   // never search the scene every frame
        if (cottage && cottage.Sill && !StoryFlags.Get("CottageChoiceMade") && (ce == null || ce.HasMet)) { goal = cottage.Sill.position + cottage.WindowNormal * 1.2f; return true; }
        return false;
    }

    // ---------------------------------------------------------------- update
    float VisT => MenuMode ? Time.unscaledTime : Time.time;
    public bool MenuMode { get; set; }
    public bool VisualReady => wisp != null;
    public void SetMenuPose(Vector3 at, float opacity)
    {
        if (!wisp) return;
        if (!wisp.gameObject.activeSelf) { wisp.gameObject.SetActive(true); trail.Clear(); }
        wisp.position = at; wisp.localScale = Vector3.one * .30f;
        foreach (var t in wisp.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
        Fade(opacity, Time.unscaledDeltaTime);
        trail.widthMultiplier = (.6f + alpha * .6f) * .30f;

        glow.intensity = 0;
    }

    void Update()
    {
        if (MenuMode) return;
        if (!star) return;
        float dt = Mathf.Min(Time.deltaTime, .05f);
        if (WishboundIntro.IsPlaying || (HouseEntrances.Instance && HouseEntrances.Instance.Inside))
        {
            sinceIntro = -1; Fade(0, dt);
            if (wisp.gameObject.activeSelf && alpha < .02f) wisp.gameObject.SetActive(false);
            return;
        }
        if (sinceIntro < 0) sinceIntro = 0; sinceIntro += dt;
        ScanTrees();

        bool has = Goal(out var goal);
        if (has) lastGoal = goal; else goal = lastGoal;
        float dStarGoal = has ? Flat(goal - star.position).magnitude : 0;
        bool show = has && sinceIntro > appearDelay && dStarGoal > arriveRadius;
        if (show && !wisp.gameObject.activeSelf) Spawn(goal);
        if (!wisp.gameObject.activeSelf) return;

        Vector3 dir = Flat(goal - star.position); dir = dir.sqrMagnitude > .01f ? dir.normalized : Flat(star.forward);
        float dWisp = Flat(pos - star.position).magnitude;
        bool waiting = show && dWisp > waitDistance;

        Vector3 target;
        if (!show) target = goal;                  // arriving: drift into the goal and fade
        else if (waiting) target = pos;            // the star is lagging: hold still and pulse
        else
        {
            if (Time.time >= nextPlan) { nextPlan = Time.time + .3f; planned = PickLead(star.position, dir, Mathf.Min(leadDistance, dStarGoal)); }
            target = planned;
            // a little dart forward every few seconds (along the planned heading, so it never darts into a tree)
            darting = Mathf.Max(0, darting - dt);
            if (!blocked && darting <= 0 && Random.value < dt * .25f) darting = 1.2f;
            if (darting > .6f) { Vector3 lead = Flat(planned - star.position); if (lead.sqrMagnitude > .01f) target += lead.normalized * 2.2f; }
        }

        // horizontal motion: spring toward the target, shoved away from any trunk it is about to clip
        Vector3 desired = Vector3.ClampMagnitude(Flat(target - pos) * 2.2f, maxSpeed) + TrunkPush(pos) * 3f;
        vel = Vector3.Lerp(vel, desired, 1 - Mathf.Exp(-4 * dt));
        pos += vel * dt;

        // vertical: hover over the terrain surface itself, smoothed so bumps never snap it
        groundY = Mathf.Lerp(groundY, TerrainProbe.GroundY(pos.x, pos.z, groundY), 1 - Mathf.Exp(-8 * dt));
        float wantY = groundY + height + Mathf.Sin(Time.time * 2.2f + phase) * .18f + (darting > .6f ? .25f : 0);
        pos.y = Mathf.Lerp(pos.y, wantY, 1 - Mathf.Exp(-6 * dt));
        wisp.position = pos;

        float pulse = waiting ? .75f + .25f * Mathf.Sin(Time.time * 6) : .85f + .15f * Mathf.Sin(Time.time * 2.5f);
        Fade(show ? pulse : 0, dt);
        if (!show && alpha < .02f) wisp.gameObject.SetActive(false);
    }

    void Spawn(Vector3 goal)
    {
        Vector3 dir = Flat(goal - star.position); dir = dir.sqrMagnitude > .01f ? dir.normalized : Flat(star.forward);
        pos = star.position + dir * 1.5f;
        groundY = TerrainProbe.GroundY(pos.x, pos.z, star.position.y);
        pos.y = groundY + height; vel = Vector3.zero; planned = pos; blocked = false; nextPlan = 0; lastSide = 0;
        wisp.position = pos; wisp.gameObject.SetActive(true);
        if (trail) trail.Clear();
    }

    // ---------------------------------------------------------------- path planning
    /// <summary>The clearest heading (closest to the goal direction) for the next hop ahead of the star.</summary>
    Vector3 PickLead(Vector3 from, Vector3 dir, float dist)
    {
        float bestScore = float.MaxValue, bestSide = 0; Vector3 best = pos; bool found = false;
        for (int i = 0; i < Fan.Length; i++)
        {
            float a = Fan[i];
            Vector3 p = from + Quaternion.AngleAxis(a, Vector3.up) * dir * dist;
            if (!SegmentClear(from, p)) continue;
            float side = a == 0 ? 0 : Mathf.Sign(a);
            float score = Mathf.Abs(a) + (side != 0 && lastSide != 0 && side != lastSide ? 14f : 0f);   // stick with the side already chosen: no dithering
            if (score < bestScore) { bestScore = score; best = p; bestSide = side; found = true; }
        }
        blocked = !found;
        if (found) lastSide = bestSide;
        return found ? best : pos;   // nothing clear: hold still rather than lead the player into water or a trunk
    }

    bool SegmentClear(Vector3 a, Vector3 b)
    {
        const int steps = 4;
        float prevY = TerrainProbe.GroundY(a.x, a.z, a.y), run = Flat(b - a).magnitude / steps;
        for (int s = 1; s <= steps; s++)
        {
            Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
            if (!TerrainProbe.TryGetGroundY(p.x, p.z, out float y)) return false;          // off the island
            if (y < TerrainProbe.SeaLevel) return false;                                     // sea / wet sand
            if (Mathf.Abs(y - prevY) > run * maxGradient) return false;                     // cliff or steep bank
            prevY = y;
            if (NearTrunk(p.x, p.z, trunkClearance)) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- trees
    void ScanTrees()
    {
        if (treesScanned || Time.time < nextTreeScan) return;
        nextTreeScan = Time.time + 2f;
        var source = FindFirstObjectByType<IslandStylizedTrees>();
        if (!source || !source.TreeCollidersGO) return;
        // every tree is one tall canopy capsule on a shared transform: its centre and a trunk-sized radius locate the trunk
        var caps = source.TreeCollidersGO.GetComponents<CapsuleCollider>();
        if (caps.Length == 0) return;
        tx = new float[caps.Length]; tz = new float[caps.Length]; tr = new float[caps.Length]; treeCount = caps.Length;
        for (int i = 0; i < caps.Length; i++)
        {
            Vector3 c = caps[i].transform.TransformPoint(caps[i].center);
            tx[i] = c.x; tz[i] = c.z; tr[i] = Mathf.Clamp(caps[i].radius * .12f, .3f, .85f);
        }
        treesScanned = true;
    }

    bool NearTrunk(float x, float z, float clearance)
    {
        for (int i = 0; i < treeCount; i++)
        {
            float dx = x - tx[i], dz = z - tz[i], r = tr[i] + clearance;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    Vector3 TrunkPush(Vector3 p)
    {
        Vector3 push = Vector3.zero;
        for (int i = 0; i < treeCount; i++)
        {
            float dx = p.x - tx[i], dz = p.z - tz[i], rr = tr[i] + trunkClearance;
            float d2 = dx * dx + dz * dz;
            if (d2 < rr * rr && d2 > 1e-4f) { float d = Mathf.Sqrt(d2); push += new Vector3(dx, 0, dz) / d * (rr - d); }
        }
        return push;
    }

    // ---------------------------------------------------------------- look
    void Fade(float to, float dt)
    {
        alpha = Mathf.MoveTowards(alpha, to, dt * 1.5f);
        if (!wisp || !wisp.gameObject.activeSelf) return;
        float flick = .92f + .08f * (Mathf.PerlinNoise(VisT * WispPreset.FlickerFreq, phase) * 2f - 1f);
        glow.intensity = alpha * WispPreset.HaloIntensity * flick; glow.range = 4.2f + alpha * .8f;
        float breathe = 1f + .1f * Mathf.Sin(VisT * WispPreset.PulseSpeed + phase);
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            if (l.orbit > 0)
            {
                float a = VisT * l.orbitSpeed + l.phase;
                l.t.localPosition = new Vector3(Mathf.Cos(a) * l.orbit, Mathf.Sin(a * 1.3f + l.tilt) * l.orbit * .5f, Mathf.Sin(a) * l.orbit);
            }
            l.t.localScale = Vector3.one * l.scale * (i < 3 ? breathe : 1f) * (.6f + .4f * alpha);
            mpb.Clear();
            mpb.SetColor("_Inner", l.inner); mpb.SetColor("_Outer", l.outer);
            mpb.SetFloat("_Falloff", l.falloff); mpb.SetFloat("_Rays", l.rays); mpb.SetFloat("_Intensity", l.intensity * alpha * flick);
            l.r.SetPropertyBlock(mpb);
        }
        if (trail) { trail.emitting = alpha > .05f; trail.widthMultiplier = .6f + alpha * .6f; }
        var em = sparks.emission; em.rateOverDistance = 7 * alpha; em.rateOverTime = 4 * alpha;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
