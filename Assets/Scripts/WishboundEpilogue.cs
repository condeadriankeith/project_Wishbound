using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Route ending overlay and clean scene reload. Never auto-selects the next wish.</summary>
public sealed class WishboundEpilogue : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static StarAlignment SuggestedRoute { get; private set; }
    static bool replayRequested;
    readonly Text[] options = new Text[3];
    int selected;
    bool busy;
    StarAlignment route;
    IslandThirdPersonController player;
    float age;

    public static void Show(StarAlignment ending)
    {
        if (Active) return;
        var root = new GameObject("Wishbound epilogue", typeof(RectTransform));
        root.AddComponent<WishboundEpilogue>().Build(ending);
    }

    public static bool ConsumeReplayRequest()
    {
        bool result = replayRequested;
        replayRequested = false;
        return result;
    }

    void Build(StarAlignment ending)
    {
        Active = true; route = ending;
        player = FindFirstObjectByType<IslandThirdPersonController>();
        if (player) player.enabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        WispCursor.Hidden = false;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080);
        var background = new GameObject("Night veil", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(transform, false);
        var rect = (RectTransform)background.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = ending == StarAlignment.Tether ? new Color(.025f,.04f,.10f,.94f) : new Color(.10f,.055f,.025f,.94f);
        Label("Closing line", ending == StarAlignment.Tether ? "Some lights find their way home." : "Some lights make a home.", 48, .64f);
        Label("Route", ending.ToString().ToUpperInvariant(), 24, .73f);
        options[0] = Label("Replay", "REPLAY", 30, .40f);
        options[1] = Label("Other wish", "EXPERIENCE THE OTHER WISH", 30, .32f);
        options[2] = Label("Quit", "QUIT", 30, .24f);
        Label("Controls", "Arrow keys / D-pad · Confirm: Enter / A", 20, .12f);
    }

    Text Label(string name, string text, int size, float y)
    {
        var label = WishboundUI.Label(transform, name, text, size, new Vector2(.5f,y),new Vector2(1500,90));
        label.GetComponent<CanvasGroup>().alpha = 1;
        return label;
    }

    void Update()
    {
        if (busy) return;
        age += Time.unscaledDeltaTime;
        var pad = Gamepad.current; 
        if ((WishboundInput.Pressed(Key.DownArrow, KeyCode.DownArrow)) || (pad != null && pad.dpad.down.wasPressedThisFrame)) selected = (selected+1)%3;
        if ((WishboundInput.Pressed(Key.UpArrow, KeyCode.UpArrow)) || (pad != null && pad.dpad.up.wasPressedThisFrame)) selected = (selected+2)%3;
        if (WishboundInput.MouseDelta.sqrMagnitude > 0)
            for(int i=0;i<3;i++) if(RectTransformUtility.RectangleContainsScreenPoint(options[i].rectTransform,WishboundInput.Pointer)) selected=i;
        for(int i=0;i<3;i++) options[i].color = i==selected ? new Color(1,.82f,.45f) : new Color(.78f,.80f,.82f);
        var point = RectTransformUtility.WorldToScreenPoint(null,options[selected].transform.position);
        WispCursor.ScreenOverride = point + Vector2.left * 270f * Screen.width / 1920f;
        bool click = WishboundInput.PrimaryPressed && RectTransformUtility.RectangleContainsScreenPoint(options[selected].rectTransform,WishboundInput.Pointer);
        bool confirm = (WishboundInput.Pressed(Key.Enter, KeyCode.Return)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        if (age > 1 && (click || confirm)) Select(selected);
    }

    public void Select(int option)
    {
        if (busy || option < 0 || option > 2) return;
        busy = true;
        if (option == 2) { WishboundFarewell.Play(WispCursor.ScreenOverride ?? new Vector2(Screen.width*.5f,Screen.height*.25f)); return; }
        SuggestedRoute = option == 1 ? (route == StarAlignment.Tether ? StarAlignment.Roots : StarAlignment.Tether) : route;
        StartCoroutine(Reload());
    }

    IEnumerator Reload()
    {
        replayRequested = true;
        Time.timeScale = 1;
        StoryFlags.NewDemoRun();
        WispCursor.ScreenOverride = null; WispCursor.Hidden = false;
        if (PauseMenu.Instance) Destroy(PauseMenu.Instance.gameObject);
        // Scene reload resets every quest actor, cinematic and generated prop together.
        yield return SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }

    void OnDestroy() { Active = false; WispCursor.ScreenOverride = null; }
}
