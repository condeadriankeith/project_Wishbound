using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed, fitted character geometry. Clothing replaces covered anatomy; it is not stacked over the child's coat.
///
/// Style target: chunky, stylised villagers (see refs). Big head, short limbs, barrel torso, sturdy boots.
/// The farmer wears a straw hat, a plaid check shirt under dungaree-style overalls with suspenders, and a beard.
/// </summary>
public static class SettlerWardrobe
{
    static Mesh sphere, tube, box;
    static Mesh Sphere
    {
        get
        {
            if (sphere) return sphere;
            const int segments = 20, bands = 10;
            var vertices = new List<Vector3> { Vector3.up };
            var triangles = new List<int>();
            for (int row = 1; row < bands; row++)
            {
                float polar = Mathf.PI * row / bands;
                for (int i = 0; i < segments; i++)
                {
                    float azimuth = i * (2 * Mathf.PI / segments);
                    vertices.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar),
                        Mathf.Sin(polar) * Mathf.Sin(azimuth)));
                }
            }
            int bottom = vertices.Count;
            vertices.Add(Vector3.down);
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.AddRange(new[] { 0, 1 + next, 1 + i });
                for (int row = 0; row < bands - 2; row++)
                {
                    int a = 1 + row * segments;
                    int b = a + segments;
                    triangles.AddRange(new[] { a + i, a + next, b + i, a + next, b + next, b + i });
                }
                triangles.AddRange(new[] { bottom, bottom - segments + i, bottom - segments + next });
            }
            sphere = new Mesh { name = "Settler closed rounded mesh" };
            sphere.SetVertices(vertices);
            sphere.SetTriangles(triangles, 0);
            sphere.SetNormals(vertices);
            sphere.RecalculateBounds();
            return sphere;
        }
    }
    // Closed tapered cylinder, including both end caps. y=0..1.
    static Mesh Tube
    {
        get
        {
            if (tube) return tube;
            var v = new List<Vector3>(); var tr = new List<int>();
            for (int j = 0; j < 2; j++) for (int i = 0; i < 20; i++) { float a = i * Mathf.PI * 2 / 20; v.Add(new Vector3(Mathf.Cos(a), j, Mathf.Sin(a))); }
            v.Add(Vector3.zero); v.Add(Vector3.up);
            for (int i = 0; i < 20; i++) { int n = (i + 1) % 20; tr.AddRange(new[] { i, i + 20, n + 20, i, n + 20, n, 40, i, n, 41, n + 20, i + 20 }); }
            tube = new Mesh { name = "Settler closed cylinder" }; tube.SetVertices(v); tube.SetTriangles(tr, 0); tube.RecalculateNormals(); tube.RecalculateBounds(); return tube;
        }
    }
    static Transform Joint(string n, Transform p, Vector3 pos) { var g = new GameObject(n).transform; g.SetParent(p, false); g.localPosition = pos; return g; }
    static Transform Part(string n, Transform p, Mesh mesh, Color color, Vector3 pos, Vector3 scale)
    {
        var t = Joint(n, p, pos); t.localScale = scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = VillageKit.Mat(color, default, .13f, .12f); return t;
    }
    static Transform Ball(string n, Transform p, Color c, Vector3 pos, Vector3 scale) => Part(n, p, Sphere, c, pos, scale);
    static Transform Box(string n, Transform p, Color c, Vector3 pos, Vector3 scale)
    { if (!box) box = VillageKit.ChamferBox(.10f); return Part(n, p, box, c, pos, scale); }
    static void Bar(string n, Transform p, Color c, Vector3 a, Vector3 b, float radius)
    { var t = Part(n, p, Tube, c, a, new Vector3(radius, (b - a).magnitude, radius)); t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); }

    public static Transform Build(Transform parent, SettlerLook.Outfit o)
    {
        var av = Joint("Body", parent, Vector3.zero);
        bool kid = o.age == SettlerLook.Age.Child, elder = o.age == SettlerLook.Age.Elder;
        // Chunky proportions: bigger head & torso, shorter limbs than before.
        float w = (kid ? .24f : .29f) * o.build, shoulder = w * .85f + .05f;
        Color leather = new Color(.25f, .16f, .12f), brass = new Color(.77f, .59f, .29f), cream = new Color(.91f, .84f, .67f);
        var hips = Joint("hips", av, new Vector3(0, .64f, 0));
        var spine = Joint("spine", hips, new Vector3(0, .18f, 0));
        var head = Joint("head", spine, new Vector3(0, .40f, 0));
        Color coat = o.apron ? o.shirt : o.jacket;
        // Barrel torso.
        Ball("Tailored tunic", spine, coat, new Vector3(0, .02f, 0), new Vector3(w, elder ? .40f : .36f, .21f));
        Ball("Trouser seat", hips, o.trousers, new Vector3(0, .015f, 0), new Vector3(w * .9f, .16f, .18f));
        Part("Waist belt", spine, Tube, leather, new Vector3(0, -.15f, 0), new Vector3(w * .92f, .05f, .18f));
        Box("Belt buckle", spine, brass, new Vector3(0, -.125f, .184f), new Vector3(.08f, .06f, .02f));
        Bar("Neck", spine, o.skin, new Vector3(0, .22f, 0), new Vector3(0, .36f, 0), .095f);
        float hx = kid ? .34f : .32f, hy = kid ? .35f : .34f;
        Ball("Face", head, o.skin, new Vector3(0, .06f, 0), new Vector3(hx, hy, .28f));
        for (int side = -1; side <= 1; side += 2)
        {
            Ball("Ear", head, o.skin, new Vector3(side * (hx - .005f), .04f, 0), new Vector3(.06f, .09f, .06f));
            Ball("Eye", head, new Color(.12f, .09f, .075f), new Vector3(side * .105f, .075f, .27f), new Vector3(.02f, .03f, .014f));
            Ball("Eye catchlight", head, cream, new Vector3(side * .105f - .005f, .084f, .283f), new Vector3(.007f, .008f, .004f));
            Ball("Cheek", head, Color.Lerp(o.skin, new Color(.77f, .29f, .24f), kid ? .30f : .20f),
                new Vector3(side * .155f, .002f, .230f), new Vector3(.049f, .025f, .021f));
            Bar("Brow", head, o.hair, new Vector3(side * .07f, .13f, .26f), new Vector3(side * .13f, .14f, .255f), .014f);
            var arm = Joint(side < 0 ? "arm.L" : "arm.R", spine, new Vector3(side * shoulder, .16f, 0));
            Bar("Sleeve", arm, coat, Vector3.zero, new Vector3(side * .235f, 0, 0), .095f);
            Ball("Shoulder seam", arm, coat, Vector3.zero, new Vector3(.095f, .1f, .1f));
            var elbow = Joint(side < 0 ? "elbow.L" : "elbow.R", arm, new Vector3(side * .26f, 0, 0));
            Ball("Elbow", elbow, o.skin, Vector3.zero, Vector3.one * .068f);
            Bar("Forearm", elbow, o.skin, Vector3.zero, new Vector3(side * .22f, 0, 0), .065f);
            Bar("Rolled cuff", arm, cream, new Vector3(side * .22f, 0, 0), new Vector3(side * .27f, 0, 0), .101f);
            var hand = Joint(side < 0 ? "hand.L" : "hand.R", elbow, new Vector3(side * .25f, 0, 0));
            Ball("Mitten", hand, o.skin, Vector3.zero, new Vector3(.085f, .075f, .07f));
            var leg = Joint(side < 0 ? "leg.L" : "leg.R", hips, new Vector3(side * .12f, -.02f, 0));
            Bar("Trouser leg", leg, o.trousers, Vector3.zero, new Vector3(0, -.26f, 0), .105f);
            var knee = Joint(side < 0 ? "knee.L" : "knee.R", leg, new Vector3(0, -.26f, 0));
            Ball("Trouser knee", knee, o.trousers, Vector3.zero, new Vector3(.10f, .09f, .10f));
            Bar("Shin", knee, o.trousers, Vector3.zero, new Vector3(0, -.25f, 0), .09f);
            Bar("Boot shaft", knee, leather, new Vector3(0, -.14f, 0), new Vector3(0, -.27f, 0), .10f);
            Box("Boot", knee, leather, new Vector3(0, -.28f, .05f), new Vector3(.21f, .15f, .32f));
            Box("Sole", knee, new Color(.13f, .11f, .10f), new Vector3(0, -.34f, .05f), new Vector3(.22f, .04f, .33f));
            Box("Boot strap", knee, brass, new Vector3(0, -.20f, .095f), new Vector3(.14f, .03f, .02f));
        }
        Ball("Nose", head, o.skin, new Vector3(0, .03f, .285f), new Vector3(.04f, .05f, .045f));
        Bar("Mouth", head, new Color(.4f, .23f, .19f), new Vector3(-.04f, -.06f, .27f), new Vector3(.04f, -.06f, .27f), .01f);
        // Hair cap + locks.
        Ball("Hair crown", head, o.hair, new Vector3(0, .19f, -.03f), new Vector3(hx + .015f, .22f, .27f));
        for (int i = 0; i < 5; i++)
            Ball("Fringe", head, o.hair, new Vector3((i - 2) * .10f, .24f - (i % 2) * .03f, .19f), new Vector3(.08f, .11f, .085f));
        if (o.hairstyle != 0 || o.bun)
            for (int side = -1; side <= 1; side += 2) Ball("Side hair", head, o.hair, new Vector3(side * .28f, .05f, -.05f), new Vector3(.085f, o.bun ? .20f : .15f, .19f));
        if (o.bun) Ball("Hair bun", head, o.hair, new Vector3(0, .21f, -.30f), new Vector3(.15f, .16f, .15f));
        if (o.beard) Ball("Trimmed beard", head, o.hair, new Vector3(0, -.16f, .18f), new Vector3(.20f, .14f, .10f));
        if (o.moustache) for (int side = -1; side <= 1; side += 2) Ball("Moustache", head, o.hair, new Vector3(side * .06f, -.015f, .285f), new Vector3(.07f, .03f, .02f));
        // ---- headwear ----
        if (o.strawHat)
        {
            // Wide straw hat with a taller crown and a ribbon band.
            Part("Straw brim", head, Tube, o.hatColor, new Vector3(0, .31f, 0), new Vector3(.52f, .04f, .44f));
            Ball("Straw crown", head, o.hatColor, new Vector3(0, .37f, -.01f), new Vector3(.33f, .19f, .29f));
            Part("Hat ribbon", head, Tube, o.jacket, new Vector3(0, .345f, 0), new Vector3(.32f, .05f, .27f));
            Box("Ribbon knot", head, o.jacket, new Vector3(.32f, .37f, 0), new Vector3(.06f, .07f, .09f));
            Part("Braided brim edge", head, Tube, Color.Lerp(o.hatColor, leather, .3f),
                new Vector3(0, .342f, 0), new Vector3(.528f, .013f, .448f));
        }
        else if (o.cap)
        {
            Ball("Cloth cap", head, o.jacket, new Vector3(0, .30f, -.02f), new Vector3(.35f, .16f, .30f));
            Box("Cap visor", head, o.jacket, new Vector3(0, .25f, .275f), new Vector3(.48f, .04f, .24f));
            Ball("Cap button", head, brass, new Vector3(0, .46f, -.02f), Vector3.one * .028f);
        }
        else if (o.scarf)
        {
            Ball("Head wrap", head, o.jacket, new Vector3(0, .25f, -.04f), new Vector3(.35f, .20f, .30f));
            Ball("Wrap knot", head, cream, new Vector3(.30f, .20f, -.12f), Vector3.one * .08f);
            Box("Wrap tail", head, o.jacket, new Vector3(.30f, .07f, -.17f), new Vector3(.09f, .26f, .05f));
        }
        // ---- overalls / apron or shirt front ----
        if (o.apron)
        {
            // Dungaree bib + straps over the shirt.
            Box("Apron skirt", spine, o.apronColor, new Vector3(0, -.13f, .20f), new Vector3(w * 1.75f, .40f, .04f));
            Box("Apron bib", spine, o.apronColor, new Vector3(0, .10f, .198f), new Vector3(.30f, .26f, .04f));
            for (int side = -1; side <= 1; side += 2)
            {
                Bar("Apron strap", spine, o.apronColor, new Vector3(side * .10f, .18f, .20f), new Vector3(side * .15f, .29f, .08f), .026f);
                Box("Patch pocket", spine, o.jacket, new Vector3(side * .10f, -.13f, .225f), new Vector3(.15f, .14f, .025f));
                Box("Pocket stitch", spine, cream, new Vector3(side * .10f, -.08f, .24f), new Vector3(.11f, .009f, .007f));
                Ball("Bib rivet", spine, brass, new Vector3(side * .095f, .18f, .22f), Vector3.one * .016f);
            }
            Bar("Apron waist tie", spine, o.apronColor, new Vector3(-w, -.05f, -.08f), new Vector3(w, -.05f, -.08f), .026f);
        }
        else
        {
            Box("Button placket", spine, cream, new Vector3(0, .035f, .188f), new Vector3(.05f, .40f, .014f));
            for (int i = 0; i < 3; i++) Ball("Coat button", spine, brass, new Vector3(0, .15f - i * .10f, .21f), Vector3.one * .018f);
            for (int side = -1; side <= 1; side += 2)
            {
                var collar = Box("Collar", spine, cream, new Vector3(side * .08f, .24f, .10f), new Vector3(.15f, .13f, .04f)); collar.localRotation = Quaternion.Euler(-25, 0, side * 25);
                Box("Coat pocket", spine, o.jacket * .83f, new Vector3(side * .14f, -.10f, .17f), new Vector3(.11f, .11f, .028f));
            }
        }
        if (o.role == SettlerBrain.Role.Wanderer || o.role == SettlerBrain.Role.Fisher)
        {
            Box("Leather satchel", spine, leather, new Vector3(w + .04f, -.13f, -.04f), new Vector3(.19f, .26f, .24f));
            Box("Satchel flap", spine, o.hatColor, new Vector3(w + .045f, -.03f, .07f), new Vector3(.20f, .11f, .04f));
            Bar("Satchel strap", spine, leather, new Vector3(-.17f, .25f, .14f), new Vector3(w, -.05f, .20f), .025f);
            Box("Satchel clasp", spine, brass, new Vector3(w + .045f, -.06f, .10f), new Vector3(.04f, .05f, .014f));
        }
        if (elder)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4; Bar("Spectacle frame", head, brass, new Vector3(side * .10f + Mathf.Cos(a) * .07f, .075f + Mathf.Sin(a) * .065f, .29f), new Vector3(side * .10f + Mathf.Cos(b) * .07f, .075f + Mathf.Sin(b) * .065f, .29f), .007f); }
            }
            Bar("Glasses bridge", head, brass, new Vector3(-.03f, .08f, .29f), new Vector3(.03f, .08f, .29f), .008f);
        }
        if (o.role == SettlerBrain.Role.Gardener)
        {
            Box("Seed pocket label", spine, cream, new Vector3(.10f, -.125f, .243f), new Vector3(.083f, .041f, .009f));
            var sprig = Ball("Pocket sprig", spine, new Color(.42f, .61f, .25f),
                new Vector3(.12f, -.033f, .225f), new Vector3(.019f, .082f, .013f));
            sprig.localRotation = Quaternion.Euler(0, 0, -24);
        }
        else if (o.role == SettlerBrain.Role.Fisher)
        {
            Ball("Cap enamel pin", head, cream, new Vector3(.15f, .30f, .248f), new Vector3(.028f, .028f, .012f));
        }
        else if (kid)
        {
            var patch = Box("Mended coat patch", spine, Color.Lerp(o.jacket, cream, .48f),
                new Vector3(.15f, .11f, .183f), new Vector3(.076f, .071f, .019f));
            patch.localRotation = Quaternion.Euler(0, 15, -12);
        }
        return av;
    }
}
