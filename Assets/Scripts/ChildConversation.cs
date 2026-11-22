using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First meeting: fade to a verified dry clearing, a hesitant entrance, then dialogue and the shine/hand beats.
/// CutsceneDirector owns the camera and input; the original dialogue and story flags are preserved.
/// </summary>
public class ChildConversation : MonoBehaviour
{
    public static ChildConversation Instance { get; private set; }
    [Tooltip("Seconds F must be held for the star to bloom.")] public float shineSeconds = 2.2f;
    [Tooltip("How close (metres) the child ends up standing next to the star after accepting, before control passes back to ProceduralFollow.")] public float holdApproachDistance = 2.0f;
    [Tooltip("Walking pace for the final approach to the star's side.")] public float holdWalkSpeed = 1.1f;
    [Tooltip("How far (m) in front of the trunk's surface the child stops to talk.")] public float standDistance = 2.4f;
    [Tooltip("Gap (m) between the child and the star during the conversation.")] public float talkGap = 3.4f;

    enum Shot { ChildCU, StarCU, Two, Wide }

    ChildEncounter ce; Transform star; StarRadiance rad; CharacterController childCC, starCC; CutsceneDirector dir; ChildPerformer act;
    Text lineText, hintText, captionText; CanvasGroup lineGroup, hintGroup, captionGroup;
    Light glow; Transform glowT;
    bool aborted, finished, gotAnswer; float held, bellFlare;

    // staging - all derived in BuildStage() from the actual tree / child / star
    Vector3 trunk, hide, peekEdge, stand, starSpot, toStar, R;
    float trunkR, sgn = 1f;
    Transform ChildT => ce.Child.transform;
    Vector3 Head() => ChildT.position + Vector3.up * .9f;
    Transform starBody;
    Vector3 StarCenter() => starBody ? starBody.position : star.position + Vector3.up * .85f;
    CanvasGroup curtain;

    public static void Run(ChildEncounter encounter, Transform star)
    {
        if (Instance || !encounter) return;
        var go = new GameObject("ChildConversation");
        var c = go.AddComponent<ChildConversation>(); c.ce = encounter; c.star = star;
    }
    public static void Cancel() { if (Instance && !Instance.finished) Instance.Teardown(); }

    void Awake() { Instance = this; }
    void OnDestroy() { if (!tornDown && dir) dir.End(0f); if (curtain) Destroy(curtain.gameObject); if (ce) ce.CutsceneControl = false; if (Instance == this) Instance = null; }

    void Start()
    {
        rad = StarRadiance.Instance; starCC = star.GetComponent<CharacterController>();
        foreach (var r in star.GetComponentsInChildren<MeshRenderer>()) if (r.name == "AmberStarBody") { starBody = r.transform; break; }
        childCC = ce.Child.GetComponent<CharacterController>(); act = ce.Performer;
        ce.CutsceneControl = true; ce.Child.playful = false;
        lineText = WishboundUI.Label(null, "ChildLine", "", 52, new Vector2(.5f, .2f), new Vector2(1500, 220)); lineGroup = lineText.GetComponent<CanvasGroup>(); lineGroup.alpha = 0;
        hintText = WishboundUI.Label(null, "ChildHint", "", 34, new Vector2(.5f, .115f), new Vector2(1100, 100)); hintGroup = hintText.GetComponent<CanvasGroup>(); hintGroup.alpha = 0;
        captionText = WishboundUI.Label(null, "StarCaption", "", 30, new Vector2(.5f, .31f), new Vector2(1100, 80)); captionGroup = captionText.GetComponent<CanvasGroup>(); captionGroup.alpha = 0;
        captionText.color = new Color(1f, .84f, .5f);
        glowT = new GameObject("StarShineLight").transform; glowT.SetParent(star, false); glowT.localPosition = Vector3.up * .3f;
        glow = glowT.gameObject.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(1f, .78f, .42f); glow.range = 8; glow.intensity = 0; glow.shadows = LightShadows.None;
        StartCoroutine(Sequence());
    }

    void Update()
    {
        // an eased bell flare (used for trembles / blooms) on top of whatever the hold is doing
        if (bellFlare > 0f) { bellFlare = Mathf.MoveTowards(bellFlare, 0f, Time.deltaTime * .9f); if (rad) rad.flare = Mathf.Max(rad.flare, bellFlare); }
    }

    // ============================================================================================ the scene
    IEnumerator Sequence()
    {
        dir = CutsceneDirector.Begin();
        if (!CutsceneDirector.Active) { Teardown(); yield break; }
        dir.handheld = .01f;
        CutsceneSkip.Begin(SkipToFollow);

        // Hide the relocation, then establish both actors on a checked dry clearing.
        var cover = new GameObject("Meeting transition", typeof(Canvas), typeof(CanvasGroup));
        cover.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        cover.GetComponent<Canvas>().sortingOrder = 250;
        curtain = cover.GetComponent<CanvasGroup>(); curtain.alpha = 0;
        var panel = new GameObject("Black", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(cover.transform, false);
        var rect = (RectTransform)panel.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = Color.black;
        yield return Fade(curtain, 1, .45f);
        if (!BuildStage())
        {
            Debug.LogError("[ChildConversation] No safe meeting clearing found; encounter deferred.");
            yield return Fade(curtain, 0, .3f); Teardown(); yield break;
        }
        ClearStage(hide, peekEdge, stand, starSpot);
        SetStar(starSpot);
        SetChild(hide);
        ChildT.rotation = Quaternion.LookRotation(Flat(starSpot - hide));
        if (act) { act.lookTarget = star; act.emotion = ChildEmotion.Shy; }
        var establish = (hide + stand + starSpot) / 3f + Vector3.up * .65f;
        dir.Cut(ClearShot(establish + R * 7f + toStar * 2f + Vector3.up * 2.2f, establish), establish, 42f);
        yield return null;
        yield return Fade(curtain, 0, .7f);
        yield return Wait(.6f);
        // The entrance stays inside the validated corridor: step forward, hesitate, then approach.
        yield return ChildWalk(peekEdge, .65f);
        yield return ChildFace(starSpot, .4f);
        yield return Say("...", ChildEmotion.Peek, .8f);
        yield return Wait(.4f);
        yield return ChildWalk(stand, .85f);
        yield return ChildFace(starSpot, .5f);
        if (act) act.Act(ChildEmotion.Shy, star);

        // 3 - first words
        yield return Cam(Shot.ChildCU, 1.5f);
        yield return Say("You didn't hurt anyone... did you?", ChildEmotion.Shy);
        yield return Say("Everybody ran when you fell. The whole sky was screaming.", ChildEmotion.Shy);
        yield return Cam(Shot.Two, 1.4f);
        yield return Say("I ran too. Then I stopped. Then I came back.", ChildEmotion.Curious);
        yield return Say("I don't know why. I think... I wanted to see who was crying.", ChildEmotion.Curious);
        yield return Caption("your light trembles", 2.2f, .5f);
        yield return Say("You were. Just a little. Your light kept shaking.", ChildEmotion.Warm);

        // 4 - shine
        yield return Cam(Shot.StarCU, 1.6f);
        yield return Say("Are you hurt?", ChildEmotion.Curious);
        yield return Say("If you're okay... shine for me. Just once. I'll know.", ChildEmotion.Shy, 3.2f);
        yield return ShineBeat();
        if (gotAnswer)
        {
            StoryFlags.Set("ChildHeardStar");
            yield return Cam(Shot.ChildCU, 1.4f);
            act.Act(ChildEmotion.Awe, star);
            yield return Say("Oh...", ChildEmotion.Awe, 1.8f);
            yield return Say("You're warm. Like the first morning after a long winter.", ChildEmotion.Awe);
        }
        else
        {
            yield return Cam(Shot.ChildCU, 1.4f);
            yield return Say("That's okay. You don't have to.", ChildEmotion.Warm);
            yield return Say("I'm quiet a lot too. Quiet still counts.", ChildEmotion.Warm);
        }

        // 5 - the missing piece, the mountain, the question
        yield return Cam(Shot.Two, 1.6f);
        StartCoroutine(Tremble());
        yield return Say("You're flickering. There... right there. Like a hole where something should be.", ChildEmotion.Curious);
        yield return Say("Gran says wishes never fall whole. They break on the way down.", ChildEmotion.Curious);
        yield return Say("Some of yours landed in the village. I've seen them. Little lights, in the strangest places.", ChildEmotion.Warm);
        yield return Say("And the biggest piece went up... to the top of the mountain.", ChildEmotion.Awe);
        yield return Cam(Shot.ChildCU, 1.4f);
        yield return Say("I know every path on this island. Even the secret ones.", ChildEmotion.Shy);
        yield return Say("Nobody ever asks me to come along.", ChildEmotion.Shy, 2.6f);
        yield return Say("Can I... can I come with you?", ChildEmotion.Reach, 2.6f);
        yield return HandBeat();
        if (gotAnswer)
        {
            StoryFlags.Set("ChildTookHand");
            yield return Cam(Shot.Two, 1.2f);
            bellFlare = Mathf.Max(bellFlare, .7f); act.Act(ChildEmotion.Warm, star);
            yield return Say("...okay.", ChildEmotion.Warm, 1.8f);
            yield return Say("I'll stay close. I promise.", ChildEmotion.Warm, 2.4f);
        }
        else yield return Say("Okay. I'll just... walk next to you.", ChildEmotion.Shy);

        // Walk over to the star's side explicitly, in character, before handing control back to ProceduralFollow -
        // avoids the old behaviour of re-enabling Follow cold and having it snap/SmoothDamp a big catch-up burst.
        Vector3 approachSpot = FindLand(star.position + Flat(ChildT.position - star.position).normalized * holdApproachDistance);
        yield return ChildWalk(approachSpot, holdWalkSpeed);
        yield return ChildFace(star.position, .4f);

        finished = true;
        act.Act(ChildEmotion.None, star);
        Teardown(true);
    }

    // ============================================================================================ staging
    // Validate the whole entrance and conversation corridor before moving either actor.
    bool BuildStage()
    {
        trunk = ce.Trunk; trunkR = Mathf.Max(.3f, ce.TrunkRadius);
        Vector3 front = Flat(star.position - trunk).normalized;
        if (front.sqrMagnitude < .1f) front = Vector3.forward;
        // Search nearby first, expanding inland only when the original tree is wet or obstructed.
        for (int ring = 0; ring <= 20; ring++)
        for (int sector = 0; sector < (ring == 0 ? 1 : 24); sector++)
        {
            float angle = sector * Mathf.PI / 12f;
            Vector3 candidate = Ground(trunk + front * (trunkR + 3f) + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ring * 3f);
            for (int facing = 0; facing < 8; facing++)
            {
                Vector3 f = Quaternion.Euler(0, facing * 45, 0) * front;
                Vector3 side = Vector3.Cross(Vector3.up, f);
                Vector3 entry = Ground(candidate - f * 2.2f + side * 1.1f);
                Vector3 stop = Ground(candidate + f * talkGap);
                if (!RouteClear(entry, candidate) || !RouteClear(candidate, stop)) continue;
                // canopies have no collider, so keep the whole stage clear of tree trunks (else foliage fills the shot)
                if (TreeNear(candidate, 7f) || TreeNear(stop, 6f) || TreeNear(entry, 4f)) continue;
                // Leave space for the side-on camera and keep every edge of the acting area dry.
                if (!Walkable(Ground(candidate + side * 2f)) || !Walkable(Ground(candidate - side * 2f))) continue;
                stand = candidate; hide = entry; peekEdge = Ground(Vector3.Lerp(entry, candidate, .45f));
                toStar = f; R = side; sgn = 1f; starSpot = stop + Vector3.up * .6f;
                return true;
            }
        }
        return false;
    }

    bool RouteClear(Vector3 a, Vector3 b)
    {
        float previousY = a.y;
        int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / .35f);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Ground(Vector3.Lerp(a, b, steps == 0 ? 0 : (float)i / steps));
            if (!Walkable(p) || Mathf.Abs(p.y - previousY) > .18f) return false;
            previousY = p.y;
        }
        return true;
    }

static readonly Collider[] treeBuf = new Collider[16];
    /// <summary>True if a tree trunk collider lies within r metres (canopies have no collider, so trunks stand in for them).</summary>
    static bool TreeNear(Vector3 p, float r)
    {
        int n = Physics.OverlapSphereNonAlloc(p, r, treeBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (treeBuf[i].gameObject.name == "TreeColliders") return true;
        return false;
    }


    void SetChild(Vector3 p)
    {
        bool enabledBefore = childCC && childCC.enabled;
        if (childCC) childCC.enabled = false;
        ChildT.position = p + Vector3.up * .03f;
        if (childCC) childCC.enabled = enabledBefore;
    }

    /// <summary>Firm, dry, unobstructed ground (the child and the star don't count as obstructions).</summary>
    bool Walkable(Vector3 p)
    {
        if (!TerrainProbe.TryGetGroundY(p.x, p.z, out float y) || y < TerrainProbe.SeaLevel + 1.0f) return false;
        Vector3 g = new Vector3(p.x, y + .05f, p.z);
        if (Surface.WetSuppression(p.x, TerrainMath.MirrorZ ? -p.z : p.z) < .9) return false;
        if (WaterProbe.Near(g, 6f, 3.5f)) return false;
        foreach (var h in Physics.OverlapCapsule(g + Vector3.up * .45f, g + Vector3.up * 1.3f, .3f, ~0, QueryTriggerInteraction.Ignore))
        {
            var rt = h.transform.root; if (rt == ChildT.root || rt == star.root) continue;
            return false;
        }
        return true;
    }

    /// <summary>Strips grass and flowers along the child's route so a 1.2 m child is never swallowed by the meadow.</summary>
    void ClearStage(params Vector3[] path)
    {
        const float radius = 2.5f; float r2 = radius * radius;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 a = path[i], b = i + 1 < path.Length ? path[i + 1] : a;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b)));
            for (int j = 0; j <= steps; j++) GrassExclusions.Add(Vector3.Lerp(a, b, (float)j / steps), radius);
        }
        bool Inside(Vector3 p)
        {
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 a = path[i], b = i + 1 < path.Length ? path[i + 1] : a;
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
                float t = ab.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                if ((ap - ab * t).sqrMagnitude < r2) return true;
            }
            return false;
        }
        var grass = FindFirstObjectByType<IslandStylizedGrass>(); if (grass) grass.ClearWhere(Inside);
        var flowers = FindFirstObjectByType<IslandStylizedFlowers>(); if (flowers) flowers.ClearWhere(Inside);
    }

    // ============================================================================================ beats
    IEnumerator ShineBeat()
    {
        gotAnswer = false; held = 0f;
        if (aborted || !hintText) yield break;
        hintText.text = "hold F   -   let your light show";
        yield return Fade(hintGroup, .9f, .5f);
        System.Func<Vector3> p = () => StarCenter() - toStar * 3.3f - R * sgn * 2f + Vector3.up * .3f;
        System.Func<Vector3> l = StarCenter;
        dir.Cut(p, l, 36f);
        for (float t = 0; t < 16f && !aborted; t += Time.deltaTime)
        {
            held = StarInput.PulseHeld ? held + Time.deltaTime : Mathf.Max(0f, held - Time.deltaTime * .8f);
            float k = Mathf.SmoothStep(0f, 1f, held / shineSeconds);
            Brighten(k);
            dir.Cut(p, l, Mathf.Lerp(36f, 30f, k));                                           // leave room for the blooming halo
            hintGroup.alpha = Mathf.Lerp(.9f, .35f + .25f * Mathf.Sin(Time.time * 6f), k);
            if (held >= shineSeconds) { gotAnswer = true; break; }
            yield return null;
        }
        if (gotAnswer) { yield return Bloom(); }
        else { for (float t = 0; t < .8f; t += Time.deltaTime) { Brighten(Mathf.Lerp(held / shineSeconds, 0f, t / .8f)); yield return null; } Brighten(0f); }
        yield return Fade(hintGroup, 0f, .35f);
    }

    /// <summary>The star's light, driven 0..1: the real orb flare, a warm point light that also falls on the child.</summary>
    void Brighten(float k)
    {
        if (rad) rad.flare = Mathf.Max(rad.flare, k);
        if (glow) { glow.intensity = k * 14f; glow.range = 7f + k * 9f; glow.color = Color.Lerp(new Color(1f, .8f, .5f), new Color(1f, .93f, .75f), k); }
    }

    IEnumerator Bloom()
    {
        SparkBurst(star.position + Vector3.up * .2f, 90);
        for (float t = 0; t < 1.4f && !aborted; t += Time.deltaTime)
        {
            float k = t / 1.4f, bell = Mathf.Sin(Mathf.Clamp01(k * 1.1f) * Mathf.PI * .5f) * (1f - Mathf.SmoothStep(.55f, 1f, k));
            Brighten(Mathf.Max(bell, 1f - k)); if (rad) rad.flare = Mathf.Max(rad.flare, 1f - k * .7f);
            yield return null;
        }
        float from = glow ? glow.intensity : 0f;
        for (float t = 0; t < 1.2f; t += Time.deltaTime) { if (glow) glow.intensity = Mathf.Lerp(from, 3f, t / 1.2f); yield return null; }   // settles to a gentle warm glow on the child
    }

    IEnumerator HandBeat()
    {
        gotAnswer = false;
        if (aborted || !hintText) yield break;
        hintText.text = "press E   -   reach out";
        yield return Fade(hintGroup, .9f, .5f);
        act.Act(ChildEmotion.Reach, star);
        for (float t = 0; t < 12f && !aborted; t += Time.deltaTime) { if (StarInput.ReachPressed) { gotAnswer = true; break; } yield return null; }
        yield return Fade(hintGroup, 0f, .35f);
    }

    IEnumerator Tremble()
    {
        for (float t = 0; t < 2.8f && !aborted; t += Time.deltaTime)
        {
            if (!rad) yield break;
            rad.missingPiece = Mathf.Max(rad.missingPiece, Mathf.Sin(t / 2.8f * Mathf.PI) * .8f);
            yield return null;
        }
    }

    // ============================================================================================ movement + camera
    void SetStar(Vector3 p) { if (starCC) starCC.enabled = false; star.position = p; if (starCC) starCC.enabled = true; }

    IEnumerator ChildWalk(Vector3 to, float speed)
    {
        // Cinematic motion follows the prevalidated terrain corridor. Physics resumes at handover.
        Vector3 from = ChildT.position;
        float duration = Mathf.Max(.1f, Flat(to - from).magnitude / Mathf.Max(.1f, speed));
        for (float t = 0; t < duration && !aborted; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            // Smooth acceleration/deceleration without overshooting the mark.
            float eased = k * k * (3f - 2f * k);
            Vector3 p = Ground(Vector3.Lerp(from, to, eased));
            Vector3 d = Flat(to - from);
            SetChild(p);
            if (d.sqrMagnitude > .001f) ChildT.rotation = Quaternion.RotateTowards(ChildT.rotation, Quaternion.LookRotation(d), 150f * Time.deltaTime);
            yield return null;
        }
        if (!aborted) SetChild(Ground(to));
    }
    IEnumerator ChildFace(Vector3 point, float seconds)
    {
        Vector3 d = Flat(point - ChildT.position); if (d.sqrMagnitude < .001f) yield break;
        Quaternion goal = Quaternion.LookRotation(d);
        for (float t = 0; t < seconds && !aborted; t += Time.deltaTime) { ChildT.rotation = Quaternion.RotateTowards(ChildT.rotation, goal, 200f * Time.deltaTime); yield return null; }
    }

    IEnumerator Cam(Shot s, float seconds)
    {
        // Shots are composed from the side so the star's bloom never covers the child and the camera never sits inside the trunk.
        // Each position is checked once (inside a collider / blocked view -> swing round the subject) and then glides there.
        Vector3 c = Head(), st = StarCenter(), m = (c + st) * .5f;
        Vector3 pos, look; float fov;
        switch (s)
        {
            case Shot.ChildCU: pos = ChildT.position + toStar * 2.7f + R * sgn * 1.8f + Vector3.up * 1.15f; look = c; fov = 30f; break;
            case Shot.StarCU:  pos = starSpot - toStar * 2.3f - R * sgn * 1.9f + Vector3.up * .8f; look = st; fov = 27f; break;
            case Shot.Wide:    pos = m + R * sgn * 8f + toStar * 3f + Vector3.up * 3f; look = m; fov = 34f; break;
            default:           pos = m + R * sgn * 6.5f + Vector3.up * 1.1f; look = m; fov = 40f; break;
        }
        pos = ClearShot(pos, look);
        return dir.Glide(pos, look, fov, seconds);
    }

    Vector3 ClearShot(Vector3 pos, Vector3 focus)
    {
        for (int i = 0; i < 11; i++)
        {
            float ang = i == 0 ? 0f : ((i + 1) / 2) * 16f * (i % 2 == 1 ? 1f : -1f);
            Vector3 p = focus + Quaternion.Euler(0, ang, 0) * (pos - focus);
            for (int lift = 0; lift < 3; lift++)
            {
                Vector3 q = p + Vector3.up * (lift * .5f);
                if (Physics.CheckSphere(q, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (ViewBlocked(q, focus)) continue;
                return q;
            }
        }
        return pos;
    }

    bool ViewBlocked(Vector3 a, Vector3 b)
    {
        for (float d = 0f; d < Vector3.Distance(a, b) - 1.2f; d += 1f) if (TreeNear(Vector3.Lerp(a, b, d / Vector3.Distance(a, b)), 2.8f)) return true;
        var hits = Physics.RaycastAll(a, b - a, Mathf.Max(.1f, Vector3.Distance(a, b) - .5f), ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits) { var r = h.collider.transform.root; if (r == star.root || r == ChildT.root) continue; return true; }
        return false;
    }

    // ============================================================================================ text
    IEnumerator Say(string s, ChildEmotion e = ChildEmotion.None, float hold = -1f)
    {
        if (aborted || !lineText) yield break;
        if (act && e != ChildEmotion.None) act.emotion = e;
        lineText.text = ""; lineGroup.alpha = 1f;
        for (int i = 1; i <= s.Length && !aborted; i++)
        {
            lineText.text = s.Substring(0, i);
            char c = s[i - 1];
            yield return new WaitForSeconds(c == '.' || c == '?' || c == '!' ? .22f : c == ',' ? .12f : .028f);
        }
        if (hold < 0f) hold = Mathf.Clamp(.9f + s.Length * .022f, 1.4f, 3.6f);
        yield return Wait(hold);
        yield return Fade(lineGroup, 0f, .3f);
        yield return Wait(.15f);
    }

    IEnumerator Caption(string s, float seconds, float flare)
    {
        if (aborted || !captionText) yield break;
        captionText.text = s; bellFlare = Mathf.Max(bellFlare, flare);
        yield return Fade(captionGroup, .9f, .5f);
        yield return Wait(seconds);
        yield return Fade(captionGroup, 0f, .5f);
    }

    IEnumerator Wait(float seconds) { for (float t = 0; t < seconds && !aborted; t += Time.deltaTime) yield return null; }

    static IEnumerator Fade(CanvasGroup g, float to, float duration)
    {
        if (!g) yield break;
        float from = g.alpha;
        for (float t = 0; t < duration; t += Time.deltaTime) { if (!g) yield break; g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)); yield return null; }
        if (g) g.alpha = to;
    }

    // ============================================================================================ helpers
    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    static Vector3 Ground(Vector3 p) { p.y = TerrainProbe.GroundY(p.x, p.z, p.y); return p; }
    static Vector3 FindLand(Vector3 p) => Ground(p);

    void SparkBurst(Vector3 at, int count)
    {
        var src = FindFirstObjectByType<GuideWisp>(); var g = new GameObject("ShineBloom"); g.transform.position = at;
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(.05f, .16f); m.gravityModifier = -.05f; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(2f, 1.7f, 1f), new Color(2f, 1.2f, .6f));
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .25f;
        var col = ps.colorOverLifetime; col.enabled = true; var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
        if (src && src.sparkMaterial) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = src.sparkMaterial;
        ps.Play(); Destroy(g, 4f);
    }

    bool tornDown;
    /// <summary>Esc during the meeting: same outcome as the full scene - the child heard the star, took its hand and now follows it.</summary>
    void SkipToFollow()
    {
        if (tornDown) return;
        StoryFlags.Set("ChildHeardStar"); StoryFlags.Set("ChildTookHand");
        finished = true;
        if (act) act.Act(ChildEmotion.None, star);
        Teardown(true);
    }
    void Teardown(bool graceful = false)
    {
        if (tornDown) return; tornDown = true;
        CutsceneSkip.End();
        aborted = true;
        StopAllCoroutines();
        if (ce) { ce.CutsceneControl = false; ce.EndTalking(); if (graceful) ce.TakeHand(); }
        if (rad) rad.missingPiece = 0f;
        if (dir) dir.End(graceful ? 1.8f : .6f);
        if (curtain) Destroy(curtain.gameObject);
        if (lineText) Destroy(lineText.gameObject);
        if (hintText) Destroy(hintText.gameObject);
        if (captionText) Destroy(captionText.gameObject);
        if (glowT) StartCoroutine(FadeGlowThenDie()); else Destroy(gameObject);
    }

    IEnumerator FadeGlowThenDie()
    {
        // keep this object alive just long enough for the star's light to settle back, then go
        float from = glow ? glow.intensity : 0f;
        for (float t = 0; t < 2f; t += Time.deltaTime) { if (glow) glow.intensity = Mathf.Lerp(from, 0f, t / 2f); yield return null; }
        if (glowT) Destroy(glowT.gameObject);
        Destroy(gameObject);
    }
}
