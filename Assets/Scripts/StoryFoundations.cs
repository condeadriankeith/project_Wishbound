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
    static WishSaveSlots.Progress progress = new WishSaveSlots.Progress();
    static int playingSlot;
    static void Save() { if (playingSlot!=0) WishSaveSlots.Write(playingSlot,progress); }
    internal static void LoadActive()
    {
        playingSlot = WishSaveSlots.Active;
        progress = WishSaveSlots.Read(playingSlot) ?? new WishSaveSlots.Progress();
        if (progress.Has("TetherEnding") || progress.Has("RootsEnding")) NewDemoRun();
        else Save();
    }
    internal static void RememberPosition(Transform actor)
    {
        if (playingSlot==0 || !actor || WishboundIntro.IsPlaying || CutsceneDirector.Active ||
            (HouseEntrances.Instance && HouseEntrances.Instance.Inside) ||
            (MountainJourney.Instance && MountainJourney.Instance.Busy)) return;
        var cc=actor.GetComponent<CharacterController>();
        if (!cc || !cc.enabled || !cc.isGrounded) return;
        if(progress.hasPosition && (progress.position-actor.position).sqrMagnitude<.09f &&
            Mathf.Abs(Mathf.DeltaAngle(progress.yaw,actor.eulerAngles.y))<5f) return;
        progress.hasPosition=true; progress.position=actor.position; progress.yaw=actor.eulerAngles.y; Save();
    }
    internal static void RestorePosition(Transform actor)
    {
        if (!actor || !progress.hasPosition) return;
        Vector3 p=progress.position;
        if (!TerrainProbe.TryGetGroundY(p.x,p.z,out float y)) return;
        p.y=Mathf.Max(y+.08f,p.y);
        var cc=actor.GetComponent<CharacterController>();
        bool enabled=cc && cc.enabled;
        if(enabled)cc.enabled=false;
        actor.SetPositionAndRotation(p,Quaternion.Euler(0,progress.yaw,0));
        if(enabled)cc.enabled=true;
    }
    public static StarAlignment Alignment
    {
        get => (StarAlignment)progress.alignment;
        set { progress.alignment=(int)value; Save(); }
    }
    public static bool Get(string key) => progress.Has(key);
    public static void Set(string key,bool v=true)
    {
        if (Get(key)==v) return;
        if (v) progress.flags.Add(key); else progress.flags.Remove(key);
        Save();
    }
    public static float Spectrum
    {
        get => progress.spectrum;
        set { progress.spectrum=Mathf.Clamp(value,-100,100); Save(); }
    }
    /// <summary>Move the spectrum by a signed amount (a story beat's weight). reasonTag is just for the console log.</summary>
    public static void Nudge(float amount, string reasonTag)
    {
        float before = Spectrum;
        Spectrum = before + amount;
        Debug.Log("[Alignment] " + reasonTag + ": " + (amount > 0 ? "+" : "") + amount.ToString("0.#") + " -> " + Spectrum.ToString("0.#"));
    }

    /// <summary>Editor play sessions start fresh.</summary>
    public static void ResetForEditorSession() { NewDemoRun(); }

    /// <summary>Isolate presentation runs from the player's persistent free-roam save.</summary>
    public static void NewDemoRun()
    {
        playingSlot=0;
        progress = new WishSaveSlots.Progress();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeCache() => NewDemoRun();
}

/// <summary>The two physical choice inputs. Starlight Pulse = hold (F / right trigger); Reach = press (E / gamepad West).</summary>
public static class StarInput
{
    internal static bool InReach(Vector3 from, Vector3 target, float radius, float height = 2f)
    {
        Vector3 offset = from - target;
        return Mathf.Abs(offset.y) <= height && offset.x * offset.x + offset.z * offset.z <= radius * radius;
    }

    public static bool PulseHeld
    {
        get { var g = Gamepad.current; return (WishboundInput.Held(Key.F, KeyCode.F)) || (g != null && g.rightTrigger.ReadValue() > .5f); }
    }
    public static bool ReachPressed
    {
        get { var g = Gamepad.current; return (WishboundInput.Pressed(Key.E, KeyCode.E)) || (g != null && g.buttonWest.wasPressedThisFrame); }
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
    /// <summary>Metres. Set on settler chatter so it fades out with distance and only reads up close (0 = always readable).</summary>
    public float hideBeyond;

    public static WhisperLine Show(Transform speaker, string line, float seconds = 3.5f, float height = 1.9f, int size = 40)
    {
        MagicCue.Say(speaker, line);
        var t = WishboundUI.Label(null, "Whisper", line, size, new Vector2(.5f, .5f), new Vector2(1000, 120), false);
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
        float near = 1f;
        if (hideBeyond > 0f) near = Mathf.InverseLerp(hideBeyond, hideBeyond * .7f, Vector3.Distance(cam.transform.position, anchor.position + offset));
        group.alpha = vp.z > 0 ? a * near : 0;
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
