using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The first conversation with the child, replacing the bare "press H to hold hands" prompt.
/// The star cannot speak - it answers in light - so the exchange is short, quiet and physical:
///
///   A  the child steps out of hiding and says what they saw
///   B  "Are you okay?"          hold F (Starlight Pulse) to shine back, or stay silent - both are heard
///   C  "You're flickering..."   the star's missing-piece flicker is shown, and the child plants the idea of
///                               the mountain (the first pointer toward the fragment quests)
///   D  "Can I come with you?"   press E (Reach) to offer a hand; if you don't, they walk beside you anyway
///
/// While it runs: the star is slowed near the child (IslandThirdPersonController.speedCap), the camera eases
/// into the IntimateCompanion profile, and walking away pauses the scene ("Wait... don't go.") instead of
/// breaking it. H still works as a debug skip (ChildEncounter calls Cancel()).
/// Lines are first-draft placeholders - rewrite freely, the flow is what matters.
/// Story flags set: ChildHeardStar (the player shone back), ChildTookHand (the player reached out).
/// </summary>
public class ChildConversation : MonoBehaviour
{
    public static ChildConversation Instance { get; private set; }

    [Tooltip("If the star gets further than this from the child the scene pauses until it comes back.")]
    public float leaveDistance = 10f;
    [Tooltip("Speed multiplier applied to the star while it is with the child (1 = no slowdown).")]
    [Range(.2f, 1f)] public float slowTo = .55f;

    ChildEncounter ce; Transform star;
    IslandThirdPersonController controller; StarRadiance rad;
    Text lineText, hintText; CanvasGroup lineGroup, hintGroup;
    bool aborted, finished, answering, gotAnswer, warnedLeave;
    float cap = 1f, myFlare;

    public static void Run(ChildEncounter encounter, Transform star)
    {
        if (Instance || !encounter) return;
        var go = new GameObject("ChildConversation");
        var c = go.AddComponent<ChildConversation>();
        c.ce = encounter; c.star = star;
    }

    /// <summary>Stop immediately (debug skip, or the hand was taken some other way).</summary>
    public static void Cancel() { if (Instance && !Instance.finished) Instance.Teardown(); }

    void Awake() { Instance = this; }

    void Start()
    {
        controller = star ? star.GetComponent<IslandThirdPersonController>() : null;
        rad = StarRadiance.Instance;
        if (ce && ce.Child) ce.Child.playful = false;
        lineText = WishboundUI.Label(null, "ChildLine", "", 52, new Vector2(.5f, .24f), new Vector2(1500, 220));
        lineGroup = lineText.GetComponent<CanvasGroup>();
        hintText = WishboundUI.Label(null, "ChildHint", "", 34, new Vector2(.5f, .13f), new Vector2(1100, 100));
        hintGroup = hintText.GetComponent<CanvasGroup>();
        if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(IsoCameraRig.Profile.IntimateCompanion);
        StartCoroutine(Sequence());
    }

    void Update()
    {
        float dt = Time.deltaTime;
        cap = Mathf.MoveTowards(cap, aborted || finished ? 1f : slowTo, dt * .8f);
        if (controller) controller.speedCap = cap;
        if (answering && StarInput.PulseHeld) myFlare = Mathf.Max(myFlare, .6f);   // holding F visibly feeds the light
        if (myFlare > 0f)
        {
            myFlare = Mathf.MoveTowards(myFlare, 0f, dt * .8f);
            if (rad) rad.flare = Mathf.Max(rad.flare, myFlare);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (controller) controller.speedCap = 1f;
    }

    // ============================================================================================ the scene
    IEnumerator Sequence()
    {
        // A - first sight
        yield return Say("...", 1.6f);
        yield return Say("You're the light that fell.");
        yield return Say("It was so loud. Everybody ran inside.");
        yield return Say("I didn't. I wanted to see.");

        // B - "are you okay?" - the star answers in light, or doesn't
        yield return Say("Are you... okay?");
        float held = 0f; answering = true;
        yield return Prompt("hold F  -  shine", dt => { held = StarInput.PulseHeld ? held + dt : 0f; return held > .8f; }, 9f);
        answering = false;
        if (gotAnswer) { myFlare = 1f; StoryFlags.Set("ChildHeardStar"); yield return Say("Oh. You're warm."); }
        else { myFlare = .5f; yield return Say("It's okay. You don't have to talk."); }

        // C - the missing piece, and the mountain
        StartCoroutine(MissingPieceFlicker());
        yield return Say("You're flickering. Like something's missing.");
        yield return Say("Gran says wishes fall in pieces.");
        yield return Say("Some landed in the village. And some...\nwent up. To the top of the mountain.");

        // D - the hand
        yield return Say("I know the way up. Can I come with you?");
        yield return Prompt("press E  -  reach out", dt => StarInput.ReachPressed, 12f);
        if (gotAnswer) { myFlare = 1f; StoryFlags.Set("ChildTookHand"); yield return Say("...okay."); }
        else yield return Say("I'll just... walk next to you.");

        finished = true;
        if (ce) ce.TakeHand();
        Teardown();
    }

    IEnumerator MissingPieceFlicker()
    {
        if (!rad) yield break;
        float from = rad.missingPiece;
        for (float t = 0; t < 2.8f && !aborted; t += Time.deltaTime)
        {
            if (!rad) yield break;
            rad.missingPiece = Mathf.Max(from, Mathf.Sin(t / 2.8f * Mathf.PI) * .8f);
            yield return null;
        }
        if (rad) rad.missingPiece = from;
    }

    // ============================================================================================ helpers
    float Dist()
    {
        if (!star || !ce || !ce.Child) return 0f;
        Vector3 a = star.position, b = ce.Child.transform.position; a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>One line of the child's speech; waits for the star to be close enough first.</summary>
    IEnumerator Say(string s, float hold = -1f)
    {
        float waited = 0f;
        while (!aborted && Dist() > leaveDistance)
        {
            waited += Time.deltaTime;
            if (waited > 1.5f && !warnedLeave) { warnedLeave = true; yield return SayRaw("Wait... don't go.", 2.2f); }
            yield return null;
        }
        if (waited > 0f) warnedLeave = false;
        yield return SayRaw(s, hold);
    }

    IEnumerator SayRaw(string s, float hold)
    {
        if (aborted || !lineText) yield break;
        lineText.text = s;
        if (hold < 0f) hold = Mathf.Clamp(1.5f + s.Length * .05f, 2.2f, 6f);
        yield return Fade(lineGroup, 1f, .35f);
        yield return Wait(hold);
        yield return Fade(lineGroup, 0f, .3f);
        yield return Wait(.2f);
    }

    /// <summary>Shows a small scribbled hint and waits until check() says the player answered, or the timeout passes.</summary>
    IEnumerator Prompt(string hint, System.Func<float, bool> check, float timeout)
    {
        gotAnswer = false;
        if (aborted || !hintText) yield break;
        hintText.text = hint;
        yield return Fade(hintGroup, .85f, .5f);
        for (float t = 0; t < timeout && !aborted; t += Time.deltaTime)
        {
            if (check(Time.deltaTime)) { gotAnswer = true; break; }
            yield return null;
        }
        yield return Fade(hintGroup, 0f, .35f);
    }

    IEnumerator Wait(float seconds) { for (float t = 0; t < seconds && !aborted; t += Time.deltaTime) yield return null; }

    static IEnumerator Fade(CanvasGroup g, float to, float duration)
    {
        if (!g) yield break;
        float from = g.alpha;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            if (!g) yield break;
            g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        if (g) g.alpha = to;
    }

    void Teardown()
    {
        aborted = true;
        if (IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(null);
        if (lineText) Destroy(lineText.gameObject);
        if (hintText) Destroy(hintText.gameObject);
        Destroy(gameObject);
    }
}
