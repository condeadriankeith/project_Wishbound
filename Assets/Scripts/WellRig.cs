using UnityEngine;

/// <summary>
/// Lives on the village Well root (the GameObject VillageKit.Well() builds). Lets settlers actually draw water:
/// finds the windlass/bucket/rope parts VillageKit built, and plays a short raise/lower when DrawWater() is
/// called, instead of just standing there. Falls back gracefully if a part is missing.
/// </summary>
public class WellRig : MonoBehaviour
{
    public Transform windlass, bucket, rope;
    public float bucketRestY = .82f, bucketDownY = -.35f;
    float drawT = -1f; float drawDuration = 2.2f;

    GameObject bucketWater;
    public void Wire(Transform axle, Transform line, Transform pail, GameObject water, float rest, float down)
    {
        windlass = axle; rope = line; bucket = pail; bucketWater = water;
        bucketRestY = rest; bucketDownY = down;
    }
    void Awake()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Windlass") windlass = t;
            else if (t.name == "Bucket") bucket = t;
            else if (t.name == "Rope") rope = t;
        }
        if (bucket) bucketRestY = bucket.localPosition.y;
    }

    /// <summary>Starts a lower-crank-raise sequence; call from a settler's FetchWater coroutine. Safe to call with
    /// no bucket/windlass present - it's then just a timer the caller can wait on.</summary>
    public void DrawWater() { if (!Drawing) { drawT = 0f; if (bucketWater) bucketWater.SetActive(false); } }
    public bool Drawing => drawT >= 0f && drawT < drawDuration;

    void Update()
    {
        if (drawT < 0f) return;
        drawT += Time.deltaTime;
        float p = Mathf.Clamp01(drawT / drawDuration);
        // down in the first third, pause, rise in the last third
        float lower = Mathf.Clamp01(p / .35f), raise = Mathf.Clamp01((p - .65f) / .35f);
        float depth = Mathf.SmoothStep(0, 1, lower) - Mathf.SmoothStep(0, 1, raise);
        if (bucket) bucket.localPosition = new Vector3(bucket.localPosition.x, Mathf.Lerp(bucketRestY, bucketDownY, depth), bucket.localPosition.z);
        if (rope && bucket) {
            float bottom = bucket.localPosition.y + .52f;
            rope.localPosition = new Vector3(0, bottom, 0);
            rope.localScale = new Vector3(rope.localScale.x, Mathf.Max(.02f, 1.6f - bottom), rope.localScale.z);
        }
        if (bucketWater && p > .65f) bucketWater.SetActive(true);
        if (windlass) windlass.Rotate(Vector3.right, (depth > .5f ? -1 : 1) * 160f * Time.deltaTime, Space.Self);
        if (drawT >= drawDuration) drawT = -1f;
    }
}
