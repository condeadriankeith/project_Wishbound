using UnityEngine;

/// <summary>
/// Life for the plasma orb: gentle hover bob, slow breathing pulse of the outer shell,
/// and a flickering point light tinted by the inner core's emission colour.
/// Put on the visual root (child of the player), not on the CharacterController object itself.
/// </summary>
public class PlasmaOrbVisual : MonoBehaviour
{
    public Transform outerShell;
    public Transform innerCore;
    public Light glowLight;

    [Header("Hover")]
    public float bobAmplitude = 0.08f;
    public float bobSpeed = 1.3f;

    [Header("Pulse")]
    public float pulseAmount = 0.04f;
    public float pulseSpeed = 1.1f;

    [Header("Light")]
    public float lightBase = 2.2f;
    public float lightFlicker = 0.5f;

    Vector3 baseLocalPos, outerBase, coreBase;
    float seed;

    void Awake()
    {
        baseLocalPos = transform.localPosition;
        if (outerShell) outerBase = outerShell.localScale;
        if (innerCore) coreBase = innerCore.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float t = Time.time + seed;
        transform.localPosition = baseLocalPos + Vector3.up * (Mathf.Sin(t * bobSpeed) * bobAmplitude);
        float p = 1f + Mathf.Sin(t * pulseSpeed) * pulseAmount;
        if (outerShell) outerShell.localScale = outerBase * p;
        if (innerCore) innerCore.localScale = coreBase * (2f - p);
        if (glowLight) glowLight.intensity = lightBase + (Mathf.PerlinNoise(t * 3f, 0.3f) - 0.5f) * 2f * lightFlicker;
    }
}
