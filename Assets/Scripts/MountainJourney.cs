using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Small, sequential mountain quests using the existing island roads and companion.</summary>
public sealed class MountainJourney : MonoBehaviour
{
    public static MountainJourney Instance { get; private set; }
    public enum Step { Parcel, Crossing, Delivery, Prisms, Vines, Water, Garden, LastFragment, Hearth, Pebbles, Stones, Music, Seal, Murals, Wish, LeaveBeam, Altar, Home, Complete }
    public Step Current { get; private set; }
    public Vector3 Destination { get; private set; }
    public bool Busy { get; private set; }
    Transform star, child, parcel, bucket, lastFragment, altar, seal, beam;
    Vector3 home, ferry, farBank, garden, spring, summit, canyon;
    readonly List<GameObject> bridges = new List<GameObject>();
    readonly List<Transform> prisms = new List<Transform>();
    readonly List<GameObject> vines = new List<GameObject>();
    readonly List<Transform> crops = new List<Transform>();
    UnityEngine.UI.Text hint;
    CanvasGroup hintGroup;
    float held, cooldown, baseRadiance = 1f;
    int count;
    bool tether, dimmed, sawWish;
    Material stone, wood, glow, green;
    CutsceneDirector director;

    public static void Begin(StarAlignment path)
    {
        if (Instance || path == StarAlignment.None) return;
        var go = new GameObject("Mountain journey");
        var journey = go.AddComponent<MountainJourney>();
        journey.tether = path == StarAlignment.Tether;
    }

    IEnumerator Start()
    {
        Instance = this;
        while (!ChildEncounter.Instance || !ChildEncounter.Instance.Child || !VillageDressing.Instance || !VillageDressing.Instance.Built || RoadNet.Paths.Count == 0) yield return null;
        star = ChildEncounter.Instance.star; child = ChildEncounter.Instance.Child.transform;
        home = VillageDressing.Instance.Well.position;
        stone=VillageKit.Mat(new Color(.43f,.45f,.48f)); wood=VillageKit.Mat(new Color(.38f,.24f,.13f));
        glow=VillageKit.Mat(new Color(.9f,.75f,.38f),new Color(1f,.65f,.2f)); green=VillageKit.Mat(new Color(.38f,.62f,.29f));
        var cottage = FindFirstObjectByType<CottageChoiceScene>();
        Vector3 start = cottage ? cottage.Cottage.position : home;
        var main = RoadNet.Paths.Find(p => p.main) ?? RoadNet.Paths[0];
        int startIndex = RoadIndexNearest(main, start);
        int ferryIndex = Mathf.Clamp(startIndex + Mathf.RoundToInt(main.x.Length * .03f), 0, main.x.Length - 1);
        ferry = Ground(RoadPosition(main, ferryIndex));
        int farBankIndex = Mathf.Clamp(ferryIndex + Mathf.Max(5, Mathf.RoundToInt(main.x.Length * .025f)), 0, main.x.Length - 1);
        farBank = Ground(RoadPosition(main, farBankIndex));

        int gardenIndex = Mathf.Clamp(Mathf.RoundToInt((main.x.Length - 1) * .58f), ferryIndex + 1, main.x.Length - 1);
        garden = Ground(RoadPosition(main, gardenIndex));
        spring = Ground(RoadSide(main, Mathf.Clamp(gardenIndex - Mathf.RoundToInt(main.x.Length * .04f), 0, main.x.Length - 1), 7f));
        summit=Ground(new Vector3((float)RoadNet.SummitX,0,TerrainMath.MirrorZ?-(float)RoadNet.SummitZ:(float)RoadNet.SummitZ));
        canyon=RoadPoint(.73f);
        BuildPlaces();
        hint=WishboundUI.Label(null,"Journey hint","",28,new Vector2(.5f,.08f),new Vector2(1400,90));
        hintGroup=hint.GetComponent<CanvasGroup>();
        Current=tether?Step.Parcel:Step.Hearth;
        SetDestination();
        WhisperLine.Show(child,tether?"The ferry keeper left a parcel. Let's take it up together.":"Then stay a while. We can start with something small.",5f);
    }

    Vector3 RoadPoint(float portion)
    {
        var road=RoadNet.Paths.Find(p=>p.main)??RoadNet.Paths[0];
        int i=Mathf.Clamp(Mathf.RoundToInt((road.x.Length-1)*portion),0,road.x.Length-1);
        return Ground(new Vector3((float)road.x[i],0,TerrainMath.MirrorZ?-(float)road.z[i]:(float)road.z[i]));
    }
    static int RoadIndexNearest(RoadNet.RoadPath road, Vector3 point)
    {
        int best = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < road.x.Length; i++)
        {
            Vector3 p = new Vector3((float)road.x[i], 0, TerrainMath.MirrorZ ? -(float)road.z[i] : (float)road.z[i]);
            float d = (p - point).sqrMagnitude;
            d += i < 2 || i > road.x.Length - 3 ? 500f : 0f;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }
    static Vector3 RoadPosition(RoadNet.RoadPath road, int index)
    {
        index = Mathf.Clamp(index, 0, road.x.Length - 1);
        return new Vector3((float)road.x[index], 0, TerrainMath.MirrorZ ? -(float)road.z[index] : (float)road.z[index]);
    }
    static Vector3 RoadSide(RoadNet.RoadPath road, int index, float sideDistance)
    {
        int before = Mathf.Max(0, index - 1);
        int after = Mathf.Min(road.x.Length - 1, index + 1);
        Vector3 forward = RoadPosition(road, after) - RoadPosition(road, before);
        forward.y = 0;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        return RoadPosition(road, index) + side * sideDistance;
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
    void BuildPlaces()
    {
        var box=VillageKit.ChamferBox();
        parcel=Part("Ferry parcel",box,wood,ferry+Vector3.up*.32f,Vector3.one*.5f).transform;
        Part("Parcel ribbon",box,glow,ferry+Vector3.up*.33f,new Vector3(.52f,.07f,.52f),false,parcel);
        for(int i=0;i<6;i++)
        {
            Vector3 p=Vector3.Lerp(ferry,farBank,(i+1)/7f);
            bridges.Add(Part("Shadow stepping stone",box,stone,p+Vector3.up*.12f,new Vector3(1.35f,.22f,1.8f),true));
        }
        for(int i=0;i<3;i++)
        {
            Vector3 p=Ground(garden+new Vector3((i-1)*2.5f,0,-3));
            var prism=Part("Thaw prism "+(i+1),VillageKit.Blob(70+i,0,3,4),glow,p+Vector3.up*.85f,new Vector3(.35f,.75f,.35f)).transform;
            prism.rotation=Quaternion.Euler(0,120+i*45,0);prisms.Add(prism);
            Part("Prism pointer",box,wood,p+Vector3.up*.85f+prism.forward*.5f,new Vector3(.08f,.08f,.8f),false,prism);
            var line=prism.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=glow;line.positionCount=2;line.startWidth=line.endWidth=.035f;
        }
        Part("Frost seedbed",box,stone,garden,new Vector3(5,.15f,3),true);
        for(int i=0;i<6;i++)
        {
            var p=garden+new Vector3((i%3-1)*1.2f,.2f,(i/3-.5f)*1.2f);
            vines.Add(Part("Frozen vine",box,wood,p,new Vector3(.15f,.7f,1.4f)));
            crops.Add(Part("Restored seedling",VillageKit.Blob(120+i,.1f),green,p,Vector3.one*.08f).transform);
        }
        bucket=Part("Water pail",VillageKit.Ring(12,.8f),wood,spring+Vector3.up*.1f,new Vector3(.22f,.35f,.22f)).transform;
        Part("Mountain spring",VillageKit.Disc(10,0),VillageKit.Mat(new Color(.25f,.6f,.75f)),spring+Vector3.up*.04f,new Vector3(1.2f,1,1.2f));
        Part("Overlook bench",box,wood,summit+new Vector3(0,.55f,-2),new Vector3(2.5f,.15f,.6f),true);
        for(int i=-1;i<=1;i+=2)Part("Bench leg",box,wood,summit+new Vector3(i,.25f,-2),new Vector3(.15f,.5f,.45f),true);
        lastFragment=Part("Last fragment",VillageKit.Blob(80,0,4,8),glow,summit+Vector3.up*1.2f,Vector3.one*.3f).transform;
        altar=Part("Observatory altar",box,stone,summit+Vector3.up*.4f,new Vector3(1.4f,.8f,1.4f),true).transform;
        beam=Part("Wish beam",VillageKit.Ring(12,.95f),glow,summit+Vector3.up*.8f,new Vector3(.4f,8,.4f)).transform;
        beam.gameObject.SetActive(!tether);
        seal=Part("Canyon sealed floor",box,stone,canyon+Vector3.up*.1f,new Vector3(3,.2f,3),true).transform;
        Part("Starfall mural",box,glow,canyon+new Vector3(0,1.2f,2),new Vector3(2.5f,2,.12f));
        foreach(var p in new[]{ferry,farBank,garden,summit,canyon})GrassExclusions.Add(p,4f);
    }
    bool ReachHeld => (Keyboard.current!=null&&Keyboard.current.eKey.isPressed)||(Gamepad.current!=null&&Gamepad.current.buttonWest.isPressed);
    bool DimHeld => (Keyboard.current!=null&&Keyboard.current.cKey.isPressed)||(Gamepad.current!=null&&Gamepad.current.leftTrigger.ReadValue()>.5f);
    bool Near(Vector3 p,float radius=3f) => Vector3.Distance(star.position,p)<radius;
    void Update()
    {
        if(!star||!hint) return;
        hintGroup.alpha=Busy||CutsceneDirector.Active||Current==Step.Complete?0:1;
        if(Busy||CutsceneDirector.Active||Time.timeScale<=0) return;
        cooldown-=Time.deltaTime;
        SetDestination();
        float distance=Vector3.Distance(star.position,Destination);
        hint.text=Instruction()+"   ("+Mathf.RoundToInt(distance)+" m)";
        if(Current==Step.Crossing)
        {
            SetDim(DimHeld);
            foreach(var b in bridges)b.SetActive(DimHeld);
            if(DimHeld&&Near(farBank,2.5f)&&Vector3.Distance(child.position,farBank)<4)Advance(Step.Delivery);
            return;
        }
        SetDim(false);
        if(Current==Step.Prisms)
        {
            int aligned=0;
            foreach(var p in prisms)
            {
                Vector3 target=garden+Vector3.up*.3f;
                Vector3 flat=Vector3.ProjectOnPlane(target-p.position,Vector3.up).normalized;
                bool aimed=Vector3.Dot(p.forward,flat)>.96f;
                var line=p.GetComponent<LineRenderer>();line.SetPosition(0,p.position);line.SetPosition(1,aimed?target:p.position+p.forward*3f);
                if(aimed)aligned++;
                if(Near(p.position,2)&&StarInput.ReachPressed)p.Rotate(0,30,0,Space.World);
            }
            if(aligned==prisms.Count)Advance(Step.Vines);
            return;
        }
        if(Current==Step.LeaveBeam) { if(Vector3.Distance(star.position,summit)>5f&&sawWish)Advance(Step.Altar);return; }
        if(!Near(Destination,Current==Step.Home?5f:3f)){held=0;return;}
        switch(Current)
        {
            case Step.Parcel:
                if(StarInput.ReachPressed&&Vector3.Distance(child.position,ferry)<5){Carry(parcel);Advance(Step.Crossing);}break;
            case Step.Delivery:
                if(StarInput.ReachPressed){parcel.SetParent(transform,true);parcel.position=farBank+Vector3.up*.3f;StartCoroutine(Reward("DuskFerryComplete","You made a way without taking anybody's light.",Step.Prisms));}break;
            case Step.Vines:
                if(Hold(StarInput.PulseHeld,1.5f)){foreach(var v in vines)v.SetActive(false);Advance(Step.Water);}break;
            case Step.Water:
                if(StarInput.ReachPressed&&Vector3.Distance(child.position,spring)<5){Carry(bucket);Advance(Step.Garden);}break;
            case Step.Garden:
                if(StarInput.ReachPressed&&Vector3.Distance(child.position,garden)<5){bucket.SetParent(transform,true);bucket.position=garden+Vector3.right*3;StartCoroutine(RestoreGarden());}break;
            case Step.LastFragment:
                if(Hold(ReachHeld,2.5f))StartCoroutine(Ending(true));break;
            case Step.Hearth:
                if(Hold(StarInput.PulseHeld,2)){foreach(var p in FindObjectsByType<FarmPlotState>(FindObjectsSortMode.None))if(Vector3.Distance(p.transform.position,home)<60)p.CompleteBlessing();Advance(Step.Pebbles);}break;
            case Step.Pebbles:
                if(StarInput.ReachPressed){Part("Stacked pebble",VillageKit.Blob(count,0),stone,Destination+Vector3.up*(.1f+count*.17f),new Vector3(.22f,.09f,.18f));if(++count==3){count=0;Advance(Step.Stones);}}break;
            case Step.Stones:
                if(StarInput.ReachPressed&&cooldown<=0){StartCoroutine(SkipStone());cooldown=1.5f;if(++count==3){count=0;Advance(Step.Music);}}break;
            case Step.Music:
                if(StarInput.ReachPressed)StartCoroutine(Listen());break;
            case Step.Seal:
                if(DimHeld&&star.position.y>canyon.y+1f)StartCoroutine(Slam());break;
            case Step.Murals:
                if(Hold(StarInput.PulseHeld,2))StartCoroutine(Reward("StarfallLore","I wished for someone who would stay. The sky answered with you.",Step.Wish));break;
            case Step.Wish:
                if(StarInput.ReachPressed){sawWish=true;WhisperLine.Show(child,"That was my wish. But you don't owe the sky an answer.",6);Advance(Step.LeaveBeam);}break;
            case Step.Altar:
                if(Hold(ReachHeld,2)){altar.gameObject.SetActive(false);beam.gameObject.SetActive(false);ChildEncounter.Instance.TakeHand();Advance(Step.Home);}break;
            case Step.Home:
                if(Vector3.Distance(child.position,home)<7)StartCoroutine(Ending(false));break;
        }
    }
    bool Hold(bool input,float seconds){held=input?held+Time.deltaTime:0;return held>=seconds;}
    void Carry(Transform item){item.SetParent(child,false);item.localPosition=new Vector3(0,.85f,.4f);}
    void SetDim(bool value)
    {
        var r=StarRadiance.Instance;if(!r)return;
        if(value&&!dimmed)baseRadiance=r.radiance;
        if(value)r.radiance=.15f;else if(dimmed)r.radiance=baseRadiance;
        dimmed=value;
    }
    void Advance(Step step){Current=step;held=0;SetDestination();}
    void SetDestination()
    {
        switch(Current)
        {
            case Step.Parcel:Destination=ferry;break;
            case Step.Crossing:case Step.Delivery:Destination=farBank;break;
            case Step.Prisms:case Step.Vines:case Step.Garden:Destination=garden;break;
            case Step.Water:Destination=spring;break;
            case Step.LastFragment:case Step.Wish:case Step.LeaveBeam:case Step.Altar:Destination=summit;break;
            case Step.Seal:case Step.Murals:Destination=canyon;break;
            case Step.Pebbles:Destination=Ground(home+Vector3.right*4);break;
            case Step.Stones:Destination=VillageDressing.Instance.transform.Find("Fishpond")?VillageDressing.Instance.transform.Find("Fishpond").position:home;break;
            default:Destination=home;break;
        }
    }
    string Instruction()
    {
        switch(Current)
        {
            case Step.Parcel:return "Dusk-Ferry Parcel · Reach (E) with the Child to pick up the parcel";
            case Step.Crossing:return "Hold C / left trigger to dim; guide the Child across the shadow stones";
            case Step.Delivery:return "Reach (E) to deliver the parcel";
            case Step.Prisms:return "Frost-Locked Seedbed · Reach (E) beside each prism to aim it at the soil";
            case Step.Vines:return "Hold Pulse (F) to clear the thawed vines";
            case Step.Water:return "Bring the Child to the spring; Reach (E) to fill the pail";
            case Step.Garden:return "Bring the water to the seedbed; Reach (E) to pour";
            case Step.LastFragment:return "Overlook Bench · Hold Reach (E) to take the last fragment";
            case Step.Hearth:return "Hold Pulse (F) by the hearth to warm the village soil";
            case Step.Pebbles:return "Reach (E) to stack three pebbles";
            case Step.Stones:return "Reach (E) to skip three stones";
            case Step.Music:return "Reach (E) to sit and listen with the Child";
            case Step.Seal:return "Whisper Canyon · Jump, then hold C to ground-slam the sealed floor";
            case Step.Murals:return "Hold Pulse (F) to light the starfall mural";
            case Step.Wish:return "Observatory · Reach (E) to see the Child's wish";
            case Step.LeaveBeam:return "Walk away from the beam";
            case Step.Altar:return "Return to the altar; hold Reach (E) to break it";
            case Step.Home:return "Walk home hand in hand";
            default:return "";
        }
    }
    bool skipCut;
    IEnumerator Beat(float s){for(float t=0;t<s&&!skipCut;t+=Time.deltaTime)yield return null;}
    IEnumerator Reward(string flag,string line,Step next)
    {
        Busy=true;director=CutsceneDirector.Begin();
        Vector3 focus=(star.position+child.position)*.5f+Vector3.up;
        director.Cut(focus+new Vector3(4,1,-4),focus,42);
        skipCut=false;CutsceneSkip.Begin(()=>skipCut=true);WhisperLine.Show(child,line,4);yield return Beat(4);
        if(!StoryFlags.Get(flag)){StoryFlags.Set(flag);if(tether)StoryFlags.Nudge(12,"Mountain fragment");}
        if(StarRadiance.Instance)StarRadiance.Instance.UpdateDerivedBucket();
        CutsceneSkip.End();skipCut=false;director.End();director=null;Busy=false;Advance(next);
    }
    IEnumerator RestoreGarden()
    {
        Busy=true;
        for(float t=0;t<4;t+=Time.deltaTime){foreach(var c in crops)c.localScale=Vector3.one*Mathf.Lerp(.08f,.55f,t/4);yield return null;}
        for(int i=0;i<5;i++)Part("Trellis step",VillageKit.ChamferBox(),wood,Ground(garden+new Vector3(0,0,3+i)) +Vector3.up*.15f,new Vector3(2,.2f,.8f),true);
        Busy=false;yield return Reward("FrostSeedbedComplete","It was waiting for spring. We brought it together.",Step.LastFragment);
    }
    IEnumerator SkipStone()
    {
        var g=Part("Skipping stone",VillageKit.Blob(3,0),stone,star.position,Vector3.one*.12f);Vector3 from=star.position;
        for(float t=0;t<1.2f;t+=Time.deltaTime){g.transform.position=Vector3.Lerp(from,Destination+Vector3.forward*4,t/1.2f)+Vector3.up*Mathf.Abs(Mathf.Sin(t*10))*.5f;yield return null;}Destroy(g);
    }
    IEnumerator Listen()
    {
        Busy=true;director=CutsceneDirector.Begin();Vector3 focus=(star.position+child.position)*.5f;
        director.Cut(focus+new Vector3(4,2,-4),focus+Vector3.up*.6f,45);
        if(MusicDirector.Instance)MusicDirector.Instance.Play("roots",2);
        skipCut=false;CutsceneSkip.Begin(()=>skipCut=true);WhisperLine.Show(child,"We don't have to go anywhere yet.",5);yield return Beat(8);
        CutsceneSkip.End();skipCut=false;director.End();director=null;Busy=false;Advance(Step.Seal);
    }
    IEnumerator Slam()
    {
        Busy=true;var pc=star.GetComponent<IslandThirdPersonController>();bool was=pc.enabled;pc.enabled=false;
        var cc=star.GetComponent<CharacterController>();
        float end=Time.time+1;
        while(star.position.y>canyon.y+.25f&&Time.time<end){cc.Move(Vector3.down*18*Time.deltaTime);yield return null;}
        seal.gameObject.SetActive(false);pc.enabled=was;Busy=false;Advance(Step.Murals);
    }
    IEnumerator Ending(bool ascend)
    {
        Busy=true;director=CutsceneDirector.Begin();hintGroup.alpha=0;
        Vector3 focus=ascend?summit:(star.position+child.position)*.5f;
        director.Cut(focus+new Vector3(5,1.3f,-1),focus+Vector3.up*.8f,45);
        skipCut=false;CutsceneSkip.Begin(()=>skipCut=true);
        WhisperLine.Show(child,ascend?"I'll look for you. Every night.":"Come on. Gran's keeping supper warm.",5);
        if(ascend)
        {
            lastFragment.gameObject.SetActive(false);
            Vector3 start=star.position;
            for(float t=0;t<6&&!skipCut;t+=Time.deltaTime){star.position=start+Vector3.up*(t*t*.8f);yield return null;}if(skipCut)star.position=start+Vector3.up*28.8f;
            StoryFlags.Set("TetherEnding");
        }
        else {yield return Beat(6);StoryFlags.Set("RootsEnding");}
        CutsceneSkip.End();skipCut=false;
        Current=Step.Complete;Busy=false;
        director.End();director=null;
        // Preserve a playable post-ending state instead of leaving the controller locked in the sky.
        if(ascend)star.GetComponent<IslandThirdPersonController>().Teleport(summit+Vector3.back*2,0);
    }
    void OnDestroy(){CutsceneSkip.End();if(hint)Destroy(hint.gameObject);SetDim(false);if(director)director.End(0);if(Instance==this)Instance=null;}
}
