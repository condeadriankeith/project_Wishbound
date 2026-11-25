using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[DefaultExecutionOrder(55)]
public class StarRadiance : MonoBehaviour
{
    public static StarRadiance Instance { get; private set; }
    [Range(0, 2)] public float radiance = 1f;
    [Range(0, 1)] public float flare;
    [Tooltip("0..1 - the rim flickers and the trail shortens, as if a piece of it is missing.")]
    [Range(0, 1)] public float missingPiece;
    public StarAlignment alignment;

    StarOrbFX fx; IslandThirdPersonController controller;
    float baseLight, baseHalo; float flareSm, missSm, radSm = 1, alignSm, rootsSm;

    void Awake()
    {
        Instance = this;
        fx = GetComponent<StarOrbFX>();
        controller = GetComponentInParent<IslandThirdPersonController>();
        alignment = StoryFlags.Alignment;
        if (fx) { baseLight = fx.lightIntensity; baseHalo = fx.haloSize; }
    }

    public void SetAlignment(StarAlignment a) { alignment = a; StoryFlags.Alignment = a; }

    /// <summary>Keeps the derived bucket (save display, MusicDirector mood key) in sync with the continuous
    /// spectrum. Call after any StoryFlags.Nudge() that should be reflected in the bucket (every choice-scene
    /// resolution does). Thresholds are deliberately wide of zero so a lot of small nudges don't flip-flop it.</summary>
    public void UpdateDerivedBucket()
    {
        float s = StoryFlags.Spectrum;
        SetAlignment(s > 20f ? StarAlignment.Tether : s < -20f ? StarAlignment.Roots : StarAlignment.None);
    }

    void LateUpdate()
    {
        if (!fx) return;
        float dt = Time.deltaTime;
        float s01 = Mathf.InverseLerp(-100f, 100f, StoryFlags.Spectrum);   // 0 = full Roots, .5 = neutral, 1 = full Tether
        flareSm = Mathf.Lerp(flareSm, flare, 1 - Mathf.Exp(-10 * dt));
        missSm = Mathf.Lerp(missSm, missingPiece, 1 - Mathf.Exp(-3 * dt));
        radSm = Mathf.Lerp(radSm, radiance, 1 - Mathf.Exp(-2.5f * dt));
        alignSm = Mathf.Lerp(alignSm, Mathf.Clamp01((s01 - .5f) * 2f), 1 - Mathf.Exp(-2 * dt));   // 0 at/below neutral, ramps to 1 at full Tether
        rootsSm = Mathf.Lerp(rootsSm, Mathf.Clamp01((.5f - s01) * 2f), 1 - Mathf.Exp(-2 * dt));    // 0 at/above neutral, ramps to 1 at full Roots

        float flicker = missSm > .01f ? (Mathf.PerlinNoise(Time.time * 7f, .3f) - .5f) * missSm * .9f : 0;
        fx.radianceScale = Mathf.Max(.05f, radSm * (1 + flicker) + flareSm * 3f + alignSm * .35f - rootsSm * .45f);
        fx.trailScale = Mathf.Clamp(1 + alignSm * 1.6f - rootsSm * .6f - missSm * .45f + flareSm, .2f, 4f);
        fx.matte = rootsSm;
        fx.prismatic = alignSm;
        fx.lightIntensity = baseLight;
        fx.haloSize = baseHalo * (1 + flareSm * 1.5f + alignSm * .3f - rootsSm * .35f);
        if (controller)
        {
            controller.gravityScale = Mathf.Lerp(1, .62f, alignSm) * Mathf.Lerp(1, 1.12f, rootsSm);
            // Tether: higher float ceiling + faster rise. Roots: lower ceiling + slower rise. Float stays
            // available on both paths (never fully taken away) - it's the earned payoff, not a locked gate.
            controller.floatReachScale = Mathf.Lerp(.55f, 1.6f, alignSm) * Mathf.Lerp(1f, .8f, rootsSm);
        }
    }
}

