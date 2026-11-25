using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A menu word the wisp can grab. The title and pause menus build their words from this.</summary>
public class WispWord
{
    public RectTransform rect;
    public Text text;
    public Shadow[] glow;   // Shadow components made by WispMagnet.AddGlow; coloured by the magnet while grabbed
    public float heat;      // 0 = plain white, 1 = fully grabbed by the wisp
}

/// <summary>
/// The magnetic wisp pointer shared by the title and pause menus. Close to a word the wisp slides into it, the word
/// takes the tutorial wisp's warm glow and flicker (WispPreset), and the wisp lets go once the pointer has moved a
/// little way off. Each frame: Track() decides the grabbed word, then ScreenPoint() gives WispCursor its spot.
/// </summary>
public sealed class WispMagnet
{
    public const float CaptureRadius = 70f;    // pointer this close to a word's letters grabs it
    public const float ReleaseRadius = 115f;   // a grabbed word is let go only once the pointer is this far away
    const float GlyphHalfHeight = 25f, GlyphPad = 20f;

    /// <summary>Index of the grabbed word, or -1 when the wisp is free and follows the pointer.</summary>
    public int Selected = -1;
    int audibleSelection = -1;
    Vector2 wisp; bool placed;

    /// <summary>Adds the soft warm glow for a grabbed word: eight clear shadows that take the wisp colour.</summary>
    public static Shadow[] AddGlow(GameObject go)
    {
        var offsets = new[]
        {
            new Vector2(3f, 0f), new Vector2(-3f, 0f), new Vector2(0f, 3f), new Vector2(0f, -3f),
            new Vector2(2.1f, 2.1f), new Vector2(-2.1f, 2.1f), new Vector2(2.1f, -2.1f), new Vector2(-2.1f, -2.1f)
        };
        var shadows = new Shadow[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            shadows[i] = go.AddComponent<Shadow>();
            shadows[i].effectDistance = offsets[i];
            shadows[i].effectColor = Color.clear;
        }
        return shadows;
    }

    /// <summary>Drops the grabbed word. The caller clears the words' heat.</summary>
    public void Reset() { audibleSelection = -1; Selected = -1; }

    /// <summary>
    /// keyboardOwned: the arrow keys (or a transition) hold the selection, so the pointer does not change it.
    /// </summary>
    public void Track(RectTransform root, IReadOnlyList<WispWord> words, Vector2 pointer, float dt, bool keyboardOwned)
    {
        if (!placed) { wisp = pointer; placed = true; }
        if (Selected >= words.Count) Selected = -1;
        if (!keyboardOwned) Selected = Pick(root, words, pointer);
        if (Selected >= 0 && Selected != audibleSelection) WorldSound.Menu("Hover");
        audibleSelection = Selected;

        Vector2 target = pointer;
        if (Selected >= 0) target = root.InverseTransformPoint(words[Selected].rect.position);   // slides into the word
        wisp = Vector2.Lerp(wisp, target, 1f - Mathf.Exp(-18f * dt));

        float t = Time.unscaledTime;
        Color warm = WispPreset.Colour;
        for (int i = 0; i < words.Count; i++)
        {
            WispWord w = words[i];
            w.heat = Mathf.Lerp(w.heat, i == Selected ? 1f : 0f, 1f - Mathf.Exp(-9f * dt));
            w.text.color = Color.Lerp(Color.white, warm, w.heat);
            w.rect.localScale = Vector3.one * (1f + .045f * w.heat);
            // the tutorial wisp's rhythm: a slow pulse and a noisy flicker, both from WispPreset
            float pulse = .8f + .2f * Mathf.Sin(t * WispPreset.PulseSpeed + i);
            float flicker = .85f + .15f * Mathf.PerlinNoise(t * WispPreset.FlickerFreq * .25f, i * 7.3f);
            Color glowColour = new Color(warm.r, warm.g, warm.b, .7f * w.heat * pulse * flicker);
            foreach (Shadow s in w.glow) s.effectColor = glowColour;
        }
    }

    int Pick(RectTransform root, IReadOnlyList<WispWord> words, Vector2 pointer)
    {
        int best = -1; float bestDistance = float.MaxValue;
        for (int i = 0; i < words.Count; i++)
        {
            float d = Distance(root, words[i], pointer);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        if (bestDistance < CaptureRadius) return best;
        // Hysteresis applies only to the current word, never a different word outside capture range.
        return Selected >= 0 && Distance(root, words[Selected], pointer) < ReleaseRadius ? Selected : -1;
    }

    /// <summary>Distance from the pointer to the visible letters of a word (zero when inside them).</summary>
    static float Distance(RectTransform root, WispWord w, Vector2 pointer)
    {
        Vector2 centre = root.InverseTransformPoint(w.rect.position);
        float halfWidth = Mathf.Min(w.rect.rect.width * .5f, w.text.preferredWidth * .5f + GlyphPad);
        Vector2 d = pointer - centre;
        d.x = Mathf.Max(0f, Mathf.Abs(d.x) - halfWidth);
        d.y = Mathf.Max(0f, Mathf.Abs(d.y) - GlyphHalfHeight);
        return d.magnitude;
    }

    /// <summary>Where WispCursor should draw the wisp this frame, in screen space.</summary>
    public Vector2 ScreenPoint(RectTransform root) => RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(wisp));
}
