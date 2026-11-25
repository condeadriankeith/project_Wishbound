using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>The collection route and its authored story beats.</summary>
public sealed partial class MountainJourney
{
    Vector3 packingStop, tableTop, canHome, pondCenter;
    Transform parcelFragment, gardenFragment, lastFragment, parcelLid, wateringCan, nozzle;
    SettlerRig packer, mother, recipient;
    ParticleSystem irrigation;
    readonly List<Transform> harvest = new List<Transform>();
    Vector3 packForward, gardenForward;
    Transform parcel, spareParcel, waterCan, waterSurface;
    Vector3 doorstep;
    Material zinc;
    Mesh squashMesh;
    bool dispatched, carryingParcel, carryingWater, gardenWatered, invited;
    bool parcelChosen;
    string address;

    IEnumerator InspectParcels()
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        Vector3 eye = tableTop - packForward * .90f + Vector3.up * .78f;
        float aim = .5f;
        Speak("Look left or right with the mouse or A/D. Click the parcel of seeds.");
        yield return null;
        while (true)
        {
            float keys = (WishboundInput.Held(Key.D,KeyCode.D) ? 1 : 0) - (WishboundInput.Held(Key.A,KeyCode.A) ? 1 : 0);
            aim = Mathf.Clamp01(aim + WishboundInput.MouseDelta.x*.005f + keys*Time.deltaTime);
            director.TrackFrame(eye,Vector3.Lerp(parcel.position,spareParcel.position,aim)+Vector3.up*.16f,35);
            if (WishboundInput.PrimaryPressed || StarInput.ReachPressed)
            {
                if (aim > .68f) break;
                Speak(aim < .32f ? "Light leaks under this lid. Leave this one here." : "Look directly at a parcel to choose it.");
            }
            yield return null;
        }
        ClearSpeech();
        parcelChosen = carryingParcel = true; StoryFlags.Set("SeedsPacked");
        Destination = doorstep;
        director.End();
        director = null;
        Busy = false;
    }

    Vector3 HouseDoor(Vector3 near, int exclude, out int settlement, out string name)
    {
        if (!LaneNet.Built) LaneNet.Build();
        settlement = -1;
        name = "the village";
        Vector3 best = near;
        float distance = float.PositiveInfinity;
        foreach (var lot in FindObjectsByType<VillageLot>(FindObjectsSortMode.InstanceID))
        {
            if (lot.role == "Summit" || lot.settlement == exclude || lot.lot < 0 || lot.lot >= LaneNet.Doors.Length) continue;
            var d = LaneNet.Doors[lot.lot];
            if (!d.ok) continue;
            Vector3 p = Ground(TerrainMath.U(d.x,0,d.z));
            float score = (near-p).sqrMagnitude;
            if (score >= distance) continue;
            distance = score;
            best = p;
            settlement = lot.settlement;
            name = SettlementMap.Settlements[lot.settlement].name;
        }
        return best;
    }
    Text dialogue;

    void BuildFragmentStops()
    {
        Mesh box = VillageKit.ChamferBox();
        var village = VillageDressing.Instance;
        packForward = FlatDirection(home, summit);
        packingStop = ClearStage(home + packForward * 8f, 20f, 2.2f);
        Vector3 right = Vector3.Cross(Vector3.up, packForward);
        tableTop = packingStop + Vector3.up * .87f;
        Part("Packing table", box, wood, tableTop - Vector3.up * .07f, new Vector3(1.7f,.15f,1.04f), true)
            .transform.rotation = Quaternion.LookRotation(packForward);
        foreach (float x in new[] { -.70f, .70f })
            foreach (float z in new[] { -.37f, .37f })
                Part("Table leg", box, wood, packingStop + right * x + packForward * z + Vector3.up * .37f,
                    new Vector3(.12f,.75f,.12f)).transform.rotation = Quaternion.LookRotation(packForward);

        // A lip along the back keeps the mail from slipping; the front edge stays open to the player.
        var tableFrame = Anchor("Sorting bench fittings",tableTop,packForward);
        Piece(tableFrame,"Raised sorting rail",box,wood,new Vector3(0,.08f,.49f),new Vector3(1.72f,.17f,.052f));
        foreach (float x in new[] {-.70f,.70f})
            Piece(tableFrame,"Leg stretcher",box,wood,new Vector3(x,-.52f,0),new Vector3(.09f,.10f,.82f));
        var crate = parcel = Anchor("Fragment parcel", tableTop - right*.34f, packForward);
        Piece(crate, "Parcel bottom", box, wood, new Vector3(0,.04f,0), new Vector3(.61f,.09f,.44f));
        foreach (float side in new[] { -1f, 1f })
        {
            Piece(crate, "Parcel end", box, wood, new Vector3(side * .285f,.13f,0), new Vector3(.055f,.24f,.45f));
            Piece(crate, "Parcel side", box, wood, new Vector3(0,.13f,side * .205f), new Vector3(.62f,.24f,.05f));
        }
        parcelLid = new GameObject("Parcel hinge").transform;
        parcelLid.SetParent(crate, false);
        parcelLid.localPosition = new Vector3(0,.245f,.218f);
        Piece(parcelLid, "Lid", box, wood, new Vector3(0,0,-.22f), new Vector3(.66f,.06f,.48f));
        Piece(parcelLid, "Linen address label", box, VillageKit.Mat(new Color(.83f,.76f,.59f)),
            new Vector3(.065f,.034f,-.24f), new Vector3(.24f,.007f,.15f));
        var brass = Resources.Load<Material>("VerticalSlice/Brass");
        foreach (float x in new[] { -.22f,.22f })
            Piece(parcelLid,"Brass lid strap",box,brass,new Vector3(x,.034f,-.22f),new Vector3(.032f,.012f,.46f));
        spareParcel = Instantiate(parcel, parcel.parent);
        spareParcel.position += right * .68f;
        var ink = VillageKit.Mat(new Color(.16f,.26f,.19f));
        var seedTag = spareParcel.Find("Parcel hinge");
        for (int i=0; i<3; i++)
        {
            var seed = Piece(seedTag,"Linocut seed on address card",VillageKit.Blob(27,0,3,8),ink,
                new Vector3(.025f+i*.044f,.041f,-.24f),new Vector3(.026f,.007f,.06f));
            seed.localRotation = Quaternion.Euler(0,24f-i*18f,0);
        }
        Piece(crate,"Light beneath the lid",box,glow,new Vector3(0,.239f,-.218f),new Vector3(.53f,.013f,.016f));
        Piece(spareParcel,"Indigo twine",box,VillageKit.Mat(new Color(.12f,.25f,.52f)),new Vector3(0,.284f,0),new Vector3(.04f,.018f,.47f));
        Piece(parcelLid,"Amber wax seal",VillageKit.Blob(46,.08f,3,8),glow,new Vector3(.12f,.052f,-.22f),new Vector3(.065f,.025f,.065f));
        parcelFragment = StarFragment.Create(crate, crate.position + Vector3.up * .145f, .20f).transform;
        packer = BuildActor("Village mailman", Ground(packingStop + packForward * 1.12f), -packForward,
            SettlerBrain.Role.Wanderer, 526, false);

        HouseDoor(packingStop,-1,out int origin,out _);
        doorstep = ClearStage(HouseDoor(packingStop,origin,out _,out address),8f,.9f);
        Vector3 receiverFacing = FlatDirection(doorstep, packingStop);
        recipient = BuildActor("Parcel recipient", Ground(doorstep - receiverFacing*1.1f), receiverFacing,
            SettlerBrain.Role.Wanderer, 418, true);
        if (packer)
        {
            var leather = Resources.Load<Material>("VerticalSlice/Leather");
            var bag = Piece(packer.transform,"Post satchel",box,leather,new Vector3(.27f,.73f,-.06f),new Vector3(.19f,.30f,.31f));
            Piece(packer.transform,"Satchel buckle",box,brass,bag.localPosition + new Vector3(.10f,.04f,0),new Vector3(.024f,.065f,.09f));
        }
        Vector3 gardenDoor = HouseDoor(garden,-1,out _,out _);
        garden = ClearStage(gardenDoor + FlatDirection(gardenDoor,garden)*4f,12f,2.5f);
        gardenForward = FlatDirection(garden, summit);
        Vector3 sideOfGarden = Vector3.Cross(Vector3.up, gardenForward);
        Vector3 gardenerAt = Ground(garden - gardenForward * 1.75f);
        mother = BuildActor("The gardener", gardenerAt, gardenForward, SettlerBrain.Role.Gardener, 763, true);
        canHome = Ground(gardenerAt + sideOfGarden * .58f + gardenForward * .4f);
        wateringCan = Anchor("Fragment watering can", canHome, gardenForward);
        zinc = VillageKit.Mat(new Color(.30f,.47f,.49f));
        Piece(wateringCan, "Galvanized can walls", VillageCraft.CanBody, zinc,
            Vector3.zero, Vector3.one);
        Piece(wateringCan, "Can base", VillageKit.Disc(62, 0, 16), zinc,
            new Vector3(0,.028f,0), new Vector3(.18f,1,.18f));
        Piece(wateringCan, "Spout", VillageKit.Ring(10, .55f), zinc, new Vector3(0,.08f,.14f),
            new Vector3(.041f,.31f,.041f)).rotation = wateringCan.rotation * Quaternion.Euler(53f,0,0);
        Piece(wateringCan, "Hoop handle", VillageKit.Ring(18, .83f), zinc, new Vector3(0,.27f,-.07f),
            new Vector3(.27f,.035f,.25f)).rotation = wateringCan.rotation * Quaternion.Euler(90f,0,0);
        Piece(wateringCan,"Wrapped grip",box,VillageKit.Mat(new Color(.62f,.19f,.12f)),new Vector3(0,.50f,-.07f),new Vector3(.16f,.055f,.055f));
        Piece(wateringCan,"Sprinkler rose",VillageKit.Ring(12,0),zinc,new Vector3(0,.25f,.39f),new Vector3(.079f,.036f,.079f)).localRotation=Quaternion.Euler(53,0,0);
        for (int i=0;i<8;i++)
        {
            float angle = i*Mathf.PI*.25f;
            Piece(wateringCan,"Pressed body rib",box,zinc,new Vector3(Mathf.Sin(angle)*.15f,.135f,Mathf.Cos(angle)*.15f),
                new Vector3(.015f,.17f,.015f));
        }
        nozzle = new GameObject("Watering spout outlet").transform;
        nozzle.SetParent(wateringCan, false);
        nozzle.localPosition = new Vector3(0,.25f,.39f);
        foreach (float y in new[] {.04f,.25f})
            Piece(wateringCan,"Rolled brass rim",VillageKit.Ring(24,.92f),brass,new Vector3(0,y,0),new Vector3(y<.1f?.194f:.15f,.012f,y<.1f?.194f:.15f));
        waterCan = Instantiate(wateringCan,wateringCan.parent);
        waterCan.name = "Blue enamel water can";
        waterCan.position = Ground(gardenerAt - sideOfGarden * 1.1f);
        waterCan.GetChild(0).GetComponent<Renderer>().sharedMaterial = VillageKit.Mat(new Color(.10f,.30f,.55f));
        waterSurface = Piece(waterCan,"Water surface",VillageKit.Disc(62,0,24),VillageKit.WaterMat(true,1),new Vector3(0,.23f,0),new Vector3(.15f,1,.15f));
        nozzle = waterCan.Find("Watering spout outlet");
        gardenFragment = StarFragment.Create(wateringCan, canHome + Vector3.up * .21f, .20f).transform;
        var bed = Anchor("Kitchen garden frame",garden,gardenForward);
        foreach (float x in new[]{-1.62f,1.62f})
        {
            Piece(bed,"Weathered bed edging",box,wood,new Vector3(x,.09f,.44f),new Vector3(.09f,.18f,1.95f));
            foreach (float z in new[]{-.48f,1.36f})
                Piece(bed,"Corner stake",box,wood,new Vector3(x,.14f,z),new Vector3(.13f,.32f,.13f));
        }
        var earth = VillageKit.Mat(new Color(.24f,.135f,.087f));
        var leaf = VillageKit.Mat(new Color(.325f,.65f,.23f));
        var fruit = VillageKit.Mat(new Color(.78f,.39f,.13f));
        Mesh foliage = VillageKit.Blob(905, .15f, 4, 8);
        Mesh produce = Instantiate(VillageKit.Blob(688,0,10,42));
        produce.name = "Seven-ribbed winter squash";
        var vertices = produce.vertices;
        for (int i=0;i<vertices.Length;i++)
        {
            var v = vertices[i];
            float rib = .91f + .09f * Mathf.Cos(7f * Mathf.Atan2(v.z,v.x));
            vertices[i] = new Vector3(v.x*rib,v.y*(1f-.13f*Mathf.Pow(Mathf.Abs(v.y),5)),v.z*rib);
        }
        produce.vertices = vertices;
        produce.RecalculateNormals();
        produce.RecalculateBounds();
        squashMesh = produce;
        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = Ground(garden + gardenForward * (row * .95f - .05f) + sideOfGarden * ((i - 1.5f) * .72f));
                Part("Garden mound", foliage, earth, point - Vector3.up * .03f, new Vector3(.46f,.11f,.45f));
                var crop = Anchor("Squash vine", point + Vector3.up * .06f, gardenForward);
                Piece(crop, "Trailing vine", foliage, leaf, new Vector3(-.07f,.08f,0), new Vector3(.25f,.024f,.036f));
                Piece(crop, "Squash", produce, fruit, new Vector3(.12f,.22f,.01f), new Vector3(.23f,.19f,.24f));
                for (int leafIndex=0; leafIndex<3; leafIndex++)
                {
                    var angle = Quaternion.Euler(0f,120f*leafIndex+i*31f,0f);
                    var tip = angle * Vector3.forward*.16f;
                    var blade = Piece(crop,"Vine leaf",VillageKit.Leaf(),leaf,tip+Vector3.up*.09f,new Vector3(.24f,.34f,.29f));
                    blade.localRotation = angle * Quaternion.Euler(-24f,0,0);
                }
                Piece(crop, "Stem", box, leaf, new Vector3(.12f,.37f,.01f), new Vector3(.052f,.11f,.045f));
                crop.localScale = Vector3.one * .20f;
                harvest.Add(crop);
            }
        }
        irrigation = MakeIrrigation();

        pondCenter = TerrainMath.U(FlowNet.LakeX, FlowNet.LakeLevel, FlowNet.LakeZ);
        foreach (var lot in FindObjectsByType<VillageLot>(FindObjectsSortMode.None))
            if (lot.role == "Summit" && lot.transform.parent) lot.transform.parent.gameObject.SetActive(false);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI / 16f;
            var outward = new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
            float radius = (float)FlowNet.LakeRadiusAt(outward.x,TerrainMath.MirrorZ ? -outward.z : outward.z);
            Vector3 bank = Ground(pondCenter + outward*(radius+3.5f));
            float score = Mathf.Abs(bank.y-pondCenter.y)*6f + Vector3.Distance(bank,summitTrail[summitTrail.Length-1]);
            if (score >= nearest || bank.y < pondCenter.y-.15f) continue;
            nearest = score;
            summit = bank;
        }
        benchForward = FlatDirection(summit,pondCenter);
        var bench = BuildSummitBench(box,false);
        var reed = VillageKit.Mat(new Color(.32f,.40f,.22f));
        var head = VillageKit.Mat(new Color(.32f,.19f,.10f));
        for(int i=0;i<14;i++)
        {
            var bank = Ground(summit + Vector3.Cross(Vector3.up,benchForward)*(i%2==0?-1f:1f)*(1.8f+.13f*(i%5))
                + benchForward*(1.45f+.23f*(i%7)));
            var stalk = Anchor("Pond bulrush",bank,benchForward);
            stalk.rotation *= Quaternion.Euler(i%5*2,0,i%7-3);
            float height = .48f+.07f*(i%6);
            Piece(stalk,"Stem",box,reed,Vector3.up*(height*.5f),new Vector3(.018f,height,.018f));
            Piece(stalk,"Seed head",VillageKit.Blob(7,0,3,7),head,Vector3.up*height,new Vector3(.058f,.18f,.058f));
        }
        for (int i=0;i<5;i++)
        {
            float a = (i-2)*.31f;
            var stone = Ground(summit+benchForward*1.4f+Vector3.Cross(Vector3.up,benchForward)*a*2f);
            Part("Water-worn bank stone",VillageKit.Blob(180+i,.12f,3,9),VillageKit.Mat(new Color(.37f,.40f,.36f)),
                stone,new Vector3(.34f+i*.025f,.16f,.31f));
        }
        lastFragment = StarFragment.Create(bench,bench.TransformPoint(new Vector3(0,.24f,.02f)),.20f).transform;
        GrassExclusions.Add(summit,2.2f);
        GrassExclusions.ClearExisting();
    }

    static Vector3 ClearStage(Vector3 near, float extent, float radius)
    {
        for (float r = 0; r <= extent; r += 2f)
            for (int i = 0; i < (r == 0 ? 1 : 16); i++)
            {
                float angle = i * Mathf.PI / 8f;
                Vector3 point = Ground(near + new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)) * r);
                double sourceZ = TerrainMath.MirrorZ ? -point.z : point.z;
                var ground = Surface.At(point.x, sourceZ);
                if (ground == SurfaceType.Water || ground == SurfaceType.DirtPath || point.y < TerrainProbe.SeaLevel) continue;
                bool blocked = false;
                foreach (var collider in Physics.OverlapSphere(point + Vector3.up * .85f, radius, ~0, QueryTriggerInteraction.Ignore))
                    if (!TerrainProbe.IsTerrain(collider) && !(collider is CharacterController)) { blocked = true; break; }
                if (!blocked && Mathf.Abs(Ground(point + Vector3.right * radius).y - Ground(point - Vector3.right * radius).y) < 1.4f)
                    return point;
            }
        return Ground(near);
    }

    SettlerRig BuildActor(string name, Vector3 at, Vector3 forward, SettlerBrain.Role role, int seed, bool woman)
    {
        var community = SettlerCommunity.Instance;
        if (!community) return null;
        var actor = community.SpawnActor(at, Quaternion.LookRotation(forward).eulerAngles.y,
            SettlerLook.Age.Adult, role, seed, woman);
        actor.name = name;
        actor.transform.SetParent(transform, true);
        return actor;
    }

    Transform Anchor(string name, Vector3 position, Vector3 forward)
    {
        var item = new GameObject(name).transform;
        item.SetParent(transform, false);
        item.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        return item;
    }

    Transform Piece(Transform parent, string name, Mesh mesh, Material material, Vector3 local, Vector3 scale)
    {
        var piece = Part(name, mesh, material, parent.TransformPoint(local), scale, parent: parent);
        piece.transform.localRotation = Quaternion.identity;
        return piece.transform;
    }

    ParticleSystem MakeIrrigation()
    {
        var go = new GameObject("Watering can droplets");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = .55f;
        main.startSize = new ParticleSystem.MinMaxCurve(.025f,.065f);
        main.gravityModifier = .8f;
        main.startColor = new Color(.58f,.86f,1f,.78f);
        var emission = ps.emission;
        emission.enabled = false;
        var material = WishboundIntro.Instance ? WishboundIntro.Instance.sparkMaterial : null;
        if (material) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        ps.Play();
        return ps;
    }

    IEnumerator PreviewFragments()
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        var follower = child.GetComponent<ProceduralFollow>();
        bool wasFollowing = follower && follower.enabled;
        if (follower) follower.enabled = false;
        CutsceneSkip.Begin(() => skipCut = true);
        Vector3 childFocus = child.position + Vector3.up * 1.12f;
        Vector3 facing = FlatDirection(child.position, star.position);
        director.Cut(childFocus + facing * 3.4f + Vector3.Cross(Vector3.up,facing) * 1.6f, childFocus, 39f);
        Speak("Child: I know where the other pieces fell. Let me show you.");
        yield return Beat(2.65f);

        Speak("Child: The mailman was sealing the parcels when a spark slipped inside.");
        yield return PackFragment();

        Speak("Child: The gardener heard one ringing inside an old can. Let's ask her.");
        director.Cut(canHome + gardenForward * 2.4f + Vector3.up * 1.1f, canHome + Vector3.up*.23f,40);
        yield return Beat(2.8f);
        Speak("Child: And after that... could we go up to the pond?");
        Vector3 focus = summit + Vector3.up*.8f;
        Vector3 bankSide = Vector3.Cross(Vector3.up, benchForward);
        Vector3 wide = summit + benchForward * 5.4f + bankSide * 2.6f;
        wide.y = Mathf.Max(summit.y, Ground(wide).y) + 2.65f;
        Vector3 reveal = focus + benchForward * 2.45f - bankSide * 1.35f + Vector3.up * .58f;
        Vector3 wideFocus = focus + benchForward * 1.2f;
        director.Cut(wide, wideFocus, 45f);
        yield return StoryShot(4.15f, t =>
        {
            Vector3 position = Vector3.Lerp(wide, reveal, t) + bankSide * (.50f * Mathf.Sin(t * Mathf.PI));
            director.TrackFrame(position, Vector3.Lerp(wideFocus,focus,t), Mathf.Lerp(45,37,t));
        });

        ClearSpeech();
        CutsceneSkip.End();
        skipCut = false;
        if (follower) follower.enabled = wasFollowing;
        director.End(0f);
        director = null;
        Busy = false;
    }

    IEnumerator PackFragment()
    {
        parcelLid.localRotation = Quaternion.identity;
        Vector3 packed = parcelFragment.position;
        Vector3 from = tableTop + packForward * .53f + Vector3.up * .40f;
        Vector3 right = Vector3.Cross(Vector3.up, packForward);
        var hand = Hand(packer);
        if (packer) { packer.pose = SettlerRig.Pose.HandOver; packer.lookPoint = packed; }
        director.Cut(tableTop - packForward * 2.1f + right * 1.45f + Vector3.up * 1.05f, tableTop + Vector3.up * .4f, 42f);
        yield return StoryShot(3.9f, t =>
        {
            float reach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f,.55f,t));
            Vector3 heldAt = hand ? hand.position : from;
            parcelFragment.position = Vector3.Lerp(heldAt,packed,reach) + Vector3.up * (.10f*Mathf.Sin(reach*Mathf.PI));
            parcelLid.localRotation = Quaternion.Euler(105f * Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.14f,t)) * (1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,.95f,t))),0,0);
            Vector3 position = tableTop - packForward * Mathf.Lerp(2.1f,1.85f,t) +
                right * Mathf.Lerp(1.45f,.35f,t) + Vector3.up * Mathf.Lerp(1.05f,1.36f,t);
            director.TrackFrame(position, tableTop + Vector3.up * Mathf.Lerp(.40f,.24f,t), 42f);
        });
        parcelFragment.position = packed;
        parcelFragment.gameObject.SetActive(false);
        if (packer) packer.pose = SettlerRig.Pose.Idle;
    }

    IEnumerator WaterGarden()
    {
        Vector3 side = Vector3.Cross(Vector3.up,gardenForward);
        Vector3 canStart = waterCan.position;
        Quaternion upright = waterCan.rotation;
        director.Cut(canHome + side * 2.5f + gardenForward * 1.0f + Vector3.up * 1.4f, canHome + Vector3.up * .58f, 46f);
        int grown = 0;
        float column = 1.5f, row = .5f;
        var moisture = new float[harvest.Count];
        Speak("Aim the spout with WASD. Hold Space to water each of the eight vines.");
        float nextPourSound = 0;
        while (grown < harvest.Count && !skipCut)
        {
            float dt = Time.deltaTime;
            column = Mathf.Clamp(column + dt*2*((WishboundInput.Held(Key.D,KeyCode.D)?1:0)-(WishboundInput.Held(Key.A,KeyCode.A)?1:0)),0,3);
            row = Mathf.Clamp01(row + dt*2*((WishboundInput.Held(Key.W,KeyCode.W)?1:0)-(WishboundInput.Held(Key.S,KeyCode.S)?1:0)));
            Vector3 target = garden + side*((column-1.5f)*.72f) + gardenForward*(row*.95f-.05f);
            bool pour = WishboundInput.Held(Key.Space,KeyCode.Space);
            waterCan.SetPositionAndRotation(target+Vector3.up*.85f-gardenForward*.32f,upright*Quaternion.Euler(pour?52:0,0,0));
            director.TrackFrame(garden-gardenForward*2.7f+Vector3.up*3.3f,garden+gardenForward*.3f,44);
            if (pour)
            {
                if (Time.time >= nextPourSound) { WorldSound.Cue(nozzle, "Water", 24f); nextPourSound = Time.time + .72f; }
                irrigation.Emit(new ParticleSystem.EmitParams { position=nozzle.position, velocity=(target-nozzle.position)*2.2f+Vector3.up*1.4f },2);
                int index = Mathf.RoundToInt(row)*4+Mathf.RoundToInt(column);
                if (moisture[index]<1f)
                {
                    moisture[index]=Mathf.Min(1,moisture[index]+dt*1.5f);
                    harvest[index].localScale=Vector3.one*Mathf.Lerp(.2f,1,moisture[index]);
                    if (moisture[index]>=1) grown++;
                }
            }
            yield return null;
        }
        foreach (var crop in harvest) crop.localScale=Vector3.one;
        ClearSpeech();
        waterCan.SetPositionAndRotation(Ground(garden-side*1.8f),upright);
        waterSurface.gameObject.SetActive(false);
        if (mother) { mother.lookPoint = garden; mother.pose = SettlerRig.Pose.Idle; }
    }

    IEnumerator RecoverAt(Step step, Transform fragment, string flag, Step next)
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        CutsceneSkip.Begin(() => skipCut = true);
        Vector3 focus = (fragment.position + star.position + Vector3.up) * .5f;
        Vector3 forward = step == Step.Parcel ? packForward : gardenForward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        director.Cut(focus + side * 3.4f - forward * 1.85f + Vector3.up * 1.1f, focus, 41);
        if (step == Step.Parcel)
        {
            Speak("Child: There you are. We kept your light safe.");
            
            yield return StoryShot(1.4f, t => parcelLid.localRotation = Quaternion.Euler(105f * t,0,0));
            fragment.gameObject.SetActive(true);
        }
        else
        {
            Speak("Gardener: It gave us more than enough. Take it home.");
            if (mother) mother.pose = SettlerRig.Pose.HandOver;
            yield return Beat(1.35f);
        }
        yield return StarFragment.Merge(fragment, star, null, () => skipCut);
        StoryFlags.Set(flag);
        StoryFlags.Nudge(12f, "Recovered fragment");
        if (StarRadiance.Instance) StarRadiance.Instance.UpdateDerivedBucket();
        if (step == Step.Parcel && recipient) recipient.pose = SettlerRig.Pose.Idle;
        if (step == Step.Garden && mother) mother.pose = SettlerRig.Pose.Idle;
        ClearSpeech();
        CutsceneSkip.End();
        skipCut = false;
        director.End();
        director = null;
        Busy = false;
        Advance(next);
    }

    static Transform Hand(SettlerRig actor)
    {
        if (!actor) return null;
        foreach (var part in actor.GetComponentsInChildren<Transform>())
            if (part.name == "hand.R") return part;
        return null;
    }

    void Speak(string text)
    {
        if (!dialogue)
        {
            dialogue = WishboundUI.Label(null, "Fragment dialogue", "", 36, new Vector2(.5f,.155f), new Vector2(1450,90));
            dialogue.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
        Transform speaker = text.StartsWith("Child:") ? child : null;
        MagicCue.Say(speaker, text);
        dialogue.text = text;
        dialogue.GetComponent<CanvasGroup>().alpha = 1;
    }

    IEnumerator GlowReply(float duration)
    {
        ClearSpeech();
        var light = StarRadiance.Instance;
        float rest = light ? light.flare : 0f;
        try
        {
            yield return StoryShot(duration, t => {
                if (light) light.flare = Mathf.Lerp(rest, .85f, Mathf.Pow(Mathf.Sin(t * Mathf.PI), 2));
            });
        }
        finally { if (light) light.flare = rest; }
    }

    void ClearSpeech() { if (dialogue) Destroy(dialogue.gameObject); dialogue = null; }

    IEnumerator StoryShot(float seconds, Action<float> draw)
    {
        for (float t = 0; t < seconds && !skipCut; t += Time.deltaTime)
        {
            draw(Mathf.SmoothStep(0f,1f,t / seconds));
            yield return null;
        }
        draw(1f);
    }

    static Vector3 FlatDirection(Vector3 from, Vector3 to)
    {
        var v = to - from;
        v.y = 0f;
        return v.sqrMagnitude > .01f ? v.normalized : Vector3.forward;
    }
}

