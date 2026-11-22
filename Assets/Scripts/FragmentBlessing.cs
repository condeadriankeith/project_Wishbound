using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A fallen star fragment: a glowing orb with a pulsing light. Kept in the world after the cutscene.</summary>
public class FragmentGlow : MonoBehaviour
{
    public static readonly List<FragmentGlow> All = new List<FragmentGlow>();
    public Light lamp; public float baseIntensity = 2.4f, boost, phase; public float baseScale = .2f;
    void OnEnable() { All.Add(this); phase = Random.value * 6.28f; }
    void OnDisable() { All.Remove(this); }
    void Update()
    {
        float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * 1.3f * Mathf.PI * 2f + phase)), 6f);
        if (lamp) lamp.intensity = (baseIntensity + boost) * (.75f + .35f * beat);
        transform.localScale = Vector3.one * baseScale * (1f + .08f * beat + boost * .04f);
    }
}

/// <summary>
/// "What the fragments did." Played at the weathered cottage once the star reaches the window.
/// The opening comet burst into pieces mid-air; this is where some of them landed, and what they have been quietly doing since:
///   the night sky -> a piece falls through to a cold cottage and warms a shivering settler
///   -> another into the dry well (water rises) -> another under an empty cooking pot (a meal) -> another into dead soil (crops grow)
///   -> a high view of the village with every fragment lit like a lantern -> back to the star and the child.
/// The child then asks the real question: help gather the pieces back (Tether), or stay and learn who the child is (Roots).
/// </summary>
public class FragmentBlessing : MonoBehaviour
{
    public static FragmentBlessing Instance { get; private set; }
    public static void Run(CottageChoiceScene host, ChildEncounter child)
    {
        if (Instance) return;
        var go = new GameObject("FragmentBlessing"); var b = go.AddComponent<FragmentBlessing>(); b.host = host; b.child = child;
    }

    CottageChoiceScene host; ChildEncounter child; CutsceneDirector dir; Transform star;
    Text captionText, lineText, hintA, hintB; CanvasGroup captionGroup, lineGroup, hintAGroup, hintBGroup; Image flash;
    Material bodyMat, sparkMat, smokeMat;
    readonly List<GameObject> props = new List<GameObject>();
    readonly List<SettlerRig> actors = new List<SettlerRig>();
    bool running = true, sprint;
    GameObject sprintCover;

    // ------------------------------------------------------------------------------------ entry
    IEnumerator Start()
    {
        Instance = this;
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
        var intro = FindFirstObjectByType<WishboundIntro>(); if (intro) { bodyMat = intro.starBodyMaterial; sparkMat = intro.sparkMaterial; }
        var crater = FindFirstObjectByType<BeachCometCrater>(); if (crater) smokeMat = crater.smokeMaterial;
        captionText = WishboundUI.Label(null, "BlessingCaption", "", 40, new Vector2(.5f, .17f), new Vector2(1500, 120)); captionGroup = captionText.GetComponent<CanvasGroup>(); captionGroup.alpha = 0;
        captionText.color = new Color(1f, .88f, .6f);
        lineText = WishboundUI.Label(null, "BlessingLine", "", 50, new Vector2(.5f, .2f), new Vector2(1500, 200)); lineGroup = lineText.GetComponent<CanvasGroup>(); lineGroup.alpha = 0;
        hintA = WishboundUI.Label(null, "ChoiceA", "", 36, new Vector2(.3f, .1f), new Vector2(760, 100)); hintAGroup = hintA.GetComponent<CanvasGroup>(); hintAGroup.alpha = 0;
        hintB = WishboundUI.Label(null, "ChoiceB", "", 36, new Vector2(.7f, .1f), new Vector2(760, 100)); hintBGroup = hintB.GetComponent<CanvasGroup>(); hintBGroup.alpha = 0;
        BuildFlash();
        if (host) GrassExclusions.AddInterior(host.RoomBounds);
        GrassExclusions.ClearExisting();
        dir = CutsceneDirector.Begin();
        if (!CutsceneDirector.Active || !host) { Finish(StarAlignment.None, false); yield break; }
        CutsceneSkip.Begin(SkipToChoice);
        yield return Sequence();
    }

    /// <summary>Esc: fast-forward the film under a white cover (so every world change still happens), then hand back at the choice.</summary>
    void SkipToChoice()
    {
        if (sprint || !running) return;
        sprint = true;
        var cv = new GameObject("BlessingSkipCover", typeof(Canvas)); cv.transform.SetParent(transform, false);
        var c = cv.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 4000;
        var g = new GameObject("White", typeof(RectTransform), typeof(Image)); g.transform.SetParent(cv.transform, false);
        var r = (RectTransform)g.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        var im = g.GetComponent<Image>(); im.color = new Color(1f, .93f, .78f, 1f); im.raycastTarget = false;
        sprintCover = cv;
        Time.timeScale = 16f;
    }

    IEnumerator EndSprint()
    {
        Time.timeScale = 1f; sprint = false;
        yield return null;
        var im = sprintCover ? sprintCover.GetComponentInChildren<Image>() : null;
        for (float t = 0; t < .8f && im; t += Time.unscaledDeltaTime) { var col = im.color; col.a = 1f - Mathf.SmoothStep(0f, 1f, t / .8f); im.color = col; yield return null; }
        if (sprintCover) Destroy(sprintCover);
    }

    void OnDestroy() { if (sprint) Time.timeScale = 1f; CutsceneSkip.End(); if (running && dir) dir.End(0f); if (child) { child.CutsceneControl = false; if (child.Child && child.HasMet) child.Child.enabled = true; } if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------------------------ the film
    IEnumerator Sequence()
    {
        Transform cot = host.Cottage; Vector3 sillOut = host.Sill.position + host.WindowNormal * .5f;
        Vector3 N = host.WindowNormal, Rt = Vector3.Cross(Vector3.up, N);
        if (child && child.Child) { var perf = child.Performer; if (perf) perf.Act(ChildEmotion.Awe, star); }
        dir.handheld = .008f;
        if (child && child.Child) { child.CutsceneControl = true; child.Child.enabled = false; }

        // ---- 1. the night sky
        yield return Caption("The night the sky broke...", 3.2f, false);
        Vector3 sky = star.position + Vector3.up * 1.2f;
        dir.Cut(sky, sky + Vector3.up * 40f + N * 6f, 58f);
        Vector3 lantern = host.LanternPoint;
        var originalFragment = cot.Find("StarFragment");
        if (originalFragment) originalFragment.gameObject.SetActive(false);
        Transform fA = MakeFragment("Fragment A", lantern + Vector3.up * 70f + N * 25f - Rt * 18f, true);
        Vector3 aFrom = fA.position;
        StartCoroutine(Fall(fA, aFrom, lantern, 3.6f));
        yield return dir.Glide(() => sky + Vector3.up * .5f, () => Vector3.Lerp(fA.position, lantern, .08f), 46f, 3.6f);

        // ---- 2. the cold cottage
        yield return Flash(.18f, true);
        Vector3 adaMark = cot.TransformPoint(new Vector3(.85f, .1f, .55f));
        var ada = Actor(adaMark, Quaternion.LookRotation(Vector3.ProjectOnPlane(lantern - adaMark, Vector3.up)).eulerAngles.y, SettlerLook.Age.Elder, SettlerBrain.Role.Elder, 11);
        ada.transform.position = CutsceneDirector.SupportPoint(adaMark);
        ada.pose = SettlerRig.Pose.Cower; ada.energy = .2f;
        host.Heartbeat.color = new Color(.45f, .55f, 1f); host.Heartbeat.intensity = .4f; host.RoomFillLight.color = new Color(.2f, .25f, .55f);
        var breath = BreathLoop(() => ada.transform.position + Vector3.up * 1.45f, 1.1f);
        Vector3 winC = host.Sill.position + Vector3.up * .5f;
        Vector3 adaChest = Vector3.Lerp(ada.transform.position + Vector3.up * .95f, lantern, .4f);
        Vector3 camWide = CutsceneCam.Pick(adaChest, ada.transform.forward, host.RoomBounds, 2.4f, 3.2f, 1.55f);
        Vector3 camNear = CutsceneCam.Pick(adaChest, ada.transform.forward, host.RoomBounds, 2.2f, 2.6f, 1.4f);
        dir.Cut(camWide, adaChest, 50f);
        yield return Flash(.5f, false);
        StartCoroutine(Caption("...a piece of it found a cold house.", 3.4f, true));
        yield return dir.Glide(camNear, adaChest, 48f, 4f);
        // the fragment wakes: warm light swells, golden motes drift to her
        var fl = fA.GetComponent<FragmentGlow>(); StartCoroutine(Ease(v => { fl.boost = v * 3f; host.Heartbeat.intensity = Mathf.Lerp(.4f, 3.4f, v); host.Heartbeat.color = Color.Lerp(new Color(.45f, .55f, 1f), new Color(1f, .66f, .32f), v); host.RoomFillLight.color = Color.Lerp(new Color(.2f, .25f, .55f), new Color(1f, .7f, .45f), v); host.RoomFillLight.intensity = Mathf.Lerp(.25f, 1.1f, v); }, 3.2f));
        var motes = Emitter("Warmth", lantern, ada.transform.position + Vector3.up * .9f, new Color(1.8f, 1.2f, .55f), new Color(1.4f, .8f, .4f));
        yield return Wait(1.6f);
        ada.pose = SettlerRig.Pose.WarmHands; ada.lookPoint = lantern;                  // she reaches toward the light, hands open
        yield return Wait(2.2f);
        EndBreath(breath);
        ada.pose = SettlerRig.Pose.WarmHands; ada.energy = .5f;                              // shoulders drop; she breathes out
        yield return Caption("...and the cold let go.", 2.8f, false);
        Destroy(motes.gameObject);

        // ---- 3. the dry well
        yield return Flash(.4f, true);
        Transform well = SettlerCommunity.Instance && SettlerCommunity.Instance.Well ? SettlerCommunity.Instance.Well : (VillageDressing.Instance ? VillageDressing.Instance.Well : null);
        Vector3 wp = well ? well.position : star.position + Vector3.forward * 20f;
        wp.y = TerrainProbe.GroundY(wp.x, wp.z, wp.y);
        Vector3 wdir = Vector3.ProjectOnPlane(star.position - wp, Vector3.up); wdir = wdir.sqrMagnitude < .1f ? Vector3.forward : wdir.normalized;
        Transform fB = MakeFragment("Fragment B", wp + Vector3.up * 1.15f, false);
        var spring = Fountain(wp + Vector3.up * 1.1f);
        var farm = Actor(wp + Quaternion.Euler(0, 40, 0) * wdir * 1.9f, Quaternion.LookRotation(-(Quaternion.Euler(0, 40, 0) * wdir)).eulerAngles.y, SettlerLook.Age.Adult, SettlerBrain.Role.Farmer, 23);
        farm.pose = SettlerRig.Pose.Idle; farm.lookPoint = wp + Vector3.up * 1.2f;
        dir.Cut(wp + wdir * 6.5f + Vector3.up * 2.2f, wp + Vector3.up * 1.2f, 36f);
        yield return Flash(.5f, false);
        StartCoroutine(Caption("Another fell into a dry well...", 2.6f, true));
        spring.Stop();
        yield return Glide2(() => wp + Quaternion.Euler(0, 40, 0) * wdir * 5.2f + Vector3.up * 1.7f, () => wp + Vector3.up * 1.2f, 33f, 3.2f);
        spring.Play(); var sl = fB.GetComponent<FragmentGlow>(); StartCoroutine(Ease(v => sl.boost = v * 3f, 1.5f));
        farm.pose = SettlerRig.Pose.Cheer;
        yield return Caption("...and the water remembered how to rise.", 3.2f, false);

        // ---- 4. the empty hearth
        yield return Flash(.4f, true);
        Vector3 hp = HearthSpot(wp, out float hy);
        Transform fC = MakeFragment("Fragment C", hp + Vector3.up * .22f, false);
        var pot = BuildPot(hp);
        var cook = Actor(hp + Quaternion.Euler(0, hy + 40, 0) * Vector3.forward * 1.4f, hy + 40 + 180, SettlerLook.Age.Adult, SettlerBrain.Role.Gardener, 31);
        cook.pose = SettlerRig.Pose.Sweep; cook.lookPoint = hp + Vector3.up * .5f;
        var kid = Actor(hp + Quaternion.Euler(0, hy - 55, 0) * Vector3.forward * 1.5f, hy - 55 + 180, SettlerLook.Age.Child, SettlerBrain.Role.Kid, 37);
        kid.pose = SettlerRig.Pose.Idle; kid.lookPoint = hp + Vector3.up * .5f;
        dir.Cut(hp + Quaternion.Euler(0, hy, 0) * Vector3.forward * 5f + Vector3.up * 1.4f, hp + Vector3.up * .6f, 34f);
        yield return Flash(.5f, false);
        StartCoroutine(Caption("Another found an empty hearth...", 2.6f, true));
        pot.fire.Stop(); pot.steam.Stop();
        yield return Glide2(() => hp + Quaternion.Euler(0, hy, 0) * Vector3.forward * 3.1f + Vector3.up * 1.1f, () => hp + Vector3.up * .55f, 30f, 3f);
        pot.fire.Play(); pot.steam.Play(); StartCoroutine(Ease(v => { pot.light.intensity = v * 3.2f; fC.GetComponent<FragmentGlow>().boost = v * 3f; }, 1.6f));
        kid.pose = SettlerRig.Pose.Cheer;
        yield return Caption("...and the pot finally had something to simmer.", 3.2f, false);

        // ---- 5. dead soil
        yield return Flash(.4f, true);
        Transform plot = SettlerCommunity.Instance && SettlerCommunity.Instance.Plots.Count > 0 ? SettlerCommunity.Instance.Plots[0] : null;
        Vector3 pp = plot ? plot.position : wp + wdir * 12f; pp.y = TerrainProbe.GroundY(pp.x, pp.z, pp.y);
        Transform fD = MakeFragment("Fragment D", pp + Vector3.up * .55f, false);
        var sprouts = PrepareCabbages(ref plot, ref pp);
        fD.position = pp + Vector3.up * .2f;
        var sower = Actor(pp + Vector3.right * 2.1f, -90f, SettlerLook.Age.Adult, SettlerBrain.Role.Farmer, 41);
        sower.pose = SettlerRig.Pose.Sow; sower.lookPoint = pp;
        dir.Cut(pp + new Vector3(-5.5f, 1.4f, -3.5f), pp + Vector3.up * .3f, 36f);
        yield return Flash(.5f, false);
        StartCoroutine(Caption("Another settled in dead soil...", 2.6f, true));
        yield return Glide2(() => pp + new Vector3(-3.6f, .9f, -2.2f), () => pp + Vector3.up * .35f, 32f, 3f);
        StartCoroutine(GrowSprouts(sprouts, 4f)); StartCoroutine(Ease(v => fD.GetComponent<FragmentGlow>().boost = v * 3f, 1.5f));
        yield return Wait(2.6f); sower.pose = SettlerRig.Pose.Cheer;
        yield return Caption("...and the seeds believed again.", 3.4f, false);

        // ---- 6. every light in the village
        yield return Flash(.5f, true);
        var beams = new List<LineRenderer>();
        foreach (var fg in new[] { fA, fB, fC, fD }) beams.Add(Beam(fg));
        Vector3 centre = (fA.position + fB.position + fC.position + fD.position) * .25f;
        dir.Cut(centre + new Vector3(0, 9f, -42f), centre + Vector3.up * 4f, 40f);
        yield return Flash(.6f, false);
        StartCoroutine(Ease(v => { foreach (var b in beams) { b.widthMultiplier = v * .35f; var c = b.startColor; } }, 2.4f));
        yield return dir.Glide(centre + new Vector3(0, 30f, -24f), centre, 46f, 6.5f);
        yield return Caption("Every piece you lost became someone's warmth.", 4.2f, false);
        foreach (var b in beams) if (b) Destroy(b.gameObject);

        // ---- 7. back to the star and the child: the real question
        yield return Flash(.5f, true);
        foreach (var a in actors) if (a) Destroy(a.gameObject);
        actors.Clear();
        EndBreath(breath);
        Vector3 Mid() => (star.position + child.Child.transform.position) * .5f + Vector3.up * .95f;
        Vector3 Side() => Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(child.Child.transform.position - star.position, Vector3.up).normalized);
        if (child.Performer) child.Performer.Act(ChildEmotion.Awe, star);
        dir.Cut(() => Mid() + Side() * 5.4f + Vector3.up * .15f, Mid, 32f);
        yield return Flash(.7f, false);
        // slow push-in while they talk: the star on one side of the frame, the child on the other
        StartCoroutine(dir.Glide(() => Mid() + Side() * 3.9f + Vector3.up * .1f, Mid, 27f, 28f));
        yield return Say("That's what your pieces do.", ChildEmotion.Awe);
        yield return Say("The well was dry all summer. Now it sings.", ChildEmotion.Warm);
        yield return Say("Gran hadn't been warm since the first frost. Look at her.", ChildEmotion.Warm);
        yield return Say("They're not just pieces of you. They're holding us up.", ChildEmotion.Awe);
        yield return Caption("your light grows quiet", 2.2f, false);
        if (child.Performer) child.Performer.Act(ChildEmotion.Curious, star);
        yield return Say("But they're yours. And you're flickering without them.", ChildEmotion.Shy);
        yield return Say("I could help you find them. All of them. I know where they fell.", ChildEmotion.Reach);
        if (child.Performer) child.Performer.Act(ChildEmotion.Shy, star);
        yield return Say("Or leave your fragments here, and stay with us. I could tell you about me. About this place.", ChildEmotion.Shy, 3.4f);
        yield return Say("I'd like that. Either way.", ChildEmotion.Warm, 2.4f);

        // ---- the choice (Esc is not offered here: this decision stays the player's)
        CutsceneSkip.End();
        if (sprint) yield return EndSprint();
        hintA.text = "hold F   -   gather your pieces"; hintB.text = "press E   -   stay, and know me";
        StartCoroutine(Fade(hintAGroup, .9f, .6f)); yield return Fade(hintBGroup, .9f, .6f);
        float held = 0f; bool collect = false, decided = false;
        var rad = StarRadiance.Instance;
        while (running && !decided)
        {
            held = StarInput.PulseHeld ? held + Time.deltaTime : Mathf.Max(0f, held - Time.deltaTime * 1.5f);
            float k = Mathf.Clamp01(held / 1.5f);
            if (rad) rad.flare = Mathf.Max(rad.flare, k);
            hintAGroup.alpha = Mathf.Lerp(.9f, 1f, k); hintA.transform.localScale = Vector3.one * (1f + .12f * k); hintBGroup.alpha = .9f - .6f * k;
            if (k >= 1f) { collect = true; decided = true; }
            else if (held < .05f && StarInput.ReachPressed) { collect = false; decided = true; }
            yield return null;
        }
        StartCoroutine(Fade(hintAGroup, 0f, .6f)); StartCoroutine(Fade(hintBGroup, 0f, .6f));
        if (!decided) yield break;
        yield return Conclude(collect);
    }

    IEnumerator Conclude(bool collect)
    {
        StoryFlags.Set("WarmthSeen");
        if (collect)
        {
            StoryFlags.Set("ChoseGatherFragments");
            StoryFlags.Nudge(+60f, "Blessing: gather the fragments");
            if (child.Performer) child.Performer.Act(ChildEmotion.Warm, star);
            var rad = StarRadiance.Instance; if (rad) { rad.flare = 1f; rad.UpdateDerivedBucket(); }
            yield return Caption("the light remembers the way home", 3f, false);
            yield return Say("Okay. Then let's go find them.", ChildEmotion.Warm, 2.6f);
        }
        else
        {
            StoryFlags.Set("ChoseLiveAmongHumans");
            StoryFlags.Nudge(-60f, "Blessing: stay among the humans");
            if (child.Performer) child.Performer.Act(ChildEmotion.Warm, star);
            var rad = StarRadiance.Instance; if (rad) rad.UpdateDerivedBucket();
            yield return Caption("the light settles, and stays", 3f, false);
            yield return Say("Really? ...Then there's so much to show you.", ChildEmotion.Warm, 2.8f);
        }
        Finish(collect ? StarAlignment.Tether : StarAlignment.Roots, true);
    }

    void Finish(StarAlignment a, bool graceful)
    {
        running = false;
        if (sprint) { Time.timeScale = 1f; sprint = false; }
        if (sprintCover) Destroy(sprintCover);
        CutsceneSkip.End();
        if (host && host.Cottage) { var f = host.Cottage.Find("StarFragment"); if (f) f.gameObject.SetActive(true); }
        if (child) child.CutsceneControl = false;
        if (graceful && host) host.CompleteChoice(a);
        if (child && child.Child) { if (child.Performer) child.Performer.Act(ChildEmotion.None); child.TakeHand(); }
        if (dir) dir.End(2f);
        foreach (var g in props) if (g) Destroy(g);
        foreach (var a2 in actors) if (a2) Destroy(a2.gameObject);
        Destroy(captionText ? captionText.gameObject : null); Destroy(lineText ? lineText.gameObject : null);
        Destroy(hintA ? hintA.gameObject : null); Destroy(hintB ? hintB.gameObject : null);
        if (flash) Destroy(flash.transform.root.gameObject == flash.gameObject ? flash.gameObject : flash.gameObject);
        Destroy(gameObject, 2f);
    }

    // ------------------------------------------------------------------------------------ props
    Transform MakeFragment(string name, Vector3 at, bool trail)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(g.GetComponent<Collider>()); g.name = name; g.transform.position = at;
        if (bodyMat) g.GetComponent<Renderer>().sharedMaterial = bodyMat;
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lg = new GameObject("Light"); lg.transform.SetParent(g.transform, false);
        var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .72f, .38f); l.range = 7; l.intensity = 2.4f; l.shadows = LightShadows.None;
        var fg = g.AddComponent<FragmentGlow>(); fg.lamp = l;
        if (trail)
        {
            var tr = g.AddComponent<TrailRenderer>(); tr.time = 1.4f; tr.widthMultiplier = .35f; tr.minVertexDistance = .2f; if (sparkMat) tr.sharedMaterial = sparkMat;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .8f), 0), new GradientColorKey(new Color(1f, .6f, .25f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); tr.colorGradient = gr;
        }
        props.Add(g); return g.transform;
    }

    SettlerRig Actor(Vector3 at, float yaw, SettlerLook.Age age, SettlerBrain.Role role, int seed)
    {
        at = CutsceneDirector.SupportPoint(at);
        var c = SettlerCommunity.Instance; if (!c) return new GameObject("NoActor").AddComponent<SettlerRig>();
        var rig = c.SpawnActor(at, yaw, age, role, seed); actors.Add(rig); return rig;
    }

    ParticleSystem Emitter(string name, Vector3 from, Vector3 to, Color a, Color b)
    {
        var g = new GameObject(name); props.Add(g); g.transform.position = from;
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startLifetime = 1.8f; m.startSpeed = 0; m.startSize = new ParticleSystem.MinMaxCurve(.03f, .08f); m.maxParticles = 200;
        m.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var em = ps.emission; em.rateOverTime = 16;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .12f;
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; Vector3 vel = (to - from) / 1.8f; v.x = vel.x; v.y = vel.y + .1f; v.z = vel.z;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = gr;
        if (sparkMat) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;
        ps.Play(); return ps;
    }

    ParticleSystem Fountain(Vector3 at)
    {
        var g = new GameObject("WellSpring"); props.Add(g); g.transform.position = at; g.transform.rotation = Quaternion.Euler(-90, 0, 0);
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 5.4f); m.startSize = new ParticleSystem.MinMaxCurve(.05f, .13f);
        m.gravityModifier = 1.1f; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startColor = new ParticleSystem.MinMaxGradient(new Color(.7f, 1.1f, 1.6f), new Color(1.4f, 1.6f, 1.8f)); m.maxParticles = 400;
        var em = ps.emission; em.rateOverTime = 120;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9; sh.radius = .18f;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .6f), new GradientAlphaKey(0, 1) }); col.color = gr;
        if (sparkMat) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;
        ps.Play(); return ps;
    }

    static GameObject PotPart(string name, Mesh mesh, Material material, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.transform.localRotation = rotation; go.transform.localScale = scale;
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
    class PotProps { public ParticleSystem fire, steam; public Light light; }
    PotProps BuildPot(Vector3 at)
    {
        var root = new GameObject("CookingFire"); props.Add(root); root.transform.position = at;
        var stone = VillageKit.Mat(new Color(.4f, .39f, .38f)); var iron = VillageKit.Mat(new Color(.3f, .32f, .35f), default, .3f, .6f);
        for (int i = 0; i < 7; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(s.GetComponent<Collider>()); s.transform.SetParent(root.transform, false);
            float a = i / 7f * Mathf.PI * 2f; s.transform.localPosition = new Vector3(Mathf.Cos(a) * .5f, .08f, Mathf.Sin(a) * .5f); s.transform.localScale = new Vector3(.24f, .16f, .22f); s.GetComponent<Renderer>().sharedMaterial = stone;
        }
        iron = VillageKit.Mat(new Color(.30f,.32f,.35f), default, .3f, .6f);
        PotPart("Iron cooking pot", VillageKit.Ring(20,.84f), iron, root.transform, new Vector3(0,.53f,0), Quaternion.identity, new Vector3(.34f,.3f,.34f));
        PotPart("Pot rim", VillageKit.Ring(20,.84f), iron, root.transform, new Vector3(0,.81f,0), Quaternion.identity, new Vector3(.37f,.055f,.37f));
        PotPart("Soup", VillageKit.Disc(4,0,20), VillageKit.Mat(new Color(.55f,.31f,.12f)), root.transform, new Vector3(0,.78f,0), Quaternion.identity, new Vector3(.285f,1,.285f));
        void Rod(string name, Vector3 a, Vector3 b, float width, Material mat)
        {
            PotPart(name, VillageKit.ChamferBox(), mat, root.transform, (a+b)*.5f, Quaternion.FromToRotation(Vector3.up,b-a), new Vector3(width,Vector3.Distance(a,b),width));
        }
        for (int i=0;i<3;i++)
        {
            float a=i*Mathf.PI*2/3;
            Rod("Tripod leg", new Vector3(Mathf.Cos(a)*.75f,0,Mathf.Sin(a)*.75f), new Vector3(0,1.65f,0), .065f, iron);
            Rod("Firewood", new Vector3(-.35f,.12f,(i-1)*.18f),new Vector3(.35f,.12f,(i-1)*.18f),.12f,VillageKit.Mat(new Color(.25f,.13f,.07f)));
        }
        Rod("Crossbar",new Vector3(-.3f,1.55f,0),new Vector3(.3f,1.55f,0),.06f,iron);
        Rod("Hanging hook",new Vector3(0,1.55f,0),new Vector3(0,1.14f,0),.035f,iron);
        Rod("Handle left",new Vector3(-.33f,.82f,0),new Vector3(0,1.15f,0),.04f,iron);
        Rod("Handle right",new Vector3(.33f,.82f,0),new Vector3(0,1.15f,0),.04f,iron);
        var p = new PotProps();
        var lg = new GameObject("FireLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = Vector3.up * .35f;
        p.light = lg.AddComponent<Light>(); p.light.type = LightType.Point; p.light.color = new Color(1f, .6f, .28f); p.light.range = 6; p.light.intensity = 0; p.light.shadows = LightShadows.None;
        ParticleSystem Make(string n, Vector3 lp, Color c0, Color c1, float speed, float s0, float s1, float rate, Material mat)
        {
            var g = new GameObject(n); g.transform.SetParent(root.transform, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.6f); m.startSpeed = speed; m.startSize = new ParticleSystem.MinMaxCurve(s0, s1); m.gravityModifier = -.05f; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = new ParticleSystem.MinMaxGradient(c0, c1); m.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = rate; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14; sh.radius = .12f;
            var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.8f));
            if (mat) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            ps.Play(); return ps;
        }
        p.fire = Make("Flames", new Vector3(0, .12f, 0), new Color(2f, 1.1f, .4f), new Color(1.6f, .5f, .2f), .9f, .12f, .28f, 40, sparkMat);
        p.steam = Make("Steam", new Vector3(0, .95f, 0), new Color(1f, 1f, 1f, .28f), new Color(.9f, .92f, 1f, .2f), .45f, .22f, .5f, 14, smokeMat);
        return p;
    }

    Vector3 HearthSpot(Vector3 wellPos, out float yaw)
    {
        yaw = 0f; Vector3 spot = wellPos + new Vector3(7f, 0, 4f);
        var doors = HouseEntrances.Instance; var d = doors ? doors.NearestDoor(wellPos) : null;
        if (d != null) { spot = d.point + d.outward * 3.4f; yaw = Quaternion.LookRotation(-d.outward).eulerAngles.y; yaw += 0f; }
        spot.y = TerrainProbe.GroundY(spot.x, spot.z, spot.y); return spot;
    }

    FarmPlotState blessingPlot;
    readonly List<Renderer> cropRenderers = new List<Renderer>();
    List<Transform> PrepareCabbages(ref Transform plot, ref Vector3 at)
    {
        var list = new List<Transform>();
        float nearest = float.MaxValue;
        foreach (var p in FindObjectsByType<FarmPlotState>(FindObjectsSortMode.None))
        {
            float d = (p.transform.position - at).sqrMagnitude;
            if (p.crop == VillageKit.Crop.Cabbage && p.cells.Count > 0 && d < nearest) { nearest = d; blessingPlot = p; }
        }
        if (!blessingPlot) return list;
        plot = blessingPlot.transform; at = plot.position;
        blessingPlot.cinematicGrowth = true;
        foreach (var c in blessingPlot.cells) if (c.root) { list.Add(c.root); c.root.gameObject.SetActive(true); c.root.localScale = Vector3.one * .15f; }
        list.Sort((a,b) => a.localPosition.z.CompareTo(b.localPosition.z));
        foreach (var c in list) cropRenderers.AddRange(c.GetComponentsInChildren<Renderer>());
        return list;
    }

    IEnumerator GrowSprouts(List<Transform> list, float seconds)
    {
        var block = new MaterialPropertyBlock();
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i]) continue;
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - i / (float)Mathf.Max(1, list.Count) * 1.5f) / (seconds - 1.5f)));
                list[i].localScale = Vector3.one * Mathf.Lerp(.15f, 1f, k);
            }
            foreach (var r in cropRenderers) if (r)
            {
                r.GetPropertyBlock(block);
                Color green = r.sharedMaterial.HasProperty("_Color") ? r.sharedMaterial.GetColor("_Color") : new Color(.5f,.72f,.36f);
                block.SetColor("_Color", Color.Lerp(new Color(.76f,.72f,.42f), green, Mathf.Clamp01(t / seconds)));
                r.SetPropertyBlock(block);
            }
            yield return null;
        }
        foreach (var r in cropRenderers) if (r) r.SetPropertyBlock(null);
        if (blessingPlot) blessingPlot.CompleteBlessing();
    }

    LineRenderer Beam(Transform fragment)
    {
        var g = new GameObject("Beam"); g.transform.SetParent(fragment, false);
        var lr = g.AddComponent<LineRenderer>(); lr.useWorldSpace = false; lr.positionCount = 2; lr.SetPosition(0, Vector3.zero); lr.SetPosition(1, Vector3.up * 45f / Mathf.Max(.01f, fragment.lossyScale.y));
        lr.widthMultiplier = 0f; lr.numCapVertices = 4; if (sparkMat) lr.sharedMaterial = sparkMat;
        var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(new Color(1.6f, 1.2f, .7f), 0), new GradientColorKey(new Color(.8f, .9f, 1.6f), 1) }, new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(0, 1) }); lr.colorGradient = gr;
        return lr;
    }

    // breath fog while cold
    class BreathHandle { public ParticleSystem ps; public System.Func<Vector3> at; public bool on = true; }
    BreathHandle BreathLoop(System.Func<Vector3> at, float rate)
    {
        var g = new GameObject("ColdBreath"); props.Add(g);
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.startLifetime = 2f; m.startSpeed = .3f; m.startSize = new ParticleSystem.MinMaxCurve(.12f, .3f); m.startColor = new Color(.9f, .94f, 1f, .32f); m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = rate * 1.5f; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14; sh.radius = .02f;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.6f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
        if (smokeMat) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMat;
        ps.Play(); var h = new BreathHandle { ps = ps, at = at }; StartCoroutine(FollowBreath(h)); return h;
    }
    IEnumerator FollowBreath(BreathHandle h) { while (h.on && h.ps) { h.ps.transform.position = h.at(); yield return null; } }
    void EndBreath(BreathHandle h) { if (h == null) return; h.on = false; if (h.ps) { h.ps.Stop(); Destroy(h.ps.gameObject, 2.5f); } }

    // ------------------------------------------------------------------------------------ choreography helpers
    IEnumerator Fall(Transform t, Vector3 from, Vector3 to, float seconds)
    {
        for (float k = 0; k < seconds; k += Time.deltaTime)
        {
            float e = k / seconds; e = e * e * (3f - 2f * e) * .6f + e * .4f;
            t.position = Vector3.Lerp(from, to, e) + Vector3.Cross(Vector3.up, to - from).normalized * Mathf.Sin(e * Mathf.PI) * 1.5f;
            yield return null;
        }
        t.position = to;
    }

    IEnumerator Ease(System.Action<float> apply, float seconds)
    {
        for (float t = 0; t < seconds; t += Time.deltaTime) { apply(Mathf.SmoothStep(0f, 1f, t / seconds)); yield return null; }
        apply(1f);
    }

    IEnumerator Glide2(System.Func<Vector3> p, System.Func<Vector3> l, float fov, float seconds) { return dir.Glide(p, l, fov, seconds); }
    IEnumerator Wait(float s) { for (float t = 0; t < s && running; t += Time.deltaTime) yield return null; }

    IEnumerator Caption(string s, float seconds, bool nonBlocking)
    {
        captionText.text = s;
        if (nonBlocking) { StartCoroutine(CaptionRoutine(seconds)); yield break; }
        yield return CaptionRoutine(seconds);
    }
    IEnumerator CaptionRoutine(float seconds)
    {
        yield return Fade(captionGroup, 1f, .7f); yield return Wait(Mathf.Max(0f, seconds - 1.4f)); yield return Fade(captionGroup, 0f, .7f);
    }

    IEnumerator Say(string s, ChildEmotion e, float hold = -1f)
    {
        if (child && child.Performer) child.Performer.emotion = e;
        lineText.text = ""; lineGroup.alpha = 1f;
        for (int i = 1; i <= s.Length && running && !sprint; i++)
        {
            lineText.text = s.Substring(0, i); char c = s[i - 1];
            yield return new WaitForSeconds(c == '.' || c == '?' || c == '!' ? .22f : c == ',' ? .12f : .028f);
        }
        if (hold < 0f) hold = Mathf.Clamp(.9f + s.Length * .024f, 1.5f, 3.8f);
        yield return Wait(hold); yield return Fade(lineGroup, 0f, .3f); yield return Wait(.15f);
    }

    static IEnumerator Fade(CanvasGroup g, float to, float duration)
    {
        if (!g) yield break; float from = g.alpha;
        for (float t = 0; t < duration; t += Time.deltaTime) { if (!g) yield break; g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)); yield return null; }
        if (g) g.alpha = to;
    }

    void BuildFlash()
    {
        var cv = new GameObject("BlessingFlash"); cv.transform.SetParent(transform, false);
        var c = cv.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 200;
        var g = new GameObject("White", typeof(RectTransform), typeof(Image)); g.transform.SetParent(cv.transform, false);
        var r = (RectTransform)g.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        flash = g.GetComponent<Image>(); flash.color = new Color(1f, .93f, .78f, 0f); flash.raycastTarget = false;
    }
    /// <summary>A warm white dissolve between vignettes: in = rises to full white, out = clears back to the new shot.</summary>
    IEnumerator Flash(float seconds, bool rising)
    {
        float from = rising ? 0f : 1f, to = rising ? 1f : 0f;
        for (float t = 0; t < seconds; t += Time.deltaTime) { var c = flash.color; c.a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)); flash.color = c; yield return null; }
        var f = flash.color; f.a = to; flash.color = f;
    }
}
