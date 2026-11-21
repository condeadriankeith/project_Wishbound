using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wishbound village dressing kit: bold, chamfered, low-poly pieces that read from the high iso camera and pick
/// up the Star's light (all Ghibli/ToonLit, so additional point lights band nicely).
/// Every builder returns a root GameObject placed on the ground at the given position.
///   Well, FarmPlot (+ cabbages / pumpkins / bean trellises), Fence, StoneWall, Path (with puddles),
///   LanternPost, RainBarrel, Woodpile, Basket, Pond (+ koi), Wildflowers.
/// </summary>
public static partial class VillageKit
{
    // ---------------------------------------------------------------- palette (soft, low-contrast painted tones)
    public static readonly Color Stone = new Color(.56f, .55f, .52f), StoneDark = new Color(.38f, .38f, .38f), Moss = new Color(.36f, .5f, .26f),
        Timber = new Color(.45f, .32f, .22f), TimberDark = new Color(.3f, .21f, .15f), Soil = new Color(.27f, .19f, .14f), SoilWet = new Color(.2f, .15f, .12f),
        Mud = new Color(.42f, .33f, .24f), Cabbage = new Color(.5f, .72f, .36f), CabbageDark = new Color(.33f, .55f, .27f), Pumpkin = new Color(.9f, .52f, .18f),
        Vine = new Color(.3f, .52f, .24f), Bamboo = new Color(.72f, .64f, .4f), Water = new Color(.1f, .32f, .38f), WaterDeep = new Color(.06f, .14f, .22f),
        Wicker = new Color(.66f, .5f, .3f), Iron = new Color(.16f, .15f, .17f);

    static Shader toon;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    public static Material Mat(Color c, Color emission = default, float brush = .22f, float rim = .14f)
    {
        string key = c.ToString() + emission + brush + rim;
        if (mats.TryGetValue(key, out var m) && m) return m;
        if (!toon) toon = Shader.Find("Ghibli/ToonLit");
        m = new Material(toon) { name = "Kit " + key };
        m.SetColor("_Color", c); m.SetColor("_InstColor", Color.white); m.SetColor("_EmissionColor", emission);
        m.SetFloat("_BrushStrength", brush); m.SetFloat("_RimStrength", rim); m.enableInstancing = true;
        mats[key] = m; return m;
    }

    /// <summary>Shared Wishbound/ToonWater material (Resources/WishboundToonWater) - radial depth for flat discs.</summary>
    public static Material WaterMat(bool radial, float depthRange)
    {
        string key = "water" + radial + depthRange;
        if (mats.TryGetValue(key, out var m) && m) return m;
        var src = Resources.Load<Material>("WishboundToonWater");
        m = src ? new Material(src) : Mat(Water);
        if (src) { m.SetFloat("_RadialDepth", radial ? 1 : 0); m.SetFloat("_DepthRange", depthRange); }
        mats[key] = m; return m;
    }

    // ---------------------------------------------------------------- ground helpers
    public static int GroundMask = ~0;
    public static Vector3 Ground(Vector3 p)
    {
        if(IslandBuilder.HGrid != null) {p.y=TerrainMath.Hm(p.x,TerrainMath.MirrorZ?-p.z:p.z);return p;}
        foreach(var hit in Physics.RaycastAll(p+Vector3.up*80,Vector3.down,200,GroundMask,QueryTriggerInteraction.Ignore))
            if(hit.collider.GetComponent<IslandBuilder>()) return hit.point;
        return p;
    }
    public static Vector3 GroundNormal(Vector3 p)
    {
        float l=Ground(p-Vector3.right*.3f).y,r=Ground(p+Vector3.right*.3f).y;
        float b=Ground(p-Vector3.forward*.3f).y,f=Ground(p+Vector3.forward*.3f).y;
        return new Vector3(l-r,.6f,b-f).normalized;
    }

    static GameObject Root(string name, Vector3 pos, Transform parent)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = Ground(pos);
        g.AddComponent<VillageKitPiece>(); return g;
    }
    static GameObject Part(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, bool collider = false)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos; g.transform.localRotation = localRot; g.transform.localScale = scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) { var c = g.AddComponent<MeshCollider>(); c.sharedMesh = mesh; c.convex = true; }
        return g;
    }

    // ---------------------------------------------------------------- meshes
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    /// <summary>Unit box (-.5..+.5) with chamfered edges so the rims catch the light.</summary>
    public static Mesh ChamferBox(float bevel = .12f)
    {
        string key = "cb" + bevel; if (meshes.TryGetValue(key, out var m)) return m;
        float a = .5f, b = .5f - bevel;
        var v = new List<Vector3>(); var t = new List<int>();
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2) { int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); t.AddRange(new[] { i, i + 1, i + 2 }); }
        // six faces (inset), twelve bevel strips, eight corner triangles - built from the 24 "face corner" points
        Vector3 P(int ax, int sx, int sy, int sz)
        {
            // a point on face 'ax' (0=x,1=y,2=z) of sign sx/sy/sz, inset on the other two axes
            float x = ax == 0 ? a * sx : b * sx, y = ax == 1 ? a * sy : b * sy, z = ax == 2 ? a * sz : b * sz; return new Vector3(x, y, z);
        }
        int[] s = { -1, 1 };
        // faces
        foreach (int sx in s) { Quad(P(0, sx, -1, -1), P(0, sx, sx > 0 ? 1 : -1, sx > 0 ? -1 : 1), P(0, sx, 1, 1), P(0, sx, sx > 0 ? -1 : 1, sx > 0 ? 1 : -1)); }
        foreach (int sy in s) { Quad(P(1, -1, sy, -1), P(1, sy > 0 ? -1 : 1, sy, sy > 0 ? 1 : -1), P(1, 1, sy, 1), P(1, sy > 0 ? 1 : -1, sy, sy > 0 ? -1 : 1)); }
        foreach (int sz in s) { Quad(P(2, -1, -1, sz), P(2, sz > 0 ? 1 : -1, sz > 0 ? -1 : 1, sz), P(2, 1, 1, sz), P(2, sz > 0 ? -1 : 1, sz > 0 ? 1 : -1, sz)); }
        // edge bevels
        foreach (int sx in s) foreach (int sy in s) Quad(P(0, sx, sy, -1), P(1, sx, sy, -1), P(1, sx, sy, 1), P(0, sx, sy, 1));
        foreach (int sy in s) foreach (int sz in s) Quad(P(1, -1, sy, sz), P(2, -1, sy, sz), P(2, 1, sy, sz), P(1, 1, sy, sz));
        foreach (int sx in s) foreach (int sz in s) Quad(P(0, sx, -1, sz), P(2, sx, -1, sz), P(2, sx, 1, sz), P(0, sx, 1, sz));
        foreach (int sx in s) foreach (int sy in s) foreach (int sz in s) Tri(P(0, sx, sy, sz), P(1, sx, sy, sz), P(2, sx, sy, sz));
        // fix winding so every triangle faces outward
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2)) < 0) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
        }
        m = new Mesh { name = "Chamfer box" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        meshes[key] = m; return m;
    }

    /// <summary>Faceted, slightly irregular rock / cobble / cabbage-core blob (unit radius).</summary>
    public static Mesh Blob(int seed, float lumpy = .18f, int rings = 4, int segs = 7)
    {
        string key = "blob" + seed + lumpy + rings + segs; if (meshes.TryGetValue(key, out var m)) return m;
        var rng = new System.Random(seed);
        var v = new List<Vector3>(); var t = new List<int>();
        var grid = new Vector3[rings + 1, segs];
        for (int r = 0; r <= rings; r++)
            for (int sgi = 0; sgi < segs; sgi++)
            {
                float th = Mathf.PI * r / rings, ph = Mathf.PI * 2 * sgi / segs;
                float k = 1 + ((float)rng.NextDouble() - .5f) * 2 * lumpy;
                grid[r, sgi] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * k;
            }
        for (int r = 0; r < rings; r++)
            for (int sgi = 0; sgi < segs; sgi++)
            {
                int n = (sgi + 1) % segs;
                Vector3 a = grid[r, sgi], b = grid[r, n], c = grid[r + 1, sgi], d = grid[r + 1, n];
                int i = v.Count; v.Add(a); v.Add(c); v.Add(b); t.AddRange(new[] { i, i + 1, i + 2 });
                i = v.Count; v.Add(b); v.Add(c); v.Add(d); t.AddRange(new[] { i, i + 1, i + 2 });
            }
        m = new Mesh { name = "Blob" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        meshes[key] = m; return m;
    }

    /// <summary>Faceted cylinder / ring (outer radius 1, height 1, inner radius 'hole', 0 = solid).</summary>
    public static Mesh Ring(int segs, float hole)
    {
        string key = "ring" + segs + hole; if (meshes.TryGetValue(key, out var m)) return m;
        var v = new List<Vector3>(); var t = new List<int>();
        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        for (int s = 0; s < segs; s++)
        {
            float a0 = s * Mathf.PI * 2 / segs, a1 = (s + 1) * Mathf.PI * 2 / segs;
            Vector3 o0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), o1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Q(o0, o0 + Vector3.up, o1 + Vector3.up, o1);                                   // outer wall
            if (hole > 0)
            {
                Q(o1 * hole, o1 * hole + Vector3.up, o0 * hole + Vector3.up, o0 * hole);   // inner wall
                Q(o0 + Vector3.up, o0 * hole + Vector3.up, o1 * hole + Vector3.up, o1 + Vector3.up); // top
            }
            else { int i = v.Count; v.Add(Vector3.up); v.Add(o1 + Vector3.up); v.Add(o0 + Vector3.up); t.AddRange(new[] { i, i + 1, i + 2 }); }
        }
        m = new Mesh { name = "Ring" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        meshes[key] = m; return m;
    }

    /// <summary>Flat irregular disc (for puddles, water surfaces, soil beds) - unit radius, lying in XZ.</summary>
    public static Mesh Disc(int seed, float wobble = .25f, int segs = 14)
    {
        string key = "disc" + seed + wobble + segs; if (meshes.TryGetValue(key, out var m)) return m;
        var rng = new System.Random(seed);
        var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
        float p1 = (float)rng.NextDouble() * 6, p2 = (float)rng.NextDouble() * 6;
        for (int s = 0; s < segs; s++)
        {
            float a = s * Mathf.PI * 2 / segs;
            float r = 1 + Mathf.Sin(a * 2 + p1) * wobble * .6f + Mathf.Sin(a * 3 + p2) * wobble * .4f;
            v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
        }
        for (int s = 0; s < segs; s++) t.AddRange(new[] { 0, (s + 1) % segs + 1, s + 1 });
        m = new Mesh { name = "Disc" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        meshes[key] = m; return m;
    }

    /// <summary>Broad painted leaf card (double sided) for cabbages and vines.</summary>
    public static Mesh Leaf()
    {
        if (meshes.TryGetValue("leaf", out var m)) return m;
        var v = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(-.45f, .08f, .45f), new Vector3(0, .18f, 1), new Vector3(.45f, .08f, .45f) };
        var t = new List<int> { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
        m = new Mesh { name = "Leaf" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        meshes["leaf"] = m; return m;
    }

    static float R(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

    // ================================================================ pieces

    /// <summary>Round cobble well with timber posts, a pitched shingle roof, windlass + bucket and a lily-pad water surface.
    /// Returns the root; the water surface is child "Water" (glow it for the star fragment).</summary>
    static GameObject LegacyWell(Vector3 pos, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("Well", pos, parent); var tr = root.transform;
        var stone = Mat(Stone); var dark = Mat(StoneDark); var moss = Mat(Moss); var wood = Mat(Timber); var woodD = Mat(TimberDark);
        // flagstone apron
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2 / 16 + R(rng, -.1f, .1f), r = R(rng, 1.7f, 2.4f);
            Part("Flag", Disc(seed * 31 + i, .2f, 7), i % 3 == 0 ? dark : stone, tr, new Vector3(Mathf.Cos(a) * r, .02f, Mathf.Sin(a) * r), Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(.45f, 1, .38f));
        }
        // the cobbled ring: chunky irregular stones in two courses + cap stones
        Part("WellCore", Ring(14, .78f), dark, tr, Vector3.zero, Quaternion.identity, new Vector3(1.05f, .85f, 1.05f), true);
        for (int course = 0; course < 2; course++)
            for (int i = 0; i < 12; i++)
            {
                float a = (i + course * .5f) * Mathf.PI * 2 / 12;
                Part("Cobble", Blob(seed * 7 + i + course * 20, .2f), (i + course) % 4 == 0 ? moss : stone, tr,
                     new Vector3(Mathf.Cos(a) * 1.02f, .2f + course * .38f, Mathf.Sin(a) * 1.02f), Quaternion.Euler(R(rng, -10, 10), -a * Mathf.Rad2Deg, 0), new Vector3(.28f, .2f, .22f));
            }
        Part("Cap", Ring(16, .74f), stone, tr, new Vector3(0, .82f, 0), Quaternion.identity, new Vector3(1.18f, .1f, 1.18f));
        // dark water with lily pads
        var water = Part("Water", Disc(seed, .05f, 16), WaterMat(true, 1.6f), tr, new Vector3(0, .6f, 0), Quaternion.identity, new Vector3(.8f, 1, .8f));
        for (int i = 0; i < 4; i++)
        {
            float a = R(rng, 0, 6.28f), r = R(rng, .15f, .5f);
            Part("LilyPad", Disc(seed + 90 + i, .05f, 9), Mat(new Color(.38f, .6f, .3f)), tr, new Vector3(Mathf.Cos(a) * r, .61f, Mathf.Sin(a) * r), Quaternion.Euler(0, R(rng, 0, 360), 0), Vector3.one * R(rng, .1f, .16f));
        }
        // posts, roof, windlass, bucket
        var box = ChamferBox(.18f);
        Part("PostL", box, wood, tr, new Vector3(-1.05f, 1.45f, 0), Quaternion.identity, new Vector3(.18f, 2.0f, .18f));
        Part("PostR", box, wood, tr, new Vector3(1.05f, 1.45f, 0), Quaternion.identity, new Vector3(.18f, 2.0f, .18f));
        Part("Ridge", box, woodD, tr, new Vector3(0, 2.62f, 0), Quaternion.identity, new Vector3(2.6f, .16f, .16f));
        Part("RoofA", box, Mat(new Color(.36f, .42f, .3f)), tr, new Vector3(0, 2.38f, .5f), Quaternion.Euler(-38, 0, 0), new Vector3(2.8f, .1f, 1.35f));
        Part("RoofB", box, Mat(new Color(.33f, .4f, .29f)), tr, new Vector3(0, 2.38f, -.5f), Quaternion.Euler(38, 0, 0), new Vector3(2.8f, .1f, 1.35f));
        Part("Windlass", Ring(8, 0), woodD, tr, new Vector3(-.95f, 1.55f, 0), Quaternion.Euler(0, 0, 90), new Vector3(.12f, 1.9f, .12f));
        Part("Crank", box, Mat(Iron), tr, new Vector3(1.2f, 1.45f, 0), Quaternion.identity, new Vector3(.05f, .3f, .05f));
        Part("Rope", Ring(6, 0), Mat(Wicker), tr, new Vector3(0, .95f, 0), Quaternion.identity, new Vector3(.02f, .6f, .02f));
        Part("Bucket", Ring(8, .8f), wood, tr, new Vector3(0, .82f, 0), Quaternion.identity, new Vector3(.17f, .22f, .17f));
        // overflowing trough + wildflowers
        Trough(tr.position + tr.right * 2.6f + tr.forward * .4f, tr, seed);
        Wildflowers(tr.position + new Vector3(1.5f, 0, -1.6f), tr, seed + 3, 6);
        Wildflowers(tr.position + new Vector3(-1.7f, 0, 1.2f), tr, seed + 4, 5);
        water.name = "Water";
        return root;
    }

    public static GameObject Trough(Vector3 pos, Transform parent, int seed = 1)
    {
        var root = Root("Trough", pos, parent); var tr = root.transform; var box = ChamferBox(.1f);
        Part("TroughBody", box, Mat(Timber), tr, new Vector3(0, .32f, 0), Quaternion.identity, new Vector3(1.5f, .5f, .55f), true);
        Part("TroughWater", box, WaterMat(true, .5f), tr, new Vector3(0, .54f, 0), Quaternion.identity, new Vector3(1.32f, .04f, .4f));
        Part("Spout", Ring(6, .6f), Mat(Bamboo), tr, new Vector3(-.85f, .75f, 0), Quaternion.Euler(0, 0, 70), new Vector3(.06f, .6f, .06f));
        // a basket of fresh vegetables beside it
        Basket(tr.position + tr.forward * .7f + tr.right * .6f, tr, seed, true);
        // overflow puddle
        Puddle(tr.position + tr.forward * -.7f, tr, seed + 5, 1.2f);
        return root;
    }

    /// <summary>A tilled bed with rhythmic dark furrows, framed by a log or stone border, filled with a crop.</summary>
    public enum Crop { Cabbage, Pumpkin, Beans, Turnip, Blighted, Carrot, Tomato, Wheat, Sunflower }
    static GameObject LegacyFarmPlot(Vector3 pos, float yaw, Vector2 size, Crop crop, bool stoneBorder, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("FarmPlot_" + crop, pos, parent); var tr = root.transform; tr.rotation = Quaternion.Euler(0, yaw, 0);
        var box = ChamferBox(.2f);
        Part("Soil", box, Mat(crop == Crop.Blighted ? new Color(.33f, .27f, .22f) : Soil, default, .3f), tr, new Vector3(0, .05f, 0), Quaternion.identity, new Vector3(size.x, .22f, size.y));
        int rows = Mathf.Max(2, Mathf.RoundToInt(size.x / .9f));
        for (int r = 0; r < rows; r++)
        {
            float x = Mathf.Lerp(-size.x * .5f + .45f, size.x * .5f - .45f, rows == 1 ? .5f : r / (float)(rows - 1));
            Part("Furrow", box, Mat(SoilWet, default, .3f), tr, new Vector3(x, .17f, 0), Quaternion.identity, new Vector3(.42f, .1f, size.y - .4f));
            int n = Mathf.Max(2, Mathf.RoundToInt(size.y / .8f));
            for (int i = 0; i < n; i++)
            {
                float z = Mathf.Lerp(-size.y * .5f + .45f, size.y * .5f - .45f, i / (float)(n - 1));
                Vector3 lp = new Vector3(x + R(rng, -.06f, .06f), .2f, z + R(rng, -.06f, .06f));
                switch (crop)
                {
                    case Crop.Cabbage: CabbageHead(tr, lp, rng); break;
                    case Crop.Pumpkin: if (rng.NextDouble() < .55) PumpkinProp(tr, lp, rng); else LeafCluster(tr, lp, rng, Vine, .35f); break;
                    case Crop.Turnip: LeafCluster(tr, lp, rng, new Color(.42f, .62f, .3f), .22f); Part("Root", Blob(rng.Next(), .1f), Mat(new Color(.85f, .75f, .8f)), tr, lp + Vector3.up * .02f, Quaternion.identity, Vector3.one * .1f); break;
                    case Crop.Blighted: if (rng.NextDouble() < .7) LeafCluster(tr, lp, rng, new Color(.45f, .4f, .26f), .2f); break;
                    case Crop.Beans: break;
                }
            }
            if (crop == Crop.Beans) Trellis(tr, new Vector3(x, .15f, 0), size.y - .3f, rng);
        }
        var border = stoneBorder ? Mat(Stone) : Mat(Timber);
        var mossM = Mat(Moss);
        void Edge(Vector3 c, float len, bool alongX)
        {
            if (stoneBorder)
            {
                int n = Mathf.Max(2, Mathf.RoundToInt(len / .45f));
                for (int i = 0; i < n; i++)
                {
                    float k = Mathf.Lerp(-len * .5f, len * .5f, (i + .5f) / n);
                    Vector3 p = c + (alongX ? new Vector3(k, 0, 0) : new Vector3(0, 0, k));
                    Part("WallStone", Blob(rng.Next(), .22f), i % 3 == 0 ? mossM : border, tr, p + Vector3.up * .18f, Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(.3f, .24f, .26f));
                }
            }
            else Part("Log", box, border, tr, c + Vector3.up * .2f, Quaternion.identity, alongX ? new Vector3(len + .3f, .3f, .3f) : new Vector3(.3f, .3f, len + .3f));
        }
        Edge(new Vector3(0, 0, size.y * .5f + .15f), size.x, true); Edge(new Vector3(0, 0, -size.y * .5f - .15f), size.x, true);
        Edge(new Vector3(size.x * .5f + .15f, 0, 0), size.y, false); Edge(new Vector3(-size.x * .5f - .15f, 0, 0), size.y, false);
        if (crop == Crop.Blighted) root.AddComponent<BlightedPatch>();
        return root;
    }

    static void CabbageHead(Transform tr, Vector3 lp, System.Random rng)
    {
        float s = R(rng, .2f, .3f);
        Part("CabbageCore", Blob(rng.Next(), .08f), Mat(Cabbage), tr, lp + Vector3.up * s * .7f, Quaternion.identity, Vector3.one * s);
        int leaves = 5;
        for (int i = 0; i < leaves; i++)
            Part("CabbageLeaf", Leaf(), Mat(i % 2 == 0 ? CabbageDark : Cabbage), tr, lp + Vector3.up * .05f, Quaternion.Euler(-28, i * 360f / leaves + R(rng, -10, 10), 0), Vector3.one * s * 1.7f);
    }
    static void PumpkinProp(Transform tr, Vector3 lp, System.Random rng)
    {
        float s = R(rng, .25f, .4f);
        Part("Pumpkin", Blob(rng.Next(), .06f, 4, 9), Mat(Pumpkin), tr, lp + Vector3.up * s * .55f, Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(s * 1.2f, s * .8f, s * 1.2f));
        Part("Stem", ChamferBox(.2f), Mat(Vine), tr, lp + Vector3.up * s * 1.3f, Quaternion.Euler(R(rng, -15, 15), 0, R(rng, -15, 15)), new Vector3(.05f, .14f, .05f));
        LeafCluster(tr, lp + new Vector3(.2f, 0, .1f), rng, Vine, .3f);
    }
    static void LeafCluster(Transform tr, Vector3 lp, System.Random rng, Color c, float s)
    {
        for (int i = 0; i < 4; i++) Part("Leaf", Leaf(), Mat(c), tr, lp, Quaternion.Euler(-40 + R(rng, -10, 10), i * 90 + R(rng, -20, 20), 0), Vector3.one * s * R(rng, .8f, 1.2f));
    }
    static void Trellis(Transform tr, Vector3 lp, float length, System.Random rng)
    {
        var pole = Ring(6, 0); var bam = Mat(Bamboo); var vine = Mat(Vine);
        int pairs = Mathf.Max(2, Mathf.RoundToInt(length / 1.2f));
        for (int i = 0; i < pairs; i++)
        {
            float z = Mathf.Lerp(-length * .5f, length * .5f, i / (float)(pairs - 1));
            Part("PoleA", pole, bam, tr, lp + new Vector3(-.28f, 0, z), Quaternion.Euler(0, 0, -14), new Vector3(.035f, 1.7f, .035f));
            Part("PoleB", pole, bam, tr, lp + new Vector3(.28f, 0, z), Quaternion.Euler(0, 0, 14), new Vector3(.035f, 1.7f, .035f));
        }
        Part("RidgePole", pole, bam, tr, lp + new Vector3(0, 1.62f, -length * .5f), Quaternion.Euler(90, 0, 0), new Vector3(.03f, length, .03f));
        // climbing vines: leaf cards spiralling up both sides
        int n = Mathf.RoundToInt(length * 9);
        for (int i = 0; i < n; i++)
        {
            float z = R(rng, -length * .5f, length * .5f), h = R(rng, .15f, 1.55f), side = rng.NextDouble() < .5 ? -1 : 1;
            Part("VineLeaf", Leaf(), vine, tr, lp + new Vector3(side * (.28f - h * .12f), h, z), Quaternion.Euler(R(rng, -60, 20), R(rng, 0, 360), R(rng, -30, 30)), Vector3.one * R(rng, .14f, .24f));
        }
    }

    /// <summary>Rustic split-rail fence along a polyline.</summary>
    public static GameObject Fence(IList<Vector3> pts, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("Fence", pts[0], parent); root.transform.position = Vector3.zero;
        var box = ChamferBox(.2f); var wood = Mat(Timber); var woodD = Mat(TimberDark);
        for (int s = 0; s < pts.Count - 1; s++)
        {
            Vector3 a = Ground(pts[s]), b = Ground(pts[s + 1]);
            float len = Vector3.Distance(a, b); int spans = Mathf.Max(1, Mathf.RoundToInt(len / 2.2f));
            for (int i = 0; i <= spans; i++)
            {
                if (s > 0 && i == 0) continue;
                Vector3 p = Ground(Vector3.Lerp(a, b, i / (float)spans));
                var post = Part("Post", box, woodD, root.transform, p + Vector3.up * .55f, Quaternion.Euler(R(rng, -4, 4), R(rng, 0, 90), R(rng, -4, 4)), new Vector3(.16f, 1.1f, .16f));
                post.AddComponent<BoxCollider>();
                if (i > 0)
                {
                    Vector3 q = Ground(Vector3.Lerp(a, b, (i - 1) / (float)spans));
                    Vector3 mid = (p + q) * .5f; Vector3 d = p - q;
                    var rot = Quaternion.LookRotation(d.normalized);
                    for (int r = 0; r < 2; r++)
                        Part("Rail", box, wood, root.transform, mid + Vector3.up * (.45f + r * .38f + R(rng, -.04f, .04f)), rot * Quaternion.Euler(0, 0, R(rng, -3, 3)), new Vector3(.1f, .1f, d.magnitude + .15f));
                }
            }
            if (rng.NextDouble() < .6) Wildflowers(a + (b - a) * .1f, root.transform, seed + s, 4);
        }
        return root;
    }

    /// <summary>Low, rounded, mossy dry-stone wall along a polyline.</summary>
    public static GameObject StoneWall(IList<Vector3> pts, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("StoneWall", pts[0], parent); root.transform.position = Vector3.zero;
        var stone = Mat(Stone); var moss = Mat(Moss); var dark = Mat(StoneDark);
        for (int s = 0; s < pts.Count - 1; s++)
        {
            Vector3 a = Ground(pts[s]), b = Ground(pts[s + 1]);
            int n = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(a, b) / .5f));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Ground(Vector3.Lerp(a, b, (i + .5f) / n));
                for (int layer = 0; layer < 2; layer++)
                {
                    var g = Part("Stone", Blob(rng.Next(), .25f), layer == 1 && rng.NextDouble() < .5 ? moss : (rng.NextDouble() < .25 ? dark : stone), root.transform,
                        p + Vector3.up * (.2f + layer * .32f), Quaternion.Euler(R(rng, -8, 8), R(rng, 0, 360), R(rng, -8, 8)), new Vector3(R(rng, .3f, .4f), .22f, R(rng, .26f, .34f)));
                    if (layer == 0) g.AddComponent<SphereCollider>().radius = 1;
                }
            }
        }
        return root;
    }

    /// <summary>Recessed packed-dirt path draped on the ground along a polyline, dotted with glossy puddles.</summary>
    static GameObject LegacyPath(IList<Vector3> pts, float width, Transform parent, int seed = 1, float puddleChance = .35f)
    {
        var rng = new System.Random(seed);
        var root = Root("Path", pts[0], parent); root.transform.position = Vector3.zero;
        var v = new List<Vector3>(); var t = new List<int>(); var cols = new List<Color>();
        float acc = 0;
        for (int s = 0; s < pts.Count - 1; s++)
        {
            Vector3 a = pts[s], b = pts[s + 1];
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / .7f));
            for (int i = 0; i <= n; i++)
            {
                if (s > 0 && i == 0) continue;
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                Vector3 dir = (b - a); dir.y = 0; dir.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, dir);
                float w = width * (.85f + .3f * Mathf.PerlinNoise(acc * .3f, seed));
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 q = p + side * sgn * w * .5f; q = Ground(q) + Vector3.up * .03f;
                    v.Add(q);
                }
                acc += .7f;
                if (v.Count >= 4) { int k = v.Count - 4; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
                if (rng.NextDouble() < puddleChance * .25f) Puddle(p + side * R(rng, -w * .3f, w * .3f), root.transform, rng.Next(), R(rng, .35f, .8f));
            }
        }
        var mesh = new Mesh { name = "Path" }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals();
        // make sure it faces up
        var nn = mesh.normals; if (nn.Length > 0 && nn[0].y < 0) { var tt = mesh.triangles; System.Array.Reverse(tt); mesh.triangles = tt; mesh.RecalculateNormals(); }
        mesh.RecalculateBounds();
        var g = new GameObject("PathSurface"); g.transform.SetParent(root.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = Mat(Mud, default, .35f, .05f);
        return root;
    }

    /// <summary>Glossy rain puddle: a dark sky-tinted disc with a soft bright rim that catches lights.</summary>
    public static GameObject Puddle(Vector3 pos, Transform parent, int seed, float size)
    {
        var root = Root("Puddle", pos, parent); var tr = root.transform;
        Part("PuddleRim", Disc(seed, .3f, 12), Mat(new Color(.3f, .25f, .2f)), tr, new Vector3(0, .035f, 0), Quaternion.Euler(0, seed * 37 % 360, 0), new Vector3(size * 1.12f, 1, size * .85f * 1.12f));
        var p = Part("PuddleSky", Disc(seed, .3f, 12), Mat(new Color(.3f, .33f, .4f), new Color(.03f, .035f, .05f), 0, .6f), tr, new Vector3(0, .045f, 0), Quaternion.Euler(0, seed * 37 % 360, 0), new Vector3(size, 1, size * .85f));
        p.AddComponent<PuddleShimmer>();
        return root;
    }

    /// <summary>Crooked wooden post with a warm paper-screened lantern (2800-4500K point light).</summary>
    /// <summary>
    /// Village lamp post (after the reference): a tall square timber post on a stone footing with an iron band,
    /// an overhanging arm held by a diagonal brace, an iron hook and chain, and a tapered iron lantern with warm
    /// glowing glass. Flagstone tiles and a few grass tufts around the foot. Faces 'yaw' (the arm reaches that way).
    /// </summary>
    public static GameObject LanternPost(Vector3 pos, Transform parent, int seed = 1, float yaw = float.NaN)
    {
        var rng = new System.Random(seed);
        var root = Root("LanternPost", pos, parent); var tr = root.transform;
        tr.rotation = Quaternion.Euler(0, float.IsNaN(yaw) ? R(rng, 0, 360) : yaw, 0);
        var box = ChamferBox(.18f);
        var wood = Mat(new Color(.5f, .37f, .26f), default, .3f); var woodD = Mat(new Color(.4f, .29f, .2f), default, .3f);
        var iron = Mat(new Color(.25f, .24f, .26f)); var bandM = Mat(new Color(.62f, .62f, .66f)); var stone = Mat(new Color(.5f, .45f, .5f), default, .3f);
        // flagstones + footing
        for (int i = 0; i < 9; i++)
        {
            float a = i * 40 + R(rng, -10, 10), r = i == 0 ? 0 : R(rng, .45f, .8f);
            Part("Flagstone", ChamferBox(.25f), i % 3 == 0 ? Mat(new Color(.42f, .38f, .44f)) : stone, tr, new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, .04f, Mathf.Sin(a * Mathf.Deg2Rad) * r), Quaternion.Euler(R(rng, -3, 3), R(rng, 0, 90), R(rng, -3, 3)), new Vector3(R(rng, .34f, .44f), .08f, R(rng, .3f, .4f)));
        }
        Part("Footing", box, stone, tr, new Vector3(0, .16f, 0), Quaternion.identity, new Vector3(.42f, .3f, .42f));
        // the post: slightly tapered, a touch crooked
        var post = Part("Post", box, wood, tr, new Vector3(0, 1.55f, 0), Quaternion.Euler(R(rng, -1.5f, 1.5f), 0, R(rng, -1.5f, 1.5f)), new Vector3(.24f, 2.8f, .24f));
        post.AddComponent<BoxCollider>();
        Part("IronBand", box, bandM, tr, new Vector3(0, 1.15f, 0), Quaternion.identity, new Vector3(.3f, .22f, .3f));
        foreach (var x in new[] { -.08f, .08f }) Part("Rivet", Blob(9, .05f), iron, tr, new Vector3(x, 1.15f, .16f), Quaternion.identity, Vector3.one * .03f);
        // arm + diagonal brace
        Part("Arm", box, woodD, tr, new Vector3(0, 2.85f, .55f), Quaternion.Euler(-4, 0, 0), new Vector3(.18f, .18f, 1.45f));
        Part("Brace", box, wood, tr, new Vector3(0, 2.42f, .32f), Quaternion.Euler(-48, 0, 0), new Vector3(.12f, .12f, .78f));
        Part("Cap", box, woodD, tr, new Vector3(0, 2.98f, 0), Quaternion.identity, new Vector3(.28f, .1f, .28f));
        // hook, chain, lantern
        Part("Hook", Ring(8, .7f), iron, tr, new Vector3(0, 2.72f, 1.12f), Quaternion.Euler(0, 0, 90), new Vector3(.06f, .02f, .06f));
        Part("Chain", box, iron, tr, new Vector3(0, 2.6f, 1.12f), Quaternion.identity, new Vector3(.025f, .2f, .025f));
        Color warm = Color.Lerp(new Color(1f, .62f, .25f), new Color(1f, .74f, .38f), (float)rng.NextDouble());
        var lantern = new GameObject("Lantern"); lantern.transform.SetParent(tr, false); lantern.transform.localPosition = new Vector3(0, 2.25f, 1.12f);
        var lt = lantern.transform;
        Part("Roof", Ring(4, 0), iron, lt, new Vector3(0, .2f, 0), Quaternion.Euler(0, 45, 0), new Vector3(.22f, .1f, .22f));
        Part("RoofPeak", Ring(4, 0), iron, lt, new Vector3(0, .3f, 0), Quaternion.Euler(0, 45, 0), new Vector3(.08f, .06f, .08f));
        Part("Glass", box, Mat(new Color(1f, .85f, .45f), warm * 2.2f, 0, .1f), lt, Vector3.zero, Quaternion.identity, new Vector3(.24f, .32f, .24f));
        foreach (var x in new[] { -.12f, .12f }) foreach (var z in new[] { -.12f, .12f }) Part("Frame", box, iron, lt, new Vector3(x, 0, z), Quaternion.identity, new Vector3(.03f, .34f, .03f));
        Part("FrameMid", box, iron, lt, Vector3.zero, Quaternion.identity, new Vector3(.26f, .03f, .26f));
        Part("Base", box, iron, lt, new Vector3(0, -.18f, 0), Quaternion.identity, new Vector3(.2f, .04f, .2f));
        lantern.AddComponent<LanternSway>();
        var lg = new GameObject("Light"); lg.transform.SetParent(lt, false);
        var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = warm; l.range = 8f; l.intensity = 2f; l.shadows = LightShadows.None;
        lg.AddComponent<LanternFlicker>();
        // grass tufts at the foot
        for (int i = 0; i < 4; i++) Part("Tuft", Leaf(), Mat(new Color(.4f, .62f, .3f)), tr, new Vector3(R(rng, -.6f, .6f), .02f, R(rng, -.6f, .6f)), Quaternion.Euler(-70, R(rng, 0, 360), 0), Vector3.one * R(rng, .15f, .25f));
        return root;
    }

    public static GameObject RainBarrel(Vector3 pos, Transform parent, int seed = 1)
    {
        var root = Root("RainBarrel", pos, parent); var tr = root.transform;
        Part("Barrel", Blob(seed, .02f, 5, 10), Mat(Timber), tr, new Vector3(0, .5f, 0), Quaternion.identity, new Vector3(.42f, .5f, .42f), true);
        Part("BandTop", Ring(10, .92f), Mat(Iron), tr, new Vector3(0, .72f, 0), Quaternion.identity, new Vector3(.43f, .06f, .43f));
        Part("BandBot", Ring(10, .92f), Mat(Iron), tr, new Vector3(0, .25f, 0), Quaternion.identity, new Vector3(.43f, .06f, .43f));
        Part("WaterTop", Disc(seed, 0, 10), Mat(Water, new Color(.02f, .05f, .07f)), tr, new Vector3(0, .9f, 0), Quaternion.identity, new Vector3(.36f, 1, .36f));
        return root;
    }

    public static GameObject Woodpile(Vector3 pos, float yaw, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("Woodpile", pos, parent); var tr = root.transform; tr.rotation = Quaternion.Euler(0, yaw, 0);
        var log = Ring(7, 0);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 5 - row; i++)
                Part("Log", log, Mat(i % 2 == 0 ? Timber : TimberDark), tr, new Vector3(-.9f + i * .4f + row * .2f, .18f + row * .32f, 0), Quaternion.Euler(90, R(rng, -4, 4), 0), new Vector3(.17f, 1.1f, .17f));
        var box = ChamferBox(.15f);
        Part("TarpRoof", box, Mat(new Color(.3f, .35f, .45f)), tr, new Vector3(0, 1.35f, -.1f), Quaternion.Euler(18, 0, 0), new Vector3(2.4f, .06f, 1.5f));
        Part("TarpPostA", box, Mat(TimberDark), tr, new Vector3(-1.1f, .65f, -.6f), Quaternion.identity, new Vector3(.1f, 1.3f, .1f));
        Part("TarpPostB", box, Mat(TimberDark), tr, new Vector3(1.1f, .65f, -.6f), Quaternion.identity, new Vector3(.1f, 1.3f, .1f));
        root.AddComponent<BoxCollider>().size = new Vector3(2.2f, 1.2f, 1.2f);
        return root;
    }

    public static GameObject Basket(Vector3 pos, Transform parent, int seed = 1, bool vegetables = false)
    {
        var rng = new System.Random(seed);
        var root = Root("Basket", pos, parent); var tr = root.transform;
        Part("Weave", Ring(10, .85f), Mat(Wicker), tr, Vector3.zero, Quaternion.identity, new Vector3(.32f, .28f, .32f));
        Part("Base", Disc(seed, 0, 10), Mat(Wicker), tr, new Vector3(0, .02f, 0), Quaternion.identity, new Vector3(.3f, 1, .3f));
        if (vegetables)
            for (int i = 0; i < 6; i++)
                Part("Veg", Blob(rng.Next(), .1f), Mat(i % 3 == 0 ? new Color(.85f, .25f, .2f) : i % 3 == 1 ? Cabbage : Pumpkin), tr,
                     new Vector3(R(rng, -.16f, .16f), .26f, R(rng, -.16f, .16f)), Quaternion.identity, Vector3.one * R(rng, .08f, .12f));
        return root;
    }

    /// <summary>Tiny clusters of white and periwinkle wildflowers to soften where props meet the ground.</summary>
    public static GameObject Wildflowers(Vector3 pos, Transform parent, int seed, int count)
    {
        var rng = new System.Random(seed);
        var root = Root("Wildflowers", pos, parent); var tr = root.transform;
        var white = Mat(new Color(.95f, .95f, .9f), new Color(.08f, .08f, .08f)); var peri = Mat(new Color(.6f, .65f, .95f), new Color(.04f, .05f, .1f));
        for (int i = 0; i < count; i++)
        {
            Vector3 p = new Vector3(R(rng, -.4f, .4f), 0, R(rng, -.4f, .4f));
            Part("Stem", ChamferBox(.3f), Mat(Vine), tr, p + Vector3.up * .1f, Quaternion.identity, new Vector3(.02f, .2f, .02f));
            Part("Bloom", Disc(rng.Next(), .4f, 5), rng.NextDouble() < .5 ? white : peri, tr, p + Vector3.up * .21f, Quaternion.Euler(0, R(rng, 0, 360), 0), Vector3.one * .07f);
        }
        return root;
    }

    /// <summary>Stepped stone-rimmed fishpond: shallow turquoise edge -> deep blue centre, mossy boulders, koi, a dock.</summary>
    public static GameObject Pond(Vector3 pos, float radius, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("Fishpond", pos, parent); var tr = root.transform;
        Part("PondBed", Disc(seed, .18f, 18), Mat(new Color(.2f, .24f, .2f), default, .1f, .1f), tr, new Vector3(0, .03f, 0), Quaternion.identity, new Vector3(radius * 1.05f, 1, radius * .84f));
        Part("PondWater", Disc(seed, .18f, 18), WaterMat(true, 2.2f), tr, new Vector3(0, .09f, 0), Quaternion.identity, new Vector3(radius, 1, radius * .8f));
        int boulders = Mathf.RoundToInt(radius * 6);
        for (int i = 0; i < boulders; i++)
        {
            float a = i * Mathf.PI * 2 / boulders + R(rng, -.1f, .1f);
            Vector3 p = new Vector3(Mathf.Cos(a) * radius * 1.05f, .1f, Mathf.Sin(a) * radius * .82f);
            var b = Part("Boulder", Blob(rng.Next(), .25f), Mat(rng.NextDouble() < .4 ? Moss : Stone), tr, p, Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(R(rng, .35f, .6f), R(rng, .25f, .4f), R(rng, .3f, .5f)));
            b.AddComponent<SphereCollider>().radius = 1;
            if (rng.NextDouble() < .35) Part("Foam", Ring(10, .8f), Mat(new Color(.95f, .97f, 1f), new Color(.15f, .15f, .15f)), tr, p + Vector3.down * .03f, Quaternion.identity, new Vector3(.55f, .02f, .55f));
        }
        // a little timber dock jutting over the water + a fish trap and bucket
        var box = ChamferBox(.15f);
        Vector3 dockAt = new Vector3(radius * .7f, .3f, 0);
        Part("Dock", box, Mat(Timber), tr, dockAt, Quaternion.identity, new Vector3(1.8f, .1f, .9f)).AddComponent<BoxCollider>();
        for (int i = 0; i < 4; i++) Part("Piling", Ring(6, 0), Mat(TimberDark), tr, dockAt + new Vector3(i < 2 ? -.8f : .8f, -.6f, i % 2 == 0 ? -.4f : .4f), Quaternion.identity, new Vector3(.08f, .9f, .08f));
        Part("FishTrap", Ring(8, .85f), Mat(Bamboo), tr, dockAt + new Vector3(.5f, .05f, .2f), Quaternion.Euler(0, 0, 90), new Vector3(.18f, .5f, .18f));
        RainBarrel(tr.position + tr.TransformDirection(dockAt + new Vector3(.4f, 0, -.3f)), tr, seed + 7).transform.localScale = Vector3.one * .6f;
        // koi
        var fish = new GameObject("Koi"); fish.transform.SetParent(tr, false);
        var school = fish.AddComponent<KoiSchool>(); school.radius = radius * .6f; school.height = .03f; school.Build(rng.Next(), 6);
        return root;
    }
}

/// <summary>Marks a GameObject as part of the village kit (so ground probes don't stack on props).</summary>
public class VillageKitPiece : MonoBehaviour { }



/// <summary>Soft rim shimmer on puddles so they read as glossy, catching nearby light.</summary>
public class PuddleShimmer : MonoBehaviour
{
    Renderer r; MaterialPropertyBlock mpb; float seed; Color baseEm;
    void Awake() { r = GetComponent<Renderer>(); mpb = new MaterialPropertyBlock(); seed = Random.value * 10; if (!r.sharedMaterial.HasProperty("_EmissionColor")) { enabled = false; return; } baseEm = r.sharedMaterial.GetColor("_EmissionColor"); }
    void Update()
    {
        if (!r || mpb == null) Awake();
        if (!enabled) return;
        float s = .8f + .4f * Mathf.PerlinNoise(Time.time * .5f, seed);
        r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", baseEm * s); r.SetPropertyBlock(mpb);
    }
}

/// <summary>Withered crops that warm back to life near the Star: leaves unfurl to emerald / ochre and warm motes linger.</summary>
public class BlightedPatch : MonoBehaviour
{
    public float reviveRadius = 6f, reviveSeconds = 3f;
    float life; Transform star; readonly List<Renderer> leaves = new List<Renderer>(); MaterialPropertyBlock mpb;
    static readonly Color Dead = new Color(.45f, .4f, .26f), Alive = new Color(.35f, .62f, .24f);
    void Start()
    {
        mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>()) if (r.name == "Leaf") leaves.Add(r);
        var p = GameObject.FindGameObjectWithTag("Player"); if (p) star = p.transform;
    }
    void Update()
    {
        if (!star) return;
        bool near = Vector3.Distance(star.position, transform.position) < reviveRadius;
        float before = life;
        life = Mathf.MoveTowards(life, near ? 1 : life, Time.deltaTime / reviveSeconds);   // once revived it stays revived
        if (Mathf.Approximately(before, life)) return;
        var c = Color.Lerp(Dead, Alive, Mathf.SmoothStep(0, 1, life));
        foreach (var r in leaves)
        {
            r.GetPropertyBlock(mpb); mpb.SetColor("_Color", c); mpb.SetColor("_EmissionColor", new Color(.25f, .2f, .05f) * Mathf.Sin(life * Mathf.PI)); r.SetPropertyBlock(mpb);
            r.transform.localScale = Vector3.one * Mathf.Lerp(.16f, .28f, life);
        }
    }
}

/// <summary>A few low-poly koi drifting along smooth looping paths; they school toward the Star when it is close.</summary>
public class KoiSchool : MonoBehaviour
{
    public float radius = 2f, height = .03f;
    readonly List<Transform> fish = new List<Transform>(); readonly List<float> phase = new List<float>(), speed = new List<float>();
    Transform star;
    public void Build(int seed, int count)
    {
        var rng = new System.Random(seed);
        var body = VillageKit.Blob(seed, .02f, 3, 6);
        for (int i = 0; i < count; i++)
        {
            var g = new GameObject("Koi"); g.transform.SetParent(transform, false);
            var f = new GameObject("Body"); f.transform.SetParent(g.transform, false); f.transform.localScale = new Vector3(.07f, .04f, .18f);
            f.AddComponent<MeshFilter>().sharedMesh = body;
            Color c = rng.NextDouble() < .5 ? new Color(.95f, .45f, .15f) : rng.NextDouble() < .5 ? new Color(.95f, .92f, .88f) : new Color(.75f, .78f, .82f);
            f.AddComponent<MeshRenderer>().sharedMaterial = VillageKit.Mat(c, c * .08f);
            fish.Add(g.transform); phase.Add((float)rng.NextDouble() * 6.28f); speed.Add(Mathf.Lerp(.25f, .5f, (float)rng.NextDouble()));
        }
    }
    void Update()
    {
        if (!star) { var p = GameObject.FindGameObjectWithTag("Player"); if (p) star = p.transform; }
        Vector3 lure = Vector3.zero; float pull = 0;
        if (star) { var local = transform.InverseTransformPoint(star.position); local.y = 0; float d = local.magnitude; pull = Mathf.InverseLerp(radius * 3, radius, d); lure = Vector3.ClampMagnitude(local, radius * .8f); }
        for (int i = 0; i < fish.Count; i++)
        {
            float t = Time.time * speed[i] + phase[i];
            Vector3 p = new Vector3(Mathf.Sin(t) * radius * (.6f + .3f * Mathf.Sin(t * .37f + i)), height, Mathf.Sin(t * 2 + i) * radius * .45f);
            p = Vector3.Lerp(p, lure + new Vector3(Mathf.Sin(t * 3 + i), 0, Mathf.Cos(t * 3 + i)) * .35f + Vector3.up * height, pull * .8f);
            Vector3 prev = fish[i].localPosition; fish[i].localPosition = Vector3.Lerp(prev, p, Time.deltaTime * 3);
            Vector3 dir = fish[i].localPosition - prev; dir.y = 0;
            if (dir.sqrMagnitude > 1e-7f) fish[i].localRotation = Quaternion.Slerp(fish[i].localRotation, Quaternion.LookRotation(dir), Time.deltaTime * 6);
        }
    }
}
