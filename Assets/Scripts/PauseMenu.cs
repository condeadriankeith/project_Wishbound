using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// General in-game pause overlay (ESC). Shows the same settings controls as the title screen
/// (volume, look speed, fullscreen) plus RESUME and QUIT. Pauses game time while open and
/// restores it cleanly on resume; QUIT plays the shared WishboundFarewell sequence and exits the game.
/// Built as its own canvas so it never depends on WishboundIntro's (which is torn down after the intro).
/// </summary>
public sealed class PauseMenu : MonoBehaviour
{
    sealed class Choice
    {
        public Text text; public CanvasGroup group; public RectTransform rect;
        public Shadow[] glow; public Action action; public float heat;
    }

    public static PauseMenu Instance { get; private set; }
    public static bool IsOpen => Instance && Instance.open;

    Canvas canvas; RectTransform root; Image dim; CanvasGroup panelGroup; RectTransform panel;
    readonly List<Choice> items = new List<Choice>();
    Font font;
    bool open, busy;
    int selected = -1;
    float priorTimeScale = 1f;
    CursorLockMode priorLockState; bool priorCursorVisible;
    IslandThirdPersonController player;

    public static void Toggle()
    {
        if (!Instance)
        {
            var go = new GameObject("PauseMenu"); DontDestroyOnLoad(go);
            Instance = go.AddComponent<PauseMenu>();
            Instance.Build();
        }
        if (Instance.busy) return;
        if (Instance.open) Instance.Close(); else Instance.Open();
    }

    void Build()
    {
        font = Resources.Load<Font>("Fonts/CutoutChaos");
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject("PauseCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 600;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        go.AddComponent<GraphicRaycaster>();
        root = (RectTransform)go.transform;

        var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dimGo.transform.SetParent(root, false);
        var dr = (RectTransform)dimGo.transform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
        dim = dimGo.GetComponent<Image>(); dim.color = new Color(0, 0, 0, 0); dim.raycastTarget = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
        panelGo.transform.SetParent(root, false);
        panel = (RectTransform)panelGo.transform; panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(900, 740);
        panelGroup = panelGo.GetComponent<CanvasGroup>(); panelGroup.alpha = 0; panelGroup.blocksRaycasts = false; panelGroup.interactable = false;

        var heading = MakeChoice("PAUSED", new Vector2(0, 280), new Vector2(800, 90), null);
        heading.text.fontSize = 58; heading.group.alpha = 1; heading.group.blocksRaycasts = false;

        items.Add(MakeChoice("RESUME", new Vector2(0, 165), new Vector2(420, 74), Resume));
        items.Add(MakeChoice("", new Vector2(0, 65), new Vector2(600, 66), () => ChangeVolume(.1f)));
        items.Add(MakeChoice("", new Vector2(0, -20), new Vector2(600, 66), () => ChangeSensitivity(.02f)));
        items.Add(MakeChoice("", new Vector2(0, -105), new Vector2(600, 66), () => { WishboundSettings.SetFullscreen(!Screen.fullScreen); Refresh(); }));
        items.Add(MakeChoice("QUIT", new Vector2(0, -225), new Vector2(420, 70), RequestQuit));

        var help = MakeChoice("UP / DOWN TO SELECT   -   LEFT / RIGHT TO ADJUST   -   ESC TO RESUME", new Vector2(0, -320), new Vector2(900, 45), null);
        help.text.fontSize = 20; help.group.alpha = .65f; help.group.blocksRaycasts = false;

        foreach (var c in items) { c.group.alpha = 1; c.group.blocksRaycasts = true; }
        Refresh();
    }

    Choice MakeChoice(string caption, Vector2 pos, Vector2 size, Action action)
    {
        var go = new GameObject(caption.Length > 0 ? caption : "Setting", typeof(RectTransform), typeof(CanvasGroup), typeof(Text));
        go.transform.SetParent(panel, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = pos; r.sizeDelta = size;
        var t = go.GetComponent<Text>(); t.text = caption; t.font = font; t.fontSize = 44; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
        var shadows = new Shadow[4]; Vector2[] offsets = { new Vector2(2, 0), new Vector2(-2, 0), new Vector2(0, 2), new Vector2(0, -2) };
        for (int i = 0; i < 4; i++) { shadows[i] = go.AddComponent<Shadow>(); shadows[i].effectDistance = offsets[i]; shadows[i].effectColor = Color.clear; }
        var c = new Choice { text = t, rect = r, group = go.GetComponent<CanvasGroup>(), action = action, glow = shadows };
        return c;
    }

    void Open()
    {
        if (!player) player = FindFirstObjectByType<IslandThirdPersonController>();
        open = true; busy = true; selected = 0;
        priorTimeScale = Time.timeScale;
        priorLockState = Cursor.lockState; priorCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Refresh();
        StartCoroutine(Fade(0f, 1f, () => { busy = false; }));
    }

    void Close()
    {
        busy = true;
        StartCoroutine(Fade(1f, 0f, () =>
        {
            open = false; busy = false;
            Time.timeScale = priorTimeScale;
            if (player && player.enabled) player.CaptureCursor();   // back to mouse-look
            else { Cursor.lockState = priorLockState; Cursor.visible = priorCursorVisible; }
        }));
    }

    void Resume() { if (!busy) Close(); }

    void RequestQuit()
    {
        if (busy) return;
        busy = true;   // input is dead from here: Update() returns while busy and the farewell canvas swallows clicks
        selected = -1;
        // the wisp leaves from the QUIT button, then the screen fades out and the game really exits
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, items[items.Count - 1].rect.position);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        WishboundFarewell.Play(screen);
    }

    IEnumerator Fade(float from, float to, Action onDone)
    {
        // Pause control (time scale) takes effect immediately on open so no gameplay advances while fading in;
        // on close we restore afterward so the fade itself still plays at normal UI (unscaled) speed.
        if (to > from) Time.timeScale = 0f;
        const float D = .28f;
        for (float t = 0; t < D; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / D);
            float a = Mathf.Lerp(from, to, k);
            panelGroup.alpha = a; panelGroup.blocksRaycasts = panelGroup.interactable = a > .5f;
            dim.color = new Color(0, 0, 0, a * .72f);
            yield return null;
        }
        panelGroup.alpha = to; panelGroup.blocksRaycasts = panelGroup.interactable = to > .5f;
        dim.color = new Color(0, 0, 0, to * .72f);
        onDone?.Invoke();
    }

    void Update()
    {
        if (!open || busy)
        {
            if (!open) return;
        }
        if (busy) return;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) { Resume(); return; }
            if (kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) selected = (selected + 1) % items.Count;
            if (kb.upArrowKey.wasPressedThisFrame) selected = (selected + items.Count - 1) % items.Count;
            if (selected >= 0 && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
            {
                float sign = kb.leftArrowKey.wasPressedThisFrame ? -1 : 1;
                if (selected == 1) ChangeVolume(sign * .1f);
                else if (selected == 2) ChangeSensitivity(sign * .02f);
                else if (selected == 3) items[selected].action();
            }
            if (selected >= 0 && kb.enterKey.wasPressedThisFrame) items[selected].action?.Invoke();
        }
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 screen = mouse.position.ReadValue();
            Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local);
            for (int i = 0; i < items.Count; i++)
            {
                if (string.IsNullOrEmpty(items[i].text.text) && items[i].action == null) continue;
                Vector2 centre = root.InverseTransformPoint(items[i].rect.position);
                float halfW = items[i].rect.rect.width * .5f, halfH = 28;
                if (Mathf.Abs(local.x - centre.x) < halfW && Mathf.Abs(local.y - centre.y) < halfH) { selected = i; if (mouse.leftButton.wasPressedThisFrame) items[i].action?.Invoke(); }
            }
        }
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i];
            if (c.action == null) continue;
            c.heat = Mathf.Lerp(c.heat, selected == i ? 1 : 0, 1 - Mathf.Exp(-9 * Time.unscaledDeltaTime));
            c.text.color = Color.Lerp(Color.white, new Color(1, .89f, .57f), c.heat);
            c.rect.localScale = Vector3.one * (1 + .045f * c.heat);
            foreach (var s in c.glow) s.effectColor = new Color(1, .66f, .25f, c.heat * .65f);
        }
    }

    void ChangeVolume(float delta) { WishboundSettings.SetVolume(WishboundSettings.Volume + delta); Refresh(); }
    void ChangeSensitivity(float delta) { WishboundSettings.SetSensitivity(WishboundSettings.Sensitivity + delta, player); Refresh(); }

    void Refresh()
    {
        if (items.Count < 5) return;
        items[1].text.text = "VOLUME   " + Mathf.RoundToInt(WishboundSettings.Volume * 100) + "%";
        items[2].text.text = "LOOK SPEED   " + (WishboundSettings.Sensitivity / .15f).ToString("0.0") + "X";
        items[3].text.text = "FULLSCREEN   " + (Screen.fullScreen ? "ON" : "OFF");
    }
}
