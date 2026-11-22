using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The QUIT farewell, shared by the title screen and the pause menu.
/// Input is locked, the wisp leaves the QUIT button, the whole screen slowly fades to black until only the glowing wisp
/// remains, the wisp flies off screen, and then the game actually exits.
/// Runs on unscaled time and on its own top-most canvas, so it works while the game is paused (timeScale 0).
/// </summary>
public sealed class WishboundFarewell : MonoBehaviour
{
    static bool running;
    public static bool IsPlaying => running;

    const float FadeTime = 3.4f, HoldTime = 1.3f, FlyTime = 1.5f, EndTime = .7f;

    /// <param name="screenStart">Screen position (pixels) the wisp leaves from: the QUIT button.</param>
    public static void Play(Vector2 screenStart)
    {
        if (running) return;
        running = true; WispCursor.Claims++;
        var go = new GameObject("Wishbound Farewell");
        DontDestroyOnLoad(go);
        go.AddComponent<WishboundFarewell>().StartCoroutine(go.GetComponent<WishboundFarewell>().Run(screenStart));
    }

    Image black; RawImage glow, wispImage; readonly List<Image> trail = new List<Image>();
    readonly List<Vector2> history = new List<Vector2>();
    Camera cam; RenderTexture rt; GuideWisp wisp; Texture2D glowTex, moteTex; Sprite moteSprite;
    Material additive; float sc = 1f;

    static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }

    void Build()
    {
        sc = Screen.height / 1080f;
        var cgo = new GameObject("Farewell canvas");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5000;
        cgo.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)cgo.transform;

        // full-screen black: also swallows every click while it plays
        var bgo = new GameObject("Black", typeof(RectTransform), typeof(Image)); bgo.transform.SetParent(root, false);
        var br = (RectTransform)bgo.transform; br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = br.offsetMax = Vector2.zero;
        black = bgo.GetComponent<Image>(); black.color = new Color(0, 0, 0, 0); black.raycastTarget = true;

        // soft halo behind the wisp
        glowTex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var px = new Color[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        { float r = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f; px[y * 64 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - r), 2.2f)); }
        glowTex.SetPixels(px); glowTex.Apply(); glowTex.wrapMode = TextureWrapMode.Clamp;
        glow = NewRaw("Halo", root, glowTex); glow.color = Color.clear;

        // trailing sparks
        moteTex = glowTex; moteSprite = Sprite.Create(glowTex, new Rect(0, 0, 64, 64), Vector2.one * .5f);
        for (int i = 0; i < 14; i++)
        {
            var m = new GameObject("Spark", typeof(RectTransform), typeof(Image)); m.transform.SetParent(root, false);
            var mr = (RectTransform)m.transform; mr.anchorMin = mr.anchorMax = Vector2.zero; mr.pivot = Vector2.one * .5f;
            var im = m.GetComponent<Image>(); im.sprite = moteSprite; im.color = Color.clear; im.raycastTarget = false; trail.Add(im);
        }

        // the wisp itself: same GuideWisp visuals the title screen uses, rendered by its own camera
        GuideWisp source = null; Material halo = null, spark = null;
        foreach (var g in FindObjectsByType<GuideWisp>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (g.haloMaterial && g.sparkMaterial) { source = g; break; }
        if (source) { halo = source.haloMaterial; spark = source.sparkMaterial; }
        else
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            { if (!halo && m.name == "StarHalo") halo = m; if (!spark && m.name == "CometEmber") spark = m; }

        var host = new GameObject("Farewell wisp"); wisp = host.AddComponent<GuideWisp>();
        wisp.haloMaterial = halo; wisp.sparkMaterial = spark; if (source) wisp.colour = source.colour;
        wisp.MenuMode = true;

        var camObject = new GameObject("Farewell wisp camera"); cam = camObject.AddComponent<Camera>();
        cam.transform.position = new Vector3(20000, 20000, -20); cam.orthographic = true; cam.orthographicSize = 5.4f;
        cam.nearClipPlane = .1f; cam.farClipPlane = 40; cam.cullingMask = 1 << 31;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.clear; cam.allowHDR = false; cam.allowMSAA = false;
        var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
        int h = Mathf.Min(Screen.height, 1080), w = Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height));
        rt = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 16, RenderTextureFormat.ARGB32); rt.Create();
        cam.targetTexture = rt; cam.aspect = (float)w / Mathf.Max(1, h);

        wispImage = NewRaw("Wisp", root, rt);
        var rr = (RectTransform)wispImage.transform; rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.pivot = Vector2.one * .5f; rr.offsetMin = rr.offsetMax = Vector2.zero;
        var shader = Resources.Load<Shader>("WishMenuAdditive");
        additive = new Material(shader ? shader : Shader.Find("UI/Default"));
        wispImage.material = additive; wispImage.color = Color.white;
    }

    RawImage NewRaw(string name, Transform parent, Texture tex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.one * .5f;
        var ri = go.GetComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false; return ri;
    }

    void Place(Vector2 px, float glowAlpha, float glowSize, float trailAmount)
    {
        Vector3 at = cam.ViewportToWorldPoint(new Vector3(px.x / Screen.width, px.y / Screen.height, 20));
        wisp.SetMenuPose(at, 1f);
        var gr = (RectTransform)glow.transform; gr.anchoredPosition = px; gr.sizeDelta = Vector2.one * glowSize * sc;
        glow.color = new Color(1f, .72f, .32f, glowAlpha);
        history.Insert(0, px); if (history.Count > 60) history.RemoveAt(history.Count - 1);
        for (int i = 0; i < trail.Count; i++)
        {
            int idx = Mathf.Min(history.Count - 1, (i + 1) * 3);
            float f = 1f - i / (float)trail.Count;
            var tr = (RectTransform)trail[i].transform; tr.anchoredPosition = history[idx]; tr.sizeDelta = Vector2.one * (34 * f + 8) * sc;
            trail[i].color = new Color(1f, .8f, .42f, f * .55f * trailAmount);
        }
    }

    IEnumerator Run(Vector2 start)
    {
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        Build();
        float W = Screen.width, H = Screen.height;
        Vector2 hover = new Vector2(W * .5f, H * .52f);
        float startVolume = AudioListener.volume;

        // 1. the wisp leaves the QUIT button while the screen slowly goes black
        for (float t = 0; t < FadeTime; t += Time.unscaledDeltaTime)
        {
            float k = Ease(t / FadeTime), u = Ease(t / (FadeTime * .85f));
            Vector2 p = Vector2.Lerp(start, hover, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * 90f * sc + Vector2.right * Mathf.Sin(t * 2.1f) * 14f * sc * u;
            black.color = new Color(0, 0, 0, k);
            AudioListener.volume = startVolume * (1 - k);
            Place(p, .35f * k, 220f, .25f * (1 - u));
            yield return null;
        }
        black.color = Color.black; AudioListener.volume = 0;

        // 2. only the wisp remains, glowing
        Vector2 last = hover;
        for (float t = 0; t < HoldTime; t += Time.unscaledDeltaTime)
        {
            float k = Ease(t / HoldTime);
            last = hover + new Vector2(Mathf.Sin(t * 1.7f) * 10f, Mathf.Sin(t * 2.3f) * 14f) * sc;
            Place(last, Mathf.Lerp(.35f, .85f, k) * (.92f + .08f * Mathf.Sin(t * 6f)), Mathf.Lerp(220f, 420f, k), 0f);
            yield return null;
        }

        // 3. the wisp flies off screen
        Vector2 ctrl = last + new Vector2(-W * .07f, -H * .14f), exit = new Vector2(W * 1.2f, H * 1.25f);
        for (float t = 0; t < FlyTime; t += Time.unscaledDeltaTime)
        {
            float u = t / FlyTime, e = Mathf.Pow(u, 2.3f);
            Vector2 p = (1 - e) * (1 - e) * last + 2 * (1 - e) * e * ctrl + e * e * exit;
            Place(p, .85f, 420f, Mathf.Clamp01(u * 3f));
            yield return null;
        }
        Place(exit, 0f, 420f, 0f);
        wispImage.color = Color.clear;

        // 4. a breath of darkness, then the game closes
        yield return new WaitForSecondsRealtime(EndTime);
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy()
    {
        running = false;
        if (cam) { cam.targetTexture = null; Destroy(cam.gameObject); }
        if (wisp) Destroy(wisp.gameObject);
        if (rt) { rt.Release(); Destroy(rt); }
        if (additive) Destroy(additive);
        if (moteSprite) Destroy(moteSprite);
        if (glowTex) Destroy(glowTex);
    }
}
