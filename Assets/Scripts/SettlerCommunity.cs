using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The settler community of Aethelgard. Once the village is dressed and the doors are known, it moves families into
/// the houses nearest the hamlet and a few mountain communities higher up (adults, elders and kids), gives each a role, a temperament and a name, ties them into families, couples and friend circles, and runs the
/// little social layer: neighbours who meet stop for a short chat, and everyone keeps a bit of personal space.
/// Far-away settlers stop being drawn and animated to keep the frame rate up.
/// </summary>
public class SettlerCommunity : MonoBehaviour
{
    /// <summary>A small social unit. Members spend time together and talk in the way that suits them.</summary>
    public sealed class Group
    {
        public enum Kind { Family, Partners, Friends }
        public Kind kind;
        public readonly List<SettlerBrain> members = new List<SettlerBrain>();
        /// <summary>Chance that an idle member goes to be with the group rather than doing something else.</summary>
        public float Togetherness => kind == Kind.Friends ? .35f : .5f;
    }

    public static SettlerCommunity Instance { get; private set; }
    [Tooltip("Houses (by distance from the hamlet) that get a family.")]
    public int households = 12;
    [Tooltip("Mountain houses that get a family, grouped into a few small communities up the slope.")]
    public int mountainHouseholds = 8;
    [Tooltip("How far above the hamlet a house has to be to count as a mountain house.")]
    public float mountainRise = 18f;
    public int maxSettlers = 60;
    public float adultScale = 1.22f;
    public int seed = 777;
    public float cullDistance = 90f;

    public readonly List<SettlerBrain> settlers = new List<SettlerBrain>();
    public readonly List<Transform> Plots = new List<Transform>();
    public Transform Well, Pond; public float PondRadius = 3.5f;
    readonly List<Vector3> occupied = new List<Vector3>();   // doorsteps with someone living behind them
    public readonly List<Group> groups = new List<Group>();
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

    // what families, couples and circles of friends say when they stop for a moment together
    static readonly string[][] FamilyChats =
    {
        new[] { "supper's nearly ready", "smells lovely", "bring the little ones in" },
        new[] { "how was your day?", "busy, busy", "come and sit a while" },
        new[] { "mind the goat!", "ha, all right then", "love you" },
    };
    static readonly string[][] PartnerChats =
    {
        new[] { "walk with me a while?", "always", "the light's lovely tonight" },
        new[] { "you were quiet today", "just tired", "then sit with me" },
        new[] { "you made me smile", "you always do", "hush, then" },
    };
    static readonly string[][] FriendChats =
    {
        new[] { "did you hear the news?", "tell me!", "ha, no way" },
        new[] { "same time tomorrow?", "always", "bring the cider" },
        new[] { "my turn to tell one", "go on", "it's about the fish" },
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

        var rng = new System.Random(seed);
        Vector3 centre = Well ? Well.position : (VillageDressing.Instance ? VillageDressing.Instance.transform.position : Vector3.zero);

        // every house door on the island, nearest the hamlet first
        var doors = new List<HouseEntrances.Door>();
        var list = typeof(HouseEntrances).GetField("doors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(HouseEntrances.Instance) as List<HouseEntrances.Door>;
        if (list != null) doors.AddRange(list);
        doors.Sort((a, b) => (a.point - centre).sqrMagnitude.CompareTo((b.point - centre).sqrMagnitude));

        // the hamlet is the doors closest to the centre; a few mountain communities sit higher up
        int hamletCount = Mathf.Min(households, doors.Count);
        float hamletY = 0f;
        for (int h = 0; h < hamletCount; h++) hamletY += doors[h].point.y;
        if (hamletCount > 0) hamletY /= hamletCount;
        var toMoveIn = new List<HouseEntrances.Door>(doors.GetRange(0, hamletCount));
        var mountainDoors = new HashSet<HouseEntrances.Door>();
        foreach (var plot in Plots)
        {
            if (!plot || (plot.position - centre).sqrMagnitude < 78f * 78f) continue;
            HouseEntrances.Door closest = null;
            float distance = 28f * 28f;
            for (int i = hamletCount; i < doors.Count; i++)
            {
                var door = doors[i];
                float candidate = (door.point - plot.position).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                closest = door;
            }
            if (closest != null && mountainDoors.Add(closest)) toMoveIn.Add(closest);
        }
        foreach (var cluster in MountainCommunities(doors.GetRange(hamletCount, doors.Count - hamletCount), hamletY))
        {
            foreach (var door in cluster)
                if (mountainDoors.Add(door)) toMoveIn.Add(door);
        }

        int nameIdx = rng.Next(Names.Length);
        foreach (var d in toMoveIn)
        {
            bool mountain = mountainDoors.Contains(d);
            int reserved = mountainDoors.Count - (mountain ? 1 : 0);
            int limit = Mathf.Max(1, maxSettlers - reserved);
            if (mountain) mountainDoors.Remove(d);
            if (settlers.Count >= limit) continue;
            Vector3 doorstep = d.point + d.outward * 1.2f;
            int adults = rng.NextDouble() < .55 ? 2 : 1; int kids = rng.NextDouble() < .55 ? rng.Next(1, 3) : 0; bool elder = rng.NextDouble() < .25;
            var household = new List<SettlerBrain>();
            for (int i = 0; i < adults + kids + (elder ? 1 : 0) && settlers.Count < limit; i++)
            {
                SettlerLook.Age age = i < adults ? SettlerLook.Age.Adult : (i < adults + kids ? SettlerLook.Age.Child : SettlerLook.Age.Elder);
                SettlerBrain.Role role;
                if (age == SettlerLook.Age.Child) role = SettlerBrain.Role.Kid;
                else if (age == SettlerLook.Age.Elder) role = SettlerBrain.Role.Elder;
                else
                {
                    double r = rng.NextDouble();
                    role = mountain && i == 0 ? SettlerBrain.Role.Gardener
                         : Plots.Count > 0 && r < .45 ? (rng.NextDouble() < .5 ? SettlerBrain.Role.Farmer : SettlerBrain.Role.Gardener)
                         : Pond && r < .6 ? SettlerBrain.Role.Fisher : SettlerBrain.Role.Wanderer;
                }
                Vector3 at = d.point + d.outward * (1.5f + i * .9f) + Vector3.Cross(Vector3.up, d.outward) * (i - 1) * 1.1f;
                // ground at the doorstep's height (not the roof above it)
                if (Physics.Raycast(new Vector3(at.x, d.point.y + 1.5f, at.z), Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) at = gh.point; else at.y = d.point.y;
                var outfit = SettlerLook.Random(age, rng);
                SettlerLook.DressForRole(outfit, role, rng);
                household.Add(Make(template, outfit, role, (SettlerBrain.Temperament)rng.Next(3), Names[nameIdx++ % Names.Length], at, doorstep));
            }
            if (household.Count == 0) continue;
            occupied.Add(doorstep);
            JoinHousehold(household);
        }
        FormFriendCircles(rng);
        SpawnCouriers(template, centre, rng);
        Debug.Log("[SettlerCommunity] " + settlers.Count + " settlers moved in (" + groups.Count + " groups)");
    }

    void SpawnCouriers(Transform template, Vector3 village, System.Random rng)
    {
        if (RoadBuild.Roads.Count == 0) return;
        var stops = new List<Stockpile> { TradeStop(village, "Village exchange", Res.Bread) };
        var candidates = new List<Vector3>(occupied);
        candidates.Sort((a, b) => a.y.CompareTo(b.y));
        foreach (Vector3 point in candidates)
        {
            if (stops.Count >= 6) break;
            if (stops.Exists(site => RouteSpacing(site.Dock, point) < 78f)) continue;
            var road = MountainRideRoute.RoadWaypoints(point, point);
            if (road == null || RouteSpacing(road[0], point) > 65f) continue;
            Vector3 at = road[0];
            Stockpile source = stops[0];
            foreach (var stop in stops)
                if (RouteSpacing(stop.Dock, at) < RouteSpacing(source.Dock, at)) source = stop;
            var waypoints = MountainRideRoute.RoadWaypoints(source.Dock, at);
            if (waypoints == null || waypoints.Count < 12) continue;
            bool uphill = at.y > village.y + 15f;
            var destination = TradeStop(at, uphill ? "Mountain trail exchange" : "Roadside exchange", uphill ? Res.Wood : Res.Carrot);
            stops.Add(destination);
            int middle = waypoints.Count / 2;
            Vector3 facing = Vector3.ProjectOnPlane(waypoints[middle + 1] - waypoints[middle], Vector3.up).normalized;
            Vector3 spawn = waypoints[middle];
            var outfit = SettlerLook.Random(SettlerLook.Age.Adult, rng);
            SettlerLook.DressForRole(outfit, SettlerBrain.Role.Wanderer, rng);
            var courier = Make(template, outfit, SettlerBrain.Role.Wanderer, SettlerBrain.Temperament.Puzzled,
                "Trail courier " + stops.Count, spawn, source.Dock);
            courier.transform.forward = facing;
            var cart = VillageKitV2.Cart(spawn - facing * 1.95f, transform, seed + stops.Count).GetComponent<HandCart>();
            cart.name = "Community freight cart";
            cart.transform.forward = facing;
            if (VillageWorld.I) VillageWorld.I.carts.Remove(cart);
            courier.AssignTrade(source, destination, waypoints, middle, cart);
        }
    }

    Stockpile TradeStop(Vector3 point, string name, Res offered)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);
        root.transform.position = point;
        var depot = root.AddComponent<Stockpile>();
        depot.label = name;
        depot.Add(offered, 8);
        return depot;
    }

    static float RouteSpacing(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    /// <summary>Mountain communities: houses well above the hamlet, grouped into little clusters (lowest first) so each
    /// one reads as its own village. At most mountainHouseholds doors in total.</summary>
    List<List<HouseEntrances.Door>> MountainCommunities(List<HouseEntrances.Door> rest, float hamletY)
    {
        var clusters = new List<List<HouseEntrances.Door>>();
        if (mountainHouseholds <= 0) return clusters;
        var up = rest.FindAll(d => d.point.y > hamletY + mountainRise);
        up.Sort((a, b) => a.point.y.CompareTo(b.point.y));
        int taken = 0;
        foreach (var d in up)
        {
            if (taken >= mountainHouseholds) break;
            List<HouseEntrances.Door> near = null; float best = 30f * 30f;
            foreach (var c in clusters)
            {
                float dd = (c[0].point - d.point).sqrMagnitude;
                if (dd < best && c.Count < 3) { best = dd; near = c; }
            }
            if (near == null) { near = new List<HouseEntrances.Door>(); clusters.Add(near); }
            near.Add(d); taken++;
        }
        return clusters;
    }

    SettlerBrain Make(Transform template, SettlerLook.Outfit o, SettlerBrain.Role role, SettlerBrain.Temperament temp, string name, Vector3 at, Vector3 home)
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
        return brain;
    }

    /// <summary>Ties the people who moved into one house together: a family when there are kids or a grandparent (the kids
    /// get a grown-up to tag along after), a couple when there are just two grown-ups, and nothing for a lone adult
    /// (they can still join a circle of friends later).</summary>
    void JoinHousehold(List<SettlerBrain> household)
    {
        bool family = household.Exists(s => s.role == SettlerBrain.Role.Kid || s.role == SettlerBrain.Role.Elder);
        if (!family && household.Count != 2) return;
        var parent = household.Find(s => s.role != SettlerBrain.Role.Kid && s.role != SettlerBrain.Role.Elder) ?? household[0];
        var g = new Group { kind = family ? Group.Kind.Family : Group.Kind.Partners };
        foreach (var s in household)
        {
            s.group = g; g.members.Add(s);
            if (s.role == SettlerBrain.Role.Kid) s.guardian = parent;
        }
        groups.Add(g);
    }

    /// <summary>Loose circles of 2-4 neighbors from different households (up to 8 circles).</summary>
    void FormFriendCircles(System.Random rng)
    {
        int made = 0;
        foreach (var seed in settlers)
        {
            if (made >= 8) break;
            if (seed.friends != null || seed.role == SettlerBrain.Role.Kid) continue;
            var near = new List<SettlerBrain>();
            foreach (var s in settlers)
                if (s != seed && s.friends == null && s.role != SettlerBrain.Role.Kid &&
                    (s.home - seed.home).sqrMagnitude > 4f && (s.home - seed.home).sqrMagnitude < 45f * 45f) near.Add(s);
            if (near.Count == 0) continue;
            near.Sort((a, b) => (a.home - seed.home).sqrMagnitude.CompareTo((b.home - seed.home).sqrMagnitude));
            int want = rng.Next(1, 4);   // one to three others: a circle of two to four
            var g = new Group { kind = Group.Kind.Friends };
            g.members.Add(seed); seed.friends = g;
            for (int k = 0; k < near.Count && g.members.Count <= want; k++) { near[k].friends = g; g.members.Add(near[k]); }
            groups.Add(g); made++;
        }
    }
    /// <summary>A scripted settler (no brain, no collider) for cutscenes: dressed, rigged and posable through SettlerRig.</summary>
    public SettlerRig SpawnActor(Vector3 at, float yaw, SettlerLook.Age age, SettlerBrain.Role role, int seed, bool woman = false)
    {
        var rng = new System.Random(seed); var o = SettlerLook.Random(age, rng); SettlerLook.DressForRole(o, role, rng);
        if (woman) { o.bun = true; o.beard = o.moustache = o.strawHat = o.cap = false; }
        var go = new GameObject("Actor_" + role); go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        float scale = (age == SettlerLook.Age.Child ? 1f : age == SettlerLook.Age.Elder ? adultScale * .95f : adultScale) * o.stature;
        var visual = new GameObject("Visual").transform; visual.SetParent(go.transform, false); visual.localScale = Vector3.one * scale;
        var body = SettlerLook.Build(null, visual, o);
        var rig = go.AddComponent<SettlerRig>(); rig.Bind(body);
        return rig;
    }

    // ================================================================== shared knowledge
    public Vector3 RandomPathPoint(Vector3 near, float radius, System.Random rng)
    {
        for (int i = 0; i < 12 && pathPoints.Count > 0; i++)
        {
            var p = pathPoints[rng.Next(pathPoints.Count)];
            if ((p - near).sqrMagnitude < radius * radius * 4) return p;
        }
        if (occupied.Count > 0 && rng.NextDouble() < .5)
        {
            // a neighbour's doorstep, but only one that is close by (mountain houses are a walk away)
            var h = occupied[rng.Next(occupied.Count)];
            if ((h - near).sqrMagnitude < radius * radius * 6.25f) return h;
        }
        return near + Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0) * Vector3.forward * radius * (.4f + (float)rng.NextDouble() * .6f);
    }
    public Vector3 RandomHome(System.Random rng) => occupied.Count > 0 ? occupied[rng.Next(occupied.Count)] : transform.position;

    public SettlerBrain NearestKid(SettlerBrain me, float range) => Nearest(me, range, b => b.role == SettlerBrain.Role.Kid);
    public SettlerBrain NearestAdult(SettlerBrain me, float range) => Nearest(me, range, b => b.role != SettlerBrain.Role.Kid);
    /// <summary>The nearest other member of my family, couple or circle of friends.</summary>
    public SettlerBrain NearestMate(SettlerBrain me, Group group, float range) =>
        group == null ? null : Nearest(me, range, b => b.group == group || b.friends == group);
    SettlerBrain Nearest(SettlerBrain me, float range, System.Func<SettlerBrain, bool> ok)
    {
        SettlerBrain best = null; float bd = range * range;
        foreach (var s in settlers) { if (s == me || !ok(s)) continue; float d = (s.transform.position - me.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
        return best;
    }

    /// <summary>Lines for a chat between members of the same family, couple or circle of friends.</summary>
    public string[] GroupLines(Group g, System.Random rng)
    {
        var pool = g.kind == Group.Kind.Family ? FamilyChats : g.kind == Group.Kind.Partners ? PartnerChats : FriendChats;
        return pool[rng.Next(pool.Length)];
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
            bool far = inside || (s.transform.position - cam.transform.position).sqrMagnitude > Mathf.Pow(Mathf.Max(160f,cullDistance),2);
            foreach (var r in s.GetComponentsInChildren<Renderer>()) r.forceRenderingOff = far;
            s.rig.enabled = !far;
        }
    }
}
