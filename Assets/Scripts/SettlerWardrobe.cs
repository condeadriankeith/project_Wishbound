using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed, fitted character geometry. Clothing replaces covered anatomy; it is not stacked over the child's coat.</summary>
public static class SettlerWardrobe
{
    static Mesh sphere, tube, box;
    static Mesh Sphere
    {
        get
        {
            if (sphere) return sphere;
            // VillageKit's legacy blob has inward winding. Keep this fix local to NPCs.
            sphere = Object.Instantiate(VillageKit.Blob(0, 0, 10, 20));
            sphere.name = "Settler closed rounded mesh";
            var t = sphere.triangles;
            for (int i=0;i<t.Length;i+=3) { int a=t[i]; t[i]=t[i+2]; t[i+2]=a; }
            sphere.triangles=t; sphere.RecalculateNormals(); return sphere;
        }
    }
    // Closed tapered cylinder, including both end caps. y=0..1.
    static Mesh Tube
    {
        get
        {
            if(tube) return tube;
            var v=new List<Vector3>(); var tr=new List<int>();
            for(int j=0;j<2;j++) for(int i=0;i<20;i++) { float a=i*Mathf.PI*2/20; v.Add(new Vector3(Mathf.Cos(a),j,Mathf.Sin(a))); }
            v.Add(Vector3.zero);v.Add(Vector3.up);
            for(int i=0;i<20;i++) {int n=(i+1)%20;tr.AddRange(new[]{i,i+20,n+20,i,n+20,n,40,i,n,41,n+20,i+20});}
            tube=new Mesh{name="Settler closed cylinder"};tube.SetVertices(v);tube.SetTriangles(tr,0);tube.RecalculateNormals();tube.RecalculateBounds();return tube;
        }
    }
    static Transform Joint(string n,Transform p,Vector3 pos)
    {var g=new GameObject(n).transform;g.SetParent(p,false);g.localPosition=pos;return g;}
    static Transform Part(string n, Transform p, Mesh mesh, Color color, Vector3 pos, Vector3 scale)
    {
        var t=Joint(n,p,pos);t.localScale=scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=VillageKit.Mat(color,default,.13f,.12f);return t;
    }
    static Transform Ball(string n,Transform p,Color c,Vector3 pos,Vector3 scale)=>Part(n,p,Sphere,c,pos,scale);
    static Transform Box(string n,Transform p,Color c,Vector3 pos,Vector3 scale)
    {if(!box)box=VillageKit.ChamferBox(.10f);return Part(n,p,box,c,pos,scale);}
    static void Bar(string n,Transform p,Color c,Vector3 a,Vector3 b,float radius)
    {var t=Part(n,p,Tube,c,a,new Vector3(radius,(b-a).magnitude,radius));t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}

    public static Transform Build(Transform parent,SettlerLook.Outfit o)
    {
        var av=Joint("Body",parent,Vector3.zero);
        bool kid=o.age==SettlerLook.Age.Child, elder=o.age==SettlerLook.Age.Elder;
        float w=(kid?.22f:.25f)*o.build, shoulder=w*.82f+.045f;
        Color leather=new Color(.25f,.16f,.12f), brass=new Color(.77f,.59f,.29f), cream=new Color(.91f,.84f,.67f);
        var hips=Joint("hips",av,new Vector3(0,.60f,0));
        var spine=Joint("spine",hips,new Vector3(0,.20f,0));
        var head=Joint("head",spine,new Vector3(0,.43f,0));
        Color coat=o.apron?o.shirt:o.jacket;
        Ball("Tailored tunic",spine,coat,new Vector3(0,.02f,0),new Vector3(w,elder?.34f:.30f,.17f));
        Ball("Trouser seat",hips,o.trousers,new Vector3(0,.015f,0),new Vector3(w*.86f,.13f,.15f));
        Part("Waist belt",spine,Tube,leather,new Vector3(0,-.17f,0),new Vector3(w*.88f,.045f,.16f));
        Box("Belt buckle",spine,brass,new Vector3(0,-.145f,.164f),new Vector3(.07f,.055f,.018f));
        Bar("Neck",spine,o.skin,new Vector3(0,.23f,0),new Vector3(0,.34f,0),.085f);
        float hx=kid?.31f:.29f, hy=kid?.32f:.31f;
        Ball("Face",head,o.skin,new Vector3(0,.065f,0),new Vector3(hx,hy,.255f));
        for(int side=-1;side<=1;side+=2)
        {
            Ball("Ear",head,o.skin,new Vector3(side*(hx-.005f),.04f,0),new Vector3(.055f,.08f,.055f));
            Ball("Eye",head,new Color(.12f,.09f,.075f),new Vector3(side*.095f,.08f,.245f),new Vector3(.018f,.027f,.012f));
            Bar("Brow",head,o.hair,new Vector3(side*.065f,.135f,.24f),new Vector3(side*.12f,.14f,.233f),.012f);
            var arm=Joint(side<0?"arm.L":"arm.R",spine,new Vector3(side*shoulder,.17f,0));
            Bar("Sleeve",arm,coat,Vector3.zero,new Vector3(side*.23f,0,0),.082f);
            Ball("Shoulder seam",arm,coat,Vector3.zero,new Vector3(.085f,.09f,.09f));
            var elbow=Joint(side<0?"elbow.L":"elbow.R",arm,new Vector3(side*.23f,0,0));
            Ball("Elbow",elbow,o.skin,Vector3.zero,Vector3.one*.061f);
            Bar("Forearm",elbow,o.skin,Vector3.zero,new Vector3(side*.19f,0,0),.058f);
            Bar("Rolled cuff",arm,cream,new Vector3(side*.19f,0,0),new Vector3(side*.24f,0,0),.087f);
            var hand=Joint(side<0?"hand.L":"hand.R",elbow,new Vector3(side*.22f,0,0));
            Ball("Mitten",hand,o.skin,Vector3.zero,new Vector3(.075f,.067f,.06f));
            var leg=Joint(side<0?"leg.L":"leg.R",hips,new Vector3(side*.115f,-.025f,0));
            Bar("Trouser leg",leg,o.trousers,Vector3.zero,new Vector3(0,-.23f,0),.095f);
            var knee=Joint(side<0?"knee.L":"knee.R",leg,new Vector3(0,-.23f,0));
            Ball("Trouser knee",knee,o.trousers,Vector3.zero,new Vector3(.093f,.08f,.092f));
            Bar("Shin",knee,o.trousers,Vector3.zero,new Vector3(0,-.22f,0),.08f);
            Bar("Boot shaft",knee,leather,new Vector3(0,-.13f,0),new Vector3(0,-.24f,0),.092f);
            Box("Boot",knee,leather,new Vector3(0,-.25f,.045f),new Vector3(.19f,.14f,.29f));
            Box("Sole",knee,new Color(.13f,.11f,.10f),new Vector3(0,-.31f,.045f),new Vector3(.195f,.035f,.295f));
            Box("Boot strap",knee,brass,new Vector3(0,-.18f,.091f),new Vector3(.12f,.025f,.018f));
        }
        Ball("Nose",head,o.skin,new Vector3(0,.035f,.259f),new Vector3(.038f,.043f,.04f));
        Bar("Mouth",head,new Color(.4f,.23f,.19f),new Vector3(-.035f,-.055f,.238f),new Vector3(.035f,-.055f,.238f),.009f);
        // A fitted hair cap with overlapping locks, all outward facing.
        Ball("Hair crown",head,o.hair,new Vector3(0,.205f,-.035f),new Vector3(hx+.012f,.20f,.245f));
        for(int i=0;i<5;i++)
            Ball("Fringe",head,o.hair,new Vector3((i-2)*.09f,.23f-(i%2)*.025f,.17f),new Vector3(.07f,.10f,.075f));
        if(o.hairstyle!=0 || o.bun)
            for(int side=-1;side<=1;side+=2)Ball("Side hair",head,o.hair,new Vector3(side*.25f,.06f,-.06f),new Vector3(.075f,o.bun?.18f:.13f,.17f));
        if(o.bun)Ball("Hair bun",head,o.hair,new Vector3(0,.22f,-.28f),new Vector3(.13f,.14f,.13f));
        if(o.beard)Ball("Trimmed beard",head,o.hair,new Vector3(0,-.14f,.16f),new Vector3(.17f,.12f,.09f));
        if(o.moustache)for(int side=-1;side<=1;side+=2)Ball("Moustache",head,o.hair,new Vector3(side*.055f,-.015f,.255f),new Vector3(.065f,.025f,.018f));
        if(o.strawHat)
        {
            Part("Straw brim",head,Tube,o.hatColor,new Vector3(0,.29f,0),new Vector3(.46f,.035f,.39f));
            Ball("Straw crown",head,o.hatColor,new Vector3(0,.35f,-.01f),new Vector3(.29f,.16f,.25f));
            Part("Hat ribbon",head,Tube,o.jacket,new Vector3(0,.325f,0),new Vector3(.285f,.045f,.246f));
            Box("Ribbon knot",head,o.jacket,new Vector3(.285f,.35f,0),new Vector3(.055f,.06f,.08f));
        }
        else if(o.cap)
        {
            Ball("Cloth cap",head,o.jacket,new Vector3(0,.29f,-.025f),new Vector3(.315f,.14f,.27f));
            Box("Cap visor",head,o.jacket,new Vector3(0,.245f,.255f),new Vector3(.43f,.035f,.22f));
            Ball("Cap button",head,brass,new Vector3(0,.43f,-.025f),Vector3.one*.025f);
        }
        else if(o.scarf)
        {
            Ball("Head wrap",head,o.jacket,new Vector3(0,.245f,-.045f),new Vector3(.315f,.18f,.27f));
            Ball("Wrap knot",head,cream,new Vector3(.28f,.19f,-.12f),Vector3.one*.075f);
            Box("Wrap tail",head,o.jacket,new Vector3(.29f,.07f,-.17f),new Vector3(.085f,.24f,.05f));
        }
        if(o.apron)
        {
            // Apron front is outside the tunic's maximum depth; every seam shares its parent.
            Box("Apron skirt",spine,o.apronColor,new Vector3(0,-.12f,.185f),new Vector3(w*1.65f,.34f,.035f));
            Box("Apron bib",spine,o.apronColor,new Vector3(0,.10f,.183f),new Vector3(.25f,.23f,.035f));
            for(int side=-1;side<=1;side+=2)
            {
                Bar("Apron strap",spine,o.apronColor,new Vector3(side*.10f,.16f,.19f),new Vector3(side*.14f,.265f,.07f),.023f);
                Box("Patch pocket",spine,o.jacket,new Vector3(side*.10f,-.12f,.211f),new Vector3(.13f,.12f,.022f));
                Box("Pocket stitch",spine,cream,new Vector3(side*.10f,-.07f,.225f),new Vector3(.10f,.008f,.006f));
                Ball("Bib rivet",spine,brass,new Vector3(side*.087f,.17f,.207f),Vector3.one*.014f);
            }
            Bar("Apron waist tie",spine,o.apronColor,new Vector3(-w,-.04f,-.08f),new Vector3(w,-.04f,-.08f),.023f);
        }
        else
        {
            Box("Button placket",spine,cream,new Vector3(0,.035f,.173f),new Vector3(.045f,.35f,.012f));
            for(int i=0;i<3;i++)Ball("Coat button",spine,brass,new Vector3(0,.15f-i*.10f,.19f),Vector3.one*.016f);
            for(int side=-1;side<=1;side+=2)
            {
                var collar=Box("Collar",spine,cream,new Vector3(side*.075f,.235f,.095f),new Vector3(.13f,.12f,.035f));collar.localRotation=Quaternion.Euler(-25,0,side*25);
                Box("Coat pocket",spine,o.jacket*.83f,new Vector3(side*.135f,-.10f,.155f),new Vector3(.10f,.10f,.025f));
            }
        }
        if(o.role==SettlerBrain.Role.Wanderer || o.role==SettlerBrain.Role.Fisher)
        {
            Box("Leather satchel",spine,leather,new Vector3(w+.035f,-.12f,-.045f),new Vector3(.17f,.24f,.22f));
            Box("Satchel flap",spine,o.hatColor,new Vector3(w+.04f,-.04f,.07f),new Vector3(.18f,.10f,.035f));
            Bar("Satchel strap",spine,leather,new Vector3(-.16f,.24f,.13f),new Vector3(w,-.04f,.19f),.022f);
            Box("Satchel clasp",spine,brass,new Vector3(w+.04f,-.07f,.095f),new Vector3(.035f,.045f,.012f));
        }
        if(elder)
        {
            for(int side=-1;side<=1;side+=2)
            {
                // Thin octagonal spectacles, no opaque lenses over the face.
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;Bar("Spectacle frame",head,brass,new Vector3(side*.095f+Mathf.Cos(a)*.065f,.08f+Mathf.Sin(a)*.06f,.264f),new Vector3(side*.095f+Mathf.Cos(b)*.065f,.08f+Mathf.Sin(b)*.06f,.264f),.006f);}
            }
            Bar("Glasses bridge",head,brass,new Vector3(-.03f,.085f,.264f),new Vector3(.03f,.085f,.264f),.007f);
        }
        return av;
    }
}
