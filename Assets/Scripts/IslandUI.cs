using UnityEngine;
using UnityEngine.UI;

/// <summary>Minimal overlay matching guide section 20: preset buttons, Orbit/Explore/Tour/Mood/Clouds.</summary>
public class IslandUI : MonoBehaviour
{
    IslandWorld world; MoodController mood; CameraDirector dir;
    Text subtitle; Button moodBtn; Button tourBtn; Button exploreBtn; Button orbitBtn; Button cloudBtn;
    GameObject topBar, botBar;
    int cloudMode = 0; // 0 Distant, 1 Off, 2 Subtle
    bool exploring;

    void Start()
    {
        world = FindFirstObjectByType<IslandWorld>();
        mood = FindFirstObjectByType<MoodController>();
        dir = FindFirstObjectByType<CameraDirector>();
        Build();
        if (dir) dir.OnTourChanged += OnTour;
    }

    void Build()
    {
        var canvasGO = new GameObject("IslandUI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        // header card
        var header = Panel(canvasGO.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -12), new Vector2(430, 64));
        var title = Label(header.transform, "Grand Spire of the Whispering Meadow", 15, true);
        title.rectTransform.anchoredPosition = new Vector2(10, -14);
        subtitle = Label(header.transform, "Drag to rotate - Scroll / pinch to zoom - Immense Ghibli Scale", 11, false);
        subtitle.rectTransform.anchoredPosition = new Vector2(10, -40);

        // button bar
        var bar = new GameObject("Bar"); bar.transform.SetParent(canvasGO.transform, false);
        var rt = bar.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(300, 60); rt.sizeDelta = new Vector2(900, 40);
        botBar = bar;
        string[] names = { "Spire Overlook", "Meadow Village", "Harbor Cove", "Peak Citadel", "Eagle Panorama" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            Btn(bar.transform, names[i], new Vector2(i * 128, 0), () => { if (world) world.FlyToPreset(k); });
        }
        orbitBtn = Btn(bar.transform, "Orbit: Off", new Vector2(5 * 128, 0), () =>
        {
            if (dir == null) return;
            dir.autoRotate = !dir.autoRotate;
            orbitBtn.GetComponentInChildren<Text>().text = dir.autoRotate ? "Orbit: On" : "Orbit: Off";
        });
        exploreBtn = Btn(bar.transform, "Explore", new Vector2(5 * 128 + 110, 0), () =>
        {
            exploring = !exploring;
            if (dir) dir.EnterPlay(exploring);
            exploreBtn.GetComponentInChildren<Text>().text = exploring ? "Exit Walk" : "Explore";
            if (subtitle) subtitle.text = exploring ? "WASD to walk - Shift to run - Q/E rotate - Wheel zoom" : "Drag to rotate - Scroll / pinch to zoom - Immense Ghibli Scale";
        });
        tourBtn = Btn(bar.transform, "Tour", new Vector2(5 * 128 + 220, 0), () =>
        {
            if (dir == null) return;
            dir.SetTour(!dir.IsTouring);
        });
        moodBtn = Btn(bar.transform, "Mood: Day", new Vector2(5 * 128 + 330, 0), () =>
        {
            if (mood == null) return;
            mood.Next();
            moodBtn.GetComponentInChildren<Text>().text = "Mood: " + mood.CurrentName;
        });
        cloudBtn = Btn(bar.transform, "Clouds: Distant", new Vector2(5 * 128 + 440, 0), () =>
        {
            cloudMode = (cloudMode + 1) % 3;
            var cm = FindFirstObjectByType<IslandWorld>();
            ApplyClouds();
        });
    }

    void ApplyClouds()
    {
        // find cloud material via IslandWorld cloudMat? simplest: toggle Cloud roots + opacity
        string[] names = { "Clouds: Distant", "Clouds: Off", "Clouds: Subtle" };
        if (cloudBtn) cloudBtn.GetComponentInChildren<Text>().text = names[cloudMode];
        for (int i = 0; i < 5; i++)
        {
            var r = GameObject.Find($"Cloud{i}");
            if (r) r.SetActive(cloudMode != 1);
        }
    }

    void OnTour(bool on)
    {
        if (tourBtn) tourBtn.GetComponentInChildren<Text>().text = on ? "Stop Tour" : "Tour";
    }

    GameObject Panel(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Card");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(1, 1, 1, 0.85f);
        return go;
    }

    Text Label(Transform parent, string s, int fs, bool bold)
    {
        var go = new GameObject("Label"); go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(410, 24);
        var t = go.AddComponent<Text>();
        t.text = s; t.fontSize = fs; t.color = new Color(0x14 / 255f, 0x32 / 255f, 0x4d / 255f);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (bold) t.fontStyle = FontStyle.Bold;
        return t;
    }

    Button Btn(Transform parent, string s, Vector2 anchored, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn_" + s); go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120, 32); rt.anchoredPosition = anchored;
        var img = go.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.9f);
        var b = go.AddComponent<Button>(); b.onClick.AddListener(onClick);
        var tg = new GameObject("T"); tg.transform.SetParent(go.transform, false);
        var trt = tg.AddComponent<RectTransform>(); trt.sizeDelta = new Vector2(116, 28);
        var t = tg.AddComponent<Text>(); t.text = s; t.fontSize = 12; t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0x14 / 255f, 0x32 / 255f, 0x4d / 255f);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return b;
    }
}
