using UnityEngine;
using UnityEngine.UI;

/// <summary>Minimal chat above a settler's head: a few words of white scribbled handwriting, no box. Fades in and out,
/// and only shows when the camera is close enough to read it.</summary>
public class SpeechBubble : MonoBehaviour
{
    Transform speaker; Text text; CanvasGroup group; float born, until, height; public float maxDistance = 26f;

    public static SpeechBubble Say(Transform speaker, string line, float seconds, float height)
    {
        var t = WishboundUI.Label(null, "Speech", line, 30, new Vector2(.5f, .5f), new Vector2(500, 80));
        var b = t.gameObject.AddComponent<SpeechBubble>();
        b.speaker = speaker; b.text = t; b.group = t.GetComponent<CanvasGroup>(); b.born = Time.time; b.until = Time.time + seconds; b.height = height;
        var s = t.GetComponent<ScribbleText>(); s.amount = .04f; s.boilsPerSecond = 6;
        return b;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (!speaker || !cam || Time.time > until) { Destroy(gameObject); return; }
        Vector3 at = speaker.position + Vector3.up * height;
        Vector3 vp = cam.WorldToViewportPoint(at);
        var r = (RectTransform)transform; r.anchorMin = r.anchorMax = new Vector2(vp.x, vp.y);
        float dist = Vector3.Distance(cam.transform.position, at);
        float a = Mathf.Clamp01((Time.time - born) / .3f) * Mathf.Clamp01((until - Time.time) / .5f) * Mathf.InverseLerp(maxDistance, maxDistance * .7f, dist);
        group.alpha = vp.z > 0 ? a : 0;
        text.fontSize = Mathf.RoundToInt(Mathf.Lerp(34, 24, Mathf.InverseLerp(8, maxDistance, dist)));
    }
}
