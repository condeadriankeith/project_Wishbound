using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.Cinemachine;

/// <summary>
/// Opening: earth, valley, finite celestial passage, visible fracture, descent,
/// aftermath title and explicit awakening. The Star remains mute.
/// Existing scene references and the gameplay handover are preserved.
/// </summary>
[DefaultExecutionOrder(-500)]
public class WishboundIntro : MonoBehaviour
{
    [Header("When to play")]
    [Tooltip("Editor: play the opening every time Play mode is entered.")]
    public bool playInEditor = true;
    [Tooltip("Builds: play only on the first launch.")]
    public bool onlyFirstLaunchInBuilds = true;
    [Tooltip("Debug: press F1 during the intro to skip straight to gameplay.")]
    public bool allowDebugSkip = true;

    [Header("Scene references (auto-found when empty)")]
    public IslandThirdPersonController player;
    public CinemachineCamera playerCamera;
    public Transform starVisual;              // PlasmaOrb/PlasmaOrbVisual
    public LivingDayNight dayNight;
    public BeachCometCrater crater;

    [Header("Assets")]
    [Tooltip("Handwriting font for all intro text (Assets/Fonts/UglyHandwriting.ttf). Rendered plain white with a scribble boil.")]
    public Font titleFont;
    [Tooltip("White WISHBOUND logo shown on the title screen (falls back to Resources/WishboundLogo).")] public Sprite titleLogoSprite;
    public Material starBodyMaterial, starHaloMaterial, sparkMaterial, streakMaterial, hearthGlowMaterial, birdMaterial;

    [Header("Audio - recorded ambience and celestial cues")]
    public UnityEngine.Audio.AudioMixerGroup ambienceBus, sfxBus;
    public AudioClip windRustle;
    public AudioClip forestNight;
    public AudioClip birdsFlutter;
    public AudioClip skyRumble;
    public AudioClip celestialChord;
    public AudioClip impactBoom;
    public AudioClip awakenTone;
    [Tooltip("Synth impact: sub boom + crack + rumble, transient at t=0 (Assets/Audio/Wishbound/Comet_ImpactSub.wav).")] public AudioClip impactSub;
    [Tooltip("Rising roar that builds through the descent and peaks at descentRiserClimax seconds (Comet_DescentRiser.wav).")] public AudioClip descentRiser;
    [Tooltip("Comet_Impact.wav has ~1 s of silence before the hit; start playback here so the boom lands on the contact frame.")] public float impactBoomStart = .98f;
    public float descentRiserClimax = 15.5f;
    [Header("Impact FX")]
    public AudioClip impactDebris;           // debris/stone rain after impact
    public AudioClip impactDustSettle;       // low dust settle rumble
    public ParticleSystem impactDustPrefab;  // large dust cloud at impact
    public ParticleSystem impactDebrisPrefab; // flying debris/rocks
    public ParticleSystem impactShockwavePrefab; // ground shockwave ring

    [Header("Comet title hold")]
    [Tooltip("Real seconds the comet takes to slow from full speed to a perfectly smooth stop above the title.")] public float cometSlowSeconds = 3.4f;
    [Tooltip("Real seconds the comet takes to ease back to full speed after PLAY.")] public float cometResumeSeconds = 2.6f;

    [Header("Timing (seconds)")]
    public float shot1Duration = 9f;
    public float shot2Duration = 8f;
    public float silenceBeat = 1.4f;
    public float cometArcDuration = 9f;
    [Tooltip("Seconds for the single continuous comet descent, from the sky to ground contact.")] public float descentDuration = 13f;

    public float vignetteDuration = 3.2f;
    public float handoverBlend = 2.6f;

    [Header("Time of day (hours)")]
    public float shot1StartHour = 16.9f;
    public float shot1EndHour = 17.6f;
    public float shot2Hour = 17.8f;
    public float shot3Hour = 18.0f;   // the game begins at sunset

    [Header("Text")]
    public string title = "Wishbound";
    public string prompt = "awaken";

    public static bool IsPlaying { get; private set; }
    public static WishboundIntro Instance { get; private set; }

    [Header("Island reveal")]
    [System.NonSerialized] public Vector3 islandViewPosition = new Vector3(30, 16, 640);
    [System.NonSerialized] public Vector3 islandViewTarget = new Vector3(0, 150, 0);
    [System.NonSerialized] public float islandViewFov = 42f;
    public static float FogOverride;   // > 0 while a wide intro shot is on: LivingDayNight / MoodController use it as the fog density
    public const float WideFogDensity = .0011f;
    [System.NonSerialized] public float titlePauseK = .94f;
    bool titleShown, wideOn; float savedTreeDist, savedTreeFade, savedGrassDist, savedGrassFade;
    IslandStylizedTrees wideTrees; IslandStylizedGrass wideGrass; ChromaticAberration chromaFx;
    /// <summary>Wide shots: keep trees + grass drawn across the whole island and thin the fog so the island reads clearly.</summary>
    void WideView(bool on)
    {
        if (on == wideOn) return; wideOn = on;
        if (on)
        {
            wideTrees = FindFirstObjectByType<IslandStylizedTrees>(); wideGrass = FindFirstObjectByType<IslandStylizedGrass>();
            if (wideTrees) { savedTreeDist = wideTrees.maxRenderDistance; savedTreeFade = wideTrees.fadeDistance; wideTrees.maxRenderDistance = 2600f; wideTrees.fadeDistance = 250f; }
            if (wideGrass) { savedGrassDist = wideGrass.maxRenderDistance; savedGrassFade = wideGrass.edgeFade; wideGrass.maxRenderDistance = 700f /* was 1600: sub-pixel at the title shot but cost ~25 ms/frame */; wideGrass.edgeFade = 250f; }
            // Heavy fog to hide horizon water edge - AAA cinematic look
            FogOverride = WideFogDensity; RenderSettings.fog = true;   // applied every frame by LivingDayNight / MoodController
        }
        else
        {
            if (wideTrees) { wideTrees.maxRenderDistance = savedTreeDist; wideTrees.fadeDistance = savedTreeFade; }
            if (wideGrass) { wideGrass.maxRenderDistance = savedGrassDist; wideGrass.edgeFade = savedGrassFade; }
            FogOverride = 0f;
        }
    }
    public string SequencePhase { get; private set; } = "Loading";
    WishboundTitleMenu titleMenu;
    bool returningLaunch;

    // runtime
    CinemachineCamera introCam;
    CinemachineBrain brain;
    CinemachineBlendDefinition savedBlend;
    Volume volume; DepthOfField dof; Vignette vignette; ColorAdjustments grade; Bloom bloomFx;

    Canvas canvas; Text promptText; Image titleLogo; RectTransform titleRect; Image fade, barTop, barBottom; CanvasGroup titleGroup, promptGroup;
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<AudioSource> sources = new List<AudioSource>();
    bool skipRequested, awakenRequested;
    Vector3 shake;
    GameObject shafts;

    // Comet title hold state
    bool titleDone, replayFromPause; AudioSource riserSource;
    ParticleSystem impactDust, impactDebrisPS, impactShockwave;

    // ===================================================================================== lifecycle
    void Awake()
    {
        Debug.Log($"[WishboundIntro] Awake() - isEditor={Application.isEditor} playInEditor={playInEditor} onlyFirstLaunchInBuilds={onlyFirstLaunchInBuilds}");
        Instance = this;
        bool play = !Application.isEditor || playInEditor;
        returningLaunch = !Application.isEditor && onlyFirstLaunchInBuilds && PlayerPrefs.GetInt("Wishbound.IntroSeen", 0) != 0;
        Debug.Log($"[WishboundIntro] Awake() - play={play} returningLaunch={returningLaunch} enabled={enabled}");
        if (!play) { enabled = false; Debug.Log($"[WishboundIntro] DISABLED"); return; }
        // TEST SYNTAX ERROR - this should cause a compilation error
        // int x = "not an int";
        BeachCometCrater.IntroPending = true;
        IsPlaying = true;
        if (titleFont) WishboundUI.Font = titleFont;
        Resolve();
        EnsureShotMarkers();
        if (starVisual) starVisual.gameObject.SetActive(false);   // the star doesn't exist yet - it is the comet
        if (player) player.enabled = false;
        if (dayNight) { dayNight.scripted = true; dayNight.scriptedHour = shot1StartHour; }
    }

    bool hitStopActive;
    void OnDestroy() { WideView(false); if (hitStopActive) Time.timeScale = 1f; if (Instance == this) Instance = null; IsPlaying = false; BeachCometCrater.IntroPending = false; }

    void Resolve()
    {
        if (!player) player = FindFirstObjectByType<IslandThirdPersonController>();
        if (!playerCamera && player) playerCamera = player.vcam;
        if (playerCamera) playerCamera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;   // keep its pose live so the hand-over can match it exactly

        if (!starVisual && player) starVisual = player.transform.Find("PlasmaOrbVisual");
        if (!dayNight) dayNight = FindFirstObjectByType<LivingDayNight>();
        if (!crater) crater = FindFirstObjectByType<BeachCometCrater>();
    }

    IEnumerator Start()
    {
        Debug.Log($"[WishboundIntro] Start() BEGIN - enabled={enabled} returningLaunch={returningLaunch} playInEditor={playInEditor} isEditor={Application.isEditor}");
        if (!enabled) yield break;
        SetupCamera(); SetupVolume(); SetupUI(); SetupAudio(); SetupImpactFX();
        yield return null;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        WishboundSettings.Apply(player);
        if (returningLaunch)
        {
            if (crater && !crater.Revealed) crater.Reveal(false);
            BirthStar(CraterCenter());
            if (dayNight) dayNight.scriptedHour = shot3Hour;
            Pose(islandViewPosition, Quaternion.LookRotation(islandViewTarget-islandViewPosition), islandViewFov);
            fade.color = Color.clear;
            yield return ShowTitleMenu();
            yield return Finish();
            yield break;
        }
        SequencePhase = "Grass";
        Debug.Log($"[WishboundIntro] Starting Shot1");
        yield return Shot1();
        Debug.Log($"[WishboundIntro] Shot1 complete, skipRequested={skipRequested}");
        if (!skipRequested) { Pose(s2Cam.position, s2Cam.rotation, 30f); yield return null; }   // hard cut (screen is black) - no travelling camera
        if (!skipRequested) { SequencePhase = "Village"; Debug.Log($"[WishboundIntro] Starting Shot2"); yield return Shot2(); Debug.Log($"[WishboundIntro] Shot2 complete, skipRequested={skipRequested}"); }
        if (!skipRequested) { SequencePhase = "Comet"; Debug.Log($"[WishboundIntro] Starting Shot3"); yield return Shot3(); Debug.Log($"[WishboundIntro] Shot3 complete, skipRequested={skipRequested}"); }
        if (!skipRequested) { Debug.Log($"[WishboundIntro] Starting Shot4"); yield return Shot4(); Debug.Log($"[WishboundIntro] Shot4 complete"); }
        Debug.Log($"[WishboundIntro] Starting Finish");
        yield return Finish();
        Debug.Log($"[WishboundIntro] Sequence COMPLETE");
    }

    void SetupImpactFX()
    {
        if (impactDustPrefab)
        {
            impactDust = Instantiate(impactDustPrefab);
            impactDust.gameObject.SetActive(false);
            spawned.Add(impactDust.gameObject);
        }
        if (impactDebrisPrefab)
        {
            impactDebrisPS = Instantiate(impactDebrisPrefab);
            impactDebrisPS.gameObject.SetActive(false);
            spawned.Add(impactDebrisPS.gameObject);
        }
        if (impactShockwavePrefab)
        {
            impactShockwave = Instantiate(impactShockwavePrefab);
            impactShockwave.gameObject.SetActive(false);
            spawned.Add(impactShockwave.gameObject);
        }
    }

    void Update()
    {
        if (allowDebugSkip && IsPlaying && Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) skipRequested = true;
        if (IsPlaying && !shafts) { var a = GameObject.Find("Atmosphere"); var t = a ? a.transform.Find("Shafts") : null; if (t) { shafts = t.gameObject; shafts.SetActive(false); } }

        // ESC pause during gameplay (after intro finishes)
        if (!IsPlaying && player && player.enabled && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            PauseMenu.Toggle();
        }
    }

    // ===================================================================================== shot markers
    Transform Marker(string name, Vector3 pos, Vector3 lookAt)
    {
        var t = transform.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(transform, false); t.position = pos; t.rotation = Quaternion.LookRotation(lookAt - pos); }
        return t;
    }

    Transform s1Cam, s1Focus, s2Cam, s2CamEnd, s2Village, s2Birds, s3Cam, s3SkyA, s3SkyB, endCam;

    static Vector3 CraterCenter() { float x = (float)CometImpact.X, z = TerrainMath.MirrorZ ? (float)-CometImpact.Z : (float)CometImpact.Z; return new Vector3(x, BeachCometCrater.FinalY(x, z), z); }
    static Vector3 CraterDownrange() => new Vector3((float)CometImpact.Dx, 0, TerrainMath.MirrorZ ? -(float)CometImpact.Dz : (float)CometImpact.Dz).normalized;
    static Vector3 F(float x, float z, float up) => new Vector3(x, BeachCometCrater.BaseY(x, z) + up, z);

    [ContextMenu("Create / reset shot markers")]
    void ResetMarkers()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) if (transform.GetChild(i).name.Contains("_")) DestroyImmediate(transform.GetChild(i).gameObject);
        Resolve(); EnsureShotMarkers();
    }

    void EnsureShotMarkers()
    {
        Vector3 c = CraterCenter();
        s1Focus = Marker("Shot1_Focus", F(35.3f, 407.6f, .2f), F(35.3f, 407.6f, .2f) + Vector3.left);
        s1Cam = Marker("Shot1_Camera", F(36.5f, 407.8f, .26f), s1Focus.position);
        s2Cam = Marker("Shot2_Camera", new Vector3(-50, 46, 408), new Vector3(8, 4, 318));
        s2CamEnd = Marker("Shot2_CameraEnd", new Vector3(-44, 50, 400), new Vector3(10, 8, 318));
        s2Village = Marker("Shot2_Village", new Vector3(10, 6, 318), new Vector3(10, 6, 300));
        s2Birds = Marker("Shot2_Birds", new Vector3(-14, 15, 362), new Vector3(-14, 60, 320));
        s3Cam = Marker("Shot3_Camera", F(22f, 370f, 1.3f), F(22f, 370f, 1.3f) + new Vector3(.3f, 1, .5f));
        s3SkyA = Marker("Shot3_CometStart", s3Cam.position + new Vector3(-320, 330, -60), c);
        s3SkyB = Marker("Shot3_CometHold", s3Cam.position + new Vector3(40, 190, 90), c);
        Vector3 down = CraterDownrange();
        Vector3 endPos = c - down * 19f + Vector3.up * 23f + Vector3.Cross(Vector3.up, down) * 6f;
        endCam = Marker("End_CraterHighAngle", endPos, c);
    }

    // ===================================================================================== setup
    void SetupCamera()
    {
        var cam = Camera.main; brain = cam ? cam.GetComponent<CinemachineBrain>() : null;
        if (cam) { var d = cam.GetUniversalAdditionalCameraData(); d.renderPostProcessing = true; }
        var go = new GameObject("IntroCam"); spawned.Add(go);
        introCam = go.AddComponent<CinemachineCamera>();
        introCam.Priority = 1000;
        introCam.Lens = LensSettings.Default; introCam.Lens.NearClipPlane = .02f; introCam.Lens.FarClipPlane = 4000;
        if (brain) { savedBlend = brain.DefaultBlend; brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f); }
        Pose(s1Cam.position, s1Cam.rotation * Quaternion.Euler(-5f, 0, 0), 24f);
    }

    void Pose(Vector3 p, Quaternion r, float fov)
    {
        introCam.transform.SetPositionAndRotation(p + shake, r * Quaternion.Euler(shake.y * 2.5f, shake.x * 2.5f, shake.z * 3f));
        introCam.Lens.FieldOfView = fov;
    }


    static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }

    IEnumerator TravelTo(Vector3 destination, Quaternion rotation, float fov, float seconds)
    {
        Vector3 origin=introCam.transform.position;
        Quaternion from=introCam.transform.rotation;
        float lens=introCam.Lens.FieldOfView;
        Vector3 a=origin+Vector3.up*Mathf.Min(30,Vector3.Distance(origin,destination)*.25f);
        Vector3 b=destination+Vector3.up*12;
        Dof(false);
        for(float t=0;t<seconds && !skipRequested;t+=Time.deltaTime)
        {
            float k=Ease(t/seconds),u=1-k;
            Vector3 point=u*u*u*origin+3*u*u*k*a+3*u*k*k*b+k*k*k*destination;
            Pose(point,Quaternion.Slerp(from,rotation,k),Mathf.Lerp(lens,fov,k));
            yield return null;
        }
        if(!skipRequested)Pose(destination,rotation,fov);
    }

    void SetupVolume()
    {
        var go = new GameObject("IntroVolume"); spawned.Add(go);
        volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 50;
        var p = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = p;
        var bloom=bloomFx=p.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(1.8f); bloom.scatter.Override(.65f);
        dof = p.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(1.2f); dof.focalLength.Override(60f); dof.aperture.Override(5.6f);
        dof.bladeCount.Override(7); dof.bladeCurvature.Override(1f);
        vignette = p.Add<Vignette>(true); vignette.intensity.Override(.4f); vignette.smoothness.Override(.55f);
        var grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(.22f); grain.response.Override(.8f);
        var ca = chromaFx = p.Add<ChromaticAberration>(true); ca.intensity.Override(.12f);
        grade = p.Add<ColorAdjustments>(true); grade.postExposure.Override(0); grade.saturation.Override(0); grade.colorFilter.Override(Color.white); grade.contrast.Override(12f);
    }

    /// <summary>Bokeh DoF. A long lens with a wide aperture focused mid-distance = miniature / tilt-shift look.</summary>
    void Dof(bool on, float focus = 10, float focal = 60, float aperture = 5.6f)
    {
        dof.active = on; if (!on) return;
        dof.focusDistance.value = focus; dof.focalLength.value = focal; dof.aperture.value = aperture;
    }

    void SetupUI()
    {
        var go = new GameObject("IntroCanvas"); spawned.Add(go);
        canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;

        Image Img(string n, Color c, Vector2 aMin, Vector2 aMax)
        {
            var g = new GameObject(n, typeof(RectTransform)); g.transform.SetParent(go.transform, false);
            var r = (RectTransform)g.transform; r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = r.offsetMax = Vector2.zero;
            var i = g.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
        }
        barTop = Img("LetterboxTop", Color.black, new Vector2(0, .9f), new Vector2(1, 1));
        barBottom = Img("LetterboxBottom", Color.black, new Vector2(0, 0), new Vector2(1, .1f));

        // title: the white WISHBOUND logo, fixed at the centre of the screen (fades in/out, bobs gently)
        {
            var lg = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            lg.transform.SetParent(go.transform, false);
            titleRect = (RectTransform)lg.transform;
            titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(.5f, .5f);
            titleRect.anchoredPosition = Vector2.zero; titleRect.sizeDelta = new Vector2(1100, 434);
            titleLogo = lg.GetComponent<Image>(); titleLogo.sprite = titleLogoSprite ? titleLogoSprite : Resources.Load<Sprite>("WishboundLogo");
            titleLogo.preserveAspect = true; titleLogo.raycastTarget = false; titleLogo.color = Color.white;
            titleGroup = lg.GetComponent<CanvasGroup>(); titleGroup.alpha = 0; titleGroup.blocksRaycasts = false; titleGroup.interactable = false;
        }
        promptText = WishboundUI.Label(go.transform, "Prompt", prompt, 54, new Vector2(.5f, .17f), new Vector2(700, 120));
        promptGroup = promptText.GetComponent<CanvasGroup>();

        fade = Img("Fade", Color.black, Vector2.zero, Vector2.one);
    }

    AudioSource Source(AudioClip clip, bool loop, float volume = 0)
    {
        var s = gameObject.AddComponent<AudioSource>(); s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0; s.volume = volume;
        s.outputAudioMixerGroup = ambienceBus; sources.Add(s); return s;
    }
    AudioSource aWind, aForest, aRumble;
    void SetupAudio()
    {
        aWind = Source(windRustle, true); aForest = Source(forestNight, true); aRumble = Source(skyRumble, true);
        if (windRustle) aWind.Play();
    }
    void OneShot(AudioClip c, float v = 1, float pitch = 1)
    {
        if (!c) return;
        var s = gameObject.AddComponent<AudioSource>(); s.clip = c; s.volume = v; s.pitch = pitch; s.spatialBlend = 0; s.playOnAwake = false; s.outputAudioMixerGroup = sfxBus; s.Play();
        Destroy(s, c.length / Mathf.Max(.1f, pitch) + .1f);
    }

    // ===================================================================================== shot 1
    IEnumerator Shot1()
    {
        var grass = BuildHeroGrass(s1Focus.position, s1Cam.position);
        var pollen = BuildPollen(s1Focus.position);
        var glints = BuildSunGlints(s1Focus.position, s1Cam.forward);
        Dof(true, Vector3.Distance(s1Cam.position, s1Focus.position), 50, 9f);
        for (float t = 0; t < shot1Duration && !skipRequested; t += Time.deltaTime)
        {
            float k = t / shot1Duration;
            { float bk = Mathf.SmoothStep(0, 1, t / 2.5f); barTop.rectTransform.anchorMin = new Vector2(0, Mathf.Lerp(1f, .9f, bk)); barBottom.rectTransform.anchorMax = new Vector2(1, Mathf.Lerp(0f, .1f, bk)); }

            fade.color = new Color(0, 0, 0, Mathf.Max(1 - Mathf.SmoothStep(0, 1, t / 2.2f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(shot1Duration - .9f, shot1Duration, t))));
            float sunset = Ease(Mathf.InverseLerp(.20f, .86f, k));
            if (dayNight) dayNight.scriptedHour = Mathf.Lerp(shot1StartHour, shot1EndHour, sunset);
            grade.colorFilter.value = Color.Lerp(new Color(.94f, .98f, 1f), new Color(1f, .72f, .48f), sunset);
            grade.postExposure.value = Mathf.Lerp(.35f, -.35f, sunset);
            aWind.volume = Mathf.Clamp01(t / 1.5f) * .8f;
            // breathing, resting-in-the-grass drift and a slow push in toward the hero blades
            // static shot: no drift, no push-in, no pan
            Quaternion r = s1Cam.rotation * Quaternion.Euler(-5f, 0, 0);
            Pose(s1Cam.position, r, 24f);
            dof.focusDistance.value = Vector3.Distance(introCam.transform.position, s1Focus.position);
            var main = pollen.main; main.startColor = Color.Lerp(new Color(1.6f, 1.15f, .55f, .9f), new Color(.6f, .7f, 1.1f, .35f), k);
            var gm = glints.main; gm.startColor = Color.Lerp(new Color(1.4f, .8f, .35f, .55f), new Color(.3f, .35f, .8f, .0f), Mathf.SmoothStep(.2f, .85f, k));
            yield return null;
        }
        if (!skipRequested) fade.color = Color.black;
        Destroy(grass); Destroy(pollen.gameObject); Destroy(glints.gameObject);
    }

    GameObject BuildHeroGrass(Vector3 focus, Vector3 camPos)
    {
        var root = new GameObject("IntroHeroGrass"); spawned.Add(root);
        var shader = Shader.Find("Ghibli/ToonLit");
        Material M(Color c, Color rim) { var m = new Material(shader); m.SetColor("_Color", c); m.SetColor("_InstColor", Color.white); m.SetColor("_EmissionColor", Color.black); m.SetFloat("_BrushStrength", .1f); m.SetColor("_RimColor", rim); m.SetFloat("_RimStrength", .55f); m.SetFloat("_Cull", 0); return m; }
        var rimGold = new Color(1f, .62f, .25f);
        var mats = new[] { M(new Color(.32f, .55f, .2f), rimGold), M(new Color(.42f, .62f, .22f), rimGold), M(new Color(.6f, .66f, .28f), rimGold), M(new Color(.26f, .46f, .2f), rimGold) };
        var rng = new System.Random(7);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var blades = new List<Mesh>();
        for (int v = 0; v < 6; v++) blades.Add(BladeMesh(R(.025f, .045f), R(.28f, .55f), R(.08f, .22f)));
        Vector3 fwd = (focus - camPos); fwd.y = 0; fwd.Normalize(); Vector3 right = Vector3.Cross(Vector3.up, fwd);
        var sway = root.AddComponent<IntroGrassSway>();
        void Blade(Vector3 p, float scale)
        {
            var g = new GameObject("blade"); g.transform.SetParent(root.transform, false);
            g.transform.position = new Vector3(p.x, BeachCometCrater.BaseY(p.x, p.z) - .01f, p.z);
            g.transform.rotation = Quaternion.Euler(0, R(0, 360), 0); g.transform.localScale = Vector3.one * scale;
            g.AddComponent<MeshFilter>().sharedMesh = blades[rng.Next(blades.Count)];
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[rng.Next(mats.Length)]; mr.shadowCastingMode = ShadowCastingMode.Off;
            sway.Add(g.transform);
        }
        for (int i = 0; i < 11; i++) Blade(focus + right * Mathf.Lerp(-.3f, .3f, (i + R(-.3f, .3f)) / 10f) + fwd * R(-.06f, .08f), R(.8f, 1.15f));
        for (int i = 0; i < 8; i++) { float side = i % 2 == 0 ? 1 : -1; Blade(camPos + fwd * R(.35f, .6f) + right * side * R(.2f, .4f), R(.5f, .8f)); }
        for (int i = 0; i < 120; i++) { float d = R(.6f, 7f); Blade(focus + fwd * d + right * R(-1f, 1f) * (1 + d * .8f), R(.35f, .6f)); }
        return root;
    }

    static Mesh BladeMesh(float width, float height, float bend)
    {
        const int seg = 5; var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float k = i / (float)seg, w = width * (1 - k * k * .92f);
            float z = bend * k * k;
            v.Add(new Vector3(-w, height * k, z)); v.Add(new Vector3(w, height * k, z));
        }
        for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
        var m = new Mesh { name = "Hero blade" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    ParticleSystem SimpleSystem(string name, Vector3 at, Material mat)
    {
        var go = new GameObject(name); spawned.Add(go); go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = true; m.playOnAwake = false;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }
    static ParticleSystem.MinMaxGradient FadeInOut(float a = .2f, float b = .8f)
    {
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, a), new GradientAlphaKey(1, b), new GradientAlphaKey(0, 1) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    ParticleSystem BuildPollen(Vector3 focus)
    {
        var ps = SimpleSystem("IntroPollen", focus + Vector3.up * .25f, sparkMaterial);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(5, 9); m.startSpeed = new ParticleSystem.MinMaxCurve(.01f, .05f);
        m.startSize = new ParticleSystem.MinMaxCurve(.006f, .02f); m.maxParticles = 220; m.prewarm = true;
        var em = ps.emission; em.rateOverTime = 28;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3, .8f, 3);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(-.05f); vel.y = new ParticleSystem.MinMaxCurve(.012f); vel.z = new ParticleSystem.MinMaxCurve(.015f);
        var n = ps.noise; n.enabled = true; n.strength = .05f; n.frequency = .8f;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut();
        ps.Play(); return ps;
    }

    /// <summary>Big soft out-of-focus sun glints far behind the hero blades (painterly bokeh discs).</summary>
    ParticleSystem BuildSunGlints(Vector3 focus, Vector3 fwd)
    {
        var ps = SimpleSystem("IntroSunGlints", focus + fwd * 7 + Vector3.up * .5f, sparkMaterial);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(3, 6); m.startSpeed = 0; m.startSize = new ParticleSystem.MinMaxCurve(.25f, .7f); m.maxParticles = 40; m.prewarm = true;
        var em = ps.emission; em.rateOverTime = 7;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(5, 1.2f, 2);
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(.35f, .6f);
        ps.Play(); return ps;
    }

    // ===================================================================================== shot 2
    IEnumerator Shot2()
    {
        var flock = new BirdFlock(s2Birds.position, birdMaterial, 34, spawned);
        if (forestNight) aForest.Play();
        bool birdsGone = false;
        float villageDist = Vector3.Distance(s2Cam.position, s2Village.position);
        for (float t = 0; t < shot2Duration && !skipRequested; t += Time.deltaTime)
        {
            float k = t / shot2Duration;
            fade.color = new Color(0, 0, 0, Mathf.Max(1 - Mathf.SmoothStep(0, 1, t / .8f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(shot2Duration - .9f, shot2Duration, t))));
            dayNight.scriptedHour = Mathf.Lerp(shot1EndHour, shot2Hour, Mathf.SmoothStep(0, 1, k));
            aWind.volume = Mathf.Lerp(.8f, .2f, k); aForest.volume = Mathf.Lerp(.2f, .75f, Mathf.Clamp01(t / 2));
            var p = Vector3.Lerp(s2Cam.position, s2CamEnd.position, Mathf.SmoothStep(0, 1, k));
            var r = Quaternion.Slerp(s2Cam.rotation, s2CamEnd.rotation, Mathf.SmoothStep(0, 1, k));
            float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 1f, k));
            r = r * Quaternion.Euler(-lift * 16f, 0, 0);
            Pose(p, r, 30);
            // miniature diorama: long lens, wide aperture, focus on the village - near canopy and far hills melt away
            Dof(false);
            if (!birdsGone && k > .42f) { birdsGone = true; flock.Scatter(); OneShot(birdsFlutter, .9f); }
            flock.Tick(Time.deltaTime);
            yield return null;
        }
        Dof(false);
        grade.colorFilter.value = Color.white; grade.postExposure.value = 0;
        if (!skipRequested) fade.color = Color.black;
        var introBirds = GameObject.Find("IntroBirds"); if (introBirds) Destroy(introBirds);   // removed only while black, so they never freeze mid-air on screen
    }

    readonly List<Hearth> hearths = new List<Hearth>();

    void BuildHearthLights(Vector3 around, float radius)
    {
        var root = new GameObject("IntroHearthLights");   // stays for the night
        foreach (var house in Houses(around, radius))
        {
            var b = Bounds(house);
            var p = new Vector3(b.center.x, b.min.y + Mathf.Min(2.2f, b.size.y * .35f), b.center.z) + (s2Cam.position - b.center).normalized * (Mathf.Max(b.extents.x, b.extents.z) + .15f);
            var go = new GameObject("Hearth"); go.transform.SetParent(root.transform, false); go.transform.position = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .58f, .22f); l.range = 7; l.intensity = 2.6f; l.shadows = LightShadows.None;
            Transform glow = null;
            if (hearthGlowMaterial)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = "Glow";
                q.transform.SetParent(go.transform, false); q.GetComponent<Renderer>().sharedMaterial = hearthGlowMaterial; glow = q.transform;
            }
            hearths.Add(new Hearth { light = l, glow = glow });
        }
        root.AddComponent<HearthFlicker>().Init(hearths);
    }

    static List<Transform> Houses(Vector3 around, float radius)
    {
        var list = new List<Transform>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            var n = t.name;
            if (!(n.Contains("MedievalHouse") || n.Contains("MedievalHo"))) continue;
            if (t.parent && (t.parent.name.Contains("MedievalHouse") || t.parent.name.Contains("MedievalHo"))) continue;
            if ((t.position - around).sqrMagnitude > radius * radius) continue;
            list.Add(t);
        }
        list.Sort((a, b) => (a.position - around).sqrMagnitude.CompareTo((b.position - around).sqrMagnitude));
        return list;
    }
    static Bounds Bounds(Transform t)
    {
        var b = new Bounds(t.position, Vector3.zero); bool init = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds); }
        return b;
    }

    class BirdFlock
    {
        readonly List<Transform> birds = new List<Transform>(); readonly List<Transform[]> wings = new List<Transform[]>();
        readonly List<Vector3> vel = new List<Vector3>(); readonly List<float> phase = new List<float>(), delay = new List<float>();
        bool flying; float clock; Vector3 origin;
        public BirdFlock(Vector3 at, Material mat, int count, List<GameObject> owner)
        {
            origin = at;
            var root = new GameObject("IntroBirds"); owner.Add(root);
            var wingMesh = new Mesh(); wingMesh.SetVertices(new List<Vector3> { Vector3.zero, new Vector3(.45f, .08f, -.08f), new Vector3(.9f, .02f, -.32f), new Vector3(0, 0, -.38f) });
            wingMesh.SetTriangles(new[] { 0, 1, 3, 1, 2, 3, 3, 1, 0, 3, 2, 1 }, 0); wingMesh.RecalculateNormals();
            var bodyMesh = new Mesh(); bodyMesh.SetVertices(new List<Vector3> { new Vector3(0, 0, .35f), new Vector3(.07f, 0, -.1f), new Vector3(-.07f, 0, -.1f), new Vector3(0, 0, -.45f) });
            bodyMesh.SetTriangles(new[] { 0, 1, 2, 2, 1, 0, 1, 3, 2, 2, 3, 1 }, 0); bodyMesh.RecalculateNormals();
            for (int i = 0; i < count; i++)
            {
                var b = new GameObject("Bird").transform; b.SetParent(root.transform, false);
                b.position = at + new Vector3(Random.Range(-16f, 16f), Random.Range(-3f, 1f), Random.Range(-12f, 12f));
                b.localScale = Vector3.one * Random.Range(.8f, 1.25f);
                var body = new GameObject("Body"); body.transform.SetParent(b, false); body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
                var br = body.AddComponent<MeshRenderer>(); br.sharedMaterial = mat; br.shadowCastingMode = ShadowCastingMode.Off;
                var w = new Transform[2];
                for (int s = 0; s < 2; s++)
                {
                    var g = new GameObject("Wing"); g.transform.SetParent(b, false); g.transform.localScale = new Vector3(s == 0 ? 1 : -1, 1, 1);
                    g.AddComponent<MeshFilter>().sharedMesh = wingMesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
                    w[s] = g.transform;
                }
                b.gameObject.SetActive(false);
                birds.Add(b); wings.Add(w); vel.Add(Vector3.zero); phase.Add(Random.value * 6); delay.Add(Random.Range(0, .7f));
            }
        }
        public void Scatter() { flying = true; clock = 0; }
        public void Tick(float dt)
        {
            if (!flying) return;
            clock += dt;
            for (int i = 0; i < birds.Count; i++)
            {
                if (clock < delay[i]) continue;
                if (!birds[i].gameObject.activeSelf) { birds[i].gameObject.SetActive(true); vel[i] = new Vector3(Random.Range(-4f, 4f), Random.Range(8f, 12f), Random.Range(-8f, -3f)); }
                var v = vel[i]; v += new Vector3(Mathf.Sin(Time.time * 1.3f + phase[i]) * 1.5f, -1.4f, -1.4f) * dt; v.y = Mathf.Max(v.y, 2.5f); vel[i] = v;
                birds[i].position += v * dt; birds[i].rotation = Quaternion.LookRotation(v);
                float flap = Mathf.Sin(Time.time * 14f + phase[i]) * 50f;
                wings[i][0].localRotation = Quaternion.Euler(0, 0, flap); wings[i][1].localRotation = Quaternion.Euler(0, 0, -flap);
            }
        }
    }

    // ===================================================================================== shot 3
    WishCometVisual wishingVisual;
    Transform comet; StarOrbFX cometFx; TrailRenderer tailA, tailB; ParticleSystem stardust;

IEnumerator Shot3()
    {
        Debug.Log($"[Shot3] STARTED - shot3Hour={shot3Hour} descentDuration={descentDuration} titlePauseK={titlePauseK} TIME={Time.time}");
        // ONE continuous descent: a single arc that only ever accelerates (no stops), a chase camera that is guarded so
        // the comet never leaves frame, and the hit on the exact frame the comet reaches the ground (see CoreImpact).
        if (dayNight) dayNight.scriptedHour = shot3Hour;
        BuildComet(); Dof(false); shake = Vector3.zero;
        Vector3 c = CraterCenter(), down = CraterDownrange();
        Vector3 side = Vector3.Cross(Vector3.up, down).normalized;
        // target the untouched beach: the crater does not exist yet, aiming at its floor made the comet sink into the sand
        Vector3 ground = new Vector3(c.x, BeachCometCrater.BaseY(c.x, c.z) + .8f, c.z);
        Vector3 from = ground - down * 430f + Vector3.up * 310f;
        Vector3 control = Vector3.Lerp(from, ground, .5f) + Vector3.up * 70f;
        // raise the arc until the flight AND the tail behind it clear the island, so nothing clips the terrain
        for (int pass = 0; pass < 40; pass++)
        {
            bool clear = true;
            for (int i = 0; i <= 48 && clear; i++)
            {
                float sv = i / 48f * .93f;
                Vector3 pt = Bezier(from, control, ground, sv);
                float need = Mathf.Lerp(60f, 8f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, .97f, sv)));
                if (pt.y < GroundY(pt.x, pt.z) + need) clear = false;
            }
            Vector3 back = BezierTangent(from, control, ground, 0f);
            for (int j = 1; j <= 10 && clear; j++) { Vector3 pt2 = from - back * (j * 20f); if (pt2.y < GroundY(pt2.x, pt2.z) + 40f) clear = false; }
            if (clear) break;
            from += Vector3.up * 25f; control += Vector3.up * 25f;
        }

        Vector3 vantage = islandViewPosition;
        vantage.y = Mathf.Max(vantage.y, GroundY(vantage.x, vantage.z) + 12f);
        Vector3 dirS = BezierTangent(from, control, ground, 0f);
        Vector3 camPos = from - dirS * 50f + Vector3.Cross(Vector3.up, dirS).normalized * 42f + Vector3.up * 6f;
        camPos.y = Mathf.Max(camPos.y, GroundY(camPos.x, camPos.z) + 14f);
        Quaternion camRot = Quaternion.LookRotation(from - dirS * 30f - camPos);
        float camLift = 0f, camLiftVel = 0f; Vector3 lastCamBase = camPos;
        float fov = 45f, fovVel = 0f;
        Pose(camPos, camRot, fov); yield return null;   // hard cut while the screen is black
        if (skipRequested) yield break;

        WideView(true);
        comet.position = from; comet.localScale = Vector3.one * 4f;
        comet.gameObject.SetActive(true); cometFx.cometBlend = 1f;
        cometFx.trailScale = 1.6f; cometFx.haloSize = 3.2f; cometFx.pulseDepth = .12f; cometFx.lightIntensity = 9f;
        // the comet IS the star: its plasma body, halo, sparkle shell and wisp trail stay alive (same shaders as the player)
        tailA.Clear(); tailB.Clear(); tailA.emitting = tailB.emitting = true; tailA.time = 5f; tailB.time = 7f; tailA.widthMultiplier = 5f; tailB.widthMultiplier = 12f;
        cometFx.enabled = true;
        // renderers stay on: star body, halo, trails
        wishingVisual = WishCometVisual.Create(comet); wishingVisual.ionLength = 240f; wishingVisual.dustLength = 180f;
        wishingVisual.PrimeFlight(dirS);
        // Populate the trail before the cut; the visible shot opens on an established flight.
        for (int i = 60; i >= 0; i--) { var point = from-dirS*i*4f; tailA.AddPosition(point); tailB.AddPosition(point); }
        fade.color = Color.black;
        yield return null;
        if (stardust) { var sdm = stardust.main; sdm.startSize = new ParticleSystem.MinMaxCurve(1.2f, 4.5f); var sds = stardust.shape; sds.radius = 4f; stardust.Play(); }
        aForest.volume = .24f; aWind.volume = .13f;
        if (skyRumble) { aRumble.volume = 0; aRumble.Play(); }
        OneShot(celestialChord, .55f);
        riserSource = PlayFrom(descentRiser, 1f, Mathf.Max(0f, descentRiserClimax - descentDuration));   // the roar peaks exactly as the comet reaches the ground

        float D = Mathf.Max(6f, descentDuration);
        const float preRoll = 1.2f;
        bool split = false; float splitT = 0; Shard shard = null; Vector3 shardVel = Vector3.zero;

        // === COMET TITLE HOLD: slow-motion -> smooth stop -> (PLAY) -> smooth resume -> fall ===
        // The whole hold is driven by Time.timeScale, so the comet, both tail ribbons, particles, sway and light pulses
        // slow down, settle and resume TOGETHER. The comet and its path history simply stop moving, so nothing retracts.
        float slowT = Mathf.Max(1f, cometSlowSeconds);
        float holdK = titlePauseK, slowStartK = holdK - slowT / (2f * D);
        int phase = replayFromPause ? 4 : 0;   // 0 cruise, 1 slowing, 2 held (title up), 3 easing back to speed, 4 free fall
        replayFromPause = false; titleDone = false;
        float phaseClock = 0f, breathK = 0f;
        float t = -preRoll;

        while (t < D && !skipRequested)
        {
            float dt = Mathf.Max(Time.deltaTime, .0001f);
            if (phase == 0 && t / D >= slowStartK) { phase = 1; phaseClock = 0f; }
            if (phase == 1)
            {
                phaseClock += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(phaseClock / slowT);
                // exact distance travelled by an ease from full speed to rest (speed = 1 - smoothstep): the comet is where it should be every frame
                t = slowStartK * D + slowT * (x - x * x * x + .5f * x * x * x * x);
                Time.timeScale = 1f - Mathf.SmoothStep(0f, 1f, x);   // speed 1 -> 0, zero slope at both ends
                TimeLockedAudio(Time.timeScale); breathK = Mathf.SmoothStep(0f, 1f, x);
                if (x >= 1f) { t = holdK * D; Time.timeScale = 0f; TimeLockedAudio(0f); phase = 2; titleShown = true; StartCoroutine(TitleHold()); }
            }
            else if (phase == 2) { breathK = 1f; if (titleDone) { phase = 3; phaseClock = 0f; } }
            else if (phase == 3)
            {
                phaseClock += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(phaseClock / Mathf.Max(.5f, cometResumeSeconds));
                Time.timeScale = Ease(x);                           // leaves rest with no jolt, reaches full speed with no kink
                TimeLockedAudio(Time.timeScale); breathK = 1f - Mathf.SmoothStep(0f, 1f, x);
                if (x >= 1f) { Time.timeScale = 1f; TimeLockedAudio(1f); breathK = 0f; phase = 4; }
            }

            float k = Mathf.Max(0, t / D);
            float s = .35f * k + .65f * k * k;   // speed rises 0.35x -> 1.65x: it only accelerates, so it never stalls
            fade.color = new Color(0, 0, 0, 1 - Mathf.SmoothStep(0, 1, t / .9f));
            Vector3 head = t < 0 ? from + dirS * (t * Vector3.Distance(from, ground) * .35f / D) : Bezier(from, control, ground, s);
            Vector3 velocity = head - comet.position; comet.position = head;
            if (velocity.sqrMagnitude > .00001f) SplitTails(velocity.normalized);   // while held there is no velocity, so the tails keep their shape
            wishingVisual.Progress = k;
            if (bloomFx != null) { bloomFx.intensity.value = Mathf.Lerp(2.6f, 5f, k); bloomFx.threshold.value = .8f; }
            wishingVisual.Atmosphere = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, .9f, k));
            dirS = Vector3.Slerp(dirS, BezierTangent(from, control, ground, s), 1f - Mathf.Exp(-4f * dt)).normalized;
            // the star swells and breathes a little while the world holds its breath
            if (cometFx) cometFx.haloSize = Mathf.Lerp(3.2f, 4.1f, breathK) * (1f + .05f * breathK * Mathf.Sin(Time.unscaledTime * 1.6f));

            // the fracture happens mid-fall without interrupting it
            if (!split && k > .42f)
            {
                split = true; splitT = t;
                shard = MakeShard(new Color(.65f, .85f, 1f), .28f);
                shard.t.position = head; shard.trail.time = 5f; shard.trail.widthMultiplier = .45f; shard.light.intensity = .6f;
                shardVel = velocity / dt * .7f + side * 14f + Vector3.up * 16f;
                OneShot(awakenTone, .28f, .7f);
            }
            float since = split ? t - splitT : 0f;
            if (shard != null)
            {
                shardVel *= 1f - .45f * dt; shardVel += Vector3.down * 4f * dt;
                shard.t.position += shardVel * dt;
                float f = Mathf.SmoothStep(0f, 1f, since / 3.5f);
                shard.light.intensity = .6f * (1f - f); shard.t.localScale = Vector3.one * (1f - f);
                if (f >= 1f) { shard.trail.emitting = false; shard = null; }
            }
            shake = Noise3(.5f * (split ? Mathf.Exp(-since * 3.5f) : 0f) + .35f * k * k * k, 12f) + Noise3(.08f, .7f);

            // camera: chase beside/behind the comet, then ease to a fixed vantage on the beach, always aimed at the comet
            Vector3 sideNow = Vector3.Cross(Vector3.up, dirS).normalized;
            float w = Ease(Mathf.InverseLerp(.48f, .92f, k));
            Vector3 chase = head - dirS * 50f + sideNow * 42f + Vector3.up * 6f;
            camPos = Vector3.Lerp(chase, vantage, w);
            // stay clear of terrain, trees and roofs with a smoothed lift that looks ahead along the camera's motion (no sudden bumps)
            Vector3 camVel = (camPos - lastCamBase) / dt; lastCamBase = camPos;
            float camNeed = Mathf.Max(GroundY(camPos.x, camPos.z), Mathf.Max(GroundY(camPos.x + camVel.x * .5f, camPos.z + camVel.z * .5f), GroundY(camPos.x + camVel.x, camPos.z + camVel.z))) + 14f;
            camLift = Mathf.SmoothDamp(camLift, Mathf.Max(0f, camNeed - camPos.y), ref camLiftVel, .3f, Mathf.Infinity, dt);
            camPos.y += camLift;
            Vector3 aim = Vector3.Lerp(head - dirS * 30f, islandViewTarget, w);
            camRot = Quaternion.Slerp(camRot, Quaternion.LookRotation(aim - camPos), 1f - Mathf.Exp(-5f * dt));
            Vector3 toHead = head - camPos;
            Quaternion onHead = Quaternion.LookRotation(toHead);
            float maxAng = fov * .25f, ang = Quaternion.Angle(camRot, onHead);
            if (w < .4f && ang > maxAng) camRot = Quaternion.Slerp(onHead, camRot, maxAng / ang);   // frame guard
            float fovWant = Mathf.Clamp(2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Lerp(30f, 20f, k) / Mathf.Max(8f, toHead.magnitude)), 30f, 55f);
            fovWant = Mathf.Lerp(fovWant, islandViewFov, w);
            fov = Mathf.SmoothDamp(fov, fovWant, ref fovVel, .6f, Mathf.Infinity, dt);
            // slow, weightless drift of the camera while the moment is held (real time, so it keeps living at timeScale 0); eased in/out with the hold
            Vector3 drift = (camRot * Vector3.right * Mathf.Sin(Time.unscaledTime * .31f) * .9f + camRot * Vector3.up * Mathf.Sin(Time.unscaledTime * .23f + 1f) * .45f + camRot * Vector3.forward * Mathf.Sin(Time.unscaledTime * .17f) * .8f) * breathK;
            Pose(camPos + drift, camRot, fov - 1.2f * breathK);

            aRumble.volume = Mathf.Lerp(.03f, .35f, k * k);
            aForest.volume = Mathf.Lerp(.24f, 0f, k);

            yield return null;
            if (phase == 0 || phase == 3 || phase == 4) t += Time.deltaTime;
        }

        TimeLockedAudio(1f);

        // Ensure time scale is restored
        Time.timeScale = 1f;
        shake = Vector3.zero;
        if (!skipRequested) comet.position = ground;
    }

    /// <summary>Runs the title menu while the comet is held; flags titleDone once PLAY has been chosen and the menu has faded out.</summary>
    IEnumerator TitleHold() { titleDone = false; yield return ShowTitleMenu(); titleDone = true; }

    /// <summary>Audio that is locked to the comet's flight (the descent riser and sky rumble) slows and stops with it, then resumes in step.</summary>
    void TimeLockedAudio(float speed)
    {
        float p = Mathf.Clamp01(speed);
        if (riserSource) riserSource.pitch = p;
        if (aRumble) aRumble.pitch = p;
    }

    static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float s)
    {
        return (Bezier(a, b, c, Mathf.Min(1f, s + .01f)) - Bezier(a, b, c, Mathf.Max(0f, s - .01f))).normalized;
    }
    bool promptFading;

    void SplitTails(Vector3 dir)
    {
        // two tails peeling apart on either side of the head
        var cam = Camera.main ? Camera.main.transform.forward : Vector3.forward;
        var side = Vector3.Cross(dir, cam).normalized;
        tailA.transform.localPosition = side * .08f;
        tailB.transform.localPosition = -side * .25f;
    }

    void BuildComet()
    {
        var root = new GameObject("IntroComet"); spawned.Add(root);
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); body.name = "AmberStarBody"; Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false); body.transform.localScale = Vector3.one * .85f;
        var light = new GameObject("OrbLight").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Point; light.color = new Color(.7f, .85f, 1f); light.shadows = LightShadows.None;
        root.SetActive(false);
        cometFx = root.AddComponent<StarOrbFX>();
        cometFx.bodyMaterial = starBodyMaterial; cometFx.haloMaterial = starHaloMaterial; cometFx.sparkMaterial = sparkMaterial; cometFx.glowLight = light;
        cometFx.cometBlend = 1; cometFx.hideLegacyVFX = false; cometFx.lightIntensity = 6;
        comet = root.transform;

        TrailRenderer Tail(string n, Color head, Color mid, Color end)
        {
            var g = new GameObject(n); g.transform.SetParent(root.transform, false);
            var tr = g.AddComponent<TrailRenderer>();
            tr.time = 18f; tr.minVertexDistance = .8f; tr.widthMultiplier = 1.6f; tr.numCapVertices = 2;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, .12f), new Keyframe(.06f, .65f), new Keyframe(.28f, 1f), new Keyframe(1, 0));
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, .97f, .92f), 0), new GradientColorKey(head, .12f), new GradientColorKey(mid, .5f), new GradientColorKey(end, 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.85f, .25f), new GradientAlphaKey(0, 1) });
            tr.colorGradient = gr; tr.sharedMaterial = streakMaterial; tr.shadowCastingMode = ShadowCastingMode.Off;
            return tr;
        }
        tailA = Tail("Tail cyan", new Color(.45f, .95f, 1f), new Color(.2f, .6f, 1f), new Color(.3f, .3f, .9f));
        tailB = Tail("Tail violet haze", new Color(.62f, .7f, 1f), new Color(.48f, .4f, .8f), new Color(.28f, .25f, .5f));
        tailB.widthMultiplier = 3f;
        var haze = tailB.colorGradient;
        haze.SetKeys(haze.colorKeys, new[] { new GradientAlphaKey(.12f, 0), new GradientAlphaKey(.28f, .2f), new GradientAlphaKey(0, 1) });
        tailB.colorGradient = haze;

        // lingering iridescent stardust that hangs in the sky long after the head has passed
        stardust = SimpleSystem("IntroStardust", Vector3.zero, sparkMaterial);
        stardust.transform.SetParent(root.transform, false);
        var m = stardust.main; m.startLifetime = new ParticleSystem.MinMaxCurve(7, 12); m.startSpeed = new ParticleSystem.MinMaxCurve(.2f, 1.2f); m.maxParticles = 3000;
        m.startSize = new ParticleSystem.MinMaxCurve(.4f, 1.6f);
        var rainbow = new Gradient();
        rainbow.SetKeys(new[] { new GradientColorKey(new Color(.5f, .95f, 1f), 0), new GradientColorKey(new Color(.75f, .6f, 1f), .3f), new GradientColorKey(new Color(1f, .55f, .9f), .6f), new GradientColorKey(new Color(1f, .85f, .6f), 1) },
                        new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        m.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };
        var em = stardust.emission; em.rateOverTime = 0; em.rateOverDistance = 4.5f;
        var sh = stardust.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 3f;
        var n = stardust.noise; n.enabled = true; n.strength = .6f; n.frequency = .08f; n.scrollSpeed = .05f;
        var col = stardust.colorOverLifetime; col.enabled = true;
        var g2 = new Gradient(); g2.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .05f), new GradientAlphaKey(.45f, .5f), new GradientAlphaKey(0, 1) });
        col.color = g2;
        var sz = stardust.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(.2f, 1), new Keyframe(1, .6f)));
    }

    /// <summary>Subtle idle bob of the title logo around the centre of the screen (no following of the comet).</summary>
    void BobTitle()
    {
        // The title is deliberately stationary; ambient motion belongs to the world.
        if (!titleRect) return;
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.localRotation = Quaternion.identity;
    }

    /// <summary>Same as pressing a key on the "awaken" prompt (for UI buttons / testing).</summary>
    public void RequestAwaken() { awakenRequested = true; }

    bool AwakenPressed()
    {
        if (awakenRequested) { awakenRequested = false; return true; }
        var kb = Keyboard.current; var gp = Gamepad.current;
        return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) ||
               (gp != null && (gp.buttonSouth.wasPressedThisFrame || gp.startButton.wasPressedThisFrame));
    }

    // ===================================================================================== shot 4
    class Shard { public Transform t; public Light light; public TrailRenderer trail; public Color color; }

    Shard MakeShard(Color color, float size)
    {
        var root = new GameObject("WishShard"); spawned.Add(root);
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(body.GetComponent<Collider>()); body.name = "Core";
        body.transform.SetParent(root.transform, false); body.transform.localScale = Vector3.one * size;
        body.GetComponent<Renderer>().sharedMaterial = starBodyMaterial;
        if (starHaloMaterial)
        {
            var h = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(h.GetComponent<Collider>()); h.name = "Halo";
            h.transform.SetParent(root.transform, false); h.transform.localScale = Vector3.one * size * 5f;
            h.GetComponent<Renderer>().sharedMaterial = starHaloMaterial;
        }
        var l = root.AddComponent<Light>(); l.type = LightType.Point; l.color = color; l.range = 9; l.intensity = 3; l.shadows = LightShadows.None;
        var tr = root.AddComponent<TrailRenderer>(); tr.time = 1.4f; tr.widthMultiplier = size * 1.2f; tr.minVertexDistance = .2f; tr.sharedMaterial = streakMaterial;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(color, .3f), new GradientColorKey(color, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        tr.colorGradient = g; tr.widthCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        return new Shard { t = root.transform, light = l, trail = tr, color = color };
    }

IEnumerator Shot4()
    {
        SequencePhase = "Impact";
        if (!skipRequested) yield return CoreImpact();
        // title now appears while the comet hangs in the sky (see Shot3)
    }

    IEnumerator ShowTitleMenu()
    {
        SequencePhase = "Title";
        Dof(false); promptGroup.alpha = 0;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(.5f,.5f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(860,340);
        titleMenu = canvas.gameObject.AddComponent<WishboundTitleMenu>();
        titleMenu.Configure(titleRect, titleGroup, fade, player);
        yield return titleMenu.Present();
        while (!skipRequested && !titleMenu.PlayRequested)
        {
            if (awakenRequested) { awakenRequested=false; titleMenu.SelectPlay(); }
            yield return null;
        }
        if (!skipRequested) { OneShot(awakenTone,.5f); yield return titleMenu.Dismiss(); }
        else titleMenu.ReleaseCursor();
    }

    class VignettePlan { public string kind; public Vector3 land, cam, look; public Transform house; public GameObject prop; public Light propLight; public Renderer glowRenderer; public Transform curtain, villager; }

    List<VignettePlan> PlanVignettes(List<Transform> houses)
    {
        var plans = new List<VignettePlan>();
        var shader = Shader.Find("Ghibli/ToonLit");
        Material Toon(Color c, Color emission) { var m = new Material(shader); m.SetColor("_Color", c); m.SetColor("_InstColor", Color.white); m.SetColor("_EmissionColor", emission); m.SetFloat("_BrushStrength", .2f); return m; }
        var stone = Toon(new Color(.55f, .52f, .5f), Color.black);
        var darkStone = Toon(new Color(.32f, .3f, .32f), Color.black);
        if (houses.Count == 0) { var v = new VignettePlan { kind = "none", land = s2Village.position }; plans.Add(v); return plans; }

        Vector3 Ground(Vector3 p) { if (Physics.Raycast(p + Vector3.up * 50, Vector3.down, out var h, 120, ~0, QueryTriggerInteraction.Ignore)) return h.point; return p; }
        GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m, Transform parent)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }

        // 1. the blacksmith's forge: a stone hearth with a sill and a bed of dying coals beside the first house
        {
            var h = houses[0]; var b = Bounds(h);
            Vector3 toCam = (s2Cam.position - b.center); toCam.y = 0; toCam.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toCam);
            Vector3 basePos = Ground(b.center + side * (b.extents.magnitude * .55f + 1.6f));
            var root = new GameObject("Vignette_Forge");   // stays: the fallen wish keeps glowing in the village root.transform.position = basePos;
            Prim(PrimitiveType.Cube, "Hearth", basePos + Vector3.up * .45f, new Vector3(1.6f, .9f, 1.2f), stone, root.transform);
            Prim(PrimitiveType.Cube, "Sill", basePos + Vector3.up * .93f + toCam * .35f, new Vector3(1.8f, .12f, .55f), darkStone, root.transform);
            Prim(PrimitiveType.Cube, "Chimney", basePos + Vector3.up * 1.6f - toCam * .35f, new Vector3(.7f, 1.6f, .5f), stone, root.transform);
            var coalsMat = Toon(new Color(.12f, .08f, .07f), new Color(.6f, .12f, .02f));
            var coals = Prim(PrimitiveType.Cylinder, "Coals", basePos + Vector3.up * .92f, new Vector3(1.1f, .04f, .8f), coalsMat, root.transform);
            var l = new GameObject("ForgeLight").AddComponent<Light>(); l.transform.SetParent(root.transform, false); l.transform.position = basePos + Vector3.up * 1.4f;
            l.type = LightType.Point; l.color = new Color(1f, .45f, .12f); l.range = 6; l.intensity = .6f;
            Vector3 land = basePos + Vector3.up * 1.0f + toCam * .35f;
            plans.Add(new VignettePlan { kind = "forge", land = land, house = h, prop = coals, propLight = l, glowRenderer = coals.GetComponent<Renderer>(), cam = land + toCam * 7 + side * 3 + Vector3.up * 6, look = land });
        }
        // 2. the courtyard well - the village kit's well hub when the village is dressed, otherwise a simple stand-in
        var dressing = VillageDressing.Instance;
        if (dressing && dressing.Well && dressing.WellWater)
        {
            Vector3 basePos = dressing.Well.position;
            var l = new GameObject("WellLight").AddComponent<Light>(); l.transform.SetParent(dressing.Well, false); l.transform.position = basePos + Vector3.up * .45f;
            l.type = LightType.Point; l.color = new Color(.35f, .9f, 1f); l.range = 8; l.intensity = 0;
            Vector3 toCam = (s2Cam.position - basePos); toCam.y = 0; toCam.Normalize();
            Vector3 land = basePos + Vector3.up * .62f;
            plans.Add(new VignettePlan { kind = "well", land = land, prop = dressing.WellWater.gameObject, propLight = l, glowRenderer = dressing.WellWater, cam = land + toCam * 5.5f + Vector3.Cross(Vector3.up, toCam) * -2.5f + Vector3.up * 7f, look = land });
        }
        else
        {
            Vector3 mid = houses.Count > 2 ? (houses[1].position + houses[2].position) * .5f : houses[0].position + Vector3.forward * 8;
            for (int tries = 0; tries < 16 && Physics.CheckSphere(Ground(mid) + Vector3.up * 5f, 3.2f, ~0, QueryTriggerInteraction.Collide); tries++)
                mid += Quaternion.Euler(0, tries * 67f, 0) * Vector3.forward * (2.5f + tries * .6f);
            Vector3 basePos = Ground(mid);
            var root = new GameObject("Vignette_Well");   // stays: the fallen wish keeps glowing in the village root.transform.position = basePos;
            Prim(PrimitiveType.Cylinder, "WellRing", basePos + Vector3.up * .45f, new Vector3(1.6f, .45f, 1.6f), stone, root.transform);
            var waterMat = Toon(new Color(.05f, .1f, .16f), Color.black);
            var water = Prim(PrimitiveType.Cylinder, "Water", basePos + Vector3.up * .8f, new Vector3(1.3f, .02f, 1.3f), waterMat, root.transform);
            Prim(PrimitiveType.Cube, "PostA", basePos + new Vector3(.75f, 1.3f, 0), new Vector3(.12f, 1.2f, .12f), darkStone, root.transform);
            Prim(PrimitiveType.Cube, "PostB", basePos + new Vector3(-.75f, 1.3f, 0), new Vector3(.12f, 1.2f, .12f), darkStone, root.transform);
            Prim(PrimitiveType.Cube, "Beam", basePos + new Vector3(0, 1.9f, 0), new Vector3(1.7f, .12f, .14f), darkStone, root.transform);
            var l = new GameObject("WellLight").AddComponent<Light>(); l.transform.SetParent(root.transform, false); l.transform.position = basePos + Vector3.up * .5f;
            l.type = LightType.Point; l.color = new Color(.35f, .9f, 1f); l.range = 7; l.intensity = 0;
            Vector3 toCam = (s2Cam.position - basePos); toCam.y = 0; toCam.Normalize();
            Vector3 land = basePos + Vector3.up * .8f;
            plans.Add(new VignettePlan { kind = "well", land = land, prop = water, propLight = l, glowRenderer = water.GetComponent<Renderer>(), cam = land + toCam * 5 + Vector3.Cross(Vector3.up, toCam) * -2.5f + Vector3.up * 6.5f, look = land });
        }
        // 3. the cottage roof: a shard settles on the moss; a curtain twitches and a silhouette peers out
        {
            var h = houses.Count > 3 ? houses[3] : houses[houses.Count - 1]; var b = Bounds(h);
            Vector3 toCam = (s2Cam.position - b.center); toCam.y = 0; toCam.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toCam);
            Vector3 land = new Vector3(b.center.x, b.max.y - b.size.y * .18f, b.center.z) + toCam * b.extents.magnitude * .25f;
            var root = new GameObject("Vignette_Cottage");   // stays: the fallen wish keeps glowing in the village
            // a window on the wall facing the camera, warm and dim, with a curtain and a villager silhouette
            Vector3 wall = b.center + toCam * (Vector3.Scale(b.extents, new Vector3(Mathf.Abs(toCam.x), 0, Mathf.Abs(toCam.z))).magnitude + .06f);
            wall.y = b.min.y + Mathf.Min(1.8f, b.size.y * .3f);
            var winMat = Toon(new Color(.1f, .07f, .05f), new Color(.9f, .55f, .2f));
            var win = Prim(PrimitiveType.Quad, "Window", wall, new Vector3(.9f, .8f, 1), winMat, root.transform); win.transform.rotation = Quaternion.LookRotation(-toCam);
            var villagerMat = Toon(new Color(.03f, .025f, .03f), Color.black);
            var villager = Prim(PrimitiveType.Capsule, "Villager silhouette", wall - toCam * .2f + Vector3.down * .15f, new Vector3(.32f, .32f, .1f), villagerMat, root.transform);
            villager.SetActive(false);
            var curtainMat = Toon(new Color(.55f, .25f, .18f), new Color(.25f, .1f, .03f));
            var curtain = Prim(PrimitiveType.Quad, "Curtain", wall + toCam * .01f, new Vector3(.9f, .8f, 1), curtainMat, root.transform); curtain.transform.rotation = Quaternion.LookRotation(-toCam);
            var l = new GameObject("RoofLight").AddComponent<Light>(); l.transform.SetParent(root.transform, false); l.transform.position = land + Vector3.up * .8f;
            l.type = LightType.Point; l.color = Color.white; l.range = 8; l.intensity = 0;
            plans.Add(new VignettePlan { kind = "roof", land = land, house = h, propLight = l, curtain = curtain.transform, villager = villager.transform, cam = land + toCam * 9 + side * 4 + Vector3.up * 3.5f, look = Vector3.Lerp(land, wall, .5f) });
        }
        return plans;
    }

    /// <summary>Rotates / raises a planned camera around its subject until nothing (canopy, roofs) blocks the view.</summary>
    static Vector3 ClearViewpoint(Vector3 look, Vector3 cam)
    {
        Vector3 offset = cam - look;
        for (int lift = 0; lift < 4; lift++)
            for (int step = 0; step < 12; step++)
            {
                float yaw = (step % 2 == 0 ? 1 : -1) * ((step + 1) / 2) * 30f;
                Vector3 o = Quaternion.Euler(0, yaw, 0) * offset + Vector3.up * lift * 3f;
                Vector3 p = look + o;
                Vector3 dir = (p - look); float dist = dir.magnitude;
                if (!Physics.SphereCast(look + Vector3.up * .6f, .45f, dir / dist, out _, dist - .5f, ~0, QueryTriggerInteraction.Collide)
                    && !Physics.CheckSphere(p, 1.2f, ~0, QueryTriggerInteraction.Collide)) return p;
            }
        return look + offset + Vector3.up * 12f;
    }

    IEnumerator Vignette(VignettePlan v, Shard s)
    {
        v.cam = ClearViewpoint(v.look, v.cam);
        // hard cut into a close, high miniature view; the shard drifts down into frame and settles
        float d = vignetteDuration;
        Vector3 from = v.land + Vector3.up * 14 + (v.land - v.cam).normalized * -2;
        s.t.position = from; s.trail.Clear();
        float camDist = Vector3.Distance(v.cam, v.look);
        bool landed = false;
        var mpb = new MaterialPropertyBlock();
        for (float t = 0; t < d && !skipRequested; t += Time.deltaTime)
        {
            float k = t / d;
            float fall = Mathf.Clamp01(t / (d * .45f));
            float e = 1 - Mathf.Pow(1 - fall, 2);
            s.t.position = Vector3.Lerp(from, v.land, e) + new Vector3(Mathf.Sin(t * 4) * .25f, 0, Mathf.Cos(t * 3) * .25f) * (1 - e);
            s.light.intensity = Mathf.Lerp(3, 1.2f, e);
            if (!landed && fall >= 1) { landed = true; OneShot(awakenTone, .45f, v.kind == "forge" ? .8f : v.kind == "well" ? 1.2f : 1.5f); }
            float after = landed ? Mathf.Clamp01((t - d * .45f) / (d * .4f)) : 0;
            switch (v.kind)
            {
                case "forge":
                    // dying coals flare to life in a burst of warm gold
                    v.propLight.intensity = Mathf.Lerp(.6f, 7f, Mathf.SmoothStep(0, 1, after)) * (1 + Mathf.PerlinNoise(t * 6, 0) * .25f);
                    v.propLight.range = Mathf.Lerp(5, 11, after);
                    v.glowRenderer.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", Color.Lerp(new Color(.6f, .12f, .02f), new Color(3.2f, 1.4f, .3f), after)); v.glowRenderer.SetPropertyBlock(mpb);
                    break;
                case "well":
                    // the water lights from beneath with a breathing cyan
                    float breath = .75f + .25f * Mathf.Sin(t * 2.2f);
                    v.propLight.intensity = 5f * after * breath;
                    v.glowRenderer.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", new Color(.2f, 1.1f, 1.4f) * after * breath); mpb.SetColor("_Glow", new Color(.2f, 1.1f, 1.4f) * after * breath); v.glowRenderer.SetPropertyBlock(mpb);
                    if (landed) s.t.position = Vector3.Lerp(s.t.position, v.land + Vector3.down * .6f, after);   // sinks into the water
                    break;
                case "roof":
                    // prismatic hues wash the dark timber; the curtain twitches and a villager peers out
                    v.propLight.intensity = 4f * after;
                    v.propLight.color = Color.HSVToRGB(Mathf.Repeat(t * .25f, 1), .55f, 1);
                    if (after > .45f && v.villager && !v.villager.gameObject.activeSelf) v.villager.gameObject.SetActive(true);
                    if (v.curtain) v.curtain.localScale = new Vector3(Mathf.Lerp(.9f, .35f, Mathf.SmoothStep(.35f, .75f, after)), .8f, 1);
                    if (v.curtain) v.curtain.position += Vector3.zero;
                    break;
            }
            // slow drift around the subject; tilt-shift focus on the landing point
            Vector3 orbit = Quaternion.Euler(0, Mathf.Lerp(-6, 6, k), 0) * (v.cam - v.look);
            Vector3 p = v.look + orbit * Mathf.Lerp(1.05f, .95f, k);
            Pose(p, Quaternion.LookRotation(Vector3.Lerp(v.look, s.t.position, .3f) - p), 34);
            Dof(true, Vector3.Distance(p, v.look), 50, 11f);
            yield return null;
        }
        s.trail.emitting = false;
        Dof(false);
    }

IEnumerator CoreImpact()
    {
        Vector3 c = CraterCenter(), down = CraterDownrange();
        Vector3 side = Vector3.Cross(Vector3.up, down).normalized;
        Vector3 impactCamera = introCam.transform.position;
        Quaternion impactRotation = introCam.transform.rotation;
        float impactFov = introCam.Lens.FieldOfView;
        Vector3 titleCamera = islandViewPosition;
        Quaternion titleRotation = Quaternion.LookRotation(islandViewTarget - titleCamera);

        // THE HIT: sound, flash, crater, dust, hit-stop and shake all start on the same frame the comet touches the ground.
        comet.gameObject.SetActive(false);
        foreach (var source in sources) if (source && source.loop) source.volume = 0;

        // === AAA IMPACT AUDIO LAYERING ===
        PlayFrom(impactBoom, 1.2f, impactBoomStart);   // main boom - louder
        PlayFrom(impactSub, 1.5f, 0f);                  // sub boom - heavier
        if (impactDebris) OneShot(impactDebris, 1f, 0.9f); // debris rain
        if (impactDustSettle) PlayFrom(impactDustSettle, 0.8f, 0f); // dust settle rumble

        // === VISUAL IMPACT EFFECTS ===
        if (crater) { crater.formSeconds = .35f; crater.Reveal(true); }

        // Spawn impact dust cloud
        if (impactDust)
        {
            impactDust.transform.position = c + Vector3.up * 2f;
            impactDust.gameObject.SetActive(true);
            impactDust.Play(true);
        }

        // Spawn flying debris
        if (impactDebrisPS)
        {
            impactDebrisPS.transform.position = c + Vector3.up * 3f;
            impactDebrisPS.gameObject.SetActive(true);
            impactDebrisPS.Play(true);
        }

        // Spawn ground shockwave ring
        if (impactShockwave)
        {
            impactShockwave.transform.position = c + Vector3.up * 0.1f;
            impactShockwave.gameObject.SetActive(true);
            impactShockwave.Play(true);
        }

        StartCoroutine(PunchFlash(1.0f, new Color(1f, .95f, .85f))); // longer, brighter flash
        StartCoroutine(ImpactLight(c));
        StartCoroutine(ImpactTimeAAA());
        StartCoroutine(BloomPulseAAA());
        StartCoroutine(ChromaticAberrationPulse());
        StartCoroutine(VignettePulse());
        StartCoroutine(ShakePulse());

        bool born = false;
        float settle = Mathf.Max(4.5f, crater ? crater.formSeconds + 1.5f : 4.5f); // longer settle for drama
        for (float t = 0; t < settle && !skipRequested; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / settle);
            if (!born && t > (crater ? crater.formSeconds + .15f : .5f)) { born = true; BirthStar(c); }
            shake = Noise3(2.5f * Mathf.Exp(-t * 1.8f), 12f) + Noise3(0.8f * Mathf.Exp(-t * 3f), 25f); // layered shake
            float punch = 12f * Mathf.Exp(-t * 5f); // stronger FOV punch

            // Camera push-in on impact then pull to title
            Pose(Vector3.Lerp(impactCamera, titleCamera, Ease(k)),
                 Quaternion.Slerp(impactRotation, titleRotation, Ease(k)),
                 Mathf.Lerp(impactFov, islandViewFov, Ease(k)) + punch);
            aRumble.volume = Mathf.Lerp(0.5f, 0f, k * k);
            yield return null;
        }
        if (!born && !skipRequested) BirthStar(c);
        shake = Vector3.zero; aRumble.Stop();
        Pose(titleCamera, titleRotation, islandViewFov);
    }

    /// <summary>A hard point-light flash that floods the beach and the village roofs on contact, then decays (real time).</summary>
    IEnumerator ImpactLight(Vector3 at)
    {
        var go = new GameObject("ImpactLight"); spawned.Add(go); go.transform.position = at + Vector3.up * 6f;
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .8f, .5f); l.shadows = LightShadows.None;
        for (float r = 0; r < 2.6f; r += Time.unscaledDeltaTime)
        {
            if (!l) yield break;
            float k = r / 2.6f;
            l.intensity = 45f * Mathf.Exp(-r * 3.4f) + 5f * (1f - k) * (1f - k);
            l.range = Mathf.Lerp(90f, 230f, Mathf.SmoothStep(0f, 1f, k));
            l.color = Color.Lerp(new Color(1f, .93f, .78f), new Color(1f, .6f, .3f), Mathf.SmoothStep(0f, 1f, k * 1.6f));
            yield return null;
        }
        if (go) Destroy(go);
    }

    /// <summary>AAA Impact Timing: Deep freeze on contact, dramatic slow-mo, then smooth ramp out.</summary>
    IEnumerator ImpactTimeAAA()
    {
        hitStopActive = true;
        try
        {
            // Frame 0: Near-total freeze
            Time.timeScale = 0.01f;
            yield return new WaitForSecondsRealtime(0.15f);

            // Frames 1-8: Extreme slow-mo with gradual restoration
            for (float r = 0; r < 0.8f; r += Time.unscaledDeltaTime)
            {
                Time.timeScale = Mathf.Lerp(0.02f, 0.15f, Mathf.SmoothStep(0f, 1f, r / 0.8f));
                yield return null;
            }

            // Frames 9-30: Cinematic slow-mo (bullet time feel)
            for (float r = 0; r < 1.5f; r += Time.unscaledDeltaTime)
            {
                Time.timeScale = Mathf.Lerp(0.15f, 0.5f, Mathf.SmoothStep(0f, 1f, r / 1.5f));
                yield return null;
            }

            // Final ramp to normal
            for (float r = 0; r < 1.2f; r += Time.unscaledDeltaTime)
            {
                Time.timeScale = Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, r / 1.2f));
                yield return null;
            }
        }
        finally { Time.timeScale = 1f; hitStopActive = false; }
    }

    /// <summary>AAA Bloom Pulse: Massive blowout, long tail, color shift.</summary>
    IEnumerator BloomPulseAAA()
    {
        float originalThreshold = bloomFx != null ? bloomFx.threshold.value : 1f;
        for (float r = 0; r < 5f; r += Time.unscaledDeltaTime)
        {
            if (bloomFx != null)
            {
                float k = Mathf.SmoothStep(0f, 1f, r / 5f);
                bloomFx.intensity.value = Mathf.Lerp(15f, 1.8f, k); // massive initial blowout
                bloomFx.threshold.value = Mathf.Lerp(0.1f, 1f, k);
                bloomFx.scatter.value = Mathf.Lerp(1f, 0.65f, k);
                // Color shift: warm gold to white
                bloomFx.tint.value = Color.Lerp(new Color(1f, 0.85f, 0.5f), Color.white, k);
            }
            yield return null;
        }
        if (bloomFx != null) { bloomFx.intensity.value = 1.8f; bloomFx.threshold.value = 1f; bloomFx.tint.value = Color.white; }
    }

    /// <summary>Chromatic aberration pulse on impact for lens distortion feel.</summary>
    IEnumerator ChromaticAberrationPulse()
    {
        if (chromaFx == null) yield break;
        float original = chromaFx.intensity.value;
        for (float r = 0; r < 2.5f; r += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, r / 2.5f);
            chromaFx.intensity.value = Mathf.Lerp(1.2f, original, k); // strong separation
            yield return null;
        }
        chromaFx.intensity.value = original;
    }

    /// <summary>Vignette pulse - darkens edges on impact for focus.</summary>
    IEnumerator VignettePulse()
    {
        if (vignette == null) yield break;
        float original = vignette.intensity.value;
        for (float r = 0; r < 2f; r += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, r / 2f);
            vignette.intensity.value = Mathf.Lerp(0.85f, original, k);
            vignette.smoothness.value = Mathf.Lerp(0.3f, original, k);
            yield return null;
        }
        vignette.intensity.value = original;
    }

    /// <summary>Multi-layered camera shake with rotational component.</summary>
    IEnumerator ShakePulse()
    {
        float rotShake = 0f, rotShakeVel = 0f;
        for (float r = 0; r < 3f; r += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, r / 3f);
            float amp = Mathf.Lerp(1f, 0f, k);

            // Rotational shake (dutch angle)
            float targetRot = (Mathf.PerlinNoise(Time.unscaledTime * 30f, 0.5f) - 0.5f) * 2.5f * amp;
            rotShake = Mathf.SmoothDamp(rotShake, targetRot, ref rotShakeVel, 0.05f, Mathf.Infinity, Time.unscaledDeltaTime);
            introCam.transform.rotation *= Quaternion.Euler(0, 0, rotShake);

            yield return null;
        }
    }

    /// <summary>Height of whatever is solid at x,z (island, trees, roofs), falling back to the island height field.</summary>
    static float GroundY(float x, float z)
    {
        if (Physics.Raycast(new Vector3(x, 1500f, z), Vector3.down, out var h, 3000f, ~0, QueryTriggerInteraction.Ignore)) return h.point.y;
        return BeachCometCrater.BaseY(x, z);
    }

    /// <summary>Plays a clip starting at a given offset (to skip silent lead-ins or to line a swell up with an event).</summary>
    AudioSource PlayFrom(AudioClip c, float v, float from)
    {
        if (!c) return null;
        var s = gameObject.AddComponent<AudioSource>(); s.clip = c; s.volume = v; s.spatialBlend = 0; s.playOnAwake = false; s.outputAudioMixerGroup = sfxBus;
        s.time = Mathf.Clamp(from, 0f, Mathf.Max(0f, c.length - .05f)); s.Play();
        Destroy(s, Mathf.Max(.2f, c.length - from + .2f));
        return s;
    }

    
/// <summary>Impact timing: a brief freeze on contact, then a quick ramp out of slow motion.</summary>
    IEnumerator ImpactTime()
    {
        hitStopActive = true;
        try
        {
            Time.timeScale = .03f; yield return new WaitForSecondsRealtime(.09f);
            for (float r = 0; r < 1.1f; r += Time.unscaledDeltaTime) { Time.timeScale = Mathf.Lerp(.18f, 1f, Mathf.SmoothStep(0f, 1f, r / 1.1f)); yield return null; }
        }
        finally { Time.timeScale = 1f; hitStopActive = false; }
    }

    /// <summary>Bloom blows out on contact and settles back over a few seconds.</summary>
    IEnumerator BloomPulse()
    {
        for (float r = 0; r < 3.5f; r += Time.unscaledDeltaTime)
        {
            if (bloomFx != null) bloomFx.intensity.value = Mathf.Lerp(9f, 1.8f, Mathf.SmoothStep(0f, 1f, r / 3.5f));
            yield return null;
        }
        if (bloomFx != null) { bloomFx.intensity.value = 1.8f; bloomFx.threshold.value = 1f; }
    }

    
static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) { float u = 1 - t; return u * u * a + 2 * u * t * b + t * t * c; }

    void BirthStar(Vector3 c)
    {
        Vector3 down = CraterDownrange();
        Vector3 spawn = c - down * 1.2f; spawn.y = BeachCometCrater.FinalY(spawn.x, spawn.z) + .6f;
        if (player) player.Teleport(spawn, Quaternion.LookRotation(-down).eulerAngles.y);
        if (starVisual) starVisual.gameObject.SetActive(true);
    }

/// <summary>Smooth, band-limited camera shake (Perlin) - reads as weight and rumble instead of the jitter of random noise.</summary>
    static Vector3 Noise3(float amplitude, float frequency)
    {
        float t = Time.unscaledTime * frequency;
        return new Vector3(Mathf.PerlinNoise(t, .1f) - .5f, Mathf.PerlinNoise(.3f, t) - .5f, Mathf.PerlinNoise(t * .8f, t * .6f + 5f) - .5f) * 2f * amplitude;
    }

    /// <summary>Near-freeze of game time for a few real milliseconds - the classic impact hit-stop.</summary>
    IEnumerator HitStop(float realSeconds)
    {
        hitStopActive = true;
        Time.timeScale = .04f;
        try { yield return new WaitForSecondsRealtime(realSeconds); }
        finally { Time.timeScale = 1f; hitStopActive = false; }
    }

    /// <summary>Gives the star back to the player and starts its wake-up (StarAwakening).</summary>
    void HandOverControl()
    {
        if (!player) return;
        player.enabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        StarAwakening.Begin(player.gameObject);
    }


    IEnumerator PunchFlash(float duration, Color c)
    {
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            float a = t < .06f ? t / .06f : 1 - Mathf.SmoothStep(0, 1, (t - .06f) / (duration - .06f));
            fade.color = new Color(c.r, c.g, c.b, a * .97f);
            yield return null;
        }
        fade.color = Color.clear;
    }

    static IEnumerator FadeGroup(CanvasGroup g, float a, float b, float d)
    {
        for (float t = 0; t < d; t += Time.deltaTime) { g.alpha = Mathf.Lerp(a, b, Mathf.SmoothStep(0, 1, t / d)); yield return null; }
        g.alpha = b;
    }

    // ===================================================================================== hand-over
    IEnumerator Finish()
    {
        SequencePhase = "Arrival";
        if (player) player.PrepareThirdPerson();
        yield return null; yield return null;
        if (skipRequested)
        {
            if (crater && !crater.Revealed) crater.Reveal(false);   // instant crater so the star lands on its floor
            BirthStar(CraterCenter());
            Pose(endCam.position, endCam.rotation, 38);
            fade.color = Color.clear; titleGroup.gameObject.SetActive(false); promptGroup.gameObject.SetActive(false); shake = Vector3.zero; grade.postExposure.value = 0;
            if (comet) comet.gameObject.SetActive(false);
        }
        if (dayNight) { dayNight.storyNightHour = shot3Hour; dayNight.BeginStoryNight(); dayNight.scripted = false; }

        // hand-over is driven manually below: ease onto the live gameplay camera, then cut on an exact pose match
        float hb = Mathf.Max(handoverBlend, 7f);
        Vector3 hp0 = introCam.transform.position; Quaternion hr0 = introCam.transform.rotation; float hf0 = introCam.Lens.FieldOfView;
        Vector3 starPos = CraterCenter() + Vector3.up * 1.5f;
        float bloom0 = bloomFx != null ? bloomFx.intensity.value : 1.8f, ca0 = chromaFx != null ? chromaFx.intensity.value : .12f, vig0 = vignette != null ? vignette.intensity.value : .4f;
        OneShot(celestialChord, .7f, 1.15f);
        // HYPERLAPSE: slow wind-up, a banking swoop straight at the star with FOV warp, chroma + bloom surge and shake, then a soft landing on the third-person camera
        for (float t = 0; t < hb; t += Time.deltaTime)
        {
            float u = Mathf.Clamp01(t / hb);
            float ke = Mathf.Pow(Ease(u), 1.35f);
            float rush = Mathf.Sin(u * Mathf.PI), rush2 = rush * rush;
            if (playerCamera)
            {
                var st = playerCamera.State; Vector3 end = st.GetFinalPosition(); Quaternion endR = st.GetFinalOrientation();
                Vector3 flat = end - hp0; flat.y = 0; flat = flat.sqrMagnitude > .01f ? flat.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, flat);
                Vector3 hp = Vector3.Lerp(hp0, end, ke) + Vector3.up * (Mathf.Sin(ke * Mathf.PI) * 70f) + side * (Mathf.Sin(ke * Mathf.PI * 2f) * 45f * (1f - ke));
                Vector3 toStar = starPos - hp;
                Quaternion aimStar = toStar.sqrMagnitude > .01f ? Quaternion.LookRotation(toStar) : endR;
                Quaternion rot = Quaternion.Slerp(Quaternion.Slerp(hr0, aimStar, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .5f, ke))), endR, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, ke)));
                rot *= Quaternion.AngleAxis(Mathf.Sin(ke * Mathf.PI * 2f) * -16f * rush, Vector3.forward);
                shake = Noise3(.45f * rush2, 18f);
                Pose(hp, rot, Mathf.Lerp(hf0, st.Lens.FieldOfView, ke) + 32f * rush2);
            }
            if (bloomFx != null) bloomFx.intensity.value = bloom0 + 3.5f * rush2;
            if (chromaFx != null) chromaFx.intensity.value = ca0 + .55f * rush2;
            if (vignette != null) vignette.intensity.value = vig0 + .3f * rush;
            if (volume) volume.weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.8f, 1f, u));
            float k = Mathf.SmoothStep(0, 1, u);
            barTop.rectTransform.anchorMin = new Vector2(0, Mathf.Lerp(.9f, 1f, k));
            barBottom.rectTransform.anchorMax = new Vector2(1, Mathf.Lerp(.1f, 0f, k));
            yield return null;
        }
        shake = Vector3.zero; WideView(false);
        introCam.Priority = -1000; yield return null; yield return null;   // poses match, so this cut is invisible
        if (player && !player.enabled) HandOverControl();
        if (brain) brain.DefaultBlend = savedBlend;
        IsPlaying = false; SequencePhase = "Gameplay";
        if (shafts) shafts.SetActive(true);
        if (!Application.isEditor) { PlayerPrefs.SetInt("Wishbound.IntroSeen", 1); PlayerPrefs.Save(); }
        foreach (var s in sources) if (s) Destroy(s);
        foreach (var g in spawned) if (g) Destroy(g);
        spawned.Clear();
    }

    /// <summary>
    /// Returns to the title menu from gameplay. Called by PauseMenu when QUIT is pressed.
    /// </summary>
    public IEnumerator ReturnToTitle()
    {
        // Disable player
        if (player) { player.enabled = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        // Fade to black
        if (fade)
        {
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime)
            {
                fade.color = Color.Lerp(Color.clear, Color.black, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            fade.color = Color.black;
        }

        // Reset time scale
        Time.timeScale = 1f;
        hitStopActive = false;

        // Re-enable intro camera
        if (introCam) introCam.Priority = 100;

        // Reset day/night to cinematic hour
        if (dayNight) { dayNight.scripted = true; dayNight.scriptedHour = shot1StartHour; }

        // Reset sky/fog
        WideView(true);

        // Show title menu again
        SequencePhase = "Title";
        Dof(false);
        if (promptGroup) promptGroup.alpha = 0;
        if (titleRect)
        {
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(.5f, .5f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(860, 340);
        }
        if (titleGroup) titleGroup.gameObject.SetActive(true);

        // Recreate title menu
        if (canvas)
        {
            titleMenu = canvas.gameObject.AddComponent<WishboundTitleMenu>();
            titleMenu.Configure(titleRect, titleGroup, fade, player);
            yield return titleMenu.Present();
            while (!skipRequested && !titleMenu.PlayRequested)
            {
                if (awakenRequested) { awakenRequested = false; titleMenu.SelectPlay(); }
                yield return null;
            }
            if (!skipRequested) { OneShot(awakenTone, .5f); yield return titleMenu.Dismiss(); }
            else titleMenu.ReleaseCursor();
        }

        // Fade in from black
        if (fade)
        {
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime)
            {
                fade.color = Color.Lerp(Color.black, Color.clear, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            fade.color = Color.clear;
        }

        // Restart the intro sequence (the title was just chosen, so the comet does not hold for a second title screen)
        replayFromPause = true;
        StartCoroutine(RunFullSequence());
    }

    /// <summary>Runs the full intro sequence from the beginning (for returning to title).</summary>
    IEnumerator RunFullSequence()
    {
        skipRequested = false;
        awakenRequested = false;
        SequencePhase = "Grass";
        yield return Shot1();
        if (!skipRequested) { Pose(s2Cam.position, s2Cam.rotation, 30f); yield return null; }
        if (!skipRequested) { SequencePhase = "Village"; yield return Shot2(); }
        if (!skipRequested) { SequencePhase = "Comet"; yield return Shot3(); }
        if (!skipRequested) yield return Shot4();
        yield return Finish();
    }
}

/// <summary>Wind sway for the intro's hero grass blades.</summary>
public class IntroGrassSway : MonoBehaviour
{
    readonly List<Transform> blades = new List<Transform>(); readonly List<Quaternion> rest = new List<Quaternion>(); readonly List<float> seed = new List<float>();
    public void Add(Transform t) { blades.Add(t); rest.Add(t.rotation); seed.Add(Random.value * 10); }
    void Update()
    {
        float time = Time.time;
        for (int i = 0; i < blades.Count; i++)
        {
            var p = blades[i].position;
            float gust = Mathf.PerlinNoise(p.x * .8f + time * .55f, p.z * .8f) - .5f;
            float a = Mathf.Sin(time * 1.9f + seed[i] + p.x * 2.1f) * 4f + gust * 16f;
            blades[i].rotation = Quaternion.Euler(a * .35f, 0, a) * rest[i];
        }
    }
}

/// <summary>One village window/hearth light (warm point light + small billboard glow).</summary>
public class Hearth
{
    public Light light; public Transform glow; readonly float seed = Random.value * 100;
    public void Tick()
    {
        float f = .75f + Mathf.PerlinNoise(Time.time * 3.2f, seed) * .5f;
        if (light) light.intensity = 2.6f * f;
        if (glow) glow.localScale = Vector3.one * (1.4f + f * .5f);
    }
}

/// <summary>Keeps the village hearth lights flickering (created by the intro, lives on through the night).</summary>
public class HearthFlicker : MonoBehaviour
{
    List<Hearth> items;
    public void Init(List<Hearth> list) { items = list; }
    void Update() { if (items != null) foreach (var h in items) h.Tick(); }
}
