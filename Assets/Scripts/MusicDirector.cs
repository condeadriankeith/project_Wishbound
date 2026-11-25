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
    readonly AudioSource[] beds = new AudioSource[2];
    AudioSource waterfall;
    AudioLowPassFilter[] bedFilters = new AudioLowPassFilter[2];
    int activeBed;
    float nextZoneCheck;
    bool indoors;
    AudioClip shore, stream, insects, forest;
    float duckUntil;
    float duckGain = 1f;

    void Awake() { Instance = this; }
    IEnumerator Start()
    {
        // The intro owns its soundtrack until camera handover, including the skip path.
        yield return null;
        while (WishboundIntro.IsPlaying) yield return null;
        if (current == null) Play(StoryFlags.Alignment == StarAlignment.Tether ? "tether" : StoryFlags.Alignment == StarAlignment.Roots ? "roots" : "explore", 5f);
        shore = Resources.Load<AudioClip>("WishboundAudio/Ambience/Shore");
        stream = Resources.Load<AudioClip>("WishboundAudio/Ambience/Stream");
        insects = Resources.Load<AudioClip>("WishboundAudio/Ambience/Night");
        forest = Resources.Load<AudioClip>("WishboundAudio/Environment/Bird");
        for (int i = 0; i < beds.Length; i++)
        {
            var bedObject = new GameObject("Ambient bed");
            bedObject.transform.SetParent(transform, false);
            beds[i] = bedObject.AddComponent<AudioSource>();
            bedFilters[i] = bedObject.AddComponent<AudioLowPassFilter>();
            bedFilters[i].cutoffFrequency = 20000;
            beds[i].playOnAwake = false; beds[i].loop = true;
            beds[i].outputAudioMixerGroup = ambienceBus; beds[i].volume = 0;
        }
        var fallObject = new GameObject("Nearest waterfall");
        fallObject.transform.SetParent(transform,false);
        waterfall = fallObject.AddComponent<AudioSource>();
        waterfall.clip = Resources.Load<AudioClip>("WishboundAudio/Ambience/Waterfall");
        waterfall.outputAudioMixerGroup = ambienceBus;
        waterfall.playOnAwake = false; waterfall.loop = true; waterfall.spatialBlend = 1;
        waterfall.dopplerLevel = 0; waterfall.rolloffMode = AudioRolloffMode.Linear;
        waterfall.minDistance = 3; waterfall.maxDistance = 38; waterfall.volume = .19f;
    }
    void Update()
    {
        float nextDuck = Mathf.MoveTowards(duckGain, Time.unscaledTime < duckUntil ? .45f : 1f, Time.unscaledDeltaTime * 1.8f);
        foreach (var pair in sources) foreach (var src in pair.Value) src.volume *= nextDuck / Mathf.Max(.001f, duckGain);
        duckGain = nextDuck;
        UpdateAmbience();
    }
    void UpdateAmbience()
    {
        if (!beds[0] || !AudioListenerGuard.Ear) return;
        if (Time.unscaledTime >= nextZoneCheck)
        {
            nextZoneCheck = Time.unscaledTime + 1;
            var p = AudioListenerGuard.Ear.position;
            indoors = HouseEntrances.Instance && HouseEntrances.Instance.Inside;
            float nearest = waterfall.maxDistance * waterfall.maxDistance;
            bool found = false;
            if (!indoors && !WishboundIntro.IsPlaying)
            {
                foreach (var fall in WaterBuilder.Falls)
                {
                    var pool = fall.pts[fall.pts.Length-1];
                    float distance = (p-pool).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; waterfall.transform.position = pool; found = true;
                }
            }
            if (found && !waterfall.isPlaying) waterfall.Play();
            if (!found && waterfall.isPlaying) waterfall.Stop();
            float hour = LivingDayNight.Instance ? LivingDayNight.Instance.CurrentHour : 12;
            AudioClip bed = indoors ? nightAir : p.y < TerrainProbe.SeaLevel + 2 ? shore :
                WaterProbe.Near(p, 9, 4) ? stream : hour < 6 || hour > 19 ? insects : forest;
            if (bed && beds[activeBed].clip != bed)
            {
                activeBed = 1 - activeBed;
                var source = beds[activeBed]; source.volume = 0; source.clip = bed; source.Play();
            }
        }
        for (int i = 0; i < beds.Length; i++)
        {
            var source = beds[i];
            float gain = i == activeBed && !WishboundIntro.IsPlaying ? (indoors ? .025f : .12f) : 0;
            bedFilters[i].cutoffFrequency = Mathf.Lerp(bedFilters[i].cutoffFrequency, indoors ? 1300 : 20000, Time.unscaledDeltaTime * 2);
            source.volume = Mathf.MoveTowards(source.volume,gain,Time.unscaledDeltaTime * .07f);
            if (i != activeBed && source.volume == 0 && source.isPlaying) source.Stop();
        }
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

