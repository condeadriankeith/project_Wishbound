using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "Wishbound/Sound Event")]
public sealed class IslandSoundEvent : ScriptableObject
{
    public AudioClip[] clips;
    public AudioMixerGroup bus;
    [Range(0,1)] public float gain = .25f;
    public float pitchSpread = .04f, cooldown = .1f;
    [Range(0,256)] public int priority = 140;
    public int maxVoices = 3;
    public bool screen;
}
