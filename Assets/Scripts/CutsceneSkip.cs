using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// The one skip system for every cutscene.
///
///   CutsceneSkip.Begin(onSkip);   // when a cutscene starts. onSkip must leave the game in the "after cutscene" state.
///   CutsceneSkip.End();           // when the cutscene ends by itself (safe to call more than once).
///
/// Two seconds after Begin, a small "Skip [ESC]" tag fades in at the lower right, just above the letterbox bars.
/// Esc (or a click, when the cursor is free) fires onSkip once and removes the tag.
/// Set Suppressed = true while something inside a cutscene needs Esc itself (title menu, choices).
/// </summary>
public class CutsceneSkip : MonoBehaviour
{
    public const float ShowDelay = 2f;
    const float BarFraction = .12f;   // bars are 10% (intro) / 10.5% (CutsceneDirector) of the screen height

    static CutsceneSkip inst;
    public static bool Active => inst != null;
    public static bool Suppressed;
    public static int LastFireFrame = -10;
    /// <summary>True on the frame a skip fired (and the next). Other Esc handlers (pause, cursor release) should ignore it.</summary>
    public static bool JustFired => Time.frameCount - LastFireFrame <= 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Suppressed = false; LastFireFrame = -10; }

    Action onSkip; float startedAt; CanvasGroup group; bool fired;

    public static void Begin(Action skipCallback)
    {
        End();
        var go = new GameObject("CutsceneSkip");
        inst = go.AddComponent<CutsceneSkip>();
        inst.onSkip = skipCallback; inst.startedAt = Time.unscaledTime;
        inst.Build();
    }

    public static void End()
    {
        Suppressed = false;
        if (!inst) { inst = null; return; }
        var g = inst.gameObject; inst = null; Destroy(g);
    }

    void Build()
    {
        var canvasGo = new GameObject("SkipCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;

        var root = new GameObject("SkipTag", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        root.transform.SetParent(canvasGo.transform, false);
        group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        var rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, BarFraction); rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-40f, 8f); rt.sizeDelta = new Vector2(150f, 40f);
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, .42f);
        root.GetComponent<Button>().onClick.AddListener(Fire);

        var label = Text(root.transform, "Label", "Skip", 22, new Color(1f, 1f, 1f, .92f), TextAnchor.MiddleLeft);
        var lrt = (RectTransform)label.transform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(16f, 0f); lrt.offsetMax = new Vector2(-62f, 0f);

        var key = new GameObject("Key", typeof(RectTransform), typeof(Image)); key.transform.SetParent(root.transform, false);
        var krt = (RectTransform)key.transform; krt.anchorMin = krt.anchorMax = new Vector2(1f, .5f); krt.pivot = new Vector2(1f, .5f);
        krt.anchoredPosition = new Vector2(-8f, 0f); krt.sizeDelta = new Vector2(48f, 26f);
        key.GetComponent<Image>().color = new Color(1f, 1f, 1f, .88f); key.GetComponent<Image>().raycastTarget = false;
        var kt = Text(key.transform, "KeyLabel", "ESC", 15, new Color(.1f, .1f, .13f, 1f), TextAnchor.MiddleCenter);
        var ktr = (RectTransform)kt.transform; ktr.anchorMin = Vector2.zero; ktr.anchorMax = Vector2.one; ktr.offsetMin = ktr.offsetMax = Vector2.zero;
    }

    static Text Text(Transform parent, string name, string s, int size, Color c, TextAnchor a)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>(); t.text = s; t.fontSize = size; t.color = c; t.alignment = a; t.raycastTarget = false;
        t.font = WishboundUI.Font ? WishboundUI.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return t;
    }

    void Update()
    {
        if (fired) return;
        bool show = Time.unscaledTime - startedAt >= ShowDelay && !Suppressed;
        if (group)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            group.blocksRaycasts = group.interactable = show;
        }
        // Esc only counts once the tag is showing, so a stray press at the start of a cutscene does nothing.
        if (show && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Fire();
    }

    void Fire()
    {
        if (fired) return;
        fired = true; LastFireFrame = Time.frameCount;
        var cb = onSkip;
        End();
        try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
    }
}
