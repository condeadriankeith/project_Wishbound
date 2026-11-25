using System.Collections.Generic;
using UnityEngine;

public static class VillageCraft
{
    static readonly Color Teal = new Color(.16f,.45f,.43f), Ochre = new Color(.91f,.63f,.28f), Clay = new Color(.66f,.32f,.19f);
    static readonly Dictionary<string,Mesh> vessels = new Dictionary<string, Mesh>();
    static Mesh Vessel(string name, params Vector2[] profile)
    {
        if (vessels.TryGetValue(name,out var cached) && cached) return cached;
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        for(int i=1;i<profile.Length;i++)
            for(int j=0;j<12;j++)
            {
                Vector3 Point(Vector2 p,int k) {float a=k*Mathf.PI/6; return new Vector3(p.x*Mathf.Cos(a),p.y,p.x*Mathf.Sin(a));}
                int n=vertices.Count;
                vertices.Add(Point(profile[i-1],j));vertices.Add(Point(profile[i],j));
                vertices.Add(Point(profile[i],j+1));vertices.Add(Point(profile[i-1],j+1));
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        return vessels[name]=mesh;
    }
    public static Mesh CanBody => Vessel("Hammered watering can",new Vector2(.14f,.012f),new Vector2(.195f,.045f),new Vector2(.18f,.19f),new Vector2(.144f,.264f),new Vector2(.127f,.278f),new Vector2(.115f,.256f));
    static GameObject Part(Transform t,string name,Mesh mesh,Color color,Vector3 pos,Vector3 scale)
        => VillageKit.Piece(t,name,mesh,VillageKit.Mat(color),pos,Vector3.zero,scale);
    static void Rail(Transform t,string name,Vector3 a,Vector3 b,float width,float depth,Color color)
    {
        var part=Part(t,name,VillageKit.ChamferBox(.09f),color,(a+b)*.5f,new Vector3(width,depth,(b-a).magnitude));
        part.transform.localRotation=Quaternion.LookRotation(b-a);
    }
    public static void Pot(Transform parent,Vector3 at,int style)
    {
        var t=new GameObject(style==0?"Painted herb jar":style==1?"Flowering prickly pear":"Daisy crock").transform;
        t.SetParent(parent,false);t.localPosition=at;
        var jar=Vessel("Wheel thrown belly",new Vector2(.16f,0),new Vector2(.25f,.05f),new Vector2(.33f,.23f),new Vector2(.29f,.38f),new Vector2(.21f,.43f),new Vector2(.24f,.46f),new Vector2(.20f,.47f),new Vector2(.18f,.38f));
        Part(t,"Glazed stoneware",jar,style==0?Teal:Clay,Vector3.zero,Vector3.one);
        Part(t,"Ochre slip band",VillageKit.Ring(12,.94f),Ochre,Vector3.up*.225f,new Vector3(.332f,.026f,.332f));
        Part(t,"Earth",VillageKit.Disc(11,.02f,12),VillageKit.Soil,Vector3.up*.412f,new Vector3(.195f,1,.195f));
        var green=new Color(.40f,.61f,.24f);
        var bud=VillageKit.Blob(32,.07f,4,8);
        for(int i=0;i<5;i++)
        {
            float a=i*2.4f;Vector3 tip=new Vector3(Mathf.Sin(a)*.19f,.61f+.07f*(i%3),Mathf.Cos(a)*.19f);
            if(style==1)
            {
                var pad=Part(t,"Pear pad",bud,green,tip-Vector3.up*.07f,new Vector3(.14f,.21f,.067f));
                pad.transform.localRotation=Quaternion.Euler(0,a*Mathf.Rad2Deg,22);
                Part(t,"Berry",bud,new Color(.78f,.19f,.36f),tip+Vector3.up*.09f,Vector3.one*.058f);
            }
            else
            {
                Rail(t,"Leaf stalk",Vector3.up*.42f,tip,.018f,.018f,green);
                var blade=Part(t,"Folded blade",VillageKit.Leaf(),green,tip,Vector3.one*.35f);
                blade.transform.localRotation=Quaternion.Euler(-36,i*137,0);
                if(style!=2)continue;
                for(int k=0;k<6;k++){float q=k*Mathf.PI/3;Part(t,"Petal",bud,new Color(.97f,.86f,.63f),tip+new Vector3(Mathf.Cos(q)*.05f,.016f,Mathf.Sin(q)*.05f),new Vector3(.048f,.014f,.048f));}
                Part(t,"Pollen",bud,Ochre,tip+Vector3.up*.034f,Vector3.one*.038f);
            }
        }
    }
    public static void Hearth(Vector3 at,Transform parent)
    {
        var t=new GameObject("Village cooking hearth").transform;t.SetParent(parent,false);t.position=at;
        for(int i=0;i<10;i++){float a=i*Mathf.PI/5;Part(t,"Fieldstone",VillageKit.Blob(21+i,.14f),VillageKit.Stone,new Vector3(Mathf.Sin(a)*.66f,.11f,Mathf.Cos(a)*.66f),new Vector3(.25f,.17f,.22f));}
        for(int i=0;i<3;i++){var dir=Quaternion.Euler(0,60*i,0)*Vector3.forward*.42f;Rail(t,"Charred log",-dir+Vector3.up*.14f,dir+Vector3.up*.18f,.14f,.15f,VillageKit.TimberDark);}
        foreach(int side in new[]{-1,1})foreach(int foot in new[]{-1,1})
            Rail(t,"Forked support",new Vector3(side*.86f,0,foot*.37f),new Vector3(side*.79f,1.64f,0),.12f,.14f,VillageKit.Timber);
        Rail(t,"Cross timber",new Vector3(-1,1.55f,0),new Vector3(1,1.55f,0),.12f,.12f,VillageKit.TimberDark);
        foreach(int side in new[]{-1,1})Rail(t,"Iron bail",Vector3.up*1.53f,new Vector3(side*.32f,.89f,0),.032f,.032f,VillageKit.Iron);
        Part(t,"Cast iron kettle",Vessel("Kettle",new Vector2(.15f,.42f),new Vector2(.34f,.52f),new Vector2(.39f,.72f),new Vector2(.32f,.88f),new Vector2(.35f,.91f),new Vector2(.29f,.91f)),VillageKit.Iron,Vector3.zero,Vector3.one);
        Part(t,"Stew",VillageKit.Disc(12,0,12),Ochre,Vector3.up*.85f,new Vector3(.30f,1,.30f));
    }
    public static void Landing(Transform parent,Vector3 dock)
    {
        var t=new GameObject("Fishing landing").transform;t.SetParent(parent,false);t.localPosition=dock;
        for(int i=0;i<9;i++)Part(t,"Deck board",VillageKit.ChamferBox(.09f),Color.Lerp(VillageKit.Timber,Clay,(i%3)*.13f),new Vector3((i-4)*.2f,.06f,0),new Vector3(.19f,.09f,.96f));
        foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
            Part(t,"Hemp lashing",VillageKit.Ring(10,.75f),Ochre,new Vector3(side*.8f,.18f,end*.4f),new Vector3(.09f,.065f,.09f));
        var boat=new GameObject("Clinker dinghy").transform;boat.SetParent(t,false);boat.localPosition=new Vector3(0,-.28f,-1.06f);
        float[] widths={.04f,.34f,.44f,.38f,.19f};
        for(int j=0;j<4;j++)foreach(int side in new[]{-1,1})for(int row=0;row<3;row++)
        {
            Vector3 Edge(int k){float flare=.60f+.19f*row;return new Vector3((k-2)*.59f,row*.09f+.07f*Mathf.Abs(k-2),side*widths[k]*flare);}
            Rail(boat,"Lapped strake",Edge(j),Edge(j+1),.055f,.115f,row==2?VillageKit.Timber:VillageKit.TimberDark);
        }
        Rail(boat,"Keel",Vector3.left*1.15f,Vector3.right*1.15f,.20f,.10f,VillageKit.TimberDark);
        for(int k=1;k<=3;k++) {var c=new Vector3((k-2)*.59f,.20f,0);Rail(boat,"Thwart",c-Vector3.forward*widths[k]*.92f,c+Vector3.forward*widths[k]*.92f,.17f,.055f,VillageKit.Timber);}
        Rail(boat,"Oar shaft",new Vector3(-.9f,.28f,-.19f),new Vector3(.75f,.29f,.22f),.036f,.04f,Ochre);
        Rail(boat,"Oar blade",new Vector3(.72f,.29f,.21f),new Vector3(1.1f,.29f,.31f),.12f,.03f,Ochre);
    }
}
