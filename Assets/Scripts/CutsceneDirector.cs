using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;

/// <summary>
/// Takes over the camera, the star's controls and the pointer for a story beat.
/// Begin() locks input (the wisp cursor is hidden), slides in letterbox bars and puts a dedicated Cinemachine camera in
/// charge, starting exactly where the gameplay camera is so there is no jump. Shots are Glide(position, look, fov, seconds):
/// positions and look points are delegates, so a shot can track a moving actor. End() hands the camera back with a blend.
/// </summary>
public sealed class CutsceneDirector : MonoBehaviour
{
    public static CutsceneDirector Instance { get; private set; }
    public static bool Active => Instance && Instance.active;

    CinemachineCamera cam; CinemachineBrain brain; Camera mainCam; IslandThirdPersonController player;
    bool active; int depth;
    bool playerWasEnabled, wispWasHidden; float savedSpeedCap; CursorLockMode savedLock; bool savedCursorVisible;
    Func<Vector3> posFn, lookFn; float fovGoal = 40, fov, roll;
    Vector3 fromP, fromL, curP, curL; float fromF, t, dur;
    public Vector3 Focus => curL;
    /// <summary>Current letterbox bar height as a 0..1 screen fraction (0 when no cutscene).</summary>
    public static float BarFraction => Instance ? .105f * Smoother(Instance.bars) : 0f;
    public float handheld = .012f;
    RectTransform barTop, barBottom; float bars, barsGoal;
    CinemachineBlendDefinition savedBlend; bool blendSaved;

    public static CutsceneDirector Begin()
    {
        if (!Instance) { var go = new GameObject("CutsceneDirector"); Instance = go.AddComponent<CutsceneDirector>(); }
        Instance.Enter(); return Instance;
    }

    void Enter()
    {
        if (active) { depth++; return; }
        mainCam = Camera.main; if (!mainCam) return;
        active = true; depth = 1;
        brain = mainCam.GetComponent<CinemachineBrain>();
        var p = GameObject.FindGameObjectWithTag("Player"); player = p ? p.GetComponent<IslandThirdPersonController>() : null;
        if (!cam) { var g = new GameObject("CutsceneCam"); g.transform.SetParent(transform, false); cam = g.AddComponent<CinemachineCamera>(); }
        curP = mainCam.transform.position; curL = curP + mainCam.transform.forward * 10f; fov = mainCam.fieldOfView;
        roll = 0f;
        posFn = () => curP; lookFn = () => curL; fromP = curP; fromL = curL; fromF = fov; fovGoal = fov; dur = 0; t = 0;
        Apply();
        if (brain) { savedBlend = brain.DefaultBlend; blendSaved = true; brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f); }
        cam.enabled = true; cam.Priority.Value = 1000;
        wispWasHidden = WispCursor.Hidden; savedLock = Cursor.lockState; savedCursorVisible = Cursor.visible;
        WispCursor.Hidden = true;
        if (player) { playerWasEnabled = player.enabled; savedSpeedCap = player.speedCap; player.speedCap = 0f; player.enabled = false; }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        BuildBars(); barsGoal = 1f;
    }

    /// <summary>Hands everything back. The gameplay camera blends in from wherever the last shot ended.</summary>
    public void End(float blendBack = 1.6f)
    {
        if (!active) return;
        if (--depth > 0) return;
        active = false; barsGoal = 0f; RestoreCutaway();
        if (brain)
        {
            if (player && Vector3.Distance(cam.transform.position, player.transform.position) > 25f) blendBack = 0f;
            brain.DefaultBlend = new CinemachineBlendDefinition(blendBack > 0f ?
                CinemachineBlendDefinition.Styles.EaseInOut : CinemachineBlendDefinition.Styles.Cut, blendBack);
        }
        cam.enabled = false;
        if (player) { player.enabled = playerWasEnabled; player.speedCap = savedSpeedCap; }
        Cursor.lockState = savedLock; Cursor.visible = savedCursorVisible;
        WispCursor.Hidden = wispWasHidden;
        if (isActiveAndEnabled) StartCoroutine(RestoreBlend(blendBack + .3f));
        else if (brain && blendSaved) brain.DefaultBlend = savedBlend;
    }

    IEnumerator RestoreBlend(float after)
    {
        yield return new WaitForSecondsRealtime(after);
        if (brain && blendSaved && !active) brain.DefaultBlend = savedBlend;
    }

    readonly System.Collections.Generic.List<Renderer> cutaway = new System.Collections.Generic.List<Renderer>();
    public static Vector3 SupportPoint(Vector3 point)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.normal.y < .65f || h.collider.GetComponentInParent<SettlerRig>() || h.collider.GetComponentInParent<IslandThirdPersonController>()) continue;
            if (h.point.y > point.y + .6f || h.point.y <= best) continue;
            best = h.point.y;
        }
        if (!float.IsNegativeInfinity(best)) point.y = best;
        return point;
    }

    public static bool ClearShot(Vector3 position, Vector3 focus)
    {
        foreach (var overlap in Physics.OverlapSphere(position, .25f, ~0, QueryTriggerInteraction.Ignore))
            if (CameraGroundClearance.BlocksCamera(overlap)) return false;
        float distance = Vector3.Distance(position, focus);
        foreach (var hit in Physics.RaycastAll(position, (focus-position).normalized, distance, ~0, QueryTriggerInteraction.Ignore))
            if (CameraGroundClearance.BlocksCamera(hit.collider) && hit.distance < distance - .55f) return false;
        return true;
    }

    public static Vector3 SafeShot(Vector3 desired, Vector3 focus, float fieldOfView = 40f)
    {
        Vector3 offset = desired-focus;
        if (offset.sqrMagnitude < .25f) return desired;
        var actors = UnityEngine.Object.FindObjectsByType<SettlerRig>(FindObjectsSortMode.None);
        Vector3 best = desired; float score = float.NegativeInfinity;
        for (int i=0;i<24;i++)
        {
            float angle = i == 0 ? 0 : ((i+1)/2)*15*(i%2==0?-1:1);
            Vector3 candidate = focus + Quaternion.Euler(0,angle,0)*offset;
            if (!ClearShot(candidate,focus)) continue;
            Vector3 forward = (focus-candidate).normalized;
            bool blocked=false; float face=0;
            foreach (var actor in actors)
            {
                Vector3 delta = actor.transform.position+Vector3.up*.9f-candidate;
                float depth=Vector3.Dot(delta,forward);
                if (depth <= .1f || depth >= offset.magnitude+.5f) continue;
                float height = 1.8f/(2*depth*Mathf.Tan(fieldOfView*.5f*Mathf.Deg2Rad));
                float sideways=(delta-forward*depth).magnitude;
                if (height>.35f && sideways<.65f && depth<offset.magnitude-.8f) { blocked=true; break; }
                face += Vector3.Dot(actor.transform.forward,-forward)*.1f;
            }
            if (blocked) continue;
            float value = -Mathf.Abs(angle)*.01f+face;
            if (value>score) { best=candidate; score=value; }
        }
        return best;
    }

    void RestoreCutaway()
    {
        foreach (var r in cutaway) if (r) r.enabled=true;
        cutaway.Clear();
    }

    void PrepareCutaway(Vector3 position, Vector3 focus)
    {
        RestoreCutaway();
        var ray = new Ray(position,(focus-position).normalized);
        float distance = Vector3.Distance(position,focus);
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string name=r.name.ToLowerInvariant();
            if (!r.enabled || (!name.Contains("wall") && !name.Contains("roof"))) continue;
            if (r.bounds.IntersectRay(ray,out float hit) && hit<distance-.3f) { cutaway.Add(r); r.enabled=false; }
        }
    }

    // ------------------------------------------------------------------ shots
    void SetGoal(Func<Vector3> pos, Func<Vector3> look, float f, float seconds)
    {
        Vector3 requested=pos(), focus=look();
        Vector3 safe=SafeShot(requested,focus,f);
        Vector3 correction=safe-requested;
        PrepareCutaway(safe,focus);
        roll = 0f;
        var original=pos; pos=()=>original()+correction;
        fromP = curP; fromL = curL; fromF = fov; posFn = pos; lookFn = look; fovGoal = f; dur = Mathf.Max(0f, seconds); t = 0f;
        if (dur <= 0f) { curP = pos(); curL = look(); fov = f; Apply(); }
    }
    // Continuous shots provide their own framing and occlusion correction each frame.
    public void TrackFrame(Vector3 position, Vector3 focus, float fieldOfView, float dutch = 0f)
    {
        if (!active) return;
        posFn = lookFn = null;
        curP = position;
        curL = focus;
        fov = fieldOfView;
        roll = dutch;
        Apply();
    }

    public void Cut(Func<Vector3> pos, Func<Vector3> look, float fovDeg) => SetGoal(pos, look, fovDeg, 0f);
    public void Cut(Vector3 pos, Vector3 look, float fovDeg) => SetGoal(() => pos, () => look, fovDeg, 0f);
    public IEnumerator Glide(Func<Vector3> pos, Func<Vector3> look, float fovDeg, float seconds)
    {
        SetGoal(pos, look, fovDeg, seconds);
        while (active && t < dur) yield return null;
    }
    public IEnumerator Glide(Vector3 pos, Vector3 look, float fovDeg, float seconds) => Glide(() => pos, () => look, fovDeg, seconds);

    static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6 - 15) + 10); }

    void Update()
    {
        if (!cam) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        bars = Mathf.MoveTowards(bars, barsGoal, dt * 1.2f);
        if (barTop) { float h = Screen.height * .105f * Smoother(bars); barTop.sizeDelta = new Vector2(0, h); barBottom.sizeDelta = new Vector2(0, h); }
        if (!active) { if (bars <= 0f && barTop) { Destroy(barTop.parent.gameObject); barTop = barBottom = null; } return; }
        if (posFn == null) return;
        Vector3 tp = posFn(), tl = lookFn(); float tf = fovGoal;
        if (dur > 0f && t < dur) { t += Time.deltaTime; float e = Smoother(t / dur); curP = Vector3.Lerp(fromP, tp, e); curL = Vector3.Lerp(fromL, tl, e); fov = Mathf.Lerp(fromF, tf, e); }
        else { curP = tp; curL = tl; fov = tf; }
        Apply();
    }

    readonly RaycastHit[] shotHits = new RaycastHit[96];

    void Apply()
    {
        if (!cam) return;
        Vector3 cameraPoint = curP;
        float radius = Mathf.Max(.3f, cam.Lens.NearClipPlane * 1.8f);
        if (TerrainProbe.TryGetGroundY(cameraPoint.x, cameraPoint.z, out float hill))
            cameraPoint.y = Mathf.Max(cameraPoint.y, hill + radius + .25f);
        Vector3 offset = cameraPoint - curL;
        if (offset.sqrMagnitude > .01f)
        {
            int count = Physics.SphereCastNonAlloc(curL, radius, offset.normalized, shotHits,
                offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float travel = offset.magnitude;
            for (int i = 0; i < count; i++)
                if (CameraGroundClearance.BlocksCamera(shotHits[i].collider))
                    travel = Mathf.Min(travel, Mathf.Max(.05f, shotHits[i].distance - .08f));
            cameraPoint = curL + offset.normalized * travel;
        }
        if (TerrainProbe.TryGetGroundY(cameraPoint.x,cameraPoint.z,out float floor))
            cameraPoint.y = Mathf.Max(cameraPoint.y, floor + radius + .15f);
        Vector3 look = curL - cameraPoint; if (look.sqrMagnitude < .0001f) look = Vector3.forward;
        float n = Time.time;
        Quaternion shake = Quaternion.Euler((Mathf.PerlinNoise(n * .6f, 3f) - .5f) * 2f * handheld * 60f, (Mathf.PerlinNoise(n * .5f, 9f) - .5f) * 2f * handheld * 60f, 0);
        cam.transform.SetPositionAndRotation(cameraPoint, Quaternion.LookRotation(look) * shake);
        var lens = cam.Lens; lens.FieldOfView = fov; lens.Dutch = roll; cam.Lens = lens;
    }

    void OnDisable() { if (active) { depth = 1; End(0f); } }

    void BuildBars()
    {
        if (barTop) return;
        var cv = new GameObject("Cutscene bars"); cv.transform.SetParent(transform, false);
        var c = cv.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 100;
        RectTransform Bar(string n, float y)
        {
            var g = new GameObject(n, typeof(RectTransform), typeof(Image)); g.transform.SetParent(cv.transform, false);
            var r = (RectTransform)g.transform; r.anchorMin = new Vector2(0, y); r.anchorMax = new Vector2(1, y); r.pivot = new Vector2(.5f, y); r.sizeDelta = Vector2.zero;
            var im = g.GetComponent<Image>(); im.color = Color.black; im.raycastTarget = false; return r;
        }
        barTop = Bar("Top", 1f); barBottom = Bar("Bottom", 0f);
    }
}
