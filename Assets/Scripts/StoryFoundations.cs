using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// =====================================================================================================
//  Story foundations: star radiance / alignment, whispered lines, adaptive music, story flags,
//  camera profile zones and the two choice inputs.
// =====================================================================================================

public enum StarAlignment { None, Tether, Roots }

/// <summary>Persistent story choices (PlayerPrefs-backed so later scenes / sessions can read them).</summary>
public static class StoryFlags
{
    // PlayerPrefs is the registry on Windows: read each key once, then serve it from memory (callers poll these every frame).
    static readonly Dictionary<string, bool> flagCache = new Dictionary<string, bool>();
    static int alignCache = -1;
    public static StarAlignment Alignment
    {
        get { if (alignCache < 0) alignCache = PlayerPrefs.GetInt("Wishbound.Alignment", 0); return (StarAlignment)alignCache; }
        set { alignCache = (int)value; PlayerPrefs.SetInt("Wishbound.Alignment", alignCache); PlayerPrefs.Save(); }
    }
    public static bool Get(string key)
    {
        if (flagCache.TryGetValue(key, out var v)) return v;
        v = PlayerPrefs.GetInt("Wishbound.Flag." + key, 0) == 1; flagCache[key] = v; return v;
    }
    public static void Set(string key, bool v = true) { flagCache[key] = v; PlayerPrefs.SetInt("Wishbound.Flag." + key, v ? 1 : 0); PlayerPrefs.Save(); }

    static float spectrumCache = float.NaN;
    /// <summary>-100 (full Roots) .. 0 (neutral) .. +100 (full Tether). The continuous alignment driver: movement
    /// and visuals read this directly. Alignment (above) stays as a derived bucket for save display and the
    /// music mood key - UpdateDerivedBucket() (StarRadiance) keeps it in sync with this value.</summary>
    public static float Spectrum
    {
        get { if (float.IsNaN(spectrumCache)) spectrumCache = PlayerPrefs.GetFloat("Wishbound.Spectrum", 0f); return spectrumCache; }
        set { spectrumCache = Mathf.Clamp(value, -100f, 100f); PlayerPrefs.SetFloat("Wishbound.Spectrum", spectrumCache); PlayerPrefs.Save(); }
    }
    /// <summary>Move the spectrum by a signed amount (a story beat's weight). reasonTag is just for the console log.</summary>
    public static void Nudge(float amount, string reasonTag)
    {
        float before = Spectrum;
        Spectrum = before + amount;
        Debug.Log("[Alignment] " + reasonTag + ": " + (amount > 0 ? "+" : "") + amount.ToString("0.#") + " -> " + Spectrum.ToString("0.#"));
    }

    /// <summary>Editor play sessions start fresh.</summary>
    public static void ResetForEditorSession() { flagCache.Clear(); alignCache = -1; spectrumCache = 0f; PlayerPrefs.DeleteKey("Wishbound.Alignment"); PlayerPrefs.DeleteKey("Wishbound.Spectrum"); PlayerPrefs.DeleteKey("Wishbound.Flag.CottageChoiceMade"); PlayerPrefs.DeleteKey("Wishbound.Flag.ChildHeardStar"); PlayerPrefs.DeleteKey("Wishbound.Flag.ChildTookHand"); PlayerPrefs.DeleteKey("Wishbound.Flag.CanyonCrossed"); PlayerPrefs.DeleteKey("Wishbound.Flag.VillageHearthResolved"); }
}

/// <summary>The two physical choice inputs. Starlight Pulse = hold (F / right trigger); Reach = press (E / gamepad West).</summary>
public static class StarInput
{
    public static bool PulseHeld
    {
        get { var k = Keyboard.current; var g = Gamepad.current; return (k != null && k.fKey.isPressed) || (g != null && g.rightTrigger.ReadValue() > .5f); }
    }
    public static bool ReachPressed
    {
        get { var k = Keyboard.current; var g = Gamepad.current; return (k != null && k.eKey.wasPressedThisFrame) || (g != null && g.buttonWest.wasPressedThisFrame); }
    }
}

/// <summary>
/// The Star's inner state: radiance (how much light it holds), a temporary flare, a "missing piece" flicker and
/// its alignment. Drives the orb look (StarOrbFX), its light and trail ("ribbons"), and the controller's gravity.
///   Tether: brighter, long prismatic trail, lighter gravity.   Roots: matte ivory, short heavy trail, grounded.
/// </summary>
/// <summary>Whispered lines: plain white scribbled handwriting floating just above a speaker, no boxes.</summary>
public class WhisperLine : MonoBehaviour
{
    Transform anchor; Text text; CanvasGroup group; float until, fadeIn = .8f, fadeOut = 1.2f, born; Vector3 offset;

    public static WhisperLine Show(Transform speaker, string line, float seconds = 3.5f, float height = 1.9f, int size = 40)
    {
        var t = WishboundUI.Label(null, "Whisper", line, size, new Vector2(.5f, .5f), new Vector2(1000, 120));
        var w = t.gameObject.AddComponent<WhisperLine>();
        w.anchor = speaker; w.text = t; w.group = t.GetComponent<CanvasGroup>(); w.offset = Vector3.up * height;
        w.born = Time.time; w.until = Time.time + seconds;
        var s = t.GetComponent<ScribbleText>(); s.amount = .035f; s.boilsPerSecond = 6;
        return w;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (!anchor || !cam) { Destroy(gameObject); return; }
        Vector3 vp = cam.WorldToViewportPoint(anchor.position + offset);
        var r = (RectTransform)transform; r.anchorMin = r.anchorMax = new Vector2(vp.x, Mathf.Clamp(vp.y, .08f, .92f));
        float a = Mathf.Clamp01((Time.time - born) / fadeIn) * Mathf.Clamp01((until - Time.time) / fadeOut);
        group.alpha = vp.z > 0 ? a : 0;
        if (Time.time > until) Destroy(gameObject);
    }
}

/// <summary>Layered adaptive music: each mood is a set of looping stems; moods cross-fade.</summary>
/// <summary>Trigger volume that eases the iso camera into a named profile while the player is inside.</summary>
[RequireComponent(typeof(Collider))]
public class CameraProfileZone : MonoBehaviour
{
    public IsoCameraRig.Profile profile = IsoCameraRig.Profile.IntimateCompanion;
    public bool active = true;
    void Awake() { GetComponent<Collider>().isTrigger = true; }
    void OnTriggerEnter(Collider o) { if (active && o.CompareTag("Player") && IsoCameraRig.Active) IsoCameraRig.Active.SetProfile(profile); }
    void OnTriggerExit(Collider o) { if (o.CompareTag("Player") && IsoCameraRig.Active && IsoCameraRig.Active.ProfileName == profile.name) IsoCameraRig.Active.SetProfile(null); }
}
