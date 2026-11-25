using System.Collections.Generic;
using UnityEngine;

public sealed class IslandAudio : MonoBehaviour
{
    sealed class Voice
    {
        public AudioSource source;
        public Transform owner;
        public IslandSoundEvent sound;
        public float started;
    }
    static IslandAudio instance;
    readonly List<Voice> voices = new();
    readonly Dictionary<string, IslandSoundEvent> events = new();
    readonly Dictionary<IslandSoundEvent, int> last = new();
    const int Limit = 24;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance) return;
        var host = new GameObject("Island audio");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<IslandAudio>();
        foreach (var item in Resources.LoadAll<IslandSoundEvent>("IslandSoundEvents"))
            instance.events[item.name] = item;
    }

    public static void Play(Transform owner, string cue, float range)
    {
        if (!instance) return;
        if (owner)
        {
            var ear = AudioListenerGuard.Ear;
            if (!ear || (owner.position-ear.position).sqrMagnitude >= range*range) return;
            if (cue == "Step" || cue == "ChildStep" || cue == "AnimalStep")
            {
                var pos = owner.position;
                float z = TerrainMath.MirrorZ ? -pos.z : pos.z;
                if (Surface.At(pos.x,z) == SurfaceType.Sand) cue += "Sand";
            }
        }
        if (instance.events.TryGetValue(cue, out var sound)) instance.Emit(owner, sound, range);
    }

    void Emit(Transform owner, IslandSoundEvent sound, float range)
    {
        Voice free = null, weakest = null;
        int count = 0;
        foreach (var slot in voices)
        {
            if (slot.sound == sound && slot.owner == owner && Time.unscaledTime-slot.started < sound.cooldown) return;
            if (!slot.source.isPlaying) { if (free == null) free = slot; continue; }
            if (slot.sound == sound) count++;
            if (slot.source.priority > sound.priority && (weakest == null || slot.source.priority > weakest.source.priority))
                weakest = slot;
        }
        if (count >= sound.maxVoices || sound.clips.Length == 0) return;
        if (free == null && voices.Count < Limit)
        {
            var host = new GameObject("Pooled sound");
            host.transform.SetParent(transform, false);
            free = new Voice { source = host.AddComponent<AudioSource>() };
            free.source.playOnAwake = false;
            free.source.rolloffMode = AudioRolloffMode.Linear;
            free.source.dopplerLevel = 0;
            voices.Add(free);
        }
        free ??= weakest;
        if (free == null) return;
        var source = free.source;
        int index = Random.Range(0, sound.clips.Length);
        if (sound.clips.Length > 1 && last.TryGetValue(sound, out int prev) && index == prev)
            index = (index+Random.Range(1,sound.clips.Length)) % sound.clips.Length;
        last[sound] = index;
        free.owner = owner; free.sound = sound; free.started = Time.unscaledTime;
        source.Stop();
        source.clip = sound.clips[index]; source.outputAudioMixerGroup = sound.bus;
        source.priority = sound.priority; source.volume = sound.gain;
        source.pitch = 1 + Random.Range(-sound.pitchSpread,sound.pitchSpread);
        source.spatialBlend = sound.screen ? 0 : 1;
        source.ignoreListenerPause = sound.screen;
        source.minDistance = .8f; source.maxDistance = Mathf.Max(1,range);
        if (owner) source.transform.position = owner.position;
        source.Play();
    }

    void LateUpdate()
    {
        foreach (var voice in voices)
        {
            if (!voice.source.isPlaying || voice.sound.screen) continue;
            if (voice.owner && voice.owner.gameObject.activeInHierarchy) voice.source.transform.position = voice.owner.position;
            else voice.source.Stop();
        }
    }
}
