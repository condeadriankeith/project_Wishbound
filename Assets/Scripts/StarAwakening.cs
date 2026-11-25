using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The first minute of control after the impact. The star is not "ready" the moment the camera hands over:
/// it wakes up. It starts dim and sluggish, and every metre it travels (plus a slow clock, so standing still
/// never leaves it dim forever) feeds it back toward its normal radiance and speed.
///   - first movement  -> a flare ("it felt that")
///   - first jump      -> a smaller flare
///   - 1/3, 2/3, full  -> milestone flares, so growth is felt as well as seen
/// Uses IslandThirdPersonController.speedScale and StarRadiance.radiance / flare. Added automatically by
/// WishboundIntro at hand-over; remove the component to skip the wake-up.
/// </summary>
[DefaultExecutionOrder(60)]
public class StarAwakening : MonoBehaviour
{
    [Tooltip("Speed multiplier when the star first wakes (1 = full speed).")]
    [Range(.1f, 1f)] public float startSpeed = .4f;
    [Tooltip("Radiance when the star first wakes, as a fraction of its normal radiance.")]
    [Range(.05f, 1f)] public float startRadiance = .3f;
    [Tooltip("Metres of walking it takes the star to warm up fully.")]
    public float warmUpDistance = 38f;
    [Tooltip("Seconds that also warm it, so a player who stands still is never stuck dim.")]
    public float warmUpSeconds = 75f;

    IslandThirdPersonController controller;
    StarRadiance rad;
    float baseRadiance, lastWritten = float.NaN, travelled, elapsed, myFlare;
    Vector3 last;
    bool firstMove, firstJump, m1, m2;

    public static void Begin(GameObject player)
    {
        if (player && !player.GetComponent<StarAwakening>()) player.AddComponent<StarAwakening>();
    }

    void Start()
    {
        controller = GetComponent<IslandThirdPersonController>();
        rad = StarRadiance.Instance;
        if (rad) baseRadiance = rad.radiance;
        last = transform.position;
        Apply(0f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        Vector3 d = transform.position - last; d.y = 0; last = transform.position;
        float step = d.magnitude;
        if (step < 3f) travelled += step;   // ignore teleports

        float p = Mathf.Clamp01(travelled / Mathf.Max(1f, warmUpDistance) + elapsed / Mathf.Max(1f, warmUpSeconds));
        Apply(Mathf.SmoothStep(0f, 1f, p));

        if (!firstMove && travelled > .4f) { firstMove = true; Flare(.9f); }
        if (!firstJump && WishboundInput.Pressed(Key.Space, KeyCode.Space)) { firstJump = true; Flare(.7f); }
        if (!m1 && p > .33f) { m1 = true; Flare(.5f); }
        if (!m2 && p > .66f) { m2 = true; Flare(.6f); }

        if (myFlare > 0f)
        {
            myFlare = Mathf.MoveTowards(myFlare, 0f, dt * .7f);
            if (rad) rad.flare = Mathf.Max(rad.flare, myFlare);
        }

        if (p >= 1f)
        {
            Flare(.8f);
            Apply(1f);
            if (controller) controller.speedScale = 1f;
            Destroy(this);
        }
    }

    void Flare(float v) { myFlare = Mathf.Max(myFlare, v); }

    void Apply(float e)
    {
        if (controller) controller.speedScale = Mathf.Lerp(startSpeed, 1f, e);
        if (!rad) return;
        // if something else changed the radiance (a story beat), stop fighting it
        if (!float.IsNaN(lastWritten) && !Mathf.Approximately(rad.radiance, lastWritten)) { rad = null; return; }
        lastWritten = Mathf.Lerp(baseRadiance * startRadiance, baseRadiance, e);
        rad.radiance = lastWritten;
    }

    void OnDestroy() { if (controller) controller.speedScale = 1f; }
}
