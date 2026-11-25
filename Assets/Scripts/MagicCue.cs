using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Layered magic cues (real CC0 recordings, see Assets/Audio/audio_manifest.json) and the child's dialogue voice.
/// The voice plays short slices of recorded child speech, one burst per syllable, so it reads as the child talking
/// instead of a crowd. Nothing here synthesises audio.
/// </summary>
public sealed class MagicCue : MonoBehaviour
{
    static MagicCue inst;
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static MagicCue Inst
    {
        get
        {
            if (!inst) { var g = new GameObject("Magic cues"); DontDestroyOnLoad(g); inst = g.AddComponent<MagicCue>(); }
            return inst;
        }
    }

    static AudioClip Clip(string name)
    {
        if (!cache.TryGetValue(name, out var c) || !c) { c = Resources.Load<AudioClip>("WishboundAudio/" + name); cache[name] = c; }
        return c;
    }

    static AudioMixerGroup Bus
    {
        get
        {
            if (WishboundIntro.Instance && WishboundIntro.Instance.sfxBus) return WishboundIntro.Instance.sfxBus;
            var talk = Resources.Load<IslandSoundEvent>("IslandSoundEvents/Talk");
            return talk ? talk.bus : null;
        }
    }

    // ---------- layered one-shots ----------
    public static void Layer(string clip, float volume, float delay = 0f, float pitch = 1f, float fadeIn = .02f, float fadeOut = .5f, float maxLength = 0f, float startAt = 0f)
    {
        var c = Clip(clip);
        if (c) Inst.StartCoroutine(Inst.Run(c, volume, delay, pitch, fadeIn, fadeOut, maxLength, startAt));
    }

    IEnumerator Run(AudioClip c, float volume, float delay, float pitch, float fadeIn, float fadeOut, float maxLength, float startAt)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = c; s.spatialBlend = 0; s.playOnAwake = false; s.loop = false; s.outputAudioMixerGroup = Bus;
        s.pitch = pitch; s.volume = 0; s.ignoreListenerPause = true;
        startAt = Mathf.Clamp(startAt, 0f, Mathf.Max(0f, c.length - .1f));
        s.time = startAt; s.Play();
        float avail = c.length - startAt;
        float len = (maxLength > 0f ? Mathf.Min(maxLength, avail) : avail) / Mathf.Max(.1f, pitch);
        for (float t = 0; t < len; t += Time.unscaledDeltaTime)
        {
            float a = Mathf.Min(fadeIn > 0f ? t / fadeIn : 1f, (len - t) / Mathf.Max(.01f, fadeOut));
            s.volume = volume * Mathf.Clamp01(a);
            yield return null;
        }
        s.Stop(); Destroy(s);
    }

    // ---------- story cues ----------
    // Soft, slow-attack, low-pitched layers: no bright 'sparkle' transients.
    public static void CometDescent()
    {
        Layer("Magic/DeepWhoosh", .22f, 0f, .8f, 1.2f, 2f, 4f);
        Layer("Magic/CrystalTransition", .14f, .4f, 1f, 1.8f, 2.5f, 6f, 3f);
    }

    public static void ShardSplit()
    {
        Layer("Magic/GleamGlow", .24f, 0f, .85f, .08f, 3f, 4.5f);
    }

    public static void NightSkyReveal()
    {
        Layer("Magic/CrystalTwinkle", .22f, 0f, .8f, .5f, 2.5f);
        Layer("Magic/JewelGlitter", .10f, .2f, 1f, 2f, 3f, 7f, 3f);
    }

    public static void Hyperlapse()
    {
        Layer("Magic/DeepWhoosh", .34f, 0f, .85f, .6f, 2f);
        Layer("Magic/CrystalTransition", .16f, .3f, 1f, 1.5f, 3f, 7f, 1.5f);
    }

    public static void Ascent()
    {
        Layer("Magic/DeepWhoosh", .45f, 0f, .8f, .8f, 2.5f, 4f);
        Layer("Magic/JewelGlitter", .16f, .8f, 1f, 2.5f, 3.5f, 9f, 5f);
        Layer("Magic/CrystalTransition", .16f, 2f, 1f, 2f, 3f, 8f, 10f);
    }

    public static void Arrival()
    {
        Layer("Magic/CrystalTwinkle", .3f, 0f, .85f, .15f, 3f);
        Layer("Magic/GleamGlow", .2f, .3f, .9f, .2f, 3.5f);
    }

    // ---------- child voice ----------
    struct Window { public AudioClip clip; public float start; }
    static List<Window> windows;
    static Coroutine talking;
    static readonly string[] voiceClips = { "Voice/Child1", "Voice/Child2", "Voice/Child3" };

    static void BuildWindows()
    {
        windows = new List<Window>();
        foreach (var n in voiceClips)
        {
            var c = Clip(n);
            if (!c) continue;
            var data = new float[c.samples * c.channels];
            if (!c.GetData(data, 0)) continue;
            int step = Mathf.Max(1, Mathf.RoundToInt(.16f * c.frequency));
            for (int i = 0; i + step < c.samples; i += step / 2)
            {
                float e = 0f;
                for (int k = 0; k < step; k++) { float v = data[(i + k) * c.channels]; e += v * v; }
                if (Mathf.Sqrt(e / step) > .05f) windows.Add(new Window { clip = c, start = (float)i / c.frequency });
            }
        }
    }

    static bool IsChild(Transform t) => t && (t.GetComponentInParent<ChildPerformer>() != null || t.name.ToLowerInvariant().Contains("child"));

    /// <summary>Call when a line is shown. Only the child has a voice; other speakers stay silent.</summary>
    public static void Say(Transform speaker, string line)
    {
        if (!IsChild(speaker) || string.IsNullOrEmpty(line)) return;
        if (talking != null) Inst.StopCoroutine(talking);
        talking = Inst.StartCoroutine(Inst.Babble(speaker, line));
    }

    IEnumerator Babble(Transform speaker, string line)
    {
        if (windows == null) BuildWindows();
        if (windows.Count == 0) yield break;
        int colon = line.IndexOf(':');
        if (colon >= 0 && colon < 12) line = line.Substring(colon + 1);
        var words = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        float budget = Mathf.Clamp(line.Length / 14f, 1f, 6f), spent = 0f;
        int last = -1;
        foreach (var word in words)
        {
            if (!speaker || spent > budget) break;
            int slices = word.Length > 6 ? 3 : word.Length > 3 ? 2 : 1;
            bool question = word.EndsWith("?");
            for (int i = 0; i < slices; i++)
            {
                int pick = Random.Range(0, windows.Count);
                if (windows.Count > 1 && pick == last) pick = (pick + 1) % windows.Count;
                last = pick;
                float pitch = Random.Range(1.04f, 1.2f) * (question && i == slices - 1 ? 1.12f : 1f);
                StartCoroutine(Slice(speaker, windows[pick], pitch, .3f));
                float gap = Random.Range(.12f, .17f);
                spent += gap;
                yield return new WaitForSeconds(gap);
            }
            float pause = word.EndsWith(".") || word.EndsWith(",") || question ? .22f : .07f;
            spent += pause;
            yield return new WaitForSeconds(pause);
        }
        talking = null;
    }

    IEnumerator Slice(Transform speaker, Window w, float pitch, float volume)
    {
        var go = new GameObject("Child voice");
        go.transform.SetParent(transform, false);
        go.transform.position = speaker.position + Vector3.up * 1.1f;
        var s = go.AddComponent<AudioSource>();
        s.clip = w.clip; s.time = w.start; s.pitch = pitch; s.volume = 0; s.playOnAwake = false;
        s.spatialBlend = .7f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 3f; s.maxDistance = 30f; s.dopplerLevel = 0;
        s.outputAudioMixerGroup = Bus;
        s.Play();
        float len = .17f / pitch;
        for (float t = 0; t < len && speaker; t += Time.deltaTime)
        {
            go.transform.position = speaker.position + Vector3.up * 1.1f;
            s.volume = volume * Mathf.Min(t / .02f, (len - t) / .06f, 1f);
            yield return null;
        }
        s.Stop(); Destroy(go);
    }
}
