using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hand-drawn "line boil" for UI Text: every vertex is displaced by a small noise offset that is re-rolled a few
/// times per second (like redrawing each frame of a 2D animation), so plain white handwriting wobbles and
/// scribbles without any outline / glow styling. Put it on the same GameObject as a UnityEngine.UI.Text.
/// </summary>
[RequireComponent(typeof(Text))]
[DisallowMultipleComponent]
public class ScribbleText : BaseMeshEffect
{
    [Tooltip("Displacement as a fraction of the font size.")]
    [Range(0, .2f)] public float amount = .045f;
    [Tooltip("How many times per second the drawing is 're-drawn' (6-12 feels hand-animated).")]
    [Range(1, 24)] public float boilsPerSecond = 8;
    [Tooltip("Slow continuous drift on top of the boil, so letters also gently sway.")]
    [Range(0, .1f)] public float sway = .02f;
    public bool useUnscaledTime = true;

    int frame = -1;
    readonly List<UIVertex> verts = new List<UIVertex>();

    void Update()
    {
        float t = useUnscaledTime ? Time.unscaledTime : Time.time;
        int f = Mathf.FloorToInt(t * boilsPerSecond);
        if (f != frame || sway > 0) { frame = f; if (graphic) graphic.SetVerticesDirty(); }
    }

    static float Hash(float n) { n = Mathf.Sin(n) * 43758.5453f; return n - Mathf.Floor(n); }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        var text = graphic as Text; float size = text ? text.fontSize : 40;
        float t = useUnscaledTime ? Time.unscaledTime : Time.time;
        verts.Clear(); vh.GetUIVertexStream(verts);
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            int glyph = i / 6;                 // 6 stream vertices per character quad
            int corner = i % 6;
            // corner ids so that the two triangles of a quad share their corner offsets (no tearing)
            int c = corner == 3 ? 2 : corner == 4 ? 3 : corner == 5 ? 0 : corner;
            float seed = glyph * 17.13f + c * 3.71f + frame * 91.7f;
            Vector2 boil = new Vector2(Hash(seed) - .5f, Hash(seed + 5.3f) - .5f) * 2f * amount * size;
            // keep the whole letter loosely together: a shared per-glyph jitter plus a smaller per-corner one
            float gs = glyph * 31.1f + frame * 47.3f;
            boil = boil * .55f + new Vector2(Hash(gs) - .5f, Hash(gs + 2.2f) - .5f) * 2f * amount * size * .45f;
            Vector2 drift = new Vector2(Mathf.Sin(t * 1.3f + glyph * .9f), Mathf.Sin(t * 1.7f + glyph * 1.3f)) * sway * size;
            v.position += (Vector3)(boil + drift);
            verts[i] = v;
        }
        vh.Clear(); vh.AddUIVertexTriangleStream(verts);
    }
}

/// <summary>Shared minimal UI for Wishbound: plain white handwriting with the scribble boil.</summary>
public static class WishboundUI
{
    public static Font Font;   // assigned by WishboundIntro / ChildEncounter from Assets/Fonts/UglyHandwriting.ttf

    static Canvas canvas;
    public static Canvas Canvas
    {
        get
        {
            if (canvas) return canvas;
            var go = new GameObject("WishboundUI");
            Object.DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
            var s = go.AddComponent<CanvasScaler>(); s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080); s.matchWidthOrHeight = .5f;
            return canvas;
        }
    }

    /// <summary>Creates a white, unstyled, scribbling label. anchor is in 0..1 screen space.</summary>
    public static Text Label(Transform parent, string name, string content, int size, Vector2 anchor, Vector2 box, bool shadow = true)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent ? parent : Canvas.transform, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = anchor; r.sizeDelta = box;
        if (!parent && anchor.y <= .22f) go.AddComponent<LetterboxLift>().baseY = anchor.y;
        var t = go.AddComponent<Text>();
        t.font = Font ? Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content; t.fontSize = size; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
        go.AddComponent<ScribbleText>();
        if (shadow)
        {
            var shade = go.AddComponent<Shadow>();
            shade.effectColor = new Color(.025f,.018f,.03f,.72f);
            shade.effectDistance = new Vector2(1.5f,-2f);
        }
        go.AddComponent<CanvasGroup>().alpha = 0;
        return t;
    }
}


/// <summary>Keeps low-screen text just above the cutscene letterbox bars (uniform shift so stacked labels keep their spacing).</summary>
public sealed class LetterboxLift : MonoBehaviour
{
    public float baseY;
    void LateUpdate()
    {
        var r = (RectTransform)transform; float y = baseY + CutsceneDirector.BarFraction;
        if (!Mathf.Approximately(r.anchorMin.y, y)) { r.anchorMin = new Vector2(r.anchorMin.x, y); r.anchorMax = new Vector2(r.anchorMax.x, y); }
    }
}
