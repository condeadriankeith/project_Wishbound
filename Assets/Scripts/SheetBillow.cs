using UnityEngine;

/// <summary>Gentle wind billow for cloth (sheets, nets).</summary>
public class SheetBillow : MonoBehaviour
{
    Quaternion rest; float seed;
    void Awake() { rest = transform.localRotation; seed = Random.value * 10; }
    void Update()
    {
        float g = Mathf.PerlinNoise(Time.time * .7f + seed, seed) - .5f;
        transform.localRotation = rest * Quaternion.Euler(g * 22f + Mathf.Sin(Time.time * 1.8f + seed) * 4f, 0, Mathf.Sin(Time.time * 1.1f + seed) * 2f);
    }
}
