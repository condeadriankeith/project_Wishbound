using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The settler community of Aethelgard. Once the village is dressed and the doors are known, it moves families into
/// the houses nearest the hamlet (adults, elders and kids), gives each a role, a temperament and a name, and runs the
/// little social layer: neighbours who meet stop for a short chat, and everyone keeps a bit of personal space.
/// Far-away settlers stop being drawn and animated to keep the frame rate up.
/// </summary>
public class SettlerCommunity : MonoBehaviour
{
    public static SettlerCommunity Instance { get; private set; }
    [Tooltip("Houses (by distance from the hamlet) that get a family.")]
    public int households = 12;
    public int maxSettlers = 28;
    public float adultScale = 1.22f;
    public int seed = 777;
    public float cullDistance = 90f;

    public readonly List<SettlerBrain> settlers = new List<SettlerBrain>();
    public readonly List<Transform> Plots = new List<Transform>();
    public Transform Well, Pond; public float PondRadius = 3.5f;
    readonly List<Vector3> homes = new List<Vector3>();
    readonly List<Vector3> pathPoints = new List<Vector3>();

    static readonly string[] Names = { "Ada", "Bram", "Cora", "Dell", "Edda", "Finn", "Greta", "Hob", "Ivo", "Juno", "Kit", "Lark", "Mira", "Ned", "Orla", "Pip", "Quill", "Rosa", "Sten", "Tilly", "Ulf", "Vera", "Wren", "Yara" };
    static readonly string[][] Chats =
    {
        new[] { "lovely evening", "the sky's all gold", "mm" },
        new[] { "beans are up!", "already?", "finally rain helped" },
        new[] { "did you see that light?", "fell by the beach...", "strange night" },
        new[] { "need any water?", "please, the well's far", "i'll bring some" },
        new[] { "caught anything?", "just the one", "better than me" },
        new[] { "my back aches", "too much hoeing", "ha, every day" },
        new[] { "smell that bread?", "the baker's oven", "i'm hungry now" },
        new[] { "hello!", "hello hello", "good day" },
        new[] { "wind's turning", "storm tomorrow?", "maybe" },
        new[] { "kids are loud today", "they're happy", "true" },
        new[] { "...", "...", "nice, isn't it" },
    };

    void Awake() { Instance = this; }

    IEnumerator Start()
    {
        while (IslandBuilder.HGrid == null) yield return null;
        while (VillageDressing.Instance && !VillageDressing.Instance.Built) yield return null;
        while (HouseEntrances.Instance == null || HouseEntrances.Instance.DoorCount == 0) yield return null;
        yield return null;
        Gather();
        Spawn();
        StartCoroutine(SocialLoop());
    }

    void Gather()
    {
        var vd = VillageDressing.Instance;
        if (vd)
        {
            Well = vd.Well;
            foreach (Transform t in vd.transform)
            {
                if (t.name.StartsWith("FarmPlot")) Plots.Add(t);
                if (t.name == "Fishpond") Pond = t;
                if (t.name == "Path") foreach (Transform c in t) { var mf = c.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { var v = mf.sharedMesh.vertices; for (int i = 0; i < v.Length; i += 6) pathPoints.Add(c.TransformPoint(v[i])); } }
            }
        }
    }

    void Spawn()
    {
        var child = FindFirstObjectByType<ProceduralFollow>(FindObjectsInactive.Include);
        Transform template = child ? child.transform.Find("ChildAnimatedAvatar") : null;
        if (!template) { Debug.LogWarning("[SettlerCommunity] no child avatar to base settlers on"); return; }
        var rng = new System.Random(seed);
        Vector3 centre = Well ? Well.position : (VillageDressing.Instance ? VillageDressing.Instance.transform.position : Vector3.zero);

        // households: the doors closest to the hamlet
        var doors = new List<HouseEntrances.Door>();
        var list = typeof(HouseEntrances).GetField("doors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(HouseEntrances.Instance) as List<HouseEntrances.Door>;
        if (list != null) doors.AddRange(list);
        doors.Sort((a, b) => (a.point - centre).sqrMagnitude.CompareTo((b.point - centre).sqrMagnitude));
        foreach (var d in doors) homes.Add(d.point + d.outward * 1.2f);
        int nameIdx = rng.Next(Names.Length);
        for (int h = 0; h < Mathf.Min(households, doors.Count) && settlers.Count < maxSettlers; h++)
        {
            var d = doors[h];
            int adults = rng.NextDouble() < .55 ? 2 : 1; int kids = rng.NextDouble() < .55 ? rng.Next(1, 3) : 0; bool elder = rng.NextDouble() < .25;
            for (int i = 0; i < adults + kids + (elder ? 1 : 0) && settlers.Count < maxSettlers; i++)
            {
                SettlerLook.Age age = i < adults ? SettlerLook.Age.Adult : (i < adults + kids ? SettlerLook.Age.Child : SettlerLook.Age.Elder);
                SettlerBrain.Role role;
                if (age == SettlerLook.Age.Child) role = SettlerBrain.Role.Kid;
                else if (age == SettlerLook.Age.Elder) role = SettlerBrain.Role.Elder;
                else
                {
                    double r = rng.NextDouble();
                    role = Plots.Count > 0 && r < .45 ? (rng.NextDouble() < .5 ? SettlerBrain.Role.Farmer : SettlerBrain.Role.Gardener)
                         : Pond && r < .6 ? SettlerBrain.Role.Fisher : SettlerBrain.Role.Wanderer;
                }
                Vector3 at = d.point + d.outward * (1.5f + i * .9f) + Vector3.Cross(Vector3.up, d.outward) * (i - 1) * 1.1f;
                // ground at the doorstep's height (not the roof above it)
                if (Physics.Raycast(new Vector3(at.x, d.point.y + 1.5f, at.z), Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) at = gh.point; else at.y = d.point.y;
                var outfit = SettlerLook.Random(age, rng);
                SettlerLook.DressForRole(outfit, role, rng);
                Make(template, outfit, role, (SettlerBrain.Temperament)rng.Next(3), Names[nameIdx++ % Names.Length], at, d.point + d.outward * 1.2f);
            }
        }
        Debug.Log("[SettlerCommunity] " + settlers.Count + " settlers moved in");
    }

    void Make(Transform template, SettlerLook.Outfit o, SettlerBrain.Role role, SettlerBrain.Temperament temp, string name, Vector3 at, Vector3 home)
    {
        var go = new GameObject("Settler_" + name + "_" + role);
        go.transform.SetParent(transform, false); go.transform.position = at + Vector3.up * .1f;
        go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        float scale = o.age == SettlerLook.Age.Child ? 1f : o.age == SettlerLook.Age.Elder ? adultScale * .95f : adultScale;
        scale *= o.stature;
        var cc = go.AddComponent<CharacterController>(); cc.height = 1.7f * scale; cc.radius = .3f * scale; cc.center = Vector3.up * cc.height * .5f; cc.slopeLimit = 50; cc.stepOffset = .4f; cc.minMoveDistance = 0;
        var visual = new GameObject("Visual").transform; visual.SetParent(go.transform, false); visual.localScale = Vector3.one * scale;
        var body = SettlerLook.Build(template, visual, o);
        var rig = go.AddComponent<SettlerRig>(); rig.Bind(body);
        var brain = go.AddComponent<SettlerBrain>();
        brain.role = role; brain.temperament = temp; brain.settlerName = name; brain.home = home; brain.rig = rig; brain.community = this; brain.height = 1.75f * scale;
        settlers.Add(brain);
    }

    // ================================================================== shared knowledge
    public Vector3 RandomPathPoint(Vector3 near, float radius, System.Random rng)
    {
        for (int i = 0; i < 12 && pathPoints.Count > 0; i++)
        {
            var p = pathPoints[rng.Next(pathPoints.Count)];
            if ((p - near).sqrMagnitude < radius * radius * 4) return p;
        }
        if (homes.Count > 0 && rng.NextDouble() < .5) return homes[rng.Next(Mathf.Min(homes.Count, households))];
        return near + Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0) * Vector3.forward * radius * (.4f + (float)rng.NextDouble() * .6f);
    }
    public Vector3 RandomHome(System.Random rng) => homes.Count > 0 ? homes[rng.Next(Mathf.Min(homes.Count, households))] : transform.position;

    public SettlerBrain NearestKid(SettlerBrain me, float range) => Nearest(me, range, b => b.role == SettlerBrain.Role.Kid);
    public SettlerBrain NearestAdult(SettlerBrain me, float range) => Nearest(me, range, b => b.role != SettlerBrain.Role.Kid);
    SettlerBrain Nearest(SettlerBrain me, float range, System.Func<SettlerBrain, bool> ok)
    {
        SettlerBrain best = null; float bd = range * range;
        foreach (var s in settlers) { if (s == me || !ok(s)) continue; float d = (s.transform.position - me.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
        return best;
    }

    /// <summary>Gentle push away from settlers who are too close (and from the Star and the child).</summary>
    public Vector3 Separation(SettlerBrain me)
    {
        Vector3 push = Vector3.zero; Vector3 p = me.transform.position;
        foreach (var s in settlers)
        {
            if (s == me) continue;
            Vector3 d = p - s.transform.position; d.y = 0; float m = d.magnitude;
            if (m < 1.1f && m > .001f) push += d / m * (1.1f - m);
        }
        return push;
    }

    // ================================================================== conversations + culling
    IEnumerator SocialLoop()
    {
        var rng = new System.Random(seed + 1);
        while (true)
        {
            yield return new WaitForSeconds(1.5f);
            // pair up two free neighbours who happen to be near each other
            for (int i = 0; i < settlers.Count; i++)
            {
                var a = settlers[i]; if (!a.CanChat || a.role == SettlerBrain.Role.Kid && rng.NextDouble() < .6) continue;
                for (int j = i + 1; j < settlers.Count; j++)
                {
                    var b = settlers[j]; if (!b.CanChat) continue;
                    if ((a.transform.position - b.transform.position).sqrMagnitude > 7f * 7f) continue;
                    var lines = Chats[rng.Next(Chats.Length)];
                    if (a.role == SettlerBrain.Role.Kid && b.role == SettlerBrain.Role.Kid) lines = new[] { "race you!", "no fair!", "hehe" };
                    a.StartCoroutine(a.Chat(b, lines, true)); b.StartCoroutine(b.Chat(a, lines, false));
                    goto next;
                }
            }
            next:;
            Cull();
        }
    }

    void Cull()
    {
        var cam = Camera.main; if (!cam) return;
        bool inside = HouseEntrances.Instance && HouseEntrances.Instance.Inside;
        foreach (var s in settlers)
        {
            bool far = inside || (s.transform.position - cam.transform.position).sqrMagnitude > cullDistance * cullDistance;
            foreach (var r in s.GetComponentsInChildren<Renderer>()) r.forceRenderingOff = far;
            s.rig.enabled = !far;
        }
    }
}
