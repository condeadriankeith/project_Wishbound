using UnityEngine;

/// <summary>
/// The comet's landing wind blast, as data the trees read. A shockwave runs outward over the ground at Speed; each
/// tree leans away from the impact as the crest reaches it, springs back and settles. BeachCometCrater starts it
/// (Trigger); IslandStylizedTrees poses the tree instances every frame while it runs (GhibliBatch.Pose).
/// Uses unscaled time, so it plays through the comet's time hold like the impact itself.
/// </summary>
public static class CometBlast
{
    public const float Speed = 34f;       // metres per second the crest runs over the ground
    public const float Reach = 181f;      // metres; beyond this the air stays still
    public const float PeakLean = .43f;    // radians (about 25 degrees) right beside the impact
    public const float Life = 9.5f;         // seconds until every tree has settled

    public static bool Active;
    public static Vector3 Origin;
    static float startTime;

    public static void Trigger(Vector3 origin)
    {
        Origin = origin;
        startTime = Time.unscaledTime;
        Active = true;
    }

    /// <summary>True once the blast has run its course (the trees are then restored to upright).</summary>
    public static bool Done => Active && Time.unscaledTime - startTime > Life;

    public static float Age => Time.unscaledTime - startTime;

    public static float LeanAt(Vector3 p)
    {
        float local = Age - Distance(p) / Speed;
        if (!Active || local <= 0f || local > 4f) return 0f;
        float rise = Mathf.SmoothStep(0f, 1f, local / .18f);
        return PeakLean * Falloff(p) * rise * Mathf.Exp(-1.32f * local) * Mathf.Cos(local * 4.5f);
    }

    public static float FlutterAt(Vector3 p)
    {
        float local = Age - Distance(p) / Speed;
        if (!Active || local <= 0f || local > 4f) return 0f;
        float pulse = Mathf.SmoothStep(0f, 1f, local / .11f) * Mathf.Exp(-1.4f * local);
        return Falloff(p) * pulse * (.11f + .10f * Mathf.Sin(local * 21f + p.x * .41f + p.z * .3f));
    }

    static float Distance(Vector3 p) => new Vector2(p.x - Origin.x, p.z - Origin.z).magnitude;
    static float Falloff(Vector3 p) => Mathf.Pow(Mathf.Clamp01(1f - Distance(p) / Reach), .85f);
}
