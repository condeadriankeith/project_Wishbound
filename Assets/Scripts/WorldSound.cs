using UnityEngine;

// Only recurring wildlife and canopy sources need a scheduler of their own.
public sealed class WorldSound : MonoBehaviour
{
    string recurring;
    float range;
    public static void Repeat(Transform owner, string cue, float radius)
    {
        var emitter = owner.gameObject.AddComponent<WorldSound>();
        emitter.recurring = cue;
        emitter.range = radius;
        emitter.InvokeRepeating(nameof(PlayCall), Random.Range(1f, 9f), Random.Range(7f, 16f));
    }
    void PlayCall() => Cue(transform, recurring, range);
    public static void Cue(Transform owner, string cue, float range) => IslandAudio.Play(owner, cue, range);
    public static void Menu(string cue) => IslandAudio.Play(null, cue, 0);
}
