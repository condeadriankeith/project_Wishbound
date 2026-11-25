using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Small, sequential mountain quests using the existing island roads and companion.</summary>
public sealed partial class MountainJourney : MonoBehaviour
{
    public static MountainJourney Instance { get; private set; }
    public enum Step { Parcel, Garden, LastFragment, Hearth, Murals, Wish, SummitBench, Complete }
    public Step Current { get; private set; }
    public Vector3 Destination { get; private set; }
    public bool Busy { get => busy || (ride && ride.Active); private set => busy = value; }
    bool busy;
    public bool Ready => star && hint;
    public event System.Action<Step> ObjectiveChanged;
    public Vector3 GuideDestination
    {
        get
        {
            if (Current == Step.Garden || Current == Step.Murals || Current == Step.Wish ||
                Current == Step.LastFragment || Current == Step.SummitBench)
                return trailGuide;
            return Destination;
        }
    }
    Transform star, child, mural, altar;
    Vector3 home, garden, summit, canyon, observatory, benchPosition, benchForward;
    RoadNet.RoadPath summitRoad;
    Vector3[] summitTrail;
    Vector3 trailGuide;
    float nextTrailGuide;
    UnityEngine.UI.Text hint;
    CanvasGroup hintGroup, travelPrompt;
    MountainStarRide ride;

    bool tether;
    Material wood, glow;
    CutsceneDirector director;

    public static void Begin(StarAlignment path)
    {
        if (Instance || path == StarAlignment.None) return;
        var go = new GameObject("Mountain journey");
        var journey = go.AddComponent<MountainJourney>();
        Instance = journey;
        journey.tether = path == StarAlignment.Tether;
    }

    IEnumerator Start()
    {
        Instance = this;
        while (!ChildEncounter.Instance || !ChildEncounter.Instance.Child || !VillageDressing.Instance || !VillageDressing.Instance.Built || RoadNet.Paths.Count == 0) yield return null;
        star = ChildEncounter.Instance.star; child = ChildEncounter.Instance.Child.transform;
        var village = VillageDressing.Instance;
        home = village.Well ? village.Well.position : Ground(child.position);
        wood=VillageKit.Mat(new Color(.38f,.24f,.13f));
        glow=VillageKit.Mat(new Color(.9f,.75f,.38f),new Color(1f,.65f,.2f));
        var cottage = FindFirstObjectByType<CottageChoiceScene>();
        Vector3 start = cottage ? cottage.Cottage.position : home;
        var main = RoadNet.Paths[0];
        float nearestRoad = float.PositiveInfinity;
        foreach (var road in RoadNet.Paths)
        {
            if (road.x.Length < 2) continue;
            float distance = (RoadPosition(road, RoadIndexNearest(road, start)) - start).sqrMagnitude;
            if (distance < nearestRoad) { nearestRoad = distance; main = road; }
        }
        summitRoad = RoadNet.Paths.Find(road => road.main && road.endCell == RoadNet.SummitCell)
            ?? RoadNet.Paths.Find(road => road.main) ?? main;
        summitTrail = new Vector3[summitRoad.x.Length];
        for (int i = 0; i < summitTrail.Length; i++) summitTrail[i] = Ground(RoadPosition(summitRoad, i));
        int trailStart = NearestTrailIndex(start);
        int trailEnd = summitRoad.x.Length - 1;
        garden = RouteAnchor("Garden", TrailPoint(trailStart, trailEnd, .28f));

        canyon = RouteAnchor("Mural", TrailPoint(trailStart, trailEnd, .24f));
        observatory = RouteAnchor("Observatory", TrailPoint(trailStart, trailEnd, .67f));
        Vector3 peak = Ground(TerrainMath.U(RoadNet.SummitX, RoadNet.SummitH, RoadNet.SummitZ));
        summit = RouteAnchor("Summit overlook", tether ? peak : FindSummitOverlook(peak));
        home = RouteAnchor("Village well", home);
        if (!tether)
        {
            yield return IslandStroll(trailStart, trailEnd);
            yield break;
        }
        BuildFragmentStops();
        hint=WishboundUI.Label(null,"Journey hint","",28,new Vector2(.5f,.08f),new Vector2(1400,90));
        hintGroup=hint.GetComponent<CanvasGroup>();
        var transport = WishboundUI.Label(null, "Star ride prompt", "Press Tab to travel to the next task",
            30, new Vector2(1, 0), new Vector2(590, 52));
        transport.alignment = TextAnchor.MiddleRight;
        transport.rectTransform.pivot = new Vector2(1, 0);
        transport.rectTransform.anchoredPosition = new Vector2(-42, 34);
        travelPrompt = transport.GetComponent<CanvasGroup>();
        ride = gameObject.AddComponent<MountainStarRide>();
        ride.Bind(child, star);
        Current = StoryFlags.Get("GardenFragmentRecovered") ? Step.LastFragment :
            StoryFlags.Get("ParcelFragmentRecovered") ? Step.Garden : Step.Parcel;
        if (Current!=Step.Parcel) parcelFragment.gameObject.SetActive(false);
        if (Current==Step.LastFragment) gardenFragment.gameObject.SetActive(false);
        dispatched = StoryFlags.Get("SeedsRequested");
        parcelChosen = StoryFlags.Get("SeedsPacked");
        carryingParcel = parcelChosen && !StoryFlags.Get("SeedsDelivered");
        gardenWatered = StoryFlags.Get("VinesWatered");
        carryingWater = !gardenWatered && StoryFlags.Get("WaterCollected");
        if (parcelChosen && !carryingParcel) spareParcel.position=doorstep;
        if (gardenWatered) foreach (var vine in harvest) vine.localScale=Vector3.one;
        var fx=star.GetComponentInChildren<StarOrbFX>();
        if (fx && Current!=Step.Parcel) fx.FragmentPower += Current==Step.LastFragment ? 2 : 1;
        SetDestination();
        UpdateTrailGuide();
        if (Current==Step.Parcel && !dispatched) yield return PreviewFragments();
        else if (Current==Step.Parcel) Destination=carryingParcel ? doorstep : parcel.position;
        else if (Current==Step.Garden && (carryingWater || gardenWatered)) Destination=gardenWatered ? canHome : garden;
    }

    IEnumerator IslandStroll(int trailStart, int trailEnd)
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        CutsceneSkip.Begin(() => skipCut = true);
        var follow = child.GetComponent<ProceduralFollow>();
        if (follow) follow.enabled = false;
        foreach (var actor in new[] {child,star})
        {
            var controller = actor.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
        }
        var performer = child.GetComponent<ChildPerformer>();
        if (performer) { performer.Seated = false; performer.Act(ChildEmotion.Curious,star); }
        int index = Mathf.RoundToInt(Mathf.Lerp(trailStart,trailEnd,.33f));
        int direction = trailEnd >= trailStart ? 1 : -1;
        var nearby = new Collider[96];
        int bestTrees = 0;
        for (int sample=1;sample<=20;sample++)
        {
            int candidate = Mathf.RoundToInt(Mathf.Lerp(trailStart,trailEnd,sample/24f));
            int count = Physics.OverlapSphereNonAlloc(summitTrail[candidate],12f,nearby,~0,QueryTriggerInteraction.Ignore);
            int trees = 0;
            for(int i=0;i<count;i++) if(nearby[i].name=="TreeColliders") trees++;
            if(trees>bestTrees) { index=candidate; bestTrees=trees; }
        }
        Vector3 path = summitTrail[index];
        Vector3 forward = FlatDirection(path,summitTrail[Mathf.Clamp(index+direction,0,summitTrail.Length-1)]);
        Vector3 side = Vector3.Cross(Vector3.up,forward);
        child.rotation = Quaternion.LookRotation(forward);
        Speak("Child: Gran says the trees remember the sea. Here. Put your ear where mine is.");
        yield return StoryShot(7f,t =>
        {
            child.position = Ground(path + forward * (t*8f));
            star.position = Ground(child.position + side*.9f) + Vector3.up*(.09f*Mathf.Sin(t*12f));
            var focus = child.position + side*.42f + Vector3.up;
            director.TrackFrame(focus+side*3.3f+forward*1.5f+Vector3.up*.45f,focus,43);
        });
        yield return GlowReply(3.6f);
        Speak("Child: A woodpecker. Gran used to knock back with her stick. Drove him mad.");
        if (performer) performer.Act(ChildEmotion.Shy,star);
        yield return Beat(4.5f);

        Vector3 door = HouseDoor(child.position,-1,out _,out _);
        Vector3 porch = ClearStage(door,8f,.85f);
        forward = FlatDirection(porch,path);
        side = Vector3.Cross(Vector3.up,forward);
        var teller = BuildActor("The lantern keeper",porch,forward,SettlerBrain.Role.Wanderer,817,true);
        if (teller) { teller.pose = SettlerRig.Pose.Wave; teller.lookPoint = porch+forward*2f+Vector3.up; }
        ClearSpeech();
        yield return StoryShot(3.5f,t =>
        {
            child.position = Ground(porch+forward*Mathf.Lerp(5f,1.7f,t));
            child.rotation = Quaternion.LookRotation(-forward);
            star.position = Ground(child.position+side*.85f);
            var focus = Vector3.Lerp(child.position,porch,.45f)+Vector3.up;
            director.TrackFrame(focus + side*4.5f + forward*1.4f + Vector3.up*.5f,focus,43);
        });
        if (performer && teller) performer.Act(ChildEmotion.Curious,teller.transform);
        if (teller) teller.pose = SettlerRig.Pose.Talk;
        Speak("Keeper: My mother lit that window for the fishing boats. One foggy night the wick went out.");
        yield return Beat(6.8f);
        Speak("Keeper: Across the water, every house lit a candle. She always swore the island winked at her.");
        yield return Beat(7f);
        if (teller) teller.pose = SettlerRig.Pose.Idle;
        Speak("Child: Gran tells it the other way round. She says the boats winked first.");
        yield return Beat(5.3f);
        Speak("Keeper: She was in one of them. I suppose she ought to know.");
        yield return Beat(4.8f);

        BuildSummitBench(VillageKit.ChamferBox());
        side = Vector3.Cross(Vector3.up,benchForward);
        child.SetPositionAndRotation(summit+side*.43f+Vector3.up*.22f,Quaternion.LookRotation(benchForward));
        star.position = summit-side*.52f+Vector3.up*.32f;
        if (performer) { performer.Seated = true; performer.Act(ChildEmotion.Warm,star); }
        var seats = (child.position+star.position)*.5f+Vector3.up*.72f;
        director.Cut(seats+benchForward*3.7f+side*2.2f,seats,39);
        yield return GlowReply(3.8f);
        Speak("Child: We take turns. Tomorrow's mine. Will you get up early with me?");
        yield return Beat(5.1f);
        yield return GlowReply(4.9f);
        ClearSpeech();
        yield return StoryShot(5f,t=>director.TrackFrame(seats+benchForward*(3.7f+t*2f)+side*(2.2f+t)+Vector3.up*t,seats,42f));
        // Match the promise on the bench to the same pair on tomorrow's doorstep.
        var day = FindFirstObjectByType<LivingDayNight>();
        if (day) { day.scripted = true; day.scriptedHour = 5.7f; }
        forward = FlatDirection(porch,path);
        side = Vector3.Cross(Vector3.up,forward);
        if (performer) { performer.Seated = false; performer.Act(ChildEmotion.Warm,star); }
        if (teller) teller.pose = SettlerRig.Pose.Wave;
        child.rotation = Quaternion.LookRotation(-forward);
        Speak("Child: You got here before me.");
        yield return StoryShot(5f,t =>
        {
            child.position = Ground(porch + forward*(4f-2.4f*t));
            star.position = Ground(porch + forward*1.55f + side*.85f);
            Vector3 focus = (child.position+star.position)*.5f + Vector3.up;
            director.TrackFrame(focus+side*3f+forward*1.8f+Vector3.up*.35f,focus,39f);
        });
        yield return GlowReply(4.8f);
        if (teller) teller.pose = SettlerRig.Pose.Talk;
        Speak("Keeper: Two on watch! Then I can put the kettle on.");
        yield return Beat(4.6f);
        if (performer) performer.Act(ChildEmotion.Wave,star);
        Speak("Child: Look down there. The first boat saw us.");
        yield return Beat(4.4f);
        ClearSpeech();
        Vector3 dawnFocus = (child.position+star.position)*.5f + Vector3.up;
        yield return StoryShot(6f,t =>
        {
            if (day) day.scriptedHour = Mathf.Lerp(5.7f,6.5f,t);
            director.TrackFrame(dawnFocus+side*(3f+t*3f)+forward*(1.8f+t)+Vector3.up*(.35f+t*1.3f),dawnFocus,41f);
        });
        CutsceneSkip.End();
        StoryFlags.Set("RootsEnding");
        Current = Step.Complete;
        yield return FadeFinale();
        WishboundIntro.QueueFinaleTitle();
        StoryFlags.NewDemoRun();
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
    }

    Vector3 TrailPoint(int start, int end, float fraction)
    {
        return summitTrail[Mathf.RoundToInt(Mathf.Lerp(start, end, fraction))];
    }

    Vector3 FindSummitOverlook(Vector3 peak)
    {
        var overlap = new Collider[24];
        for (float radius = 6f; radius <= 24f; radius += 2f)
        {
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var side = Vector3.Cross(Vector3.up, outward);
                Vector3 ground = Ground(peak + outward * radius);
                if (Mathf.Abs(peak.y - ground.y) > 6f ||
                    Mathf.Abs(Ground(ground + side).y - Ground(ground - side).y) > .28f) continue;
                int hits = Physics.OverlapBoxNonAlloc(ground + Vector3.up, new Vector3(1.7f,.85f,1.3f),
                    overlap, Quaternion.LookRotation(outward), ~0, QueryTriggerInteraction.Ignore);
                bool obstructed = hits == overlap.Length;
                for (int k = 0; k < hits && !obstructed; k++)
                    obstructed = !TerrainProbe.IsTerrain(overlap[k]);
                if (obstructed) continue;
                Vector3 focus = ground + Vector3.up * .8f;
                if (!CutsceneDirector.ClearShot(ground + outward * 3.4f + side * 3 + Vector3.up * 1.5f, focus) ||
                    !CutsceneDirector.ClearShot(ground + outward * 1.8f + side * 3.8f + Vector3.up * 1.55f, focus)) continue;
                benchForward = outward;
                return ground;
            }
        }
        benchForward = Vector3.forward;
        Debug.LogWarning("No clear summit overlook found for the current island layout.");
        return peak + Vector3.forward * 6f;
    }

    int NearestTrailIndex(Vector3 position)
    {
        int closest = 0;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < summitTrail.Length; i++)
        {
            float distance = (summitTrail[i] - position).sqrMagnitude;
            if (distance >= nearest) continue;
            closest = i;
            nearest = distance;
        }
        return closest;
    }

    void UpdateTrailGuide()
    {
        if (Time.time < nextTrailGuide) return;
        nextTrailGuide = Time.time + .35f;
        if (summitTrail == null || Vector3.Distance(star.position, Destination) < 12f)
        {
            trailGuide = Destination;
            return;
        }
        int from = NearestTrailIndex(star.position);
        int to = NearestTrailIndex(Destination);
        var join = summitTrail[from];
        if (Vector3.Distance(star.position, join) > 12f) { trailGuide = join; return; }
        int direction = to >= from ? 1 : -1;
        float distance = 0f;
        int next = from;
        while (next != to && distance < 14f)
        {
            distance += Vector3.Distance(summitTrail[next], summitTrail[next + direction]);
            next += direction;
        }
        trailGuide = next == to ? Destination : summitTrail[next];
    }

    Vector3 RouteAnchor(string name, Vector3 position)
    {
        var anchor = new GameObject(name + " quest anchor").transform;
        anchor.SetParent(transform, false);
        anchor.position = Ground(position);
        return anchor.position;
    }

    static int RoadIndexNearest(RoadNet.RoadPath road, Vector3 point)
    {
        int best = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < road.x.Length; i++)
        {
            Vector3 p = new Vector3((float)road.x[i], 0, TerrainMath.MirrorZ ? -(float)road.z[i] : (float)road.z[i]);
            float d = (p - point).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }
    static Vector3 RoadPosition(RoadNet.RoadPath road, int index)
    {
        index = Mathf.Clamp(index, 0, road.x.Length - 1);
        return new Vector3((float)road.x[index], 0, TerrainMath.MirrorZ ? -(float)road.z[index] : (float)road.z[index]);
    }
    static Vector3 Ground(Vector3 p) { p.y=TerrainProbe.GroundY(p.x,p.z,p.y); return p; }
    GameObject Part(string name, Mesh mesh, Material mat, Vector3 at, Vector3 scale, bool solid=false, Transform parent=null)
    {
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        g.transform.SetParent(parent?parent:transform,false); g.transform.position=at; g.transform.localScale=scale;
        g.GetComponent<MeshFilter>().sharedMesh=mesh; g.GetComponent<MeshRenderer>().sharedMaterial=mat;
        if(solid) { var c=g.AddComponent<MeshCollider>(); c.sharedMesh=mesh; }
        return g;
    }
    Vector3[] echoSites;
    int foundEcho = -1;
    bool SeekEcho(int index, string clue)
    {
        if (foundEcho == index) return false;
        foundEcho = index;
        Destination = echoSites[index];
        WhisperLine.Show(child,clue,6);
        return true;
    }
    void BuildPlaces()
    {
        var box=VillageKit.ChamferBox();
        echoSites = new Vector3[3];
        mural = Authored("ShadowStone0", canyon);
        mural.name = "Old trail marker";
        mural.localScale = new Vector3(1.4f, 1.2f, 1.4f);
        altar = Authored("ObservatoryAltar", observatory);
        Vector3[] landmarks = { home, canyon, observatory };
        string[] tokens = { "Well bell clapper", "Chipped waystone", "Lantern wick" };
        for (int i=0;i<3;i++)
        {
            Vector3 dir = FlatDirection(landmarks[i],landmarks[(i+1)%3]);
            echoSites[i] = ClearStage(landmarks[i]+dir*(6+i*2),7,.6f);
            Part(tokens[i],VillageKit.Blob(114+i,.16f,4,7),wood,echoSites[i]+Vector3.up*.33f,new Vector3(.30f,.55f,.24f));
            Part("Remembered light",VillageKit.Blob(86,.05f,3,8),glow,echoSites[i]+Vector3.up*.63f,Vector3.one*.10f);
        }
        BuildSummitBench(box);
        foreach (var p in new[] { canyon, observatory, benchPosition }) GrassExclusions.Add(p, 4f);
        GrassExclusions.ClearExisting();
    }

    Transform BuildSummitBench(Mesh box, bool withLamp = true)
    {
        benchPosition = summit;
        var root = new GameObject("Summit bench").transform;
        root.SetParent(transform);
        root.SetPositionAndRotation(benchPosition, Quaternion.LookRotation(benchForward));
        var cushion = VillageKit.Mat(new Color(.42f,.23f,.18f));
        void Piece(string name, Vector3 offset, Vector3 size, Material material, bool solid = false)
        {
            var piece = Part(name, box, material, root.TransformPoint(offset), size, solid, root);
            piece.transform.rotation = root.rotation;
        }
        Piece("Wide timber seat", new Vector3(0,.41f,0), new Vector3(2.45f,.15f,.75f), wood, true);
        if (withLamp) Piece("Soft seat cushion", new Vector3(0,.5f,.03f), new Vector3(2.1f,.14f,.64f), cushion);
        for (int i=0;i<3;i++)
            Piece("Back slat", new Vector3(0,.62f+i*.14f,-.33f), new Vector3(2.35f,.11f,.11f),wood);
        foreach(float side in new[]{-1f,1f})
        {
            for (int slat=0;slat<4;slat++)
            {
                float z = -.27f+slat*.18f;
                if (side < 0) Piece("Seat plank",new Vector3(0,.499f,z),new Vector3(2.38f,.035f,.16f),wood);
                Piece("Peg head",new Vector3(side*.92f,.52f,z),new Vector3(.045f,.013f,.045f),cushion);

            }
            Piece("Bench trestle",new Vector3(side*.93f,.2f,0),new Vector3(.2f,.42f,.7f),wood);
            Piece("Bench armrest",new Vector3(side*1.05f,.72f,.02f),new Vector3(.14f,.12f,.66f),wood);
            Piece("Bench back support",new Vector3(side*.99f,.63f,-.33f),new Vector3(.12f,.64f,.12f),wood);
        }
        if (!withLamp) return root;
        var lampPosition = root.TransformPoint(new Vector3(1.25f,.58f,-.29f));
        Part("Summit lamp", VillageKit.Blob(19,0,5,4), glow, lampPosition, new Vector3(.13f,.22f,.13f), parent:root);
        var lamp = new GameObject("Summit lamp light").AddComponent<Light>();
        lamp.transform.SetParent(root, false);
        lamp.transform.position = lampPosition + Vector3.up * .16f;
        lamp.color = new Color(1f,.7f,.42f);
        lamp.range = 4f;
        lamp.intensity = 1.6f;
        return root;
    }
    Transform Authored(string name, Vector3 position)
    {
        var prefab = Resources.Load<GameObject>("VerticalSlice/" + name);
        if (!prefab) throw new System.InvalidOperationException("Missing authored quest prefab: " + name);
        return Instantiate(prefab, position, Quaternion.identity, transform).transform;
    }
    int lastReachFrame = -1;
    internal bool ClaimsReach => lastReachFrame == Time.frameCount || ReachAvailable;
    internal bool ReachAvailable => Ready && !InteractionBlocked && HasTargetInReach && CompanionReady;

    bool InteractionBlocked => Busy || CutsceneDirector.Active || PauseMenu.IsOpen ||
        Time.timeScale <= 0 || WishboundIntro.IsPlaying ||
        (HouseEntrances.Instance && HouseEntrances.Instance.BlocksOutdoorInteraction);

    bool CompanionReady => Current != Step.SummitBench ||
        StarInput.InReach(child.position, benchPosition, 7f, 3f);

    bool HasTargetInReach => Current switch
    {
        Step.Parcel when !dispatched => StarInput.InReach(star.position, packer ? packer.transform.position : Destination, 3.5f),
        Step.Parcel when !parcelChosen => StarInput.InReach(star.position, parcel.position, 2.5f) ||
            StarInput.InReach(star.position, spareParcel.position, 2.5f),
        Step.Garden when !carryingWater => StarInput.InReach(star.position, Destination, 2.5f),
        Step.Garden or Step.LastFragment or Step.SummitBench => StarInput.InReach(star.position, Destination, 4f),
        Step.Complete => false,
        _ => StarInput.InReach(star.position, Destination, 3.5f)
    };
    void Update()
    {
        if(!star||!hint) return;
        bool blocked = InteractionBlocked;
        hintGroup.alpha = blocked || Current == Step.Complete ? 0f : 1f;
        bool canRide = !blocked && CanRide();
        travelPrompt.alpha = Mathf.MoveTowards(travelPrompt.alpha, canRide ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        if (blocked) return;
        if (canRide && WishboundInput.Pressed(Key.Tab, KeyCode.Tab))
        {

            if (ride.TryBegin(Destination, null, () => { nextTrailGuide = 0f; }))
            {

                hintGroup.alpha = travelPrompt.alpha = 0f;
                return;
            }
        }
        UpdateTrailGuide();
        float distance=Vector3.Distance(star.position,Destination);
        hint.text = Instruction() + (ReachAvailable ? "  ·  E" :
            HasTargetInReach && !CompanionReady ? "  ·  Wait for the child" : $"  ·  {Mathf.Max(1, Mathf.CeilToInt(distance))} m away");
        AdvanceQuest(StarInput.ReachPressed);
    }

    bool CanRide()
    {
        if (!ride || !ride.Ready) return false;
        if (Current is not (Step.Parcel or Step.Garden or Step.LastFragment or Step.Hearth or Step.Murals or Step.Wish or Step.SummitBench))
            return false;
        Vector3 offset = Destination - star.position;
        offset.y = 0f;
        return offset.sqrMagnitude > 9f * 9f;
    }

    void AdvanceQuest(bool reachPressed)
    {
        if (!reachPressed || !ReachAvailable) return;
        lastReachFrame = Time.frameCount;
        switch(Current)
        {
            case Step.Parcel:
                if (!dispatched)
                {
                    dispatched = true; StoryFlags.Set("SeedsRequested");
                    Destination = Ground(parcel.position);
                    WhisperLine.Show(packer ? packer.transform : child,"Take the seeds to " + address + ". Keep the starlight here.",5);
                }
                else if (!parcelChosen)
                {
                    StartCoroutine(InspectParcels());
                }
                else if (carryingParcel)
                {
                    carryingParcel = false; StoryFlags.Set("SeedsDelivered");
                    spareParcel.position = doorstep;
                    Destination = parcel.position;
                    WhisperLine.Show(child,"The seeds are safe. Let's go back for the light.",4);
                }
                else StartCoroutine(RecoverAt(Current,parcelFragment,"ParcelFragmentRecovered",Step.Garden));
                break;
            case Step.Garden:
                if (gardenWatered) StartCoroutine(RecoverAt(Current,gardenFragment,"GardenFragmentRecovered",Step.LastFragment));
                else if (carryingWater) StartCoroutine(TendGarden());
                else { carryingWater = true; StoryFlags.Set("WaterCollected"); Destination = garden; }
                break;
            case Step.LastFragment:
                if (!invited) StartCoroutine(InviteToBench());
                else StartCoroutine(SummitFarewell());
                break;
            case Step.Hearth:
                if (SeekEcho(0,"The bell is gone. Follow the rim toward the ridge and find its clapper.")) break;
                StartCoroutine(Reward("VillageHearthResolved", "See the notch in the well rim? When the fog took the path, Gran rang her bucket against that stone. Someone on the ridge always answered.", Step.Murals));
                break;
            case Step.Murals:
                if (SeekEcho(1,"One stone broke away. Look uphill for the missing piece.")) break;
                StartCoroutine(Reward("StarfallLore", "Those scratches are names. Look where the smallest hand stopped carving. That's Gran. She was the child lost in the fog.", Step.Wish));
                break;
            case Step.Wish:
                if (SeekEcho(2,"Someone carried the lantern outside. Find where its ember fell.")) break;
                StartCoroutine(Reward("ObservatoryWishHeard", "The light that answered Gran came from here. I lit her lantern again the night you fell. I thought I had called you down. Did you come because you heard me?", Step.SummitBench));
                break;
            case Step.SummitBench:
                StartCoroutine(RestAtSummit());
                break;
        }
    }
    void LateUpdate()
    {
        if (carryingParcel) spareParcel.position = star.position + Vector3.up*.82f + star.right*.54f;
        if (carryingWater) waterCan.position = star.position + Vector3.up*.58f + star.right*.56f;
    }

    IEnumerator TendGarden()
    {
        Busy = true;
        carryingWater = false;
        director = CutsceneDirector.Begin();
        CutsceneSkip.Begin(() => skipCut = true);
        yield return WaterGarden();
        gardenWatered = true; StoryFlags.Set("VinesWatered");
        Destination = canHome;
        CutsceneSkip.End();
        skipCut = false;
        director.End();
        director = null;
        Busy = false;
    }

    IEnumerator InviteToBench()
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        var follow = child.GetComponent<ProceduralFollow>();
        var collider = child.GetComponent<CharacterController>();
        if (follow) follow.enabled = false;
        if (collider) collider.enabled = false;
        var actor = child.GetComponent<ChildPerformer>();
        var side = Vector3.Cross(Vector3.up,benchForward);
        var seat = summit + side*.42f + Vector3.up*.16f;
        Vector3 approach = Ground(seat + benchForward*.8f);
        var start = child.position;
        director.Cut(seat + side*3.4f + benchForward*2.4f + Vector3.up*1.2f,seat + Vector3.up*.75f,43);
        Speak("Child: Sit with you? I'd like that.");
        yield return StoryShot(1.6f,t => child.position = Ground(Vector3.Lerp(start,approach,t)));
        child.rotation = Quaternion.LookRotation(benchForward);
        if (actor) { actor.Seated = true; actor.Act(ChildEmotion.Warm,star); }
        yield return StoryShot(.85f,t => child.position = Vector3.Lerp(approach,seat,t));
        ClearSpeech();
        invited = true;
        director.End();
        director = null;
        Busy = false;
    }

    void Advance(Step step)
    {
        Current = step;

        SetDestination();
        nextTrailGuide = 0;
        UpdateTrailGuide();
        ObjectiveChanged?.Invoke(step);
    }

    void SetDestination()
    {
        Destination = Current switch
        {
            Step.Parcel => packer ? Ground(packer.transform.position) : packingStop,
            Step.Garden => waterCan.position,
            Step.LastFragment => summit,
            Step.Murals => canyon,
            Step.Wish => observatory,
            Step.SummitBench => benchPosition,
            _ => home
        };
    }

    string Instruction() => Current switch
    {
        Step.Parcel => !dispatched ? "Speak to the mailman" : carryingParcel ? "Deliver the sealed parcel to " + address + "" : parcelChosen ? "Return to the glowing parcel" : "Inspect the packages on the table",
        Step.Garden => gardenWatered ? "Collect the light from the brass-rimmed can" : carryingWater ? "Water the squash bed" : "Pick up the blue water can beside the house",
        Step.LastFragment => invited ? "Talk to the child on the bench" : "Invite the child to sit beside the summit pond",
        Step.Hearth => "Hear how the village found water",
        Step.Murals => "Learn about the old trail mark",
        Step.Wish => "Hear the child’s wish at the observatory",
        Step.SummitBench => "Rest beside the child on the summit",
        _ => ""
    };
    bool skipCut;
    IEnumerator Beat(float s){for(float t=0;t<s&&!skipCut;t+=Time.deltaTime)yield return null;}
    IEnumerator Reward(string flag,string line,Step next)
    {
        Busy=true;director=CutsceneDirector.Begin();
        Vector3 focus=(star.position+child.position)*.5f+Vector3.up;
        if (Vector3.Distance(star.position, child.position) > 8f) focus = star.position + Vector3.up;
        director.Cut(focus+new Vector3(4,1,-4),focus,42);
        var caption = WishboundUI.Label(null, "Journey dialogue", "Child: " + line, 34,
            new Vector2(.5f,.16f), new Vector2(1400,115));
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        caption.GetComponent<CanvasGroup>().alpha = 1;
        skipCut = false;
        CutsceneSkip.Begin(() => skipCut=true);
        var speaker = child.GetComponent<ChildPerformer>();
        if (speaker) speaker.Act(next == Step.SummitBench ? ChildEmotion.Shy : ChildEmotion.Warm,star);
        Vector3 detail = Current == Step.Murals ? mural.position : Current == Step.Wish ? altar.position : home;
        Vector3 axis = FlatDirection(detail,star.position);
        Vector3 side = Vector3.Cross(Vector3.up,axis);
        yield return StoryShot(Mathf.Clamp(line.Length / 16f + 1f,4f,9f),t =>
        {
            var subject = Vector3.Lerp(Vector3.Lerp(echoSites[foundEcho]+Vector3.up*.48f,detail+Vector3.up*.75f,Mathf.SmoothStep(0,1,t*2)),focus,Mathf.SmoothStep(0,1,t*2-1));
            director.TrackFrame(subject + side*(3.2f-t*.55f) + axis*2.3f + Vector3.up*(1.1f-t*.4f),subject,Mathf.Lerp(46f,37f,t));
        });
        if (speaker) speaker.Act(ChildEmotion.None);
        Destroy(caption.gameObject);
        StoryFlags.Set(flag);
        if(StarRadiance.Instance)StarRadiance.Instance.UpdateDerivedBucket();
        CutsceneSkip.End();skipCut=false;director.End();director=null;Busy=false;Advance(next);
    }
    IEnumerator RestAtSummit()
    {
        Busy = true;
        hintGroup.alpha = 0;
        director = CutsceneDirector.Begin();
        var companion = child.GetComponent<ProceduralFollow>();
        if (companion) companion.enabled = false;
        var controller = child.GetComponent<CharacterController>();
        if (controller) controller.enabled = false;
        var performer = child.GetComponent<ChildPerformer>();
        Vector3 right = Vector3.Cross(Vector3.up,benchForward);
        Vector3 seat = benchPosition + right * .45f + Vector3.up*.16f;
        Vector3 starSeat = benchPosition - right * .55f + Vector3.up*.53f;
        Vector3 childStart = child.position;
        Vector3 starStart = star.position;
        Vector3 staging = Ground(benchPosition + benchForward * .9f + right * .45f);
        Quaternion entryRotation = child.rotation;
        Quaternion seatingRotation = Quaternion.LookRotation(benchForward);
        Vector3 focus = benchPosition + Vector3.up*.8f;
        var starVisual = WishboundIntro.Instance ? WishboundIntro.Instance.starVisual : null;
        Vector3 visualScale = starVisual ? starVisual.localScale : Vector3.one;
        var light = StarRadiance.Instance;
        if (light) { light.flare = 0; light.radiance = .22f; }
        director.Cut(benchPosition+benchForward*3.4f+right*3f+Vector3.up*1.5f,focus,42);
        for (float t=0;t<1.3f;t+=Time.deltaTime)
        {
            float blend = Mathf.SmoothStep(0,1,t/1.3f);
            child.position = Ground(Vector3.Lerp(childStart,staging,blend));
            child.rotation = Quaternion.Slerp(entryRotation,seatingRotation,blend);
            star.position = Vector3.Lerp(starStart,starSeat,blend);
            if (starVisual) starVisual.localScale = visualScale * Mathf.Lerp(1f,.64f,blend);
            yield return null;
        }
        if (performer)
        {
            performer.Seated = true;
            performer.lookTarget = null;
            performer.lookPoint = benchPosition + benchForward * 50 + Vector3.up*5;
        }
        for (float t=0;t<.85f;t+=Time.deltaTime)
        {
            child.position = Vector3.Lerp(staging,seat,Mathf.SmoothStep(0,1,t/.85f));
            yield return null;
        }
        child.SetPositionAndRotation(seat,seatingRotation);
        star.position = starSeat;
        if (starVisual) starVisual.localScale = visualScale * .64f;
        StoryFlags.Set("RestedAtSummit");
        WhisperLine.Show(child,"We can come back tomorrow. There's no hurry now.",5);
        yield return director.Glide(benchPosition + benchForward*1.8f + right*3.8f + Vector3.up*1.55f,
            focus + benchForward*.7f, 55, 6);
        yield return new WaitForSeconds(2f);
        FinishEnding(StarAlignment.Roots);
    }

    void FinishEnding(StarAlignment ending)
    {
        StoryFlags.Set(ending == StarAlignment.Tether ? "TetherEnding" : "RootsEnding");
        Advance(Step.Complete);
        Busy = false;
        // Keep the final camera in place behind the epilogue.
        WishboundEpilogue.Show(ending);
    }

    void OnDisable()
    {
        if (ride) ride.Cancel();
        if (travelPrompt) travelPrompt.alpha = 0f;
    }

    void OnDestroy()
    {
        if (squashMesh) Destroy(squashMesh);
        CutsceneSkip.End();
        ClearSpeech();
        if (hint) Destroy(hint.gameObject);
        if (travelPrompt) Destroy(travelPrompt.gameObject);
        if (director) director.End(0f);
        if (Instance == this) Instance = null;
    }
}
