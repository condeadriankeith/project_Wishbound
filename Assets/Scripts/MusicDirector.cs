using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class MusicDirector : MonoBehaviour
{
    public static MusicDirector Instance { get; private set; }
    [System.Serializable] public class Mood { public string name; public AudioClip[] stems; [Range(0, 1)] public float volume = .6f; }
    public List<Mood> moods = new List<Mood>();
    public float crossfade = 3f;
    public UnityEngine.Audio.AudioMixerGroup musicBus, sfxBus, ambienceBus;
    public AudioClip nightAir;
    readonly Dictionary<string, List<AudioSource>> sources = new Dictionary<string, List<AudioSource>>();
    readonly Dictionary<string, Coroutine> fades = new Dictionary<string, Coroutine>();
    readonly List<AudioSource> oneShots = new List<AudioSource>();
    string current;
    AudioSource ambience;
    float duckUntil;
    float duckGain = 1f;

    void Awake() { Instance = this; }
    IEnumerator Start()
    {
        // The intro owns its soundtrack until camera handover, including the skip path.
        yield return null;
        while (WishboundIntro.IsPlaying) yield return null;
        if (current == null) Play(StoryFlags.Alignment == StarAlignment.Tether ? "tether" : StoryFlags.Alignment == StarAlignment.Roots ? "roots" : "explore", 5f);
        if (nightAir)
        {
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.playOnAwake = false; ambience.clip = nightAir; ambience.loop = true;
            ambience.outputAudioMixerGroup = ambienceBus; ambience.volume = 0; ambience.Play();
        }
    }
    void Update()
    {
        float nextDuck = Mathf.MoveTowards(duckGain, Time.unscaledTime < duckUntil ? .45f : 1f, Time.unscaledDeltaTime * 1.8f);
        foreach (var pair in sources) foreach (var src in pair.Value) src.volume *= nextDuck / Mathf.Max(.001f, duckGain);
        duckGain = nextDuck;
        if (ambience) ambience.volume = Mathf.MoveTowards(ambience.volume, WishboundIntro.IsPlaying ? 0 : .45f, Time.unscaledDeltaTime * .12f);
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Play(string mood, float fade = -1)
    {
        if (current == mood || !moods.Exists(m => m.name == mood)) return;
        current = mood;
        if (fade < 0) fade = crossfade;
        foreach (var m in moods)
        {
            if (!sources.ContainsKey(m.name))
            {
                var list = new List<AudioSource>();
                foreach (var c in m.stems)
                {
                    if (!c) continue;
                    var src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false;
                    src.clip = c; src.loop = true; src.volume = 0; src.spatialBlend = 0;
                    src.outputAudioMixerGroup = musicBus; list.Add(src);
                }
                sources[m.name] = list;
            }
            if (fades.TryGetValue(m.name, out var previous) && previous != null) StopCoroutine(previous);
            if (m.name == mood) foreach (var src in sources[m.name]) if (!src.isPlaying) src.Play();
            fades[m.name] = StartCoroutine(FadeTo(sources[m.name], m.name == mood ? m.volume : 0, Mathf.Max(.05f, fade)));
        }
    }

    public void OneShot(AudioClip c, float v = 1)
    {
        if (!c) return;
        AudioSource src = oneShots.Find(x => !x.isPlaying);
        if (!src && oneShots.Count < 8)
        {
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false;
            src.outputAudioMixerGroup = sfxBus; oneShots.Add(src);
        }
        if (!src) return;
        src.clip = c; src.volume = Mathf.Clamp01(v); src.pitch = 1; src.Play();
        duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + Mathf.Min(c.length, 6f));
    }

    IEnumerator FadeTo(List<AudioSource> list, float target, float d)
    {
        var start = new float[list.Count];
        for (int i = 0; i < list.Count; i++) start[i] = list[i].volume / Mathf.Max(.001f, duckGain);
        for (float t = 0; t < d; t += Time.unscaledDeltaTime)
        {
            for (int i = 0; i < list.Count; i++) list[i].volume = Mathf.Lerp(start[i], target, Mathf.SmoothStep(0, 1, t / d)) * duckGain;
            yield return null;
        }
        foreach (var src in list) { src.volume = target * duckGain; if (target == 0) src.Stop(); }
    }

}

