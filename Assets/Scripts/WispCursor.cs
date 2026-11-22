using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The one pointer used everywhere the mouse is free (pause menu, settings, any UI). It is a real GuideWisp running in
/// MenuMode, so it is built from WispPreset exactly like the tutorial wisp: same layers, motes, trail and sparks.
/// The OS cursor is hidden whenever this is showing. Anything that draws its own wisp pointer (title menu, farewell)
/// raises Claims so two are never shown; cutscenes raise Hidden to take the pointer away completely.
/// </summary>
public sealed class WispCursor : MonoBehaviour
{
    public static int Claims;
    public static bool Hidden;
    public static Vector2? ScreenOverride;   // set by the title menu so the one cursor wisp follows its magnetic pointer
    public static WispCursor Instance { get; private set; }

    GuideWisp wisp; Camera cam; RenderTexture rt; RawImage image; Material additive; Canvas canvas;
    float fade, nextTry; bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance) return;
        var go = new GameObject("WispCursor"); DontDestroyOnLoad(go); go.AddComponent<WispCursor>();
    }

    void Awake() { if (Instance && Instance != this) { Destroy(gameObject); return; } Instance = this; Claims = 0; Hidden = false; }

    bool Want => Claims <= 0 && !Hidden && (!WishboundIntro.IsPlaying || ScreenOverride.HasValue) && !WishboundFarewell.IsPlaying
                 && Cursor.lockState != CursorLockMode.Locked && Application.isFocused && Mouse.current != null;

    void LateUpdate()
    {
        bool want = Want;
        if (!built) { if (want && Time.unscaledTime > nextTry) TryBuild(); return; }
        fade = Mathf.MoveTowards(fade, want ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        bool show = fade > .001f;
        image.enabled = show; cam.enabled = show;
        if (!show) return;
        if (want) Cursor.visible = false;
        if (Mathf.Abs(cam.aspect - (float)Screen.width / Mathf.Max(1, Screen.height)) > .01f) Resize();
        if (Mouse.current == null && !ScreenOverride.HasValue) return;
        Vector2 p = ScreenOverride ?? Mouse.current.position.ReadValue();
        Vector3 at = cam.ViewportToWorldPoint(new Vector3(p.x / Screen.width, p.y / Screen.height, 20f));
        wisp.SetMenuPose(at, fade);
        image.color = new Color(1, 1, 1, fade);
    }

    void TryBuild()
    {
        nextTry = Time.unscaledTime + 1f;
        Material halo = null, spark = null; Color col = WispPreset.Colour;
        foreach (var g in FindObjectsByType<GuideWisp>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g.haloMaterial && !g.MenuMode) { halo = g.haloMaterial; spark = g.sparkMaterial; col = g.colour; break; }
        if (!halo) { halo = Resources.Load<Material>("Art/Star/StarHalo"); spark = Resources.Load<Material>("Art/Comet/CometEmber"); }   // title screen has no tutorial wisp
        var shader = Resources.Load<Shader>("WishMenuAdditive");
        if (!halo || !shader) return;

        var host = new GameObject("Cursor wisp (GuideWisp visuals)"); host.transform.SetParent(transform, false);
        wisp = host.AddComponent<GuideWisp>();
        wisp.haloMaterial = halo; wisp.sparkMaterial = spark; wisp.colour = col; wisp.MenuMode = true;

        var co = new GameObject("Cursor wisp camera"); co.transform.SetParent(transform, false);
        co.transform.position = new Vector3(10000, 10000, -20);
        cam = co.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5.4f; cam.nearClipPlane = .1f; cam.farClipPlane = 40;
        cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.allowHDR = true; cam.allowMSAA = false;
        var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;

        var cv = new GameObject("Cursor canvas"); cv.transform.SetParent(transform, false);
        canvas = cv.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        var go = new GameObject("Cursor overlay", typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(cv.transform, false);
        var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        image = go.GetComponent<RawImage>(); image.raycastTarget = false; image.color = Color.clear;
        additive = new Material(shader); image.material = additive;
        Resize();
        built = true;
    }

    void Resize()
    {
        if (rt) { cam.targetTexture = null; rt.Release(); Destroy(rt); }
        int h = Mathf.Min(Screen.height, 1080), w = Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height));
        rt = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 16, RenderTextureFormat.ARGBHalf); rt.Create();
        cam.targetTexture = rt; cam.aspect = (float)w / Mathf.Max(1, h); image.texture = rt;
    }

    void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (additive) Destroy(additive); if (Instance == this) Instance = null; }
}
