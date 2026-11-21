using UnityEngine;

/// <summary>A hanging lantern swinging gently on its hook in the breeze.</summary>
public class LanternSway : MonoBehaviour
{
    Quaternion rest; float seed;
    void Awake() { rest = transform.localRotation; seed = Random.value * 10; }
    void Update()
    {
        float t = Time.time;
        transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 1.1f + seed) * 4f + (Mathf.PerlinNoise(t * .4f, seed) - .5f) * 6f, 0, Mathf.Sin(t * .8f + seed * 2) * 3f);
    }
}
