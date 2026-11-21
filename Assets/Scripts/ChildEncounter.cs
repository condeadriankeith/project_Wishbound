using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// First meeting with the child. The child isn't with the star when it lands: they're hiding behind a tree
/// near the crater, peeking out. As the star wanders closer the child leans out further and watches it;
/// close enough, a plain scribbled prompt appears - "press H to hold hands" - and from then on the child
/// follows the star (ProceduralFollow, at hand-holding distance).
/// </summary>
[DefaultExecutionOrder(-400)]
public class ChildEncounter : MonoBehaviour
{
    [Header("References (auto-found)")]
    public ProceduralFollow child;
    public Transform star;                 // PlasmaOrb
    public Font font;                      // Assets/Fonts/UglyHandwriting.ttf

    [Header("Where the child hides")]
    [Tooltip("Search for a tree trunk this far (min..max metres) from the crater.")]
    public Vector2 hideDistance = new Vector2(22, 42);
    [Tooltip("Optional explicit hiding tree (world position of its trunk). Leave at zero to pick one automatically.")]
    public Vector3 hideTreeOverride;

    [Header("Meeting")]
    public float noticeRadius = 16f;
    public float promptRadius = 4.5f;
    [Tooltip("The conversation starts when the star gets this close to the child.")]
    public float talkRadius = 7f;

    public Key holdKey = Key.H;
    public string promptText = "press H to hold hands";

    [Header("Holding hands")]
    public float holdFollowDistance = 2.2f;

    public static bool HoldingHands { get; private set; }
    public static ChildEncounter Instance { get; private set; }
    public ProceduralFollow Child => child;
    public bool HasMet => state == State.Met;
    bool refused; Transform directTarget;

    /// <summary>Send the child to stand at a spot (story beats); null hands control back to following the star.</summary>
    public void Direct(Vector3? spot)
    {
        if (!child) return;
        if (spot == null) { child.target = star; return; }
        if (!directTarget) directTarget = new GameObject("ChildDirectTarget").transform;
        directTarget.position = spot.Value; child.target = directTarget; child.followDistance = .2f; child.idleDistance = .2f; child.minSeparation = .1f;
    }
    /// <summary>The child slips their hand into the star's.</summary>
    public void TakeHand() { refused = false; if (state != State.Met) HoldHands(); else { HoldingHands = true; Restore(); } }
    /// <summary>The child steps back and keeps their distance (won't hold hands).</summary>
    public void Refuse()
    {
        refused = true; HoldingHands = false; if (!child) return;
        child.target = star; child.followDistance = 5.5f; child.idleDistance = 5f; child.minSeparation = 4f; child.enabled = true;
    }
    void Restore() { if (!child) return; child.target = star; child.followDistance = holdFollowDistance; child.idleDistance = holdFollowDistance * .9f; child.minSeparation = 1.6f; child.enabled = true; }

    bool talking;
    
enum State { Waiting, Hiding, Met }
    State state = State.Waiting;
    Vector3 trunk, hidePos, peekPos; float trunkRadius = .6f;
    Text prompt; CanvasGroup promptGroup;
    float peek, promptAlpha;
    CharacterController cc;
    BeachCometCrater crater;

    void Awake()
    {
        if (!child) child = FindFirstObjectByType<ProceduralFollow>();
        if (!star) { var p = GameObject.FindGameObjectWithTag("Player"); if (p) star = p.transform; }
        Instance = this;
        if (font) WishboundUI.Font = font;
        HoldingHands = false;
        if (child)
        {
            cc = child.GetComponent<CharacterController>();
            child.enabled = false;
            child.gameObject.SetActive(false);   // not there yet: it appears once the star has landed
        }
    }

    void Update()
    {
        if (!child || !star) return;
        switch (state)
        {
            case State.Waiting:
                if (!crater) crater = FindFirstObjectByType<BeachCometCrater>();
                if (!WishboundIntro.IsPlaying && (crater == null || crater.Revealed)) PlaceBehindTree();
                break;
            case State.Hiding: UpdateHiding(); break;
        }
        if (promptGroup) { promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, promptAlpha, Time.deltaTime * 2.5f); }
    }

    void PlaceBehindTree()
    {
        Vector3 site = crater ? crater.Center : star.position;
        if (hideTreeOverride != Vector3.zero) { trunk = hideTreeOverride; }
        else if (!FindTree(site, out trunk, out trunkRadius)) trunk = site + new Vector3(-26, 0, -18);

        Vector3 away = trunk - site; away.y = 0; away = away.sqrMagnitude > .01f ? away.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, away);
        float r = trunkRadius + .45f;
        hidePos = Ground(trunk + away * r);
        peekPos = Ground(trunk + away * (r * .55f) + side * (r + .25f));
        Warp(hidePos, Quaternion.LookRotation(-away));
        child.gameObject.SetActive(true);
        child.enabled = false;
        state = State.Hiding;

        prompt = WishboundUI.Label(null, "HoldHandsPrompt", promptText, 46, new Vector2(.5f, .14f), new Vector2(900, 120));
        promptGroup = prompt.GetComponent<CanvasGroup>();
    }

    bool FindTree(Vector3 around, out Vector3 best, out float radius)
    {
        best = default; radius = .6f; float bestScore = float.MaxValue;
        foreach (var col in FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
        {
            if (!col.enabled || col.gameObject.name != "TreeColliders") continue;
            Vector3 p = col.transform.TransformPoint(col.center);
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(around.x, around.z));
            if (d < hideDistance.x || d > hideDistance.y) continue;
            float score = Mathf.Abs(d - (hideDistance.x + hideDistance.y) * .5f);
            if (score < bestScore) { bestScore = score; best = new Vector3(p.x, p.y - col.height * .5f, p.z); radius = col.radius; }
        }
        return bestScore < float.MaxValue;
    }

    Vector3 Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 20, Vector3.down, out var hit, 60, ~0, QueryTriggerInteraction.Ignore)) return hit.point + Vector3.up * .05f;
        return p;
    }

    void Warp(Vector3 pos, Quaternion rot)
    {
        if (cc) cc.enabled = false;
        child.transform.SetPositionAndRotation(pos, rot);
        if (cc) cc.enabled = true;
    }

void UpdateHiding()
    {
        float d = Vector3.Distance(Flat(star.position), Flat(child.transform.position));
        // shy peeking: a little lean-out every few seconds; leaning out further (and staying out) as the star nears
        float curious = Mathf.InverseLerp(noticeRadius, promptRadius, d);
        float idlePeek = Mathf.SmoothStep(0, 1, Mathf.PingPong(Time.time * .35f, 1)) * .35f;
        float target = Mathf.Max(idlePeek, curious);
        if (talking) target = 1f;   // mid-conversation the child has stepped out for good
        peek = Mathf.MoveTowards(peek, target, Time.deltaTime * (target > peek ? .9f : .5f));
        Vector3 pos = Vector3.Lerp(hidePos, peekPos, Mathf.SmoothStep(0, 1, peek));
        Vector3 look = (curious > .01f || talking) ? star.position - pos : trunk - pos; look.y = 0;
        if (look.sqrMagnitude > .001f)
        {
            if (cc) cc.enabled = false;
            child.transform.position = pos;
            child.transform.rotation = Quaternion.Slerp(child.transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 3f);
            if (cc) cc.enabled = true;
        }

        // no HUD prompt any more: getting close starts the conversation (ChildConversation)
        promptAlpha = 0;
        bool close = d < talkRadius;
        if (close && !talking) { talking = true; ChildConversation.Run(this, star); }
        // debug / quick skip: H (or gamepad North) still takes the hand straight away
        if (close && Keyboard.current != null && Keyboard.current[holdKey].wasPressedThisFrame) HoldHands();
        var gp = Gamepad.current; if (close && gp != null && gp.buttonNorth.wasPressedThisFrame) HoldHands();
    }

void HoldHands()
    {
        ChildConversation.Cancel();
        talking = false;
        state = State.Met; HoldingHands = true; promptAlpha = 0;
        child.target = star;
        child.followDistance = holdFollowDistance; child.idleDistance = holdFollowDistance * .9f; child.minSeparation = 1.6f;
        child.playful = false;   // they're holding hands now - no wandering off
        child.enabled = true;
        if (prompt) Destroy(prompt.gameObject, 1.5f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
