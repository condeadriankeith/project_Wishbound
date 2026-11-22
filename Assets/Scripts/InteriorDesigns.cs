using System.Collections.Generic;
using UnityEngine;



/// <summary>
/// Hand-laid interiors, one per house type, each a home with its own story. Every prop position below is placed by
/// hand (room-local metres: x = across, z = back(-) to door(+), y = up). Baked into the Interior_* scenes as plain
/// editable GameObjects by Tools > Wishbound > Bake Interiors, so they can be adjusted freely in the Scene view.
///   MedievalHouse            - the baker's family: brick oven, flour sacks, bread racks, kneading table, family table
///   SmallMedievalHouse       - the old herbalist: herbs drying from every beam, rocking chair, mortar, jars, a cat
///   TwoStoryMedievalHouse    - the weaver: a big loom, spinning wheel, yarn baskets, bolts of cloth, loft bedroom
///   TwoStoryMedievalHouse_2  - the fisherman: nets and oars on the walls, barrels of fish, a half-built boat, hammock loft
/// </summary>
public static class InteriorDesigns
{
    // ------------------------------------------------------------------ toolkit
    public class Room
    {
        public Transform root; public float W, D, H; public System.Random rng;
        public Mesh box, boxSoft;
        public float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        public Material M(Color c, Color e = default, float brush = .25f) => VillageKit.Mat(c, e, brush, .12f);
        public GameObject P(string n, Mesh m, Material mat, Vector3 lp, Vector3 sc, Vector3 euler = default, bool col = false, Transform parent = null)
        {
            var g = new GameObject(n); g.transform.SetParent(parent ? parent : root, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(euler); g.transform.localScale = sc;
            g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (col) g.AddComponent<BoxCollider>();
            return g;
        }
        public GameObject Group(string n, Vector3 lp, float yaw = 0)
        {
            var g = new GameObject(n); g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(0, yaw, 0); return g;
        }
        public GameObject B(string n, Material mat, Vector3 lp, Vector3 sc, Vector3 euler = default, bool col = false, Transform parent = null) => P(n, box, mat, lp, sc, euler, col, parent);
        public Light L(string n, Vector3 lp, Color c, float range, float intensity, bool flicker = false, bool shadows = false)
        {
            var l = new GameObject(n).AddComponent<Light>(); l.transform.SetParent(root, false); l.transform.localPosition = lp;
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (flicker) l.gameObject.AddComponent<LanternFlicker>();
            return l;
        }
        public void Cut(GameObject g, Vector3 inward) { g.AddComponent<CutawayWall>().inward = inward; }
    }

    // shared palette
    static Color Plaster = new Color(.66f, .59f, .5f), Beam = new Color(.33f, .22f, .15f), Plank = new Color(.5f, .36f, .24f), PlankD = new Color(.42f, .3f, .2f),
        Stone = new Color(.5f, .48f, .45f), Wood = new Color(.52f, .37f, .24f), WoodD = new Color(.36f, .25f, .17f), Linen = new Color(.9f, .86f, .78f), Iron = new Color(.17f, .16f, .18f);

    public static void Build(Transform root, InteriorBuilder.Style style, int seed)
    {
        var r = new Room { root = root, rng = new System.Random(seed + (int)style * 17), box = VillageKit.ChamferBox(.08f), boxSoft = VillageKit.ChamferBox(.25f) };
        switch (style)
        {
            case InteriorBuilder.Style.MedievalHouse: r.W = 9.2f; r.D = 10.2f; r.H = 3.4f; Shell(r, Plaster, Plank); Baker(r); break;
            case InteriorBuilder.Style.SmallMedievalHouse: r.W = 7.8f; r.D = 7.6f; r.H = 3.1f; Shell(r, new Color(.62f, .6f, .5f), PlankD); Herbalist(r); break;
            case InteriorBuilder.Style.TwoStoryMedievalHouse: r.W = 9.6f; r.D = 9.6f; r.H = 3.3f; Shell(r, new Color(.7f, .64f, .58f), Plank); Weaver(r); break;
            default: r.W = 8.4f; r.D = 8.4f; r.H = 3.2f; Shell(r, new Color(.58f, .62f, .64f), PlankD); Fisher(r); break;
        }
        // spawn + exit at the door (+Z)
        var sc = root.GetComponent<InteriorScene>() ?? root.gameObject.AddComponent<InteriorScene>();
        var spawn = new GameObject("Spawn").transform; spawn.SetParent(root, false); spawn.localPosition = new Vector3(0, .1f, r.D / 2 - 2.8f); spawn.localRotation = Quaternion.Euler(0, 180, 0);
        var exit = new GameObject("Exit").transform; exit.SetParent(root, false); exit.localPosition = new Vector3(0, 0, r.D / 2 - .45f);
        sc.spawn = spawn; sc.exit = exit;
        var cut = root.GetComponent<InteriorCutaway>() ?? root.gameObject.AddComponent<InteriorCutaway>();
        cut.loftHeight = style == InteriorBuilder.Style.TwoStoryMedievalHouse || style == InteriorBuilder.Style.TwoStoryMedievalHouse_2 ? r.H : 0;
    }

    // ------------------------------------------------------------------ room shell: floor, timber-framed walls, door, windows
    static void Shell(Room r, Color plasterC, Color plankC)
    {
        float W = r.W, D = r.D, H = r.H, t = .25f;
        var plaster = r.M(plasterC); var beam = r.M(Beam); var plank = r.M(plankC, default, .35f); var plankD = r.M(plankC * .85f, default, .35f);
        var floor = r.Group("Floor", Vector3.zero);
        int boards = Mathf.RoundToInt(W / .32f);
        for (int i = 0; i < boards; i++) r.B("Plank", i % 3 == 0 ? plankD : plank, new Vector3(-W / 2 + (i + .5f) * W / boards, -.05f, 0), new Vector3(W / boards - .02f, .1f, D), default, false, floor.transform);
        floor.AddComponent<BoxCollider>().size = new Vector3(W, .1f, D);
        var walls = r.Group("Walls", Vector3.zero).transform;
        void Wall(string n, Vector3 c, Vector3 size, Vector3 inward)
        {
            var w = r.B(n, plaster, c, size, default, true, walls); r.Cut(w, inward);
            bool alongX = size.x > size.z; float len = alongX ? size.x : size.z;
            int posts = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
            for (int i = 0; i <= posts; i++)
            {
                float k = -len / 2 + i * len / posts;
                var post = r.B("Timber", beam, c + (alongX ? new Vector3(k, 0, 0) : new Vector3(0, 0, k)) + inward * (Mathf.Min(size.x, size.z) * .5f + .05f), new Vector3(alongX ? .16f : .1f, size.y, alongX ? .1f : .16f), default, false, walls);
                r.Cut(post, inward);
            }
            var rail = r.B("Rail", beam, c + Vector3.up * (size.y * .5f - .9f) + inward * (Mathf.Min(size.x, size.z) * .5f + .05f), alongX ? new Vector3(size.x, .12f, .1f) : new Vector3(.1f, .12f, size.z), default, false, walls);
            r.Cut(rail, inward);
        }
        Wall("WallBack", new Vector3(0, H / 2, -D / 2), new Vector3(W, H, t), Vector3.forward);
        Wall("WallLeft", new Vector3(-W / 2, H / 2, 0), new Vector3(t, H, D), Vector3.right);
        Wall("WallRight", new Vector3(W / 2, H / 2, 0), new Vector3(t, H, D), Vector3.left);
        float dw = 1.1f, dh = 2.1f;
        Wall("WallFrontL", new Vector3((-W / 2 - dw / 2) / 2, H / 2, D / 2), new Vector3(W / 2 - dw / 2, H, t), Vector3.back);
        Wall("WallFrontR", new Vector3((dw / 2 + W / 2) / 2, H / 2, D / 2), new Vector3(W / 2 - dw / 2, H, t), Vector3.back);
        Wall("WallFrontTop", new Vector3(0, (dh + H) / 2, D / 2), new Vector3(dw, H - dh, t), Vector3.back);
        r.B("Door (ajar)", r.M(WoodD), new Vector3(.45f, dh / 2, D / 2 - .35f), new Vector3(.08f, dh - .05f, dw - .08f), new Vector3(0, 25, 0));
        r.B("DoorMat", r.M(new Color(.6f, .5f, .3f), default, .45f), new Vector3(0, .02f, D / 2 - .7f), new Vector3(1f, .03f, .6f));
        var seal = new GameObject("DoorwaySeal"); seal.transform.SetParent(r.root, false); seal.transform.localPosition = new Vector3(0, H / 2, D / 2 + .05f);
        seal.AddComponent<BoxCollider>().size = new Vector3(dw + .4f, H + 1f, .6f);
        // a closed door slab behind the ajar one so the doorway reads as shut from inside
        r.B("DoorClosed", r.M(WoodD * .9f), new Vector3(0, dh / 2, D / 2 + .1f), new Vector3(dw, dh, .06f));
        // skirting boards + a beam all round the top of the walls
        var trim = r.M(Beam);
        r.B("SkirtBack", trim, new Vector3(0, .08f, -D / 2 + .14f), new Vector3(W - .3f, .16f, .04f));
        r.B("SkirtLeft", trim, new Vector3(-W / 2 + .14f, .08f, 0), new Vector3(.04f, .16f, D - .3f));
        r.B("SkirtRight", trim, new Vector3(W / 2 - .14f, .08f, 0), new Vector3(.04f, .16f, D - .3f));
        Common(r);
    }

    /// <summary>Lived-in details every home shares: coat pegs with a cloak by the door, a basket and boots, a wall
    /// shelf of crockery, a jug and cups, a potted plant, a hanging pan rack and strings of garlic.</summary>
    static void Common(Room r)
    {
        float W = r.W, D = r.D, H = r.H;
        var wood = r.M(Wood); var woodD = r.M(WoodD); var crock = r.M(new Color(.86f, .82f, .74f)); var blue = r.M(new Color(.45f, .55f, .75f));
        // pegs + cloak + hat beside the door
        var pegs = r.Group("CoatPegs", new Vector3(-.95f, 1.75f, D / 2 - .16f)).transform;
        r.B("PegRail", woodD, Vector3.zero, new Vector3(.8f, .1f, .05f), default, false, pegs);
        for (int i = 0; i < 3; i++) r.B("Peg", wood, new Vector3(-.28f + i * .28f, 0, -.07f), new Vector3(.04f, .04f, .12f), default, false, pegs);
        r.P("Cloak", r.boxSoft, r.M(new Color(.35f, .4f, .32f), default, .5f), new Vector3(-.28f, -.45f, -.1f), new Vector3(.38f, .85f, .08f), default, false, pegs);
        r.P("Hat", VillageKit.Ring(10, 0), r.M(new Color(.5f, .38f, .25f)), new Vector3(.28f, -.12f, -.12f), new Vector3(.18f, .05f, .18f), new Vector3(80, 0, 0), false, pegs);
        r.P("Basket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(-1.35f, 0, D / 2 - .45f), new Vector3(.22f, .22f, .22f));
        foreach (var x in new[] { -.75f, -.6f }) r.P("Boot", VillageKit.Blob(200 + (int)(x * 100), .05f), r.M(new Color(.3f, .2f, .14f)), new Vector3(x, .1f, D / 2 - .4f), new Vector3(.07f, .1f, .14f));
        // crockery shelf on the back wall
        var cs = r.Group("CrockeryShelf", new Vector3(W * .28f, 1.55f, -D / 2 + .3f)).transform;
        r.B("Shelf", wood, Vector3.zero, new Vector3(1.2f, .05f, .26f), default, false, cs);
        r.B("Bracket", woodD, new Vector3(-.5f, -.12f, 0), new Vector3(.05f, .22f, .22f), default, false, cs);
        r.B("Bracket", woodD, new Vector3(.5f, -.12f, 0), new Vector3(.05f, .22f, .22f), default, false, cs);
        for (int i = 0; i < 4; i++) r.P("Plate", VillageKit.Ring(12, 0), i % 2 == 0 ? crock : blue, new Vector3(-.45f + i * .3f, .17f, -.06f), new Vector3(.13f, .012f, .13f), new Vector3(78, 0, 0), false, cs);
        r.P("Jug", VillageKit.Blob(210, .03f, 4, 9), blue, new Vector3(.42f, .14f, .05f), new Vector3(.08f, .12f, .08f), default, false, cs);
        for (int i = 0; i < 2; i++) r.P("Cup", VillageKit.Ring(8, .8f), crock, new Vector3(-.1f + i * .14f, .03f, .07f), new Vector3(.04f, .06f, .04f), default, false, cs);
        // pan rack + garlic strings over the hearth side
        var rack = r.Group("PanRack", new Vector3(-W * .1f, H - .65f, -D / 2 + .35f)).transform;
        r.B("Rail", r.M(new Color(.2f, .2f, .22f)), Vector3.zero, new Vector3(1.0f, .04f, .04f), default, false, rack);
        for (int i = 0; i < 3; i++) r.P("Pan", VillageKit.Ring(10, .85f), r.M(new Color(.3f, .3f, .32f)), new Vector3(-.35f + i * .35f, -.25f, .02f), new Vector3(.13f, .03f, .13f), new Vector3(85, 0, 0), false, rack);
        for (int g = 0; g < 2; g++)
            for (int i = 0; i < 5; i++) r.P("Garlic", VillageKit.Blob(220 + i, .1f), r.M(new Color(.92f, .88f, .8f)), new Vector3(W / 2 - .35f, H - .6f - i * .12f, -D / 2 + .7f + g * .25f), Vector3.one * .055f);
        // potted plant + candle on a window sill-height stool by the right wall
        r.P("Pot", VillageKit.Ring(9, .82f), r.M(new Color(.7f, .42f, .28f)), new Vector3(W / 2 - .35f, 0, D * .35f), new Vector3(.16f, .2f, .16f));
        for (int i = 0; i < 6; i++) r.P("PlantLeaf", VillageKit.Leaf(), r.M(new Color(.3f, .55f, .3f)), new Vector3(W / 2 - .35f, .2f, D * .35f), Vector3.one * .28f, new Vector3(-50, i * 60, 0));
    }

    static void Window(Room r, Vector3 c, Vector3 inward, bool shutters = true)
    {
        var beam = r.M(Beam);
        var frame = r.B("WindowFrame", beam, c, inward.x != 0 ? new Vector3(.12f, 1f, 1.1f) : new Vector3(1.1f, 1f, .12f)); r.Cut(frame, inward);
        var pane = r.B("WindowPane", r.M(new Color(.1f, .14f, .25f), new Color(.25f, .36f, .62f)), c + inward * .04f, inward.x != 0 ? new Vector3(.04f, .85f, .95f) : new Vector3(.95f, .85f, .04f)); r.Cut(pane, inward);
        var sill = r.B("WindowSill", r.M(Wood), c + inward * .14f + Vector3.down * .52f, inward.x != 0 ? new Vector3(.3f, .06f, 1.2f) : new Vector3(1.2f, .06f, .3f)); r.Cut(sill, inward);
        r.L("Moonlight", c + inward * 1.2f + Vector3.down * .6f, new Color(.55f, .65f, 1f), 3.5f, .7f);
        Vector3 along = inward.x != 0 ? Vector3.forward : Vector3.right;
        foreach (var sgn in new[] { -1f, 1f })
        {
            var cur = r.P("Curtain", r.boxSoft, r.M(new Color(.78f, .7f, .55f), default, .5f), c + inward * .12f + along * sgn * .62f + Vector3.up * .05f, inward.x != 0 ? new Vector3(.04f, 1.05f, .28f) : new Vector3(.28f, 1.05f, .04f)); r.Cut(cur, inward);
        }
        var cnd = r.B("SillCandle", r.M(Linen), c + inward * .16f + Vector3.down * .42f + along * .3f, new Vector3(.05f, .14f, .05f)); r.Cut(cnd, inward);
    }

    static void Hearth(Room r, Vector3 at, float yaw, float width, bool lit = true)
    {
        var g = r.Group("Hearth", at, yaw).transform; var stone = r.M(Stone); var dark = r.M(new Color(.08f, .06f, .06f));
        r.B("Chimneybreast", stone, new Vector3(0, .6f, 0), new Vector3(width, 1.2f, .9f), default, true, g);
        r.B("Mouth", dark, new Vector3(0, .45f, .42f), new Vector3(width * .6f, .6f, .1f), default, false, g);
        r.B("Mantel", r.M(Wood), new Vector3(0, 1.25f, .3f), new Vector3(width + .25f, .1f, .45f), default, false, g);
        r.B("Flue", stone, new Vector3(0, 1.2f + (r.H - 1.2f) / 2, -.1f), new Vector3(width * .6f, r.H - 1.2f, .7f), default, false, g);
        for (int i = 0; i < 3; i++) r.P("Log", VillageKit.Ring(7, 0), r.M(WoodD), new Vector3(-.2f + i * .2f, .18f, .45f), new Vector3(.07f, .45f, .07f), new Vector3(0, i * 25, 90), false, g);
        if (!lit) return;
        var l = r.L("Fire", Vector3.zero, new Color(1f, .55f, .2f), 6.5f, 1.7f, true, true); l.transform.SetParent(g, false); l.transform.localPosition = new Vector3(0, .5f, .9f);
        Embers(r, g, new Vector3(0, .35f, .5f));
    }

    static void Embers(Room r, Transform parent, Vector3 lp)
    {
        var g = new GameObject("FireEmbers"); g.transform.SetParent(parent, false); g.transform.localPosition = lp;
        var ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(.3f, .9f); m.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(2f, .9f, .3f), new Color(2f, .5f, .15f)); m.maxParticles = 80; m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = true;
        var em = ps.emission; em.rateOverTime = 26;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(.6f, .1f, .2f);
        var n = ps.noise; n.enabled = true; n.strength = .3f;
#if UNITY_EDITOR
        var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Comet/CometEmber.mat");
        if (mat) g.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
#endif
    }

    static void Bed(Room r, Vector3 at, float yaw, Color quilt, bool small = false)
    {
        var g = r.Group("Bed", at, yaw).transform; float w = small ? 1.0f : 1.4f, l = small ? 1.7f : 2.1f;
        r.B("Frame", r.M(Wood), new Vector3(0, .25f, 0), new Vector3(w, .35f, l), default, true, g);
        r.B("Mattress", r.M(Linen), new Vector3(0, .5f, 0), new Vector3(w - .1f, .16f, l - .1f), default, false, g);
        r.P("Quilt", r.boxSoft, r.M(quilt, default, .45f), new Vector3(0, .6f, .25f), new Vector3(w - .02f, .1f, l * .65f), default, false, g);
        r.P("Pillow", VillageKit.Blob(5, .06f), r.M(Linen), new Vector3(0, .66f, -l / 2 + .32f), new Vector3(w * .3f, .1f, .2f), default, false, g);
        r.B("Headboard", r.M(WoodD), new Vector3(0, .7f, -l / 2), new Vector3(w, .9f, .1f), default, false, g);
    }

    static void Table(Room r, Vector3 at, float yaw, float w, float d, bool benches, out Transform g)
    {
        g = r.Group("Table", at, yaw).transform; var wood = r.M(Wood); var woodD = r.M(WoodD);
        r.B("Top", wood, new Vector3(0, .78f, 0), new Vector3(w, .1f, d), default, true, g);
        foreach (var lp in new[] { new Vector3(-w / 2 + .1f, .38f, -d / 2 + .1f), new Vector3(w / 2 - .1f, .38f, -d / 2 + .1f), new Vector3(-w / 2 + .1f, .38f, d / 2 - .1f), new Vector3(w / 2 - .1f, .38f, d / 2 - .1f) })
            r.B("Leg", woodD, lp, new Vector3(.1f, .76f, .1f), default, false, g);
        if (!benches) return;
        r.B("BenchA", woodD, new Vector3(0, .42f, -d / 2 - .4f), new Vector3(w * .85f, .08f, .3f), default, true, g);
        r.B("BenchB", woodD, new Vector3(0, .42f, d / 2 + .4f), new Vector3(w * .85f, .08f, .3f), default, true, g);
    }

    static void Shelves(Room r, Vector3 at, float yaw, int levels, float width, System.Action<Transform, Vector3> fill)
    {
        var g = r.Group("Shelves", at, yaw).transform;
        r.B("Back", r.M(WoodD), new Vector3(0, .35f + levels * .25f, -.16f), new Vector3(width, levels * .5f + .3f, .04f), default, false, g);
        for (int s = 0; s < levels; s++)
        {
            r.B("Board", r.M(Wood), new Vector3(0, .6f + s * .5f, 0), new Vector3(width, .05f, .34f), default, false, g);
            for (int i = 0; i < Mathf.RoundToInt(width / .3f); i++) fill(g, new Vector3(-width / 2 + .15f + i * .3f, .63f + s * .5f, 0));
        }
    }

    static void Jar(Room r, Transform g, Vector3 lp) => r.P("Jar", VillageKit.Ring(8, .82f), r.M(new Color(r.R(.4f, .75f), r.R(.35f, .6f), r.R(.25f, .5f))), lp, new Vector3(.08f, r.R(.12f, .22f), .08f), default, false, g);
    static void Book(Room r, Transform g, Vector3 lp) => r.B("Book", r.M(new Color(r.R(.3f, .7f), r.R(.2f, .4f), r.R(.2f, .5f))), lp + Vector3.up * .1f, new Vector3(.06f, r.R(.18f, .26f), .2f), new Vector3(0, 0, r.R(-8, 8)), false, g);

    static void HangingLantern(Room r, Vector3 lp, Color c)
    {
        r.B("LanternChain", r.M(Iron), lp + Vector3.up * .4f, new Vector3(.03f, .7f, .03f));
        r.B("Lantern", r.M(new Color(.9f, .75f, .5f), c * 1.1f), lp, new Vector3(.22f, .3f, .22f));
        r.L("LanternLight", lp + Vector3.down * .1f, c, 5, .75f, true);
    }

    static void Rug(Room r, Vector3 lp, Vector2 size, Color a, Color border)
    {
        r.P("Rug", VillageKit.Disc(r.rng.Next(), .05f, 18), r.M(a, default, .45f), lp + Vector3.up * .01f, new Vector3(size.x, 1, size.y));
        r.P("RugBorder", VillageKit.Ring(18, .9f), r.M(border), lp + Vector3.up * .005f, new Vector3(size.x * 1.03f, .015f, size.y * 1.03f));
    }

    static void Barrel(Room r, Vector3 lp, float s = 1) { var g = r.P("Barrel", VillageKit.Blob(r.rng.Next(), .02f, 5, 10), r.M(Wood), lp + Vector3.up * .45f * s, new Vector3(.38f, .45f, .38f) * s, default, true); r.P("Band", VillageKit.Ring(10, .92f), r.M(Iron), lp + Vector3.up * .7f * s, new Vector3(.39f, .05f, .39f) * s); }
    static void Crate(Room r, Vector3 lp, float yaw, float s = .6f) => r.B("Crate", r.M(WoodD), lp + Vector3.up * s / 2, Vector3.one * s, new Vector3(0, yaw, 0), true);
    static void Chair(Room r, Vector3 lp, float yaw)
    {
        var g = r.Group("Chair", lp, yaw).transform;
        r.B("Seat", r.M(Wood), new Vector3(0, .45f, 0), new Vector3(.45f, .06f, .45f), default, true, g);
        r.B("Back", r.M(WoodD), new Vector3(0, .8f, -.2f), new Vector3(.45f, .6f, .06f), default, false, g);
        foreach (var x in new[] { -.18f, .18f }) foreach (var z in new[] { -.18f, .18f }) r.B("Leg", r.M(WoodD), new Vector3(x, .22f, z), new Vector3(.05f, .45f, .05f), default, false, g);
    }

    // ================================================================== 1. the baker's family home
    static void Baker(Room r)
    {
        float W = r.W, D = r.D, H = r.H;
        Window(r, new Vector3(-W / 2 + .14f, 1.7f, -D * .15f), Vector3.right);
        Window(r, new Vector3(W / 2 - .14f, 1.7f, D * .18f), Vector3.left);
        // the big domed brick oven, still warm - the heart of this home
        var oven = r.Group("BreadOven", new Vector3(-W / 2 + 1.25f, 0, -D / 2 + 1.1f)).transform;
        var brick = r.M(new Color(.62f, .38f, .28f)); var brickD = r.M(new Color(.48f, .3f, .22f));
        r.B("OvenBase", brickD, new Vector3(0, .45f, 0), new Vector3(1.9f, .9f, 1.6f), default, true, oven);
        r.P("OvenDome", VillageKit.Blob(21, .04f, 4, 12), brick, new Vector3(0, 1.0f, -.05f), new Vector3(.9f, .6f, .75f), default, false, oven);
        r.B("OvenMouth", r.M(new Color(.1f, .05f, .03f), new Color(.6f, .25f, .05f)), new Vector3(0, 1.0f, .68f), new Vector3(.55f, .4f, .05f), default, false, oven);
        r.B("OvenFlue", brickD, new Vector3(0, 1.6f + (H - 1.6f) / 2, -.35f), new Vector3(.5f, H - 1.6f, .5f), default, false, oven);
        var ol = r.L("OvenGlow", new Vector3(-W / 2 + 1.25f, 1.0f, -D / 2 + 2.1f), new Color(1f, .5f, .18f), 5.5f, 1.5f, true, true);
        Embers(r, oven, new Vector3(0, .95f, .55f));
        r.P("BreadPeel", r.box, r.M(Wood), new Vector3(-W / 2 + .3f, 1.1f, -D / 2 + 2.2f), new Vector3(.25f, 1.6f, .04f), new Vector3(0, 0, 8));
        // flour sacks slumped by the oven
        for (int i = 0; i < 4; i++) r.P("FlourSack", VillageKit.Blob(30 + i, .12f), r.M(new Color(.88f, .84f, .74f)), new Vector3(-W / 2 + .5f + (i % 2) * .45f, .3f + (i / 2) * .35f, -D / 2 + 2.5f + (i % 2) * .1f), new Vector3(.3f, .32f, .25f), new Vector3(0, i * 37, 0), i < 2);
        // long kneading table dusted with flour, dough and loaves
        Table(r, new Vector3(-.3f, 0, -D / 2 + 1.0f), 0, 2.2f, .8f, false, out var kt);
        r.B("FlourDust", r.M(new Color(.95f, .93f, .88f)), new Vector3(-.3f, .84f, 0), new Vector3(1.2f, .01f, .5f), default, false, kt);
        for (int i = 0; i < 3; i++) r.P("Dough", VillageKit.Blob(40 + i, .08f), r.M(new Color(.92f, .85f, .7f)), new Vector3(-.6f + i * .45f, .9f, .05f), new Vector3(.18f, .08f, .14f), default, false, kt);
        r.P("RollingPin", VillageKit.Ring(8, 0), r.M(Wood), new Vector3(.75f, .87f, -.1f), new Vector3(.04f, .55f, .04f), new Vector3(0, 20, 90), false, kt);
        // bread racks along the right wall, full of loaves
        var rack = r.Group("BreadRack", new Vector3(W / 2 - .4f, 0, -D / 2 + 1.2f), -90).transform;
        for (int s = 0; s < 4; s++)
        {
            r.B("RackShelf", r.M(Wood), new Vector3(0, .4f + s * .42f, 0), new Vector3(1.6f, .04f, .45f), default, false, rack);
            for (int i = 0; i < 5; i++) r.P("Loaf", VillageKit.Blob(60 + s * 7 + i, .1f), r.M(Color.Lerp(new Color(.82f, .56f, .28f), new Color(.62f, .38f, .18f), (float)r.rng.NextDouble())), new Vector3(-.6f + i * .3f, .48f + s * .42f, r.R(-.08f, .08f)), new Vector3(.13f, .08f, .09f), new Vector3(0, r.R(-20, 20), 0), false, rack);
        }
        foreach (var x in new[] { -.8f, .8f }) r.B("RackPost", r.M(WoodD), new Vector3(x, .9f, 0), new Vector3(.06f, 1.8f, .45f), default, true, rack);
        // the family table, set for four, with a child's stool and wooden toys underneath
        Table(r, new Vector3(.6f, 0, D * .08f), 90, 1.6f, .95f, true, out var ft);
        for (int i = 0; i < 4; i++) r.P("Bowl", VillageKit.Ring(10, .85f), r.M(new Color(.85f, .82f, .75f)), new Vector3(-.55f + i * .37f, .84f, (i % 2 == 0 ? -.2f : .2f)), new Vector3(.12f, .05f, .12f), default, false, ft);
        r.P("BreadBoard", r.box, r.M(Wood), new Vector3(0, .85f, 0), new Vector3(.4f, .03f, .25f), default, false, ft);
        r.P("BigLoaf", VillageKit.Blob(77, .08f), r.M(new Color(.78f, .5f, .25f)), new Vector3(0, .92f, 0), new Vector3(.17f, .1f, .12f), default, false, ft);
        r.B("Candle", r.M(Linen), new Vector3(.4f, .92f, 0), new Vector3(.05f, .16f, .05f), default, false, ft);
        r.L("CandleLight", new Vector3(.6f, 1.1f, D * .08f + .4f), new Color(1f, .75f, .45f), 2.5f, .55f, true);
        r.P("ToyHorse", VillageKit.Blob(81, .2f), r.M(new Color(.75f, .45f, .3f)), new Vector3(1.6f, .1f, D * .08f + .9f), new Vector3(.12f, .1f, .2f));
        r.P("ToyBall", VillageKit.Blob(82, .02f), r.M(new Color(.4f, .55f, .85f)), new Vector3(1.9f, .08f, D * .08f + .5f), Vector3.one * .08f);
        Rug(r, new Vector3(.6f, 0, D * .08f), new Vector2(1.6f, 1.9f), new Color(.62f, .3f, .24f), new Color(.85f, .72f, .45f));
        // curtained sleeping corner by the door
        Bed(r, new Vector3(W / 2 - .85f, 0, D / 2 - 1.5f), 90, new Color(.55f, .35f, .5f));
        r.B("CurtainRail", r.M(WoodD), new Vector3(W / 2 - 1.75f, H - .5f, D / 2 - 1.5f), new Vector3(.05f, .05f, 2.4f));
        r.P("Curtain", r.boxSoft, r.M(new Color(.72f, .62f, .45f), default, .45f), new Vector3(W / 2 - 1.75f, 1.35f, D / 2 - 2.1f), new Vector3(.05f, 2.2f, 1.1f));
        HangingLantern(r, new Vector3(.6f, H - .9f, D * .08f), new Color(1f, .72f, .42f));
        Barrel(r, new Vector3(-W / 2 + .45f, 0, D / 2 - 1.2f));
        Crate(r, new Vector3(-W / 2 + .5f, 0, D / 2 - 2.0f), 10);
        // baker's extras: a balance scale, a basket of fresh rolls, an apron on the oven peg, flour footprints
        var sc = r.Group("Scale", new Vector3(.45f, .84f, -D / 2 + 1.0f)).transform;
        r.B("ScaleBeam", r.M(new Color(.65f, .5f, .25f)), new Vector3(0, .25f, 0), new Vector3(.4f, .02f, .02f), default, false, sc);
        r.B("ScalePost", r.M(new Color(.65f, .5f, .25f)), new Vector3(0, .12f, 0), new Vector3(.02f, .25f, .02f), default, false, sc);
        foreach (var x in new[] { -.18f, .18f }) r.P("Pan", VillageKit.Ring(10, 0), r.M(new Color(.7f, .55f, .3f)), new Vector3(x, .16f, 0), new Vector3(.07f, .01f, .07f), default, false, sc);
        r.P("RollBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(.6f, .84f, D * .08f - .1f), new Vector3(.16f, .07f, .16f));
        for (int i = 0; i < 5; i++) r.P("Roll", VillageKit.Blob(300 + i, .08f), r.M(new Color(.82f, .56f, .3f)), new Vector3(.6f + r.R(-.08f, .08f), .9f, D * .08f - .1f + r.R(-.08f, .08f)), new Vector3(.06f, .04f, .05f));
        r.P("Apron", r.boxSoft, r.M(new Color(.9f, .88f, .82f), default, .45f), new Vector3(-W / 2 + .2f, 1.25f, -D / 2 + 2.6f), new Vector3(.05f, .7f, .35f));
        for (int i = 0; i < 6; i++) r.B("FlourPrint", r.M(new Color(.92f, .9f, .85f)), new Vector3(-1.2f + i * .3f, .005f, -D / 2 + 2.0f + (i % 2) * .15f), new Vector3(.08f, .005f, .14f), new Vector3(0, 15, 0));
    }

    // ================================================================== 2. the old herbalist's cottage
    static void Herbalist(Room r)
    {
        float W = r.W, D = r.D, H = r.H;
        Window(r, new Vector3(W * .1f, 1.65f, -D / 2 + .14f), Vector3.forward);
        Window(r, new Vector3(W / 2 - .14f, 1.65f, D * .1f), Vector3.left);
        Hearth(r, new Vector3(-W / 2 + .6f, 0, -D * .05f), 90, 1.4f);
        r.P("Cauldron", VillageKit.Blob(11, .03f, 5, 10), r.M(Iron), new Vector3(-W / 2 + 1.25f, .3f, -D * .05f), new Vector3(.3f, .26f, .3f), default, true);
        r.L("CauldronGlow", new Vector3(-W / 2 + 1.3f, .7f, -D * .05f), new Color(.5f, 1f, .55f), 2, .5f, true);
        // a rocking chair by the fire with a knitted shawl, and the cat asleep in her basket
        var rc = r.Group("RockingChair", new Vector3(-W / 2 + 1.75f, 0, D * .25f), 210).transform;
        r.B("Seat", r.M(Wood), new Vector3(0, .45f, 0), new Vector3(.5f, .06f, .5f), default, true, rc);
        r.B("Back", r.M(WoodD), new Vector3(0, .9f, -.24f), new Vector3(.5f, .8f, .06f), new Vector3(-10, 0, 0), false, rc);
        foreach (var x in new[] { -.22f, .22f }) r.P("Rocker", VillageKit.Ring(12, .9f), r.M(WoodD), new Vector3(x, .25f, 0), new Vector3(.5f, .04f, .5f), new Vector3(0, 0, 90), false, rc);
        r.P("Shawl", r.boxSoft, r.M(new Color(.55f, .28f, .3f), default, .5f), new Vector3(0, .95f, -.2f), new Vector3(.55f, .45f, .08f), new Vector3(-10, 0, 0), false, rc);
        r.P("CatBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(-W / 2 + 1.1f, 0, D * .3f + .6f), new Vector3(.32f, .12f, .32f));
        r.P("Cat", VillageKit.Blob(14, .1f), r.M(new Color(.25f, .22f, .22f)), new Vector3(-W / 2 + 1.1f, .14f, D * .3f + .6f), new Vector3(.2f, .09f, .16f));
        r.P("CatHead", VillageKit.Blob(15, .05f), r.M(new Color(.25f, .22f, .22f)), new Vector3(-W / 2 + 1.25f, .17f, D * .3f + .55f), Vector3.one * .08f);
        // herbs drying from every beam
        for (int row = 0; row < 3; row++)
        {
            r.B("HerbLine", r.M(VillageKit.Wicker), new Vector3(0, H - .35f, -D / 2 + 1f + row * 1.1f), new Vector3(W - .6f, .02f, .02f));
            for (int i = 0; i < 9; i++)
            {
                float x = -W / 2 + .55f + i * (W - 1.1f) / 8f;
                Color hc = Color.Lerp(new Color(.38f, .55f, .3f), new Color(.6f, .55f, .3f), (float)r.rng.NextDouble());
                if (i % 4 == 3) hc = new Color(.62f, .45f, .7f);   // lavender
                for (int k = 0; k < 3; k++) r.P("DryingHerb", VillageKit.Leaf(), r.M(hc), new Vector3(x + r.R(-.05f, .05f), H - .45f, -D / 2 + 1f + row * 1.1f), Vector3.one * r.R(.18f, .28f), new Vector3(170 + r.R(-15, 15), k * 120, 0));
            }
        }
        // the work table: mortar & pestle, open book of remedies, a candle stub
        Table(r, new Vector3(W * .12f, 0, -D / 2 + .85f), 0, 1.6f, .7f, false, out var wt);
        r.P("Mortar", VillageKit.Ring(10, .7f), r.M(Stone), new Vector3(-.4f, .85f, 0), new Vector3(.13f, .1f, .13f), default, false, wt);
        r.P("Pestle", VillageKit.Ring(6, 0), r.M(Stone * .8f), new Vector3(-.38f, .95f, .02f), new Vector3(.025f, .2f, .025f), new Vector3(20, 0, 25), false, wt);
        r.B("OpenBookL", r.M(new Color(.92f, .88f, .78f)), new Vector3(.25f, .85f, 0), new Vector3(.22f, .02f, .3f), new Vector3(0, 0, 6), false, wt);
        r.B("OpenBookR", r.M(new Color(.92f, .88f, .78f)), new Vector3(.47f, .85f, 0), new Vector3(.22f, .02f, .3f), new Vector3(0, 0, -6), false, wt);
        for (int i = 0; i < 5; i++) r.P("Petal", VillageKit.Leaf(), r.M(new Color(.85f, .55f, .7f)), new Vector3(-.1f + i * .06f, .84f, .2f), Vector3.one * .06f, new Vector3(-80, i * 70, 0), false, wt);
        r.B("CandleStub", r.M(Linen), new Vector3(.7f, .88f, -.2f), new Vector3(.05f, .08f, .05f), default, false, wt);
        r.L("CandleLight", new Vector3(W * .12f + .7f, 1.05f, -D / 2 + .65f), new Color(1f, .75f, .45f), 2.5f, .55f, true);
        // shelves of tinctures and jars along the right wall
        Shelves(r, new Vector3(W / 2 - .3f, 0, -D * .15f), -90, 4, 1.8f, (g, lp) => { if (r.rng.NextDouble() < .75) Jar(r, g, lp); else Book(r, g, lp); });
        Bed(r, new Vector3(W / 2 - .7f, 0, D / 2 - 1.25f), 180, new Color(.4f, .5f, .45f), true);
        Rug(r, new Vector3(-W * .1f, 0, D * .15f), new Vector2(1.3f, 1.1f), new Color(.45f, .5f, .38f), new Color(.75f, .65f, .45f));
        r.P("Broom", VillageKit.Ring(6, 0), r.M(Wood), new Vector3(-W / 2 + .3f, .7f, D / 2 - .6f), new Vector3(.03f, 1.4f, .03f), new Vector3(0, 0, 10));
        r.P("BroomHead", VillageKit.Blob(19, .1f), r.M(new Color(.8f, .68f, .4f)), new Vector3(-W / 2 + .22f, .15f, D / 2 - .6f), new Vector3(.12f, .18f, .08f));
        // herbalist's extras: a hanging glass charm catching the firelight, stacked books on the floor, candle cluster,
        // potted herbs along the window, a knitting basket by the rocking chair
        r.P("CharmString", r.box, r.M(VillageKit.Wicker), new Vector3(W * .1f, H - .55f, -D / 2 + .5f), new Vector3(.01f, .35f, .01f));
        r.P("Charm", VillageKit.Blob(400, .3f, 3, 6), r.M(new Color(.6f, .85f, .9f), new Color(.15f, .3f, .35f)), new Vector3(W * .1f, H - .8f, -D / 2 + .5f), Vector3.one * .06f);
        for (int i = 0; i < 5; i++) r.B("FloorBook", r.M(new Color(r.R(.3f, .65f), r.R(.2f, .4f), r.R(.2f, .45f))), new Vector3(W / 2 - .45f, .04f + i * .07f, -D / 2 + .55f), new Vector3(.28f, .06f, .2f), new Vector3(0, r.R(-20, 20), 0));
        for (int i = 0; i < 3; i++) r.B("Candle", r.M(Linen), new Vector3(W * .12f + .55f + i * .07f, .84f + .04f * i, -D / 2 + .7f), new Vector3(.04f, .08f + i * .05f, .04f));
        for (int i = 0; i < 3; i++)
        {
            Vector3 pp = new Vector3(W * .1f - .35f + i * .35f, 1.2f, -D / 2 + .3f);
            r.P("HerbPot", VillageKit.Ring(8, .8f), r.M(new Color(.66f, .4f, .26f)), pp, new Vector3(.08f, .1f, .08f));
            for (int k = 0; k < 4; k++) r.P("Sprig", VillageKit.Leaf(), r.M(new Color(.35f, .6f, .35f)), pp + Vector3.up * .1f, Vector3.one * .13f, new Vector3(-60, k * 90, 0));
        }
        r.P("KnitBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(-W / 2 + 2.25f, 0, D * .25f + .2f), new Vector3(.18f, .12f, .18f));
        r.P("Yarn", VillageKit.Blob(410, .05f), r.M(new Color(.55f, .28f, .3f), default, .5f), new Vector3(-W / 2 + 2.25f, .15f, D * .25f + .2f), Vector3.one * .08f);
        r.B("Needles", r.M(Wood), new Vector3(-W / 2 + 2.25f, .2f, D * .25f + .2f), new Vector3(.01f, .01f, .25f), new Vector3(30, 20, 0));
    }

    // ================================================================== 3. the weaver's house (two storeys)
    static void Weaver(Room r)
    {
        float W = r.W, D = r.D, H = r.H;
        Window(r, new Vector3(-W / 2 + .14f, 1.7f, D * .15f), Vector3.right);
        Window(r, new Vector3(W / 2 - .14f, 1.7f, D * .2f), Vector3.left);
        Hearth(r, new Vector3(W * .1f, 0, -D / 2 + .55f), 0, 1.5f);
        // the great loom under the window, a half-woven blue cloth on it
        var loom = r.Group("Loom", new Vector3(-W / 2 + 1.1f, 0, D * .05f), 90).transform;
        var wood = r.M(Wood); var woodD = r.M(WoodD);
        foreach (var x in new[] { -.8f, .8f }) foreach (var z in new[] { -.5f, .5f }) r.B("LoomPost", woodD, new Vector3(x, .9f, z), new Vector3(.1f, 1.8f, .1f), default, z < 0, loom);
        r.B("Beam1", wood, new Vector3(0, 1.75f, -.5f), new Vector3(1.7f, .1f, .1f), default, false, loom);
        r.B("Beam2", wood, new Vector3(0, .75f, .5f), new Vector3(1.7f, .1f, .1f), default, false, loom);
        r.B("Bench", woodD, new Vector3(0, .45f, 1.0f), new Vector3(1.2f, .07f, .35f), default, true, loom);
        r.B("Warp", r.M(new Color(.85f, .82f, .75f), default, .5f), new Vector3(0, 1.25f, 0), new Vector3(1.5f, .01f, 1.0f), new Vector3(-63, 0, 0), false, loom);
        r.B("Cloth", r.M(new Color(.3f, .42f, .7f), default, .5f), new Vector3(0, .9f, .3f), new Vector3(1.5f, .01f, .45f), new Vector3(-63, 0, 0), false, loom);
        // spinning wheel and baskets of wool
        var sw = r.Group("SpinningWheel", new Vector3(-W / 2 + 1.0f, 0, D / 2 - 1.4f), 40).transform;
        r.P("Wheel", VillageKit.Ring(16, .9f), wood, new Vector3(0, .75f, 0), new Vector3(.45f, .05f, .45f), new Vector3(0, 0, 90), false, sw);
        r.B("Stand", woodD, new Vector3(0, .35f, 0), new Vector3(.12f, .7f, .7f), default, true, sw);
        r.P("Spindle", VillageKit.Ring(6, 0), r.M(new Color(.9f, .85f, .75f)), new Vector3(0, .65f, .45f), new Vector3(.05f, .18f, .05f), new Vector3(90, 0, 0), false, sw);
        Color[] wool = { new Color(.85f, .82f, .75f), new Color(.75f, .35f, .3f), new Color(.35f, .45f, .72f), new Color(.85f, .7f, .35f), new Color(.4f, .6f, .4f) };
        for (int b = 0; b < 3; b++)
        {
            Vector3 bp = new Vector3(-W / 2 + .5f + b * .55f, 0, D / 2 - .5f);
            r.P("WoolBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), bp, new Vector3(.25f, .3f, .25f));
            for (int k = 0; k < 4; k++) r.P("Yarn", VillageKit.Blob(90 + b * 4 + k, .05f), r.M(wool[(b + k) % wool.Length], default, .5f), bp + new Vector3(r.R(-.1f, .1f), .3f + k * .03f, r.R(-.1f, .1f)), Vector3.one * .1f);
        }
        // bolts of finished cloth stacked on a rack
        var bolts = r.Group("ClothBolts", new Vector3(W / 2 - .45f, 0, -D * .2f), -90).transform;
        for (int s = 0; s < 3; s++)
        {
            r.B("Board", wood, new Vector3(0, .35f + s * .55f, 0), new Vector3(1.6f, .05f, .5f), default, false, bolts);
            for (int i = 0; i < 4; i++) r.P("Bolt", VillageKit.Ring(10, 0), r.M(wool[(s * 4 + i) % wool.Length], default, .5f), new Vector3(-.55f + i * .37f, .5f + s * .55f, 0), new Vector3(.15f, .45f, .15f), new Vector3(90, 0, 0), false, bolts);
        }
        foreach (var x in new[] { -.8f, .8f }) r.B("RackPost", woodD, new Vector3(x, .8f, 0), new Vector3(.06f, 1.6f, .5f), default, true, bolts);
        Table(r, new Vector3(W * .15f, 0, D * .12f), 0, 1.8f, .9f, true, out var t);
        r.B("Shears", r.M(Iron), new Vector3(.3f, .84f, .1f), new Vector3(.2f, .02f, .05f), new Vector3(0, 30, 0), false, t);
        r.B("Pattern", r.M(new Color(.9f, .86f, .76f)), new Vector3(-.3f, .84f, 0), new Vector3(.5f, .01f, .4f), new Vector3(0, -8, 0), false, t);
        Rug(r, new Vector3(W * .15f, 0, D * .12f), new Vector2(1.9f, 1.5f), new Color(.3f, .42f, .7f), new Color(.85f, .72f, .45f));
        HangingLantern(r, new Vector3(W * .15f, H - .9f, D * .12f), new Color(1f, .72f, .42f));
        // stairs up to the loft bedroom
        Vector3 stair = new Vector3(W / 2 - .65f, 0, D / 2 - 1.0f); int steps = 10;
        for (int i = 0; i < steps; i++) r.B("Step", r.M(Plank), stair + new Vector3(0, (i + .5f) * H / steps, -i * .3f), new Vector3(.9f, .08f, .32f), default, true);
        r.B("Loft", r.M(PlankD), new Vector3(0, H, -D * .3f), new Vector3(W - .3f, .14f, D * .4f), default, true);
        r.B("LoftRail", r.M(Beam), new Vector3(-.3f, H + .5f, -D * .1f + .05f), new Vector3(W - 1.6f, .08f, .08f));
        for (int i = 0; i < 7; i++) r.B("Baluster", r.M(Beam), new Vector3(-W / 2 + .7f + i * (W - 2f) / 6f, H + .25f, -D * .1f + .05f), new Vector3(.06f, .5f, .06f));
        Bed(r, new Vector3(-W / 4, H + .07f, -D * .32f), 90, new Color(.75f, .35f, .3f));
        Bed(r, new Vector3(W / 4, H + .07f, -D * .32f), 90, new Color(.35f, .45f, .72f), true);
        r.L("LoftLamp", new Vector3(0, H + 1.2f, -D * .3f), new Color(1f, .7f, .45f), 4, .8f, true);
        // weaver's extras: tapestries on the walls, a dress form wearing a half-finished dress, a sewing basket
        r.P("TapestryL", r.boxSoft, r.M(new Color(.62f, .32f, .3f), default, .55f), new Vector3(-W / 2 + .16f, 1.7f, -D * .25f), new Vector3(.03f, 1.1f, 1.3f));
        r.P("TapestryBorderL", r.box, r.M(new Color(.85f, .7f, .4f)), new Vector3(-W / 2 + .15f, 1.7f, -D * .25f), new Vector3(.02f, 1.18f, 1.38f));
        r.P("TapestryB", r.boxSoft, r.M(new Color(.3f, .42f, .6f), default, .55f), new Vector3(-W * .25f, 1.75f, -D / 2 + .16f), new Vector3(1.1f, 1.0f, .03f));
        var df = r.Group("DressForm", new Vector3(W / 2 - 1.2f, 0, D * .1f), -30).transform;
        r.B("Stand", r.M(WoodD), new Vector3(0, .5f, 0), new Vector3(.05f, 1f, .05f), default, false, df);
        r.B("Foot", r.M(WoodD), new Vector3(0, .03f, 0), new Vector3(.4f, .05f, .4f), default, false, df);
        r.P("Torso", VillageKit.Blob(500, .04f, 4, 10), r.M(new Color(.85f, .78f, .65f)), new Vector3(0, 1.3f, 0), new Vector3(.22f, .3f, .15f), default, false, df);
        r.P("Dress", VillageKit.Ring(12, .9f), r.M(new Color(.35f, .5f, .75f), default, .5f), new Vector3(0, .55f, 0), new Vector3(.3f, .6f, .25f), default, false, df);
        r.P("SewingBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(W * .15f + .7f, .84f, D * .12f + .25f), new Vector3(.14f, .07f, .14f));
        for (int i = 0; i < 4; i++) r.P("Spool", VillageKit.Ring(8, 0), r.M(new Color(r.R(.3f, .9f), r.R(.3f, .8f), r.R(.3f, .8f))), new Vector3(W * .15f + .65f + i * .04f, .92f, D * .12f + .25f), new Vector3(.025f, .04f, .025f));
    }

    // ================================================================== 4. the fisherman's house (two storeys)
    static void Fisher(Room r)
    {
        float W = r.W, D = r.D, H = r.H;
        Window(r, new Vector3(-W / 2 + .14f, 1.7f, -D * .1f), Vector3.right);
        Window(r, new Vector3(W * .15f, 1.7f, -D / 2 + .14f), Vector3.forward);
        Hearth(r, new Vector3(W / 2 - .55f, 0, -D * .1f), -90, 1.3f);
        r.P("Kettle", VillageKit.Blob(31, .03f, 4, 10), r.M(Iron), new Vector3(W / 2 - 1.05f, 1.35f, -D * .1f), new Vector3(.15f, .13f, .15f));
        // nets draped across the left wall with cork floats, crossed oars above the door
        var net = r.M(new Color(.5f, .55f, .52f), default, .45f);
        var n1 = r.B("WallNet", net, new Vector3(-W / 2 + .2f, 1.6f, D * .25f), new Vector3(.03f, 1.4f, 1.8f), new Vector3(0, 0, -6)); n1.AddComponent<SheetBillow>();
        for (int i = 0; i < 6; i++) r.P("Float", VillageKit.Blob(50 + i, .05f), r.M(new Color(.9f, .55f, .25f)), new Vector3(-W / 2 + .25f, 2.25f, D * .25f - .8f + i * .32f), Vector3.one * .07f);
        r.B("OarA", r.M(Wood), new Vector3(-.2f, H - .55f, D / 2 - .2f), new Vector3(1.6f, .06f, .04f), new Vector3(0, 0, 20));
        r.B("OarB", r.M(Wood), new Vector3(.2f, H - .55f, D / 2 - .2f), new Vector3(1.6f, .06f, .04f), new Vector3(0, 0, -20));
        // a half-built little boat on trestles - the winter's project
        var boat = r.Group("BoatProject", new Vector3(-W * .1f, 0, D * .05f), 15).transform;
        foreach (var z in new[] { -.6f, .6f }) r.B("Trestle", r.M(WoodD), new Vector3(0, .3f, z), new Vector3(.9f, .6f, .08f), default, true, boat);
        r.P("Hull", VillageKit.Blob(61, .02f, 4, 10), r.M(new Color(.55f, .4f, .27f)), new Vector3(0, .8f, 0), new Vector3(.45f, .25f, 1.15f), default, false, boat);
        for (int i = 0; i < 5; i++) r.P("Rib", VillageKit.Ring(12, .88f), r.M(WoodD), new Vector3(0, .85f, -.8f + i * .4f), new Vector3(.47f, .03f, .3f), new Vector3(90, 0, 0), false, boat);
        r.B("Plane", r.M(Wood), new Vector3(.4f, .64f, .9f), new Vector3(.08f, .06f, .22f), default, false, boat);
        for (int i = 0; i < 6; i++) r.P("Shaving", VillageKit.Leaf(), r.M(new Color(.85f, .72f, .5f)), new Vector3(r.R(-.5f, .5f), .01f, r.R(-.8f, .8f)), Vector3.one * .08f, new Vector3(-85, r.R(0, 360), 0), false, boat);
        // barrels of salted fish, a basket of the day's catch
        Barrel(r, new Vector3(-W / 2 + .45f, 0, -D / 2 + .5f));
        Barrel(r, new Vector3(-W / 2 + 1.0f, 0, -D / 2 + .45f), .85f);
        var bk = r.P("CatchBasket", VillageKit.Ring(10, .85f), r.M(VillageKit.Wicker), new Vector3(-W / 2 + .6f, 0, -D / 2 + 1.25f), new Vector3(.3f, .2f, .3f));
        for (int i = 0; i < 4; i++) r.P("Fish", VillageKit.Blob(70 + i, .03f, 3, 6), r.M(new Color(.7f, .75f, .8f), new Color(.05f, .06f, .08f)), new Vector3(-W / 2 + .55f + r.R(-.1f, .1f), .2f, -D / 2 + 1.25f + r.R(-.1f, .1f)), new Vector3(.05f, .03f, .16f), new Vector3(0, r.R(0, 180), 0));
        Table(r, new Vector3(W * .2f, 0, D * .3f), 90, 1.3f, .8f, true, out var t);
        r.P("Map", r.box, r.M(new Color(.86f, .8f, .64f)), new Vector3(0, .84f, 0), new Vector3(.6f, .01f, .45f), new Vector3(0, 12, 0), false, t);
        r.P("Compass", VillageKit.Ring(10, 0), r.M(new Color(.7f, .55f, .3f)), new Vector3(.2f, .86f, .1f), new Vector3(.06f, .02f, .06f), default, false, t);
        r.P("Bottle", VillageKit.Ring(8, 0), r.M(new Color(.3f, .5f, .45f), new Color(.02f, .05f, .04f)), new Vector3(-.3f, .95f, -.1f), new Vector3(.05f, .2f, .05f), default, false, t);
        HangingLantern(r, new Vector3(W * .2f, H - .9f, D * .3f), new Color(1f, .78f, .5f));
        Rug(r, new Vector3(W * .2f, 0, D * .3f), new Vector2(1.3f, 1.2f), new Color(.3f, .4f, .5f), new Color(.8f, .75f, .6f));
        // ladder to the hammock loft
        for (int i = 0; i < 9; i++) r.B("Rung", r.M(Wood), new Vector3(W / 2 - .6f, .3f + i * .3f, D / 2 - 1.6f), new Vector3(.6f, .05f, .05f));
        foreach (var x in new[] { -.3f, .3f }) r.B("LadderRail", r.M(WoodD), new Vector3(W / 2 - .6f + x, H / 2 + .1f, D / 2 - 1.6f), new Vector3(.06f, H + .3f, .06f));
        r.B("Loft", r.M(PlankD), new Vector3(0, H, -D * .3f), new Vector3(W - .3f, .14f, D * .4f), default, true);
        r.P("Hammock", r.boxSoft, r.M(new Color(.75f, .7f, .55f), default, .5f), new Vector3(0, H + .7f, -D * .3f), new Vector3(1.9f, .12f, .7f), new Vector3(0, 0, 3));
        foreach (var x in new[] { -1.15f, 1.15f }) r.B("HammockPost", r.M(WoodD), new Vector3(x, H + .6f, -D * .3f), new Vector3(.08f, 1.2f, .08f));
        r.L("LoftLamp", new Vector3(0, H + 1.2f, -D * .25f), new Color(1f, .75f, .5f), 4, .8f, true);
        // fisherman's extras: a trophy fish on a plaque, an anchor by the door, coils of rope, a ship in a bottle, sea chest
        r.B("Plaque", r.M(WoodD), new Vector3(-W / 2 + .16f, 2.1f, -D * .3f), new Vector3(.04f, .35f, .7f));
        r.P("TrophyFish", VillageKit.Blob(600, .03f, 3, 7), r.M(new Color(.55f, .65f, .7f), new Color(.04f, .05f, .06f)), new Vector3(-W / 2 + .2f, 2.1f, -D * .3f), new Vector3(.06f, .12f, .3f));
        var an = r.Group("Anchor", new Vector3(1.05f, 0, D / 2 - .4f), 20).transform;
        r.B("Shank", r.M(Iron), new Vector3(0, .45f, 0), new Vector3(.06f, .9f, .06f), default, false, an);
        r.P("Arms", VillageKit.Ring(12, .8f), r.M(Iron), new Vector3(0, .15f, 0), new Vector3(.32f, .05f, .32f), new Vector3(90, 0, 0), false, an);
        r.P("Ring", VillageKit.Ring(10, .7f), r.M(Iron), new Vector3(0, .95f, 0), new Vector3(.08f, .02f, .08f), new Vector3(90, 0, 0), false, an);
        for (int i = 0; i < 2; i++) r.P("RopeCoil", VillageKit.Ring(14, .55f), r.M(VillageKit.Wicker), new Vector3(-W / 2 + .5f, .05f + i * .1f, D / 2 - .5f), new Vector3(.28f - i * .04f, .1f, .28f - i * .04f));
        r.P("ShipBottle", VillageKit.Ring(10, 0), r.M(new Color(.4f, .6f, .55f), new Color(.03f, .06f, .05f)), new Vector3(W * .2f + .35f, .9f, D * .3f - .2f), new Vector3(.05f, .14f, .05f), new Vector3(0, 0, 90));
        r.B("SeaChest", r.M(new Color(.4f, .3f, .22f)), new Vector3(-W / 2 + .6f, .25f, D * .02f), new Vector3(.5f, .5f, .9f), default, true);
        r.B("ChestBands", r.M(Iron), new Vector3(-W / 2 + .6f, .3f, D * .02f), new Vector3(.52f, .06f, .92f));
    }
}
