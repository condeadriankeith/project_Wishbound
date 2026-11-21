using UnityEngine;

/// <summary>Warm, slightly breathing lantern light.</summary>
public class LanternFlicker : MonoBehaviour
{
    Light l; float base_, seed;
    void Awake() { l = GetComponent<Light>(); base_ = l.intensity; seed = Random.value * 100; }
    void Update() { l.intensity = base_ * (.85f + Mathf.PerlinNoise(Time.time * 2.4f, seed) * .3f); }
}
