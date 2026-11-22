using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Doors for the imported starter-pack houses. Every house type's front (door side) was identified from its mesh:
/// the door is a low, brown, outward-facing submesh on the model's local +Z face. At runtime this finds every placed
/// house, adds an entrance marker at its door, and when the Star stands at a door a scribbled "enter" appears.
/// Pressing E fades out, loads that house type's interior scene additively, moves the Star (and the child, if
/// they're together) inside, and fades back in. The interior's door brings them back out to the same doorstep.
/// </summary>
public class HouseEntrances : MonoBehaviour
{
    public static HouseEntrances Instance { get; private set; }

    /// <summary>Door point (local to the house root) and the interior scene for each house type.</summary>
    [System.Serializable]
    public class HouseType { public string namePrefix; public Vector3 doorLocal; public string interiorScene; }
    public List<HouseType> types = new List<HouseType>
    {
        new HouseType { namePrefix = "TwoStoryMedievalHouse_2", doorLocal = new Vector3(.62f, 0, 2.35f), interiorScene = "Interior_TwoStoryMedievalHouse_2" },
        new HouseType { namePrefix = "TwoStoryMedievalHouse",   doorLocal = new Vector3(0f, 0, 3.75f),   interiorScene = "Interior_TwoStoryMedievalHouse" },
        new HouseType { namePrefix = "SmallMedievalHouse",      doorLocal = new Vector3(-.59f, 0, 2.7f), interiorScene = "Interior_SmallMedievalHouse" },
        new HouseType { namePrefix = "MedievalHouse",           doorLocal = new Vector3(-.04f, 0, 3.35f), interiorScene = "Interior_MedievalHouse" },
    };
    public float doorRadius = 2.2f;
    public string promptText = "enter";

    public class Door { public Transform house; public HouseType type; public Vector3 point, outward; }
    readonly List<Door> doors = new List<Door>();
    Transform star; Text prompt; CanvasGroup promptGroup; Image fade;
    bool busy, inside; Door current; Vector3 returnPoint; float returnYaw;
    public bool Inside => inside;

    void Awake() { Instance = this; }

    IEnumerator Start()
    {
        while (IslandBuilder.HGrid == null) yield return null;
        yield return null;
        while (VillageDressing.Instance && !VillageDressing.Instance.Built) yield return null;   // so door lamps don't double up with the dressed hamlet
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
        FindDoors();
        prompt = WishboundUI.Label(null, "EnterPrompt", promptText, 46, new Vector2(.5f, .14f), new Vector2(600, 120));
        promptGroup = prompt.GetComponent<CanvasGroup>();
        var fg = new GameObject("DoorFade", typeof(RectTransform)); fg.transform.SetParent(WishboundUI.Canvas.transform, false);
        var r = (RectTransform)fg.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        fade = fg.AddComponent<Image>(); fade.color = Color.clear; fade.raycastTarget = false;
    }

    void FindDoors()
    {
        doors.Clear();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name.Contains("LOD")) continue;
            if (t.parent && t.parent.name.Contains("MedievalHo")) continue;
            HouseType type = null;
            foreach (var ht in types) if (t.name.StartsWith(ht.namePrefix)) { type = ht; break; }
            if (type == null || t.Find(t.name.Split(' ')[0] + "_LOD0") == null && t.GetComponentInChildren<MeshRenderer>() == null) continue;
            var d = new Door { house = t, type = type };
            d.point = t.TransformPoint(type.doorLocal) + t.forward * .4f;
            if (Physics.Raycast(d.point + Vector3.up * 1.2f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) d.point = gh.point;   // the doorstep, not the eave above it
            d.outward = t.forward;
            GrassExclusions.Add(d.point, 2.5f);   // keep scattered grass off the doorstep
            doors.Add(d);
        }
        Debug.Log("[HouseEntrances] " + doors.Count + " doors found");
        if (lampPostAtEveryDoor) PlaceDoorLamps();
    }

    [Tooltip("Stand a warm lamp post beside every house door (the village's night lighting).")]
    public bool lampPostAtEveryDoor = true;

    void PlaceDoorLamps()
    {
        var root = new GameObject("DoorLamps").transform;
        var existing = new List<Vector3>();
        foreach (var l in FindObjectsByType<LanternFlicker>(FindObjectsSortMode.None)) existing.Add(l.transform.position);
        int i = 0;
        foreach (var d in doors)
        {
            Vector3 side = Vector3.Cross(Vector3.up, d.outward);
            Vector3 p = d.point + d.outward * 1.2f + side * 1.6f;
            bool near = false; foreach (var e in existing) if ((new Vector3(e.x - p.x, 0, e.z - p.z)).sqrMagnitude < 9f) { near = true; break; }
            if (near) continue;
            if (Physics.CheckSphere(p + Vector3.up * 1.2f, .5f, ~0, QueryTriggerInteraction.Ignore)) p = d.point + d.outward * 1.2f - side * 1.6f;
            VillageKit.LanternPost(p, root, 3000 + i++, Quaternion.LookRotation(-side).eulerAngles.y);
            existing.Add(p);
        }
    }

    public int DoorCount => doors.Count;
    public Door NearestDoor(Vector3 p)
    {
        Door best = null; float bd = float.MaxValue;
        foreach (var d in doors) { float dist = Vector3.Distance(Flat(d.point), Flat(p)); if (dist < bd) { bd = dist; best = d; } }
        return best;
    }

    void Update()
    {
        if (!star || busy || CutsceneDirector.Active || WishboundIntro.IsPlaying) { if (promptGroup) promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 0, Time.deltaTime * 3); return; }
        bool show = false;
        if (!inside)
        {
            var d = NearestDoor(star.position);
            if (d != null && Vector3.Distance(Flat(d.point), Flat(star.position)) < doorRadius && Mathf.Abs(d.point.y - star.position.y) < 2.5f)
            {
                show = true; prompt.text = "E  -  " + promptText;
                if (StarInput.ReachPressed) StartCoroutine(Enter(d));
            }
        }
        else if (InteriorScene.Active && InteriorScene.Active.NearExit(star.position))
        {
            show = true; prompt.text = "E  -  leave";
            if (StarInput.ReachPressed) StartCoroutine(Exit());
        }
        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, show ? 1 : 0, Time.deltaTime * 3);
    }

    IEnumerator FadeTo(float a, float d)
    {
        float s = fade.color.a;
        for (float t = 0; t < d; t += Time.unscaledDeltaTime) { fade.color = new Color(0, 0, 0, Mathf.Lerp(s, a, t / d)); yield return null; }
        fade.color = new Color(0, 0, 0, a);
    }

    public IEnumerator Enter(Door d)
    {
        if (busy || inside || d == null || !star) yield break;
        busy = true; current = d;
        returnPoint = d.point + d.outward * 1.2f; returnYaw = Quaternion.LookRotation(d.outward).eulerAngles.y;
        yield return FadeTo(1, .6f);
        if (!Application.CanStreamedLevelBeLoaded(d.type.interiorScene)) { Debug.LogWarning("[HouseEntrances] interior scene not in Build Settings: " + d.type.interiorScene); yield return FadeTo(0, .4f); busy = false; yield break; }
        var op = SceneManager.LoadSceneAsync(d.type.interiorScene, LoadSceneMode.Additive);
        while (!op.isDone) yield return null;
        yield return null;
        var interior = InteriorScene.Active;
        if (interior && interior.spawn)
        {
            inside = true;
            MoveParty(interior.spawn.position, interior.spawn.eulerAngles.y);
            interior.OnEntered();
        }
        if (!inside) { Debug.LogError("[HouseEntrances] Interior has no valid spawn."); yield return SceneManager.UnloadSceneAsync(d.type.interiorScene); }
        yield return FadeTo(0, .6f);
        busy = false;
    }

    public IEnumerator Exit()
    {
        if (busy || !inside) yield break;
        busy = true;
        yield return FadeTo(1, .6f);
        var interior = InteriorScene.Active;
        if (interior) interior.OnLeaving();
        MoveParty(returnPoint, returnYaw);
        if (interior) { var op = SceneManager.UnloadSceneAsync(interior.gameObject.scene); while (op != null && !op.isDone) yield return null; }
        inside = false;
        yield return FadeTo(0, .6f);
        busy = false;
    }

    void MoveParty(Vector3 pos, float yaw)
    {
        var player = star.GetComponent<IslandThirdPersonController>();
        if (player) player.Teleport(pos + Vector3.up * .3f, yaw); else star.position = pos;
        var ce = ChildEncounter.Instance;
        if (ce && ce.HasMet && ce.Child && ce.Child.gameObject.activeInHierarchy)
        {
            var cc = ce.Child.GetComponent<CharacterController>();
            Vector3 cp = pos + Quaternion.Euler(0, yaw, 0) * new Vector3(1.2f, .3f, -1f);
            if (cc) cc.enabled = false; ce.Child.transform.position = cp; if (cc) cc.enabled = true;
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}


