using System.Collections.Generic;
using UnityEngine;

/// <summary>More village kit pieces: scarecrow, wheelbarrow, clay pots, clothesline, shrine, footbridge, net rack.</summary>
public static class VillageKitExtras
{
    static GameObject Root(string n, Vector3 pos, float yaw, Transform parent)
    {
        var g = new GameObject(n); g.transform.SetParent(parent, false); g.transform.position = VillageKit.Ground(pos); g.transform.rotation = Quaternion.Euler(0, yaw, 0);
        g.AddComponent<VillageKitPiece>(); return g;
    }
    static GameObject P(string n, Mesh m, Material mat, Transform t, Vector3 lp, Vector3 sc, Quaternion? r = null)
    {
        var g = new GameObject(n); g.transform.SetParent(t, false); g.transform.localPosition = lp; g.transform.localRotation = r ?? Quaternion.identity; g.transform.localScale = sc;
        g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat; return g;
    }
    static Mesh Box => VillageKit.ChamferBox(.18f);

    /// <summary>Rustic scarecrow draped in tattered indigo cloth.</summary>
    public static GameObject Scarecrow(Vector3 pos, float yaw, Transform parent, int seed = 1)
    {
        var r = Root("Scarecrow", pos, yaw, parent); var t = r.transform;
        var wood = VillageKit.Mat(VillageKit.TimberDark); var straw = VillageKit.Mat(new Color(.82f, .7f, .38f)); var indigo = VillageKit.Mat(new Color(.22f, .26f, .48f));
        P("Pole", Box, wood, t, new Vector3(0, 1f, 0), new Vector3(.12f, 2f, .12f)).AddComponent<BoxCollider>();
        P("Arms", Box, wood, t, new Vector3(0, 1.45f, 0), new Vector3(1.5f, .1f, .1f));
        P("Coat", Box, indigo, t, new Vector3(0, 1.2f, 0), new Vector3(.62f, .7f, .3f), Quaternion.Euler(0, 0, 4));
        P("SleeveL", Box, indigo, t, new Vector3(-.55f, 1.38f, 0), new Vector3(.45f, .22f, .22f), Quaternion.Euler(0, 0, -12));
        P("SleeveR", Box, indigo, t, new Vector3(.55f, 1.36f, 0), new Vector3(.45f, .22f, .22f), Quaternion.Euler(0, 0, 9));
        P("Head", VillageKit.Blob(seed, .1f), VillageKit.Mat(new Color(.78f, .66f, .45f)), t, new Vector3(0, 1.82f, 0), Vector3.one * .22f);
        P("Hat", VillageKit.Ring(10, 0), straw, t, new Vector3(0, 1.98f, 0), new Vector3(.36f, .05f, .36f));
        P("HatTop", VillageKit.Ring(8, 0), straw, t, new Vector3(0, 2.0f, 0), new Vector3(.18f, .14f, .18f));
        for (int i = 0; i < 4; i++) P("Straw", Box, straw, t, new Vector3(i < 2 ? -.8f : .8f, 1.42f, (i % 2 - .5f) * .08f), new Vector3(.18f, .03f, .03f), Quaternion.Euler(0, 0, (i % 2 == 0 ? 20 : -15)));
        return r;
    }

    /// <summary>Wooden wheelbarrow tilted on its side.</summary>
    public static GameObject Wheelbarrow(Vector3 pos, float yaw, Transform parent)
    {
        var r = Root("Wheelbarrow", pos, yaw, parent); var t = r.transform; var wood = VillageKit.Mat(VillageKit.Timber); var dark = VillageKit.Mat(VillageKit.TimberDark);
        var body = new GameObject("Body").transform; body.SetParent(t, false); body.localRotation = Quaternion.Euler(0, 0, 58); body.localPosition = new Vector3(0, .25f, 0);
        P("Tray", Box, wood, body, new Vector3(0, .3f, 0), new Vector3(.7f, .3f, 1f));
        P("HandleL", Box, dark, body, new Vector3(-.28f, .2f, -.9f), new Vector3(.06f, .06f, .9f));
        P("HandleR", Box, dark, body, new Vector3(.28f, .2f, -.9f), new Vector3(.06f, .06f, .9f));
        P("Wheel", VillageKit.Ring(12, .3f), dark, body, new Vector3(0, .12f, .62f), new Vector3(.24f, .07f, .24f), Quaternion.Euler(0, 0, 90));
        var collider = r.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, .4f, -.18f);
        collider.size = new Vector3(1, .8f, 2.35f);
        return r;
    }

    /// <summary>A stack of clay pots along a border.</summary>
    public static GameObject ClayPots(Vector3 pos, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var r = Root("ClayPots", pos, (float)rng.NextDouble() * 360, parent); var t = r.transform;
        VillageCraft.Pot(t,new Vector3(-.39f,0,-.18f),0);
        VillageCraft.Pot(t,new Vector3(.40f,0,-.18f),1);
        VillageCraft.Pot(t,new Vector3(0,0,.45f),2);
        for (int i=0;i<3;i++)
            foreach(int side in new[]{-1,1})
                P("Crate slat",Box,VillageKit.Mat(VillageKit.Timber),t,new Vector3(0,.05f+i*.09f,side*.57f),new Vector3(1.48f,.068f,.04f));
        foreach(int side in new[]{-1,1})
            P("Crate end",Box,VillageKit.Mat(VillageKit.TimberDark),t,new Vector3(side*.73f,.13f,0),new Vector3(.055f,.25f,1.16f));
        return r;
    }

    /// <summary>A clothesline strung between two posts with billowing linen sheets.</summary>
    public static GameObject Clothesline(Vector3 a, Vector3 b, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        a = VillageKit.Ground(a); b = VillageKit.Ground(b);
        var r = new GameObject("Clothesline"); r.transform.SetParent(parent, false); r.AddComponent<VillageKitPiece>();
        var dark = VillageKit.Mat(VillageKit.TimberDark);
        foreach (var p in new[] { a, b })
        {
            var post = P("Post", Box, dark, r.transform, p + Vector3.up * 1.05f, new Vector3(.12f, 2.1f, .12f)); post.transform.position = p + Vector3.up * 1.05f; post.AddComponent<BoxCollider>();
        }
        Vector3 ta = a + Vector3.up * 2f, tb = b + Vector3.up * 2f; Vector3 mid = (ta + tb) * .5f - Vector3.up * .15f;
        var line = P("Line", Box, VillageKit.Mat(VillageKit.Wicker), r.transform, Vector3.zero, new Vector3(.02f, .02f, Vector3.Distance(ta, tb)));
        line.transform.position = mid; line.transform.rotation = Quaternion.LookRotation(tb - ta);
        Color[] linen = { new Color(.92f, .9f, .84f), new Color(.75f, .8f, .9f), new Color(.88f, .78f, .7f) };
        int sheets = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(ta, tb) / 1.3f));
        for (int i = 0; i < sheets; i++)
        {
            Vector3 p = Vector3.Lerp(ta, tb, (i + .5f) / sheets) - Vector3.up * (.15f + .45f);
            var sheet = P("Sheet", Box, VillageKit.Mat(linen[rng.Next(linen.Length)], new Color(.04f, .04f, .04f), .1f), r.transform, Vector3.zero, new Vector3(.95f, .9f, .03f));
            sheet.transform.position = p; sheet.transform.rotation = Quaternion.LookRotation(tb - ta) * Quaternion.Euler(0, 90, 0);
            sheet.AddComponent<SheetBillow>();
        }
        return r;
    }

    /// <summary>Small stone shrine with a woven straw rope and a stack of river pebbles.</summary>
    public static GameObject Shrine(Vector3 pos, float yaw, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var r = Root("Shrine", pos, yaw, parent); var t = r.transform;
        var stone = VillageKit.Mat(VillageKit.Stone); var moss = VillageKit.Mat(VillageKit.Moss); var straw = VillageKit.Mat(new Color(.8f, .68f, .4f));
        P("Base", Box, stone, t, new Vector3(0, .15f, 0), new Vector3(.9f, .3f, .7f)).AddComponent<BoxCollider>();
        P("Stele", Box, stone, t, new Vector3(0, .7f, 0), new Vector3(.45f, .8f, .3f));
        P("Cap", Box, moss, t, new Vector3(0, 1.15f, 0), new Vector3(.65f, .12f, .45f));
        P("Rope", VillageKit.Ring(12, .88f), straw, t, new Vector3(0, .85f, 0), new Vector3(.32f, .06f, .24f));
        for (int i = 0; i < 4; i++) P("Pebble", VillageKit.Blob(rng.Next(), .15f), stone, t, new Vector3(.32f, .34f + i * .07f, .18f), new Vector3(.09f - i * .015f, .035f, .07f - i * .01f));
        var l = new GameObject("Candle").AddComponent<Light>(); l.transform.SetParent(t, false); l.transform.localPosition = new Vector3(0, .5f, .3f);
        l.type = LightType.Point; l.color = new Color(1f, .7f, .4f); l.range = 3; l.intensity = .9f; l.gameObject.AddComponent<LanternFlicker>();
        return r;
    }

    /// <summary>Weathered timber footbridge with rails, spanning from a to b.</summary>
    public static GameObject Footbridge(Vector3 a, Vector3 b, Transform parent)
    {
        a = VillageKit.Ground(a); b = VillageKit.Ground(b);
        var r = IslandDeckBridge.Between("Footbridge", a, b, 2.2f, parent, .35f);
        r.AddComponent<VillageKitPiece>();
        return r;
    }

    /// <summary>Drying rack draped with woven fishing nets, coiled rope beside it.</summary>
    public static GameObject NetRack(Vector3 pos, float yaw, Transform parent)
    {
        var r = Root("NetRack", pos, yaw, parent); var t = r.transform; var dark = VillageKit.Mat(VillageKit.TimberDark);
        var net = VillageKit.Mat(new Color(.5f, .55f, .52f), default, .4f);
        P("PostL", Box, dark, t, new Vector3(-1f, .8f, 0), new Vector3(.1f, 1.6f, .1f)).AddComponent<BoxCollider>();
        P("PostR", Box, dark, t, new Vector3(1f, .8f, 0), new Vector3(.1f, 1.6f, .1f)).AddComponent<BoxCollider>();
        P("Bar", Box, dark, t, new Vector3(0, 1.55f, 0), new Vector3(2.2f, .08f, .08f));
        P("NetFront", Box, net, t, new Vector3(0, 1.05f, .08f), new Vector3(1.9f, 1f, .03f), Quaternion.Euler(-8, 0, 0)).AddComponent<SheetBillow>();
        P("NetBack", Box, net, t, new Vector3(0, 1.15f, -.08f), new Vector3(1.9f, .8f, .03f), Quaternion.Euler(8, 0, 0));
        P("Rope", VillageKit.Ring(12, .6f), VillageKit.Mat(VillageKit.Wicker), t, new Vector3(1.4f, .05f, .3f), new Vector3(.3f, .12f, .3f));
        return r;
    }
}


