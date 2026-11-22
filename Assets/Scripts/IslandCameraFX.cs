using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the two full-screen camera effects living in the URP renderer (PC_Renderer):
///   * Tilt-shift   – shader "Hidden/Island/TiltShift"   (tune it on Assets/Settings/CameraFX/TiltShift.mat)
///   * Fade-in      – shader "Hidden/Island/ScreenFade"  (pure black -> scene when Play starts)
/// Both effects are inert unless this component is alive, so the Scene view / other scenes are unaffected.
/// The screen stays fully black until the runtime-generated world and post stack have settled, then eases in.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class IslandCameraFX : MonoBehaviour
{
    public bool tiltShift = true;
    /// <summary>Blur strength goal (controller sets 0.5 / 1 / 1.8 for camera modes 1 / 2 / 3); eased every frame.</summary>
    public static float TiltGoal = 1f;
    static float tiltNow = 1f;
    static readonly int StrengthId = Shader.PropertyToID("_IslandTiltStrength");


    [Header("Fade in from black")]
    public float fadeDuration = 3f;
    [Tooltip("Extra time to stay black once the world is ready (hides shader warm-up hitches).")]
    public float warmupSeconds = 0.6f;
    [Tooltip("Never wait longer than this for the world / post stack before fading in.")]
    public float maxWaitSeconds = 5f;

    static readonly int FadeId = Shader.PropertyToID("_IslandFade");
    static readonly int TiltId = Shader.PropertyToID("_IslandTiltShiftEnabled");

void Awake()
    {
        Shader.SetGlobalFloat(FadeId, 1f);                       // black from the very first frame
        Shader.SetGlobalFloat(TiltId, tiltShift ? 1f : 0f);
        tiltNow = TiltGoal = 1f; Shader.SetGlobalFloat(StrengthId, 1f);
    }

    IEnumerator Start()
    {
        float t0 = Time.unscaledTime;

        // IslandCinematic creates its post-processing volume ~1.5 s after Play; wait for it so the look doesn't pop in.
        while (Time.unscaledTime - t0 < maxWaitSeconds && GameObject.Find("CinematicVolume") == null)
            yield return null;

        // ...then wait for a run of smooth frames (world generation / shader compilation hitches are over).
        float readyAt = Time.unscaledTime;
        int stable = 0;
        while (Time.unscaledTime - t0 < maxWaitSeconds + 2f && (stable < 8 || Time.unscaledTime - readyAt < warmupSeconds))
        {
            stable = Time.unscaledDeltaTime < 0.08f ? stable + 1 : 0;
            yield return null;
        }

        float k = 0f;
        while (k < 1f)
        {
            k += Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            Shader.SetGlobalFloat(FadeId, 1f - e);
            yield return null;
        }
        Shader.SetGlobalFloat(FadeId, 0f);
    }

    void Update()
    {
        Shader.SetGlobalFloat(TiltId, tiltShift ? 1f : 0f);
        tiltNow = Mathf.Lerp(tiltNow, CutsceneDirector.Active ? 0f : TiltGoal, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
        Shader.SetGlobalFloat(StrengthId, tiltNow);
    }

    void OnDisable() { Reset(); }
    void OnDestroy() { Reset(); }

static void Reset()
    {
        Shader.SetGlobalFloat(FadeId, 0f);
        Shader.SetGlobalFloat(TiltId, 0f);
        Shader.SetGlobalFloat(StrengthId, 1f);
    }
}
