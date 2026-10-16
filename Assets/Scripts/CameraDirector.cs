using UnityEngine;

public class CameraDirector : MonoBehaviour
{
    public enum Mode { Orbit, Fly, Tour, Play }
    public Mode mode = Mode.Orbit;
    public Camera cam; public Transform player;
    bool attachedToPlayer;   // camera is a child of the first-person rig, so IslandFPSController owns it

    Vector3 target; float yaw, elev, dist;
    Vector2 vel; float zoomVel; public bool autoRotate;
    Vector3 p0, t0, p1, t1; float flyStart;
    Vector3[] tourP, tourT; float tourU;
    float pYaw = Mathf.PI, pTyaw = Mathf.PI, pDist = 75f;

    public System.Action<bool> OnTourChanged;

void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!cam) cam = GetComponent<Camera>();
        // If the camera lives under the FPS player, don't orbit/fly/tour it away from the player's eyes.
        attachedToPlayer = cam && (cam.GetComponentInParent<IslandFPSController>() != null
            || cam.GetComponentInParent<IslandThirdPersonController>() != null
            || cam.GetComponent<Unity.Cinemachine.CinemachineBrain>() != null);   // Cinemachine owns the camera
        if (attachedToPlayer) enabled = false;
    }

public void SetOrbitFrom(Vector3 pos, Vector3 tgt)
    {
        target = tgt; Vector3 o = pos - tgt; dist = Mathf.Max(o.magnitude, 0.001f);
        yaw = Mathf.Atan2(o.x, o.z); elev = Mathf.Asin(Mathf.Clamp(o.y / dist, -1, 1));
        if (cam && !attachedToPlayer) { cam.transform.position = pos; cam.transform.LookAt(target); }
    }

    public void FlyTo(Vector3 pos, Vector3 tgt)
    {
        if (!cam) cam = Camera.main;
        p0 = cam.transform.position; t0 = target; p1 = pos; t1 = tgt; flyStart = Time.time;
        mode = Mode.Fly; autoRotate = false; SetTour(false);
    }

    public void SetTour(bool on)
    {
        if (on) { mode = Mode.Tour; autoRotate = false; }
        else if (mode == Mode.Tour) { if (cam) SetOrbitFrom(cam.transform.position, target); mode = Mode.Orbit; }
        OnTourChanged?.Invoke(on);
    }
    public void SetTourPath(Vector3[] pts, Vector3[] tgts) { tourP = pts; tourT = tgts; }
    public bool IsTouring => mode == Mode.Tour;

    void LateUpdate()
    {
        if (!cam) return;
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        switch (mode)
        {
            case Mode.Orbit: UpdateOrbit(dt); break;
            case Mode.Fly:
                float u = Mathf.Clamp01((Time.time - flyStart) / 2f), e = u * u * (3 - 2 * u);
                target = Vector3.Lerp(t0, t1, e);
                cam.transform.position = Vector3.Lerp(p0, p1, e);
                cam.transform.LookAt(target);
                if (u >= 1) { SetOrbitFrom(p1, t1); mode = Mode.Orbit; }
                break;
            case Mode.Tour:
                if (tourP == null || tourP.Length < 2) { mode = Mode.Orbit; break; }
                tourU = (tourU + dt / 80f) % 1f;
                var p = CatmullClosed(tourP, tourU); p.y += Mathf.Sin(Time.time * 0.4f) * 2f;
                target = CatmullClosed(tourT, tourU);
                cam.transform.position = p; cam.transform.LookAt(target);
                break;
            case Mode.Play: UpdatePlay(dt); break;
        }
    }

    void UpdateOrbit(float dt)
    {
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButton(0) && !overUI)
        { vel += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 0.05f; autoRotate = false; }
        if (autoRotate) vel.x += 0.0035f * 0.35f;
        zoomVel += -Input.mouseScrollDelta.y * 0.08f * dist * 0.1f;
        yaw -= vel.x * (1f - 0.06f); elev = Mathf.Clamp(elev + vel.y * (1f - 0.06f), 0.05f, 1.5f);
        dist = Mathf.Clamp(dist + zoomVel, 6f, 1800f);
        vel *= 0.94f; zoomVel *= 0.88f;
        float ce = Mathf.Cos(elev);
        cam.transform.position = target + dist * new Vector3(ce * Mathf.Sin(yaw), Mathf.Sin(elev), ce * Mathf.Cos(yaw));
        cam.transform.LookAt(target);
    }

    public void EnterPlay(bool on)
    {
        if (on) { mode = Mode.Play; autoRotate = false; if (player) target = player.position; }
        else { if (cam && player) SetOrbitFrom(cam.transform.position, player.position); mode = Mode.Orbit; }
    }
    public void RotatePlay(float rad) => pTyaw += rad;
    void UpdatePlay(float dt)
    {
        if (Input.GetKeyDown(KeyCode.Q)) pTyaw += 0.5f;
        if (Input.GetKeyDown(KeyCode.E)) pTyaw -= 0.5f;
        pDist = Mathf.Clamp(pDist - Input.mouseScrollDelta.y * 4f, 30f, 160f);
        pYaw += (pTyaw - pYaw) * (1 - Mathf.Exp(-dt * 8f));
        if (!player) return;
        float cp = Mathf.Cos(0.82f), k = 1 - Mathf.Exp(-dt * 4f);
        Vector3 pt = player.position + Vector3.up * 2.5f;
        Vector3 want = pt + new Vector3(Mathf.Sin(pYaw) * cp * pDist, Mathf.Sin(0.82f) * pDist, Mathf.Cos(pYaw) * cp * pDist);
        cam.transform.position = Vector3.Lerp(cam.transform.position, want, k);
        target = Vector3.Lerp(target, pt, k);
        cam.transform.LookAt(target);
    }
    public float PlayYaw => pYaw;

    public static Vector3 CatmullClosed(Vector3[] pts, float u)
    {
        int n = pts.Length; float f = u * n; int i = Mathf.FloorToInt(f) % n; if (i < 0) i += n; float t = f - Mathf.Floor(f);
        Vector3 p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
    }
}
