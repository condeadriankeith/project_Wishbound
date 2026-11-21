using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The weathered cottage at the edge of the Sunken Village, and the first real choice of the game.
///
///  Location (built at runtime next to the dressed hamlet): a small timber-and-stone cottage with an unshuttered
///  window you can see through; inside a cradle with a shivering infant, a cold hearth, and a cracked clay lantern
///  holding one of the Star's fallen fragments (a heartbeat of amber light). Outside, a flooded courtyard:
///  shallow still water, flagstones, puddles, dark slick walls, cold 3000K twilight.
///
///  Flow:  Approach  -> camera eases into IntimateCompanion, the child goes ahead, tugs, points at the window
///         Discovery -> near the sill the camera holds on the window (tilt-shift on the lantern); the Star's rim
///                      flickers and its trail shortens - the missing piece is right there
///         Interaction -> whispers, motes drift from the lantern to the Star, harmonica + cello sting
///         Choice (no HUD) ->  Tether: hold F (Starlight Pulse) ~1.5 s     Roots: let the light dim, press E (Reach)
///         Exit -> camera back to Explore, music cross-fades by alignment, StoryFlags saved.
/// </summary>
public class CottageChoiceScene : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("Optional explicit spot for the cottage (its door faces the hamlet). Zero = choose automatically.")]
    public Vector3 cottageOverride;
    [Header("Feel")]
    public float pulseHoldSeconds = 1.6f;
    public float approachRadius = 16f, sillRadius = 2.6f;
    [Header("Audio (placeholders)")]
    public AudioClip sting, glassShatter, babyCry, warmHum;

    enum Phase { Waiting, Approach, Discovery, Interaction, Choice, Tether, Roots, Done }
    Phase phase = Phase.Waiting;

    Transform star, cottage, sill, lanternT, windowPane, childStand;
    Light heartbeat, roomFill; Renderer fragmentR, paneR, handprintR; Transform fragment;
    ParticleSystem motes, warmth;
    Vector3 windowNormal;   // points out of the window, toward the courtyard
    float hold;
    MaterialPropertyBlock mpb;

    void Awake()
    {
        // keep the courtyard + the cottage footprint free of scattered grass (needs a fixed spot)
        if (cottageOverride != Vector3.zero) GrassExclusions.Add(cottageOverride, 12f);
    }

    IEnumerator Start()
    {
        mpb = new MaterialPropertyBlock();
        while (!VillageDressing.Instance || !VillageDressing.Instance.Built) yield return null;
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
        BuildLocation();
        phase = Phase.Approach;
    }

    // ===================================================================================== location
    void BuildLocation()
    {
        var vd = VillageDressing.Instance;
        Vector3 hamlet = vd.Well ? vd.Well.position : vd.transform.position;
        Vector3 at = cottageOverride != Vector3.zero ? cottageOverride : vd.FindOpen(hamlet + new Vector3(-26, 0, 22), 9f, 0, 30);
        vd.Claim(at, 10f);
        at = VillageKit.Ground(at);
        var root = new GameObject("WeatheredCottage"); root.AddComponent<VillageKitPiece>();
        root.transform.position = at;
        Vector3 toHamlet = hamlet - at; toHamlet.y = 0; toHamlet.Normalize();
        root.transform.rotation = Quaternion.LookRotation(toHamlet);   // +Z (front, window wall) faces the hamlet
        cottage = root.transform;
        var tr = root.transform; var box = VillageKit.ChamferBox(.12f);

        GameObject Part(string n, Mesh m, Material mat, Vector3 lp, Vector3 sc, Quaternion? r = null, bool col = true)
        {
            var g = new GameObject(n); g.transform.SetParent(tr, false); g.transform.localPosition = lp; g.transform.localRotation = r ?? Quaternion.identity; g.transform.localScale = sc;
            g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (col) g.AddComponent<BoxCollider>();
            return g;
        }
        // slick, dark, weathered materials
        var wallStone = VillageKit.Mat(new Color(.34f, .34f, .36f), default, .3f, .35f);
        var plaster = VillageKit.Mat(new Color(.52f, .48f, .42f), default, .35f, .2f);
        var beam = VillageKit.Mat(new Color(.26f, .19f, .14f));
        var roof = VillageKit.Mat(new Color(.3f, .36f, .3f), default, .3f, .15f);
        var floorM = VillageKit.Mat(new Color(.3f, .23f, .17f));

        float W = 5.2f, D = 4.4f, H = 2.6f, t = .28f;
        Part("Floor", box, floorM, new Vector3(0, .05f, 0), new Vector3(W, .1f, D), null, false);
        // back + sides
        Part("WallBack", box, plaster, new Vector3(0, H / 2, -D / 2), new Vector3(W, H, t));
        Part("WallLeft", box, plaster, new Vector3(-W / 2, H / 2, 0), new Vector3(t, H, D));
        Part("WallRight", box, plaster, new Vector3(W / 2, H / 2, 0), new Vector3(t, H, D));
        // front wall with an unshuttered window (left) and a door (right)
        float wx = -1.1f, ww = 1.15f, wy0 = 1.0f, wy1 = 1.95f, dx = 1.35f, dw = 1.0f, dh = 2.0f;
        Part("FrontBelowWindow", box, wallStone, new Vector3(wx, wy0 / 2, D / 2), new Vector3(ww, wy0, t));
        Part("FrontAboveWindow", box, plaster, new Vector3(wx, (wy1 + H) / 2, D / 2), new Vector3(ww, H - wy1, t));
        Part("FrontLeft", box, plaster, new Vector3((-W / 2 + wx - ww / 2) / 2, H / 2, D / 2), new Vector3(wx - ww / 2 + W / 2, H, t));
        Part("FrontMid", box, plaster, new Vector3((wx + ww / 2 + dx - dw / 2) / 2, H / 2, D / 2), new Vector3(dx - dw / 2 - (wx + ww / 2), H, t));
        Part("FrontAboveDoor", box, plaster, new Vector3(dx, (dh + H) / 2, D / 2), new Vector3(dw, H - dh, t));
        Part("FrontRight", box, plaster, new Vector3((dx + dw / 2 + W / 2) / 2, H / 2, D / 2), new Vector3(W / 2 - dx - dw / 2, H, t));
        Part("Door", box, beam, new Vector3(dx, dh / 2, D / 2 + .02f), new Vector3(dw - .08f, dh - .05f, .1f));
        // stone sill + timber frame
        var sillGo = Part("Sill", box, wallStone, new Vector3(wx, wy0 + .03f, D / 2 + .16f), new Vector3(ww + .35f, .12f, .45f));
        sill = sillGo.transform;
        Part("FrameTop", box, beam, new Vector3(wx, wy1 + .04f, D / 2 + .05f), new Vector3(ww + .2f, .1f, .34f));
        Part("FrameL", box, beam, new Vector3(wx - ww / 2 - .05f, (wy0 + wy1) / 2, D / 2 + .05f), new Vector3(.1f, wy1 - wy0, .34f));
        Part("FrameR", box, beam, new Vector3(wx + ww / 2 + .05f, (wy0 + wy1) / 2, D / 2 + .05f), new Vector3(.1f, wy1 - wy0, .34f));
        Part("Mullion", box, beam, new Vector3(wx, (wy0 + wy1) / 2, D / 2), new Vector3(.05f, wy1 - wy0, .05f), null, false);
        // corner beams + roof
        foreach (var c in new[] { new Vector3(-W / 2, 0, -D / 2), new Vector3(W / 2, 0, -D / 2), new Vector3(-W / 2, 0, D / 2), new Vector3(W / 2, 0, D / 2) })
            Part("Corner", box, beam, c + Vector3.up * H / 2, new Vector3(.3f, H + .1f, .3f));
        Part("RoofFront", box, roof, new Vector3(0, H + .75f, D / 4 + .25f), new Vector3(W + .9f, .16f, D / 2 + 1.2f), Quaternion.Euler(32, 0, 0));
        Part("RoofBack", box, roof, new Vector3(0, H + .75f, -D / 4 - .25f), new Vector3(W + .9f, .16f, D / 2 + 1.2f), Quaternion.Euler(-32, 0, 0));
        Part("Ridge", box, beam, new Vector3(0, H + 1.45f, 0), new Vector3(W + 1f, .18f, .2f), null, false);
        Part("GableL", box, plaster, new Vector3(-W / 2, H + .55f, 0), new Vector3(t, 1.1f, D * .55f), null, false);
        Part("GableR", box, plaster, new Vector3(W / 2, H + .55f, 0), new Vector3(t, 1.1f, D * .55f), null, false);

        // glass pane (slightly frosted, catches the amber from inside)
        var pane = GameObject.CreatePrimitive(PrimitiveType.Quad); pane.name = "WindowPane"; Destroy(pane.GetComponent<Collider>());
        pane.transform.SetParent(tr, false); pane.transform.localPosition = new Vector3(wx, (wy0 + wy1) / 2, D / 2 + .01f); pane.transform.localRotation = Quaternion.Euler(0, 180, 0);
        pane.transform.localScale = new Vector3(ww, wy1 - wy0, 1);
        paneR = pane.GetComponent<Renderer>(); paneR.sharedMaterial = GlassMaterial(); windowPane = pane.transform;
        var hp = GameObject.CreatePrimitive(PrimitiveType.Quad); hp.name = "Handprint"; Destroy(hp.GetComponent<Collider>());
        hp.transform.SetParent(tr, false); hp.transform.localPosition = new Vector3(wx + .15f, (wy0 + wy1) / 2 - .05f, D / 2 + .03f); hp.transform.localRotation = Quaternion.Euler(0, 180, 0); hp.transform.localScale = Vector3.one * .42f;
        handprintR = hp.GetComponent<Renderer>(); handprintR.sharedMaterial = GlowMaterial(new Color(1f, .78f, .45f)); handprintR.enabled = false;

        // ---- interior: cradle, infant, cold hearth, cracked clay lantern with the fragment
        var wood = VillageKit.Mat(new Color(.45f, .32f, .22f)); var cloth = VillageKit.Mat(new Color(.62f, .55f, .5f)); var clay = VillageKit.Mat(new Color(.58f, .36f, .24f));
        Vector3 cradle = new Vector3(wx + .1f, 0, D / 2 - 1.3f);
        Part("CradleBase", box, wood, cradle + new Vector3(0, .35f, 0), new Vector3(1.0f, .35f, .55f), null, false);
        Part("CradleRockerA", box, wood, cradle + new Vector3(0, .12f, .22f), new Vector3(1.1f, .1f, .08f), null, false);
        Part("CradleRockerB", box, wood, cradle + new Vector3(0, .12f, -.22f), new Vector3(1.1f, .1f, .08f), null, false);
        Part("Blanket", VillageKit.Blob(5, .08f), cloth, cradle + new Vector3(.08f, .56f, 0), new Vector3(.36f, .1f, .2f), null, false);
        Part("InfantHead", VillageKit.Blob(9, .03f), VillageKit.Mat(new Color(.82f, .68f, .58f)), cradle + new Vector3(-.28f, .58f, 0), Vector3.one * .1f, null, false);
        Part("Hearth", box, wallStone, new Vector3(-W / 2 + .6f, .55f, -D / 2 + .55f), new Vector3(1.1f, 1.1f, .8f), null, false);
        Part("HearthAsh", box, VillageKit.Mat(new Color(.12f, .12f, .14f)), new Vector3(-W / 2 + .6f, .3f, -D / 2 + .85f), new Vector3(.7f, .05f, .3f), null, false);
        Part("Table", box, wood, new Vector3(1.4f, .45f, -.9f), new Vector3(1.2f, .08f, .8f), null, false);
        Vector3 lanternAt = cradle + new Vector3(.75f, 0, .1f);
        var lan = Part("ClayLantern", VillageKit.Ring(9, .82f), clay, lanternAt, new Vector3(.17f, .32f, .17f), null, false); lanternT = lan.transform;
        Part("LanternCrack", box, VillageKit.Mat(new Color(.1f, .06f, .05f), new Color(.5f, .3f, .1f)), lanternAt + new Vector3(.17f, .17f, 0), new Vector3(.02f, .18f, .03f), Quaternion.Euler(0, 0, 18), false);
        // the fragment
        var frag = GameObject.CreatePrimitive(PrimitiveType.Sphere); frag.name = "StarFragment"; Destroy(frag.GetComponent<Collider>());
        frag.transform.SetParent(tr, false); frag.transform.localPosition = lanternAt + Vector3.up * .26f; frag.transform.localScale = Vector3.one * .16f;
        var intro = FindFirstObjectByType<WishboundIntro>();
        fragmentR = frag.GetComponent<Renderer>(); if (intro && intro.starBodyMaterial) fragmentR.sharedMaterial = intro.starBodyMaterial;
        fragment = frag.transform;
        heartbeat = NewLight("Heartbeat", lanternAt + Vector3.up * .45f, new Color(1f, .62f, .3f), 4.5f, 1.8f, true);
        roomFill = NewLight("RoomFill", new Vector3(0, 2.0f, 0), new Color(.25f, .3f, .55f), 6f, .25f, false);

        // ---- flooded courtyard outside the window wall: shallow water, flagstones, puddles, low walls
        Vector3 yard = new Vector3(0, 0, D / 2 + 3.2f);
        var water = Part("FloodWater", VillageKit.Disc(77, .2f, 20), WaterMaterial(), yard + Vector3.up * .06f, new Vector3(4.6f, 1, 3.2f), null, false);
        water.AddComponent<PuddleShimmer>();
        var rng = new System.Random(77);
        for (int i = 0; i < 14; i++)
        {
            Vector3 fp = yard + new Vector3(Mathf.Lerp(-3.5f, 3.5f, (float)rng.NextDouble()), .1f, Mathf.Lerp(-2.4f, 2.4f, (float)rng.NextDouble()));
            Part("Flagstone", VillageKit.Disc(rng.Next(), .2f, 7), wallStone, fp, new Vector3(.45f, 1, .38f), Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), false);
        }
        VillageKit.StoneWall(new[] { tr.TransformPoint(new Vector3(-W / 2 - 1.5f, 0, D / 2 + .5f)), tr.TransformPoint(new Vector3(-W / 2 - 2.2f, 0, D / 2 + 5.5f)), tr.TransformPoint(new Vector3(0, 0, D / 2 + 7f)) }, tr, 78);
        VillageKit.LanternPost(tr.TransformPoint(new Vector3(W / 2 + 1.6f, 0, D / 2 + 1.2f)), tr, 79);

        // ---- camera framing zones + the child's standing spot at the sill
        var zone = new GameObject("CourtyardZone"); zone.transform.SetParent(tr, false); zone.transform.localPosition = yard + Vector3.up;
        var sc = zone.AddComponent<SphereCollider>(); sc.radius = approachRadius;
        zone.AddComponent<CameraProfileZone>().profile = IsoCameraRig.Profile.IntimateCompanion;
        childStand = new GameObject("ChildStand").transform; childStand.SetParent(tr, false); childStand.localPosition = new Vector3(wx + .8f, 0, D / 2 + .7f);
        windowNormal = tr.forward;

        // motes (fragment -> star) and warmth (star -> fragment), off until needed
        motes = Stream("Motes", new Color(1.6f, 1.2f, .55f), new Color(.5f, 1.2f, 1.3f));
        warmth = Stream("Warmth", new Color(1.8f, 1.1f, .5f), new Color(1.4f, .8f, .4f));
    }

    Light NewLight(string n, Vector3 local, Color c, float range, float intensity, bool shadows)
    {
        var g = new GameObject(n); g.transform.SetParent(cottage, false); g.transform.localPosition = local;
        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        return l;
    }

    ParticleSystem Stream(string n, Color a, Color b)
    {
        var g = new GameObject(n); g.transform.SetParent(cottage, false);
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = 2.2f; m.startSpeed = 0; m.startSize = new ParticleSystem.MinMaxCurve(.03f, .08f); m.maxParticles = 200;
        m.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var em = ps.emission; em.rateOverTime = 0;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .1f;
        var n2 = ps.noise; n2.enabled = true; n2.strength = .15f; n2.frequency = 1.5f;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = gr;
        var intro = FindFirstObjectByType<WishboundIntro>(); if (intro) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = intro.sparkMaterial;
        return ps;
    }

    /// <summary>Particles flow from one point to another (drives a stream each frame).</summary>
    void Flow(ParticleSystem ps, Vector3 from, Vector3 to, float rate)
    {
        ps.transform.position = from;
        var em = ps.emission; em.rateOverTime = rate;
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
        Vector3 vel = (to - from) / ps.main.startLifetime.constant;
        v.x = vel.x; v.y = vel.y + .1f; v.z = vel.z;
        if (!ps.isPlaying) ps.Play();
    }

    static Material GlassMaterial()
    {
        var asset = Resources.Load<Material>("WishboundWindowGlass"); if (asset) return asset;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
        m.SetColor("_BaseColor", new Color(.75f, .82f, .9f, .16f)); return m;
    }
    static Material GlowMaterial(Color c)
    {
        var asset = Resources.Load<Material>("WishboundHandprint"); if (asset) return asset;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2); m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 1); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD"); m.renderQueue = 3001;
        m.SetTexture("_BaseMap", HandTexture()); m.SetColor("_BaseColor", c * 2f); return m;
    }
    static Texture2D HandTexture()
    {
        // a soft palm + four finger blobs + thumb, painted into a small texture
        const int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2[] c = { new Vector2(32, 24), new Vector2(20, 44), new Vector2(28, 50), new Vector2(36, 50), new Vector2(44, 45), new Vector2(50, 28) };
        float[] r = { 13, 5, 5.5f, 5.5f, 5, 5 };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float a = 0;
                for (int i = 0; i < c.Length; i++) a = Mathf.Max(a, Mathf.Clamp01(1 - (Vector2.Distance(new Vector2(x, y), c[i]) - r[i]) / 4f));
                for (int i = 1; i < 5; i++) { Vector2 p = Vector2.Lerp(c[0] + (c[i] - c[0]) * .4f, c[i], .5f); a = Mathf.Max(a, Mathf.Clamp01(1 - (Vector2.Distance(new Vector2(x, y), p) - 4.5f) / 3f)); }
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply(); return t;
    }
    static Material WaterMaterial()
    {
        var m = new Material(VillageKit.WaterMat(true, 1.0f));
        m.SetColor("_Shallow", new Color(.3f, .5f, .55f)); m.SetColor("_Mid", new Color(.12f, .28f, .36f)); m.SetColor("_Deep", new Color(.05f, .12f, .2f));   // colder, darker flood water
        return m;
    }

    // ===================================================================================== flow
    void Update()
    {
        if (!star || !cottage) return;
        var child = ChildEncounter.Instance;
        float t = Time.time;
        Vector3 sillP = sill.position + windowNormal * .55f;
        float dSill = Flat(star.position - sillP).magnitude;
        float dCottage = Flat(star.position - cottage.position).magnitude;

        // the heartbeat: ~70 bpm pulse, faster once the fragment senses its source
        float bpm = phase == Phase.Interaction || phase == Phase.Choice ? 110 : 70;
        float beat = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * bpm / 60f * Mathf.PI * 2)), 6);
        if (phase != Phase.Tether && phase != Phase.Roots && phase != Phase.Done)
        {
            heartbeat.intensity = 1.2f + beat * 1.6f;
            fragmentR.GetPropertyBlock(mpb); mpb.SetFloat("_Intensity", .8f + beat * .9f); fragmentR.SetPropertyBlock(mpb);
        }

        switch (phase)
        {
            case Phase.Approach:
                if (dCottage < approachRadius && child && child.HasMet)
                {
                    phase = Phase.Discovery;
                    StartCoroutine(ChildLeads(child));
                }
                break;
            case Phase.Discovery:
                if (StarRadiance.Instance) StarRadiance.Instance.missingPiece = Mathf.InverseLerp(approachRadius, sillRadius, dSill);
                if (dSill < sillRadius)
                {
                    phase = Phase.Interaction;
                    if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(IsoCameraRig.Profile.WindowHold(Quaternion.LookRotation(-windowNormal).eulerAngles.y));
                    StartCoroutine(Interaction(child));
                }
                break;
            case Phase.Choice:
                UpdateChoice(child, dSill);
                break;
        }
    }

    IEnumerator ChildLeads(ChildEncounter child)
    {
        // the child hurries ahead, comes back to tug at the star, then stands pointing at the window
        child.Direct(childStand.position);
        yield return new WaitForSeconds(2.2f);
        child.Direct(star.position + (childStand.position - star.position).normalized * 1.2f);
        yield return new WaitForSeconds(1.1f);
        WhisperLine.Show(child.Child.transform, "...look", 2f, 1.6f, 34);
        child.Direct(childStand.position);
    }

    IEnumerator Interaction(ChildEncounter child)
    {
        if (child) child.Direct(sill.position + windowNormal * .6f + cottage.right * 1.5f);
        yield return new WaitForSeconds(1.8f);
        if (child) WhisperLine.Show(child.Child.transform, "it's keeping her warm...", 4f, 1.5f, 36);
        yield return new WaitForSeconds(1.2f);
        if (MusicDirector.Instance) MusicDirector.Instance.OneShot(sting, .8f);
        // the fragment recognises its source: golden motes drift out through the window cracks to the star's chest
        for (float k = 0; k < 4f; k += Time.deltaTime)
        {
            Flow(motes, fragment.position, star.position + Vector3.up * .2f, 14);
            yield return null;
        }
        if (child) child.Direct(sill.position + windowNormal * 1.9f + cottage.right * 2.2f);   // half a step back
        yield return new WaitForSeconds(.8f);
        if (child) WhisperLine.Show(child.Child.transform, "if you take it back... can you go home?", 5f, 1.5f, 34);
        phase = Phase.Choice;
    }

    void UpdateChoice(ChildEncounter child, float dSill)
    {
        Flow(motes, fragment.position, star.position + Vector3.up * .2f, 6);
        var rad = StarRadiance.Instance;
        // Tether: hold the Starlight Pulse until the star flares through the glass
        if (StarInput.PulseHeld) hold += Time.deltaTime; else hold = Mathf.MoveTowards(hold, 0, Time.deltaTime * 2);
        if (rad) { rad.flare = Mathf.Clamp01(hold / pulseHoldSeconds); rad.radiance = Mathf.Lerp(1, 1.3f, rad.flare); }
        if (hold >= pulseHoldSeconds) { phase = Phase.Tether; StartCoroutine(Tether(child)); return; }
        // Roots: with the light dimmed (not pulsing), reach out and press a hand to the glass
        if (hold < .05f && dSill < sillRadius + .6f && StarInput.ReachPressed) { phase = Phase.Roots; StartCoroutine(Roots(child)); }
    }

    IEnumerator Tether(ChildEncounter child)
    {
        var rad = StarRadiance.Instance;
        motes.Stop();
        if (MusicDirector.Instance) MusicDirector.Instance.OneShot(glassShatter, 1f);
        Shatter();
        // the fragment zips back into the star's core
        Vector3 from = fragment.position;
        for (float t = 0; t < .7f; t += Time.deltaTime)
        {
            float k = t / .7f; fragment.position = Vector3.Lerp(from, star.position + Vector3.up * .3f, k * k);
            yield return null;
        }
        fragment.gameObject.SetActive(false);
        if (rad) { rad.missingPiece = 0; StoryFlags.Nudge(+60f, "Cottage: reclaimed the light"); rad.UpdateDerivedBucket(); rad.radiance = 1.25f; }
        // inside: the room plunges into cold indigo and the baby begins to cry
        for (float t = 0; t < 1.5f; t += Time.deltaTime)
        {
            float k = t / 1.5f;
            heartbeat.intensity = Mathf.Lerp(3, 0, k); roomFill.intensity = Mathf.Lerp(.25f, .7f, k); roomFill.color = new Color(.15f, .18f, .5f);
            if (rad) rad.flare = Mathf.Lerp(1, 0, k);
            yield return null;
        }
        if (MusicDirector.Instance) MusicDirector.Instance.OneShot(babyCry, .7f);
        // the child shields their eyes and steps back; they will not hold the star's hand
        if (child) { child.Refuse(); WhisperLine.Show(child.Child.transform, "...", 2.5f, 1.6f, 34); }
        Finish(StarAlignment.Tether);
    }

    IEnumerator Roots(ChildEncounter child)
    {
        var rad = StarRadiance.Instance;
        motes.Stop();
        handprintR.enabled = true;
        if (MusicDirector.Instance) MusicDirector.Instance.OneShot(warmHum, .9f);
        // a final trickle of the star's own warmth flows into the shard
        for (float t = 0; t < 3f; t += Time.deltaTime)
        {
            float k = t / 3f;
            Flow(warmth, star.position + Vector3.up * .3f, fragment.position, 22);
            handprintR.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", new Color(1f, .78f, .45f) * 2f * Mathf.Sin(Mathf.Min(1, k * 1.4f) * Mathf.PI * .5f) * (1 - Mathf.SmoothStep(.75f, 1, k))); handprintR.SetPropertyBlock(mpb);
            heartbeat.intensity = Mathf.Lerp(1.6f, 3.4f, k); heartbeat.range = Mathf.Lerp(4.5f, 7f, k); heartbeat.color = Color.Lerp(heartbeat.color, new Color(1f, .63f, .32f), k);   // ~2800K
            roomFill.color = Color.Lerp(roomFill.color, new Color(1f, .7f, .45f), k); roomFill.intensity = Mathf.Lerp(.25f, .9f, k);
            if (rad) { rad.radiance = Mathf.Lerp(1, .75f, k); rad.missingPiece = Mathf.Lerp(rad.missingPiece, 0, k); }
            yield return null;
        }
        warmth.Stop(); handprintR.enabled = false;
        if (rad) StoryFlags.Nudge(-60f, "Cottage: gave the light"); rad.UpdateDerivedBucket();
        fragmentR.GetPropertyBlock(mpb); mpb.SetFloat("_Intensity", 1.6f); fragmentR.SetPropertyBlock(mpb);
        // the child exhales a visible breath, steps forward and slips their hand into the star's
        if (child) { Breath(child.Child.transform.position + Vector3.up * 1.2f + child.Child.transform.forward * .2f); yield return new WaitForSeconds(.8f); child.TakeHand(); }
        Finish(StarAlignment.Roots);
    }

    void Finish(StarAlignment a)
    {
        phase = Phase.Done;
        StoryFlags.Set("CottageChoiceMade");
        if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(null);
        if (MusicDirector.Instance) MusicDirector.Instance.Play(a == StarAlignment.Tether ? "tether" : "roots", 5f);
        var zone = cottage.Find("CourtyardZone"); if (zone) zone.GetComponent<CameraProfileZone>().active = false;
    }

    void Shatter()
    {
        windowPane.gameObject.SetActive(false);
        var g = new GameObject("GlassShards"); g.transform.position = windowPane.position;
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(2, 7); m.gravityModifier = 1.4f;
        m.startSize = new ParticleSystem.MinMaxCurve(.03f, .12f); m.startColor = new ParticleSystem.MinMaxGradient(new Color(.5f, 1.4f, 1.8f), new Color(1.8f, .5f, 1.6f)); m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 90) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1, .9f, .05f); g.transform.rotation = cottage.rotation;
        var intro = FindFirstObjectByType<WishboundIntro>(); if (intro) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = intro.sparkMaterial;
        ps.Play(); Destroy(g, 4);
        var flash = new GameObject("PulseFlash").AddComponent<Light>(); flash.transform.position = windowPane.position + windowNormal; flash.type = LightType.Point; flash.color = new Color(.8f, .9f, 1f); flash.range = 14; flash.intensity = 12; Destroy(flash.gameObject, .4f);
    }

    void Breath(Vector3 at)
    {
        var g = new GameObject("Breath"); g.transform.position = at;
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.startLifetime = 2.2f; m.startSpeed = .35f; m.startSize = new ParticleSystem.MinMaxCurve(.12f, .3f); m.startColor = new Color(.9f, .92f, 1f, .35f); m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 12) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12; sh.radius = .02f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 2));
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.6f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
        var intro = FindFirstObjectByType<WishboundIntro>(); var crater = FindFirstObjectByType<BeachCometCrater>();
        if (crater && crater.smokeMaterial) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = crater.smokeMaterial;
        ps.Play(); Destroy(g, 3);
    }

    /// <summary>Testing hook: make the choice without input (only while the choice is open).</summary>
    public void ForceChoice(bool tether)
    {
        if (phase != Phase.Choice) return;
        var child = ChildEncounter.Instance;
        if (tether) { phase = Phase.Tether; StartCoroutine(Tether(child)); } else { phase = Phase.Roots; StartCoroutine(Roots(child)); }
    }
    public string PhaseName => phase.ToString();
    public Transform Sill => sill;
    public Vector3 WindowNormal => windowNormal;

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
