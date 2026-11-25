using System.Collections.Generic;
using UnityEngine;

public sealed class WoodlandWildlife : MonoBehaviour
{
    sealed class Visitor
    {
        public Transform body, left, right;
        public Vector3 home, takeoff, territory;
        public float flight, duration, nextFlight;
        public bool bird;
        public float phase;
    }
    readonly List<Visitor> visitors = new List<Visitor>();
    Transform star, child;
    float nextLookup;

    public void Populate(List<Matrix4x4> trees)
    {
        for (int i=0;i<trees.Count;i+=5)
        {
            bool bird = visitors.Count%3==0;
            var basePoint = trees[i].MultiplyPoint3x4(Vector3.zero);
            Vector3 home = basePoint + Vector3.right*2.3f + Vector3.up*1.4f;
            if (i % 30 == 0) { var leaves = new GameObject("Canopy rustle").transform; leaves.SetParent(transform); leaves.position = basePoint + Vector3.up * 3f; WorldSound.Repeat(leaves, "Leaves", 30f); }
            if (bird) home = Ground(home);
            visitors.Add(CreateVisitor(home,bird,false,i*.713f));
        }
        PopulateCoast();
    }

    Visitor CreateVisitor(Vector3 home,bool bird,bool gull,float phase)
    {
        var mesh=VillageKit.Blob(16,.08f,3,7);
        var blue=VillageKit.Mat(gull ? new Color(.32f,.39f,.46f) : new Color(.18f,.38f,.51f));
        var honey=VillageKit.Mat(new Color(.95f,.58f,.20f));
        var dark=VillageKit.Mat(VillageKit.TimberDark);
            var root=new GameObject(gull?"Seagull":bird?"Woodland bluebird":"Amber butterfly").transform;
            root.SetParent(transform,true); root.position=home;
            if (bird || gull) WorldSound.Repeat(root, gull ? "Gull" : "Bird", gull ? 55f : 36f);
            if (gull) root.localScale=Vector3.one*1.85f;
            VillageKit.Piece(root,"Body",mesh,bird?blue:dark,Vector3.zero,Vector3.zero,bird?new Vector3(.14f,.15f,.28f):new Vector3(.035f,.04f,.14f));
            if (bird)
            {
                var ivory=VillageKit.Mat(new Color(.92f,.87f,.73f));
                VillageKit.Piece(root,"Breast",mesh,ivory,new Vector3(0,.005f,.065f),Vector3.zero,new Vector3(.139f,.143f,.17f));
                VillageKit.Piece(root,"Tail fan",mesh,blue,new Vector3(0,.035f,-.18f),new Vector3(12,0,0),new Vector3(.09f,.032f,.16f));
                foreach(int side in new[]{-1,1})
                    VillageKit.Piece(root,"Eye",mesh,dark,new Vector3(side*.063f,.112f,.111f),Vector3.zero,Vector3.one*.02f);
                VillageKit.Piece(root,"Head",mesh,ivory,new Vector3(0,.09f,.09f),Vector3.zero,Vector3.one*.14f);
                VillageKit.Piece(root,"Bill",mesh,honey,new Vector3(0,.09f,.18f),Vector3.zero,new Vector3(.055f,.045f,.09f));
            }
            Transform Wing(int sign)
            {
                var hinge=new GameObject("Wing hinge").transform;
                hinge.SetParent(root,false);
                VillageKit.Piece(hinge,"Wing",mesh,bird?blue:honey,Vector3.right*sign*(gull?.145f:.09f),Vector3.zero,new Vector3(gull?.32f:.20f,.025f,.16f));
                return hinge;
            }
            return new Visitor{body=root,left=Wing(-1),right=Wing(1),bird=bird,home=home,territory=home,phase=phase,nextFlight=Time.time+1f+Mathf.Repeat(phase,6f)};
    }

    readonly List<Visitor> gulls = new List<Visitor>();
    void PopulateCoast()
    {
        for (int sector=0;sector<10;sector++)
        {
            Vector3 direction=Quaternion.Euler(0,sector*36f,0)*Vector3.forward;
            for(float radius=(float)TerrainMath.E-12;radius>45;radius-=6)
            {
                Vector3 shore=Ground(direction*radius);
                if(shore.y<TerrainProbe.SeaLevel+1.2f) continue;
                Vector3 centre=shore+direction*9f;
                for(int i=0;i<3;i++) gulls.Add(CreateVisitor(centre,true,true,sector*.47f+i*1.15f));
                break;
            }
        }
    }

    void CircleShore(Visitor gull)
    {
        float phase=Time.time*.32f+gull.phase;
        Vector3 offshore=Vector3.ProjectOnPlane(gull.home,Vector3.up).normalized;
        Vector3 along=Vector3.Cross(Vector3.up,offshore);
        Vector3 position=gull.home+along*(26*Mathf.Sin(phase))+offshore*(13*Mathf.Cos(phase));
        position.y=Mathf.Max(TerrainProbe.SeaLevel,Ground(position).y)+5.5f+Mathf.Sin(phase*2)*1.4f;
        Vector3 heading=along*(26*Mathf.Cos(phase))-offshore*(13*Mathf.Sin(phase));
        gull.body.SetPositionAndRotation(position,Quaternion.LookRotation(heading)*Quaternion.Euler(0,0,-18));
        float flap=Mathf.Max(0,Mathf.Sin(phase*3))*Mathf.Sin(Time.time*22+gull.phase)*33f;
        gull.left.localRotation=Quaternion.Euler(0,0,-flap);
        gull.right.localRotation=Quaternion.Euler(0,0,flap);
    }

    static Vector3 Ground(Vector3 p)
    {
        p.y = TerrainMath.Hm(p.x,TerrainMath.MirrorZ ? -p.z : p.z) + .075f;
        return p;
    }

    void Update()
    {
        if ((!star || !child) && Time.time >= nextLookup)
        {
            nextLookup = Time.time + 1f;
            if (!star) star = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (!child) child = FindFirstObjectByType<ChildPerformer>()?.transform;
        }
        var cam = Camera.main;
        foreach(var gull in gulls) CircleShore(gull);
        foreach (var v in visitors)
        {
            float t = Time.time + v.phase;
            float flap;
            if (v.bird) flap = BirdPose(v,t);
            else
            {
                if (!cam || (cam.transform.position-v.home).sqrMagnitude > 120*120) continue;
                v.body.position=v.home+new Vector3(Mathf.Sin(t*1.3f)*1.2f,Mathf.Abs(Mathf.Sin(t))*.45f,Mathf.Cos(t*.9f));
                v.body.rotation=Quaternion.Euler(0,Mathf.Atan2(Mathf.Cos(t*1.3f),-Mathf.Sin(t*.9f))*Mathf.Rad2Deg+v.phase,0);
                flap=Mathf.Sin(t*33)*56;
            }
            v.left.localRotation=Quaternion.Euler(0,0,-flap);
            v.right.localRotation=Quaternion.Euler(0,0,flap);
        }
    }

    float BirdPose(Visitor v,float t)
    {
        if (v.duration == 0)
        {
            Transform threat = star;
            if (child && (!star || (child.position-v.home).sqrMagnitude < (star.position-v.home).sqrMagnitude)) threat=child;
            bool startled = threat && (threat.position-v.home).sqrMagnitude < 25f;
            if (!startled && Time.time < v.nextFlight) return 64f;
            v.nextFlight=Time.time+3f+Mathf.Repeat(v.phase+t,6f);
            Vector3 away = startled ? Vector3.ProjectOnPlane(v.home-threat.position,Vector3.up).normalized :
                Quaternion.Euler(0,Mathf.Repeat(t*91f,360f),0)*Vector3.forward;
            if (!startled && (v.territory-v.home).sqrMagnitude>34*34) away=Vector3.ProjectOnPlane(v.territory-v.home,Vector3.up).normalized;
            if (away.sqrMagnitude < .01f) away = v.body.forward;
            for (int i=0;i<7;i++)
            {
                int turn = ((i+1)/2)*(i%2==0?-1:1);
                Vector3 dir = Quaternion.Euler(0,turn*22f,0)*away;
                Vector3 landing = Ground(v.home + dir*((startled?8f:12f)+Mathf.Repeat(v.phase,5f)));
                float z = TerrainMath.MirrorZ ? -landing.z : landing.z;
                if (landing.y < 1f || Mathf.Abs(landing.y-v.home.y)>6f ||
                    TerrainMath.NVm(landing.x,z).y<.8f || GrassExclusions.Contains(landing.x,z,landing.y)) continue;
                v.takeoff=v.home;
                v.home=landing;
                v.flight=0;
                v.duration=(startled?1.6f:2.6f)+Mathf.Repeat(v.phase,.5f);
                v.body.rotation=Quaternion.LookRotation(dir);
                break;
            }
            if (v.duration==0) return 64f;
        }
        v.flight += Time.deltaTime;
        float k=Mathf.Clamp01(v.flight/v.duration);
        Vector3 pos=Vector3.Lerp(v.takeoff,v.home,k);
        pos.y=Mathf.Max(pos.y,Ground(pos).y)+Mathf.Sin(k*Mathf.PI)*1.7f;
        v.body.position=pos;
        if (k>=1) { v.duration=0; v.body.position=v.home; return 64f; }
        return Mathf.Sin(t*29f)*58f;
    }
}
