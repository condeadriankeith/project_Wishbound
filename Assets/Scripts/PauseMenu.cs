using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// General in-game pause overlay (ESC). Shows the same settings controls as the title screen
/// (volume, look speed, fullscreen) plus RESUME and QUIT, with the same magnetic wisp pointer (WispMagnet).
/// Pauses game time while open and restores it cleanly on resume; QUIT plays the shared WishboundFarewell
/// sequence and exits the game.
/// Built as its own canvas so it never depends on WishboundIntro's (which is torn down after the intro).
/// </summary>
public sealed class PauseMenu : MonoBehaviour
{
    sealed class Choice : WispWord
    {
        public CanvasGroup group; public Action action;
    }

    public static PauseMenu Instance { get; private set; }
    public static bool IsOpen => Instance && Instance.open;

    WishProgressPage progressPage;
    int pageClosedFrame=-1;
    Canvas canvas; RectTransform root; Image dim; CanvasGroup panelGroup; RectTransform panel;
    readonly List<Choice> items = new List<Choice>();
    readonly WispMagnet magnet = new WispMagnet();
    Font font;
    bool open, busy, keyboardSelection, pointerSeen;
    Vector2 previousPointer;
    float priorTimeScale = 1f;
    CursorLockMode priorLockState; bool priorCursorVisible;
    IslandThirdPersonController player;

    public static void Toggle()
    {
        if (WishboundEpilogue.Active) return;
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
        items.Add(MakeChoice("SAVED PROGRESS",new Vector2(0,-175),new Vector2(640,66),OpenProgress));
        items.Add(MakeChoice("QUIT", new Vector2(0, -250), new Vector2(420, 70), RequestQuit));

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
        return new Choice { text = t, rect = r, group = go.GetComponent<CanvasGroup>(), action = action, glow = action != null ? WispMagnet.AddGlow(go) : System.Array.Empty<Shadow>() };
    }

    static int Step(int from, int dir, int count) { if (from < 0) return dir > 0 ? 0 : count - 1; return (from + dir + count) % count; }

    void Open()
    {
        if (!player) player = FindFirstObjectByType<IslandThirdPersonController>();
        if(player) StoryFlags.RememberPosition(player.transform);
        open = true; busy = true;
        priorTimeScale = Time.timeScale;
        priorLockState = Cursor.lockState; priorCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        magnet.Reset();
        foreach (var c in items) c.heat = 0;
        magnet.Selected = 0; keyboardSelection = true; pointerSeen = false;   // RESUME starts focused, as before
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, WishboundInput.Pointer, null, out previousPointer);
            pointerSeen = true;
        }
        WispCursor.ScreenOverride = RectTransformUtility.WorldToScreenPoint(null, items[0].rect.position);
        Refresh();
        StartCoroutine(Fade(0f, 1f, () => { busy = false; }));
    }

    void Close()
    {
        busy = true;
        WispCursor.ScreenOverride = null;   // the wisp drifts back to the mouse while the screen fades
        StartCoroutine(Fade(1f, 0f, () =>
        {
            open = false; busy = false;
            Time.timeScale = priorTimeScale;
            if (player && player.enabled) player.CaptureCursor();   // back to mouse-look
            else { Cursor.lockState = priorLockState; Cursor.visible = priorCursorVisible; }
        }));
    }

    void OpenProgress()
    {
        panel.gameObject.SetActive(false);
        progressPage=WishProgressPage.Open(root,font,changed=>{
            progressPage=null;pageClosedFrame=Time.frameCount;
            panel.gameObject.SetActive(true);magnet.Reset();
            if(!changed)return;
            StoryFlags.NewDemoRun(); Time.timeScale=1; open=false;
            Shader.SetGlobalFloat("_WishMenuBlur",0);WispCursor.ScreenOverride=null;
            WishboundIntro.QueueFinaleTitle();Destroy(gameObject);
            UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        });
    }
    void Resume() { if (!busy) Close(); }

    void RequestQuit()
    {
        if (busy) return;
        busy = true;   // input is dead from here: Update() returns while busy and the farewell canvas swallows clicks
        magnet.Reset();
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
            Shader.SetGlobalFloat("_WishMenuBlur",a);
            dim.color = new Color(.012f,.019f,.042f,a*.68f);
            yield return null;
        }
        panelGroup.alpha = to; panelGroup.blocksRaycasts = panelGroup.interactable = to > .5f;
        Shader.SetGlobalFloat("_WishMenuBlur",to);
        dim.color = new Color(.012f,.019f,.042f,to*.68f);
        onDone?.Invoke();
    }

    void Update()
    {
        if (!open || busy || progressPage || pageClosedFrame==Time.frameCount) return;




        Vector2 pointer = Vector2.zero;
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, WishboundInput.Pointer, null, out pointer);
            if (!pointerSeen) { previousPointer = pointer; pointerSeen = true; }
            if ((pointer - previousPointer).sqrMagnitude > 2f) keyboardSelection = false;   // the mouse takes over once it moves
            previousPointer = pointer;
        }

        {
            if (WishboundInput.Pressed(Key.Escape, KeyCode.Escape)) { Resume(); return; }
            if (WishboundInput.Pressed(Key.DownArrow, KeyCode.DownArrow) || WishboundInput.Pressed(Key.Tab, KeyCode.Tab)) { magnet.Selected = Step(magnet.Selected, 1, items.Count); keyboardSelection = true; }
            if (WishboundInput.Pressed(Key.UpArrow, KeyCode.UpArrow)) { magnet.Selected = Step(magnet.Selected, -1, items.Count); keyboardSelection = true; }
            if (magnet.Selected >= 0 && (WishboundInput.Pressed(Key.LeftArrow, KeyCode.LeftArrow) || WishboundInput.Pressed(Key.RightArrow, KeyCode.RightArrow)))
            {
                float sign = WishboundInput.Pressed(Key.LeftArrow, KeyCode.LeftArrow) ? -1 : 1;
                if (magnet.Selected == 1) ChangeVolume(sign * .1f);
                else if (magnet.Selected == 2) ChangeSensitivity(sign * .02f);
                else if (magnet.Selected == 3) items[3].action();
            }
        }

        magnet.Track(root, items, pointer, Time.unscaledDeltaTime, keyboardSelection);
        WispCursor.ScreenOverride = magnet.ScreenPoint(root);

        int sel = magnet.Selected;
        bool clicked = WishboundInput.PrimaryPressed;
        bool entered = WishboundInput.Pressed(Key.Enter, KeyCode.Return);
        if (sel >= 0 && (clicked || entered)) { WorldSound.Menu("Select"); items[sel].action?.Invoke(); }
    }

    void ChangeVolume(float delta) { WorldSound.Menu("Adjust"); WishboundSettings.SetVolume(WishboundSettings.Volume + delta); Refresh(); }
    void ChangeSensitivity(float delta) { WorldSound.Menu("Adjust"); WishboundSettings.SetSensitivity(WishboundSettings.Sensitivity + delta, player); Refresh(); }

    void Refresh()
    {
        if (items.Count < 5) return;
        items[1].text.text = "VOLUME   " + Mathf.RoundToInt(WishboundSettings.Volume * 100) + "%";
        items[2].text.text = "LOOK SPEED   " + (WishboundSettings.Sensitivity / .15f).ToString("0.0") + "X";
        items[3].text.text = "FULLSCREEN   " + (Screen.fullScreen ? "ON" : "OFF");
    }
}
