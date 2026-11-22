using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Third-generation village kit pieces (partial extension of VillageKit): the stone-and-slate village well, living
/// farm plots built from raised beds (with real crop cells the FarmPlotState animates), and the warm sandy footpaths
/// that share the island road shader. Everything is Ghibli/ToonLit (via VillageKit.Mat) or the Island road shader.
/// </summary>
public static partial class VillageKit
{
    public static readonly Color Slate = new Color(.27f, .46f, .53f), SlateLight = new Color(.38f, .6f, .64f),
        SoilWarm = new Color(.5f, .33f, .2f), SandyPad = new Color(.8f, .58f, .33f), Clay = new Color(.72f, .42f, .28f);

    // ------------------------------------------------------------------ small helpers
    /// <summary>A faceted cylinder from local point a to local point b.</summary>
    public static GameObject Cyl(Transform parent, string name, Material m, Vector3 a, Vector3 b, float radius, int segs = 8, float hole = 0f)
    {
        Vector3 d = b - a; float len = Mathf.Max(1e-4f, d.magnitude);
        var g = new GameObject(name); g.transform.SetParent(parent, false);
        g.transform.localPosition = a; g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
        g.transform.localScale = new Vector3(radius, len, radius);
        g.AddComponent<MeshFilter>().sharedMesh = Ring(segs, hole); g.AddComponent<MeshRenderer>().sharedMaterial = m; return g;
    }

    public static GameObject Piece(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Vector3 euler, Vector3 sc, bool collider = false)
        => Part(name, mesh, mat, parent, lp, Quaternion.Euler(euler), sc, collider);

    public static GameObject KitRoot(string name, Vector3 pos, Transform parent) => Root(name, pos, parent);
    public static float Rnd(System.Random rng, float a, float b) => R(rng, a, b);

    // ================================================================== WELL
    /// <summary>
    /// Village well: cobbled plaza, three-course mossy stone ring with cap stones, a slate-teal shingled roof on a
    /// braced timber frame, windlass with rope coil + crank wheel, hanging bucket and a trough. The parts WellRig animates
    /// are named Windlass / Rope / Bucket. Child "Water" is the surface (the star fragment glows it).
    /// </summary>
    public static GameObject Well(Vector3 pos, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("Well", pos, parent); var tr = root.transform;
        var stone = Mat(Stone); var stoneL = Mat(new Color(.68f, .65f, .6f)); var dark = Mat(StoneDark); var moss = Mat(Moss);
        var wood = Mat(new Color(.52f, .37f, .25f), default, .3f); var woodD = Mat(TimberDark, default, .3f); var iron = Mat(Iron);
        var ropeM = Mat(Wicker); var id = Quaternion.identity;
        var box = ChamferBox(.16f);

        // ---- 1. cobbled plaza: sandy bed, three rings of irregular flags, pebbles around the rim
        Part("PlazaBase", Disc(seed + 1, .1f, 22), Mat(new Color(.66f, .52f, .35f), default, .3f, .04f), tr, new Vector3(0, .012f, 0), id, new Vector3(3.6f, 1, 3.6f));
        int[] counts = { 12, 18, 24 }; float[] radii = { 1.75f, 2.35f, 2.95f };
        for (int ring = 0; ring < 3; ring++)
            for (int j = 0; j < counts[ring]; j++)
            {
                float a = (j + ring * .5f) / counts[ring] * Mathf.PI * 2 + R(rng, -.05f, .05f), r = radii[ring] + R(rng, -.1f, .1f);
                var m = j % 6 == 0 ? moss : j % 3 == 0 ? dark : (j + ring) % 2 == 0 ? stone : stoneL;
                Part("Flag", Disc(seed * 31 + ring * 40 + j, .22f, 6), m, tr, new Vector3(Mathf.Cos(a) * r, .028f + ring * .002f, Mathf.Sin(a) * r), Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(R(rng, .34f, .5f), 1, R(rng, .26f, .4f)));
            }
        for (int i = 0; i < 22; i++)
        {
            float a = R(rng, 0, 6.28f), r = R(rng, 3.25f, 3.7f);
            Part("Pebble", Blob(seed + 400 + i, .25f, 3, 6), i % 3 == 0 ? dark : stoneL, tr, new Vector3(Mathf.Cos(a) * r, .03f, Mathf.Sin(a) * r), Quaternion.Euler(0, R(rng, 0, 360), 0), Vector3.one * R(rng, .06f, .12f));
        }

        // ---- 2. plinth, stone ring (three courses + cap stones), shaft
        Part("Plinth", Ring(20, 0), stone, tr, Vector3.zero, id, new Vector3(1.62f, .1f, 1.62f));
        Part("WellCore", Ring(16, .8f), dark, tr, new Vector3(0, .1f, 0), id, new Vector3(1.06f, .82f, 1.06f), true);
        for (int course = 0; course < 3; course++)
        {
            int n = 13;
            for (int i = 0; i < n; i++)
            {
                float a = (i + course * .5f) * Mathf.PI * 2 / n;
                var m = (i + course) % 5 == 0 ? moss : (i + course) % 2 == 0 ? stone : stoneL;
                Part("Cobble", Blob(seed * 7 + i + course * 20, .18f), m, tr, new Vector3(Mathf.Cos(a) * 1.04f, .2f + course * .25f, Mathf.Sin(a) * 1.04f),
                    Quaternion.Euler(R(rng, -6, 6), -a * Mathf.Rad2Deg, R(rng, -6, 6)), new Vector3(.2f, .17f, .34f));
            }
        }
        for (int i = 0; i < 14; i++)
        {
            float a = i * Mathf.PI * 2 / 14;
            Part("CapStone", box, i % 2 == 0 ? stoneL : stone, tr, new Vector3(Mathf.Cos(a) * 1.0f, .96f, Mathf.Sin(a) * 1.0f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), new Vector3(.4f, .1f, .46f));
        }
        Part("Water", Disc(seed, .02f, 20), WaterMat(true, 1.6f), tr, new Vector3(0, .55f, 0), id, new Vector3(.84f, 1, .84f));
        for (int i = 0; i < 3; i++)
        {
            float a = R(rng, 0, 6.28f), r = R(rng, .15f, .5f);
            Part("LilyPad", Disc(seed + 90 + i, .05f, 9), Mat(new Color(.38f, .6f, .3f)), tr, new Vector3(Mathf.Cos(a) * r, .56f, Mathf.Sin(a) * r), Quaternion.Euler(0, R(rng, 0, 360), 0), Vector3.one * R(rng, .1f, .15f));
        }

        // ---- 3. braced timber frame
        foreach (float sx in new[] { -1f, 1f })
        {
            Part(sx < 0 ? "PostL" : "PostR", box, wood, tr, new Vector3(sx * 1.24f, 1.5f, 0), id, new Vector3(.2f, 2.8f, .2f), true);
            Part("Footing", Blob(seed + 5, .15f), stone, tr, new Vector3(sx * 1.24f, .14f, 0), id, new Vector3(.34f, .2f, .34f));
            Part("Band", box, iron, tr, new Vector3(sx * 1.24f, .75f, 0), id, new Vector3(.25f, .07f, .25f));
            Part("Band", box, iron, tr, new Vector3(sx * 1.24f, 1.95f, 0), id, new Vector3(.25f, .07f, .25f));
            Part("GableStub", box, woodD, tr, new Vector3(sx * 1.24f, 2.95f, 0), id, new Vector3(.16f, .5f, .16f));
            Cyl(tr, "Brace", wood, new Vector3(sx * 1.24f, 1.85f, 0), new Vector3(sx * .45f, 2.58f, 0), .06f, 6);
            Cyl(tr, "BraceLow", wood, new Vector3(sx * 1.24f, 2.2f, .0f), new Vector3(sx * .85f, 2.6f, 0), .045f, 6);
        }
        Part("Ridge", box, woodD, tr, new Vector3(0, 2.62f, 0), id, new Vector3(2.95f, .2f, .2f));
        Cyl(tr, "RidgePole", woodD, new Vector3(-1.6f, 3.0f, 0), new Vector3(1.6f, 3.0f, 0), .075f, 6);
        Part("KingPost", box, woodD, tr, new Vector3(0, 2.8f, 0), id, new Vector3(.14f, .4f, .14f));

        // ---- 4. slate-teal fish-scale shingle roof (rows from the ridge down to the eaves)
        {
            const float theta = 38f * Mathf.Deg2Rad; const float slopeLen = 1.6f; const float Hr = 3.02f; int rows = 5, cols = 8;
            float rowStep = slopeLen / rows, w = 3.2f / cols;
            var variants = new Material[4];
            for (int k = 0; k < 4; k++) variants[k] = Mat(Color.Lerp(Slate, SlateLight, k / 3f), default, .25f, .1f);
            foreach (float s in new[] { 1f, -1f })
            {
                Vector3 dir = new Vector3(0, -Mathf.Sin(theta), s * Mathf.Cos(theta));
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
                float dMid = slopeLen * .5f;
                Part("RoofUnder", box, woodD, tr, new Vector3(0, Hr - dMid * Mathf.Sin(theta) - .05f, s * dMid * Mathf.Cos(theta)), rot, new Vector3(3.3f, .06f, slopeLen + .05f));
                for (int r = 0; r < rows; r++)
                    for (int j = 0; j < cols + 1; j++)
                    {
                        float x = -1.6f + (j + (r % 2) * .5f) * w;
                        if (x > 1.62f) continue;
                        float d = (r + .55f) * rowStep;
                        Vector3 p = new Vector3(x, Hr - d * Mathf.Sin(theta) + .02f, s * d * Mathf.Cos(theta));
                        Part("Shingle", Ring(8, 0), variants[rng.Next(4)], tr, p, rot * Quaternion.Euler(R(rng, -2, 2), 0, R(rng, -2, 2)), new Vector3(w * .56f, .05f, rowStep * .78f));
                    }
                // barge boards at both gable ends
                foreach (float sx in new[] { -1f, 1f })
                    Part("Barge", box, woodD, tr, new Vector3(sx * 1.66f, Hr - dMid * Mathf.Sin(theta) + .02f, s * dMid * Mathf.Cos(theta)), rot, new Vector3(.08f, .1f, slopeLen + .1f));
            }
            Cyl(tr, "RidgeCap", Mat(SlateLight), new Vector3(-1.7f, Hr + .04f, 0), new Vector3(1.7f, Hr + .04f, 0), .12f, 6);
            foreach (float sx in new[] { -1f, 1f }) Part("Finial", Blob(seed + 12, .1f), Mat(SlateLight), tr, new Vector3(sx * 1.74f, Hr + .06f, 0), id, Vector3.one * .1f);
        }

        // ---- 5. windlass (pivot spins about the well's X axis), rope coil, crank wheel
        var wl = new GameObject("Windlass"); wl.transform.SetParent(tr, false); wl.transform.localPosition = new Vector3(0, 1.75f, 0);
        var wt = wl.transform;
        Cyl(wt, "Drum", woodD, new Vector3(-1.2f, 0, 0), new Vector3(1.2f, 0, 0), .13f, 10);
        for (int i = 0; i < 7; i++) { float x = -.42f + i * .07f; Cyl(wt, "Coil", ropeM, new Vector3(x, 0, 0), new Vector3(x + .045f, 0, 0), .155f, 10); }
        Cyl(wt, "EndCap", iron, new Vector3(-1.2f, 0, 0), new Vector3(-1.16f, 0, 0), .17f, 8);
        Cyl(wt, "CrankWheel", wood, new Vector3(1.3f, 0, 0), new Vector3(1.35f, 0, 0), .36f, 12, .84f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI / 2;
            Cyl(wt, "Spoke", wood, new Vector3(1.32f, 0, 0), new Vector3(1.32f, Mathf.Cos(a) * .34f, Mathf.Sin(a) * .34f), .02f, 5);
        }
        Cyl(wt, "Crank", wood, new Vector3(1.35f, 0, .3f), new Vector3(1.62f, 0, .3f), .035f, 6);
        Cyl(wt, "CrankKnob", iron, new Vector3(1.62f, 0, .3f), new Vector3(1.66f, 0, .3f), .06f, 6);

        // rope (WellRig re-fits it to the bucket) and the bucket
        var rope = Cyl(tr, "Rope", ropeM, new Vector3(0, 1.28f, 0), new Vector3(0, 1.6f, 0), .022f, 5);
        var bucket = new GameObject("Bucket"); bucket.transform.SetParent(tr, false); bucket.transform.localPosition = new Vector3(0, 1.0f, 0);
        var bt = bucket.transform;
        Part("BucketBody", Ring(12, .86f), wood, bt, Vector3.zero, id, new Vector3(.2f, .28f, .2f));
        Part("BucketBase", Disc(seed + 3, 0, 10), woodD, bt, new Vector3(0, .02f, 0), id, new Vector3(.19f, 1, .19f));
        Part("Hoop", Ring(12, .9f), iron, bt, new Vector3(0, .05f, 0), id, new Vector3(.206f, .03f, .206f));
        Part("Hoop", Ring(12, .9f), iron, bt, new Vector3(0, .22f, 0), id, new Vector3(.206f, .03f, .206f));
        Cyl(bt, "HandleL", iron, new Vector3(-.2f, .27f, 0), new Vector3(0, .52f, 0), .014f, 5);
        Cyl(bt, "HandleR", iron, new Vector3(0, .52f, 0), new Vector3(.2f, .27f, 0), .014f, 5);
        var bw = Part("BucketWater", Disc(seed + 4, 0, 10), Mat(new Color(.25f, .5f, .6f), new Color(.02f, .05f, .07f), 0, .3f), bt, new Vector3(0, .22f, 0), id, new Vector3(.17f, 1, .17f));
        bw.SetActive(false);
        var wr = root.AddComponent<WellRig>(); wr.Wire(wt, rope.transform, bt, bw, 1.0f, .36f);

        // ---- 6. dressing: hanging lantern, pail, jug, rope coil, ivy, trough, flowers
        Color warm = new Color(1f, .7f, .32f);
        Part("WellLantern", box, Mat(new Color(1f, .85f, .45f), warm * 2f, 0, .1f), tr, new Vector3(1.24f, 2.25f, .24f), id, new Vector3(.13f, .18f, .13f));
        Part("LanternCap", box, iron, tr, new Vector3(1.24f, 2.37f, .24f), id, new Vector3(.17f, .04f, .17f));
        Cyl(tr, "LanternBracket", iron, new Vector3(1.24f, 2.5f, 0), new Vector3(1.24f, 2.4f, .24f), .012f, 4);
        Part("Pail", Ring(10, .84f), wood, tr, new Vector3(1.75f, .035f, -1.25f), id, new Vector3(.16f, .2f, .16f));
        Part("PailBand", Ring(10, .9f), iron, tr, new Vector3(1.75f, .1f, -1.25f), id, new Vector3(.165f, .025f, .165f));
        Part("Jug", Blob(seed + 21, .05f, 4, 8), Mat(Clay), tr, new Vector3(-1.85f, .16f, 1.4f), id, new Vector3(.13f, .17f, .13f));
        Part("JugNeck", Ring(8, .6f), Mat(Clay), tr, new Vector3(-1.85f, .3f, 1.4f), id, new Vector3(.06f, .1f, .06f));
        Part("RopeCoil", Ring(14, .6f), ropeM, tr, new Vector3(1.4f, .04f, 1.55f), id, new Vector3(.2f, .05f, .2f));
        for (int i = 0; i < 10; i++)
        {
            float sx = i % 2 == 0 ? -1f : 1f;
            Part("Ivy", Leaf(), Mat(i % 3 == 0 ? Moss : Vine), tr, new Vector3(sx * 1.24f + R(rng, -.1f, .1f), R(rng, .3f, 1.6f), R(rng, -.12f, .12f)), Quaternion.Euler(R(rng, -50, 20), R(rng, 0, 360), R(rng, -30, 30)), Vector3.one * R(rng, .12f, .2f));
        }
        Trough(tr.position + tr.right * 3.3f + tr.forward * .8f, tr, seed);
        Wildflowers(tr.position + new Vector3(1.9f, 0, 1.9f), tr, seed + 3, 7);
        Wildflowers(tr.position + new Vector3(-2.0f, 0, -1.6f), tr, seed + 4, 6);
        return root;
    }

    // ================================================================== FARM PLOT
    public class FaceCamera : MonoBehaviour
    {
        Transform cam;
        void LateUpdate()
        {
            if (!cam) { var c = Camera.main; if (!c) return; cam = c.transform; }
            Vector3 d = transform.position - cam.position; d.y = 0;
            if (d.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// A farm plot of raised beds in a tidy grid: timber sleepers or a mossy dry-stone frame with corner posts, ridged soil
    /// the state darkens when watered, crop cells the FarmPlotState scales as they grow (with fruit on ripe), and a
    /// sign post with paper need-cards (water / hoe / seeds / done) that always face the camera.
    /// </summary>
    public static GameObject FarmPlot(Vector3 pos, float yaw, Vector2 size, Crop crop, bool stoneBorder, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = Root("FarmPlot_" + crop, pos, parent); var tr = root.transform; tr.rotation = Quaternion.Euler(0, yaw, 0);
        bool blight = crop == Crop.Blighted; var id = Quaternion.identity;
        var state = root.AddComponent<FarmPlotState>(); state.size = size; state.crop = crop; state.blighted = blight;
        var box = ChamferBox(.22f); var soft = ChamferBox(.34f);
        float hx = size.x * .5f, hz = size.y * .5f;

        // swept earth pad, slightly wider than the frame
        Part("Pad", ChamferBox(.08f), Mat(blight ? new Color(.55f, .45f, .33f) : SandyPad, default, .3f, .04f), tr, new Vector3(0, .02f, 0), id, new Vector3(size.x + 1.2f, .05f, size.y + 1.2f));

        // ---- frame
        var border = stoneBorder ? Mat(Stone) : Mat(Timber); var borderD = Mat(TimberDark); var mossM = Mat(Moss); var stoneL = Mat(new Color(.68f, .65f, .6f));
        void Edge(Vector3 c, float len, bool alongX)
        {
            if (stoneBorder)
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(len / .4f));
                for (int i = 0; i < n; i++)
                {
                    float k = Mathf.Lerp(-len * .5f, len * .5f, (i + .5f) / n);
                    Vector3 p = c + (alongX ? new Vector3(k, 0, 0) : new Vector3(0, 0, k));
                    Part("WallStone", Blob(rng.Next(), .2f), i % 4 == 0 ? mossM : (i % 2 == 0 ? border : stoneL), tr, p + Vector3.up * .22f, Quaternion.Euler(R(rng, -5, 5), R(rng, 0, 360), R(rng, -5, 5)), new Vector3(.27f, .22f, .24f));
                }
            }
            else
            {
                Part("Sleeper", box, border, tr, c + Vector3.up * .2f, id, alongX ? new Vector3(len + .35f, .3f, .24f) : new Vector3(.24f, .3f, len + .35f));
                int nails = Mathf.Max(2, Mathf.RoundToInt(len / 1.1f));
                for (int i = 0; i < nails; i++)
                {
                    float k = Mathf.Lerp(-len * .45f, len * .45f, nails == 1 ? .5f : i / (float)(nails - 1));
                    Part("Nail", Blob(7, .05f, 3, 5), Mat(Iron), tr, c + (alongX ? new Vector3(k, .3f, 0) : new Vector3(0, .3f, k)), id, Vector3.one * .025f);
                }
            }
        }
        Edge(new Vector3(0, 0, hz + .15f), size.x, true); Edge(new Vector3(0, 0, -hz - .15f), size.x, true);
        Edge(new Vector3(hx + .15f, 0, 0), size.y, false); Edge(new Vector3(-hx - .15f, 0, 0), size.y, false);
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 c = new Vector3(sx * (hx + .15f), 0, sz * (hz + .15f));
                if (stoneBorder) Part("Pillar", Blob(rng.Next(), .15f, 4, 7), stoneL, tr, c + Vector3.up * .32f, Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(.24f, .36f, .24f));
                else
                {
                    // Tall, chunky fence posts with rounded caps (Images 3 & 4 style).
                    Part("CornerPost", box, borderD, tr, c + Vector3.up * .55f, id, new Vector3(.22f, 1.1f, .22f));
                    Part("PostCap", Blob(rng.Next(), .06f, 4, 7), borderD, tr, c + Vector3.up * 1.18f, id, new Vector3(.16f, .10f, .16f));
                    Part("PostRing", box, borderD, tr, c + Vector3.up * 1.02f, id, new Vector3(.26f, .04f, .26f));
                }
            }

        // ---- raised beds in a grid
        int cols = Mathf.Clamp(Mathf.RoundToInt(size.x / 1.7f), 1, 4), rows = Mathf.Clamp(Mathf.RoundToInt(size.y / 1.5f), 1, 3);
        float gap = .22f, innerX = size.x - .3f, innerZ = size.y - .3f;
        float cw = (innerX - gap * (cols - 1)) / cols, cd = (innerZ - gap * (rows - 1)) / rows;
        var soilMat = Mat(SoilWarm, default, .3f, .04f); var ridgeMat = Mat(new Color(.4f, .27f, .17f), default, .35f, .04f);
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
            {
                float bx = -innerX * .5f + cw * .5f + cx * (cw + gap), bz = -innerZ * .5f + cd * .5f + cz * (cd + gap);
                var bed = Part("Soil", soft, soilMat, tr, new Vector3(bx, .17f, bz), id, new Vector3(cw, .28f, cd));
                state.soils.Add(bed.GetComponent<Renderer>());
                int nr = Mathf.Max(2, Mathf.RoundToInt(cw / .5f));
                for (int r = 0; r < nr; r++)
                {
                    float rx = bx + Mathf.Lerp(-cw * .5f + .2f, cw * .5f - .2f, r / (float)(nr - 1));
                    float rh = .1f;
                    var pivot = new GameObject("Ridge"); pivot.transform.SetParent(tr, false);
                    pivot.transform.localPosition = new Vector3(rx, .28f, bz); pivot.transform.localScale = new Vector3(.2f, rh, cd * .86f);
                    pivot.AddComponent<RidgeBase>().h = rh;
                    var m = new GameObject("RidgeMesh"); m.transform.SetParent(pivot.transform, false); m.transform.localPosition = new Vector3(0, .5f, 0);
                    m.AddComponent<MeshFilter>().sharedMesh = ChamferBox(.4f); m.AddComponent<MeshRenderer>().sharedMaterial = ridgeMat;
                    state.ridges.Add(pivot.transform);
                    if (crop == Crop.Beans || crop == Crop.Sunflower && false) continue;
                    int nz = Mathf.Max(1, Mathf.RoundToInt(cd * .86f / .56f));
                    for (int i = 0; i < nz; i++)
                    {
                        float z = nz == 1 ? bz : bz + Mathf.Lerp(-cd * .38f, cd * .38f, i / (float)(nz - 1));
                        if (crop == Crop.Wheat && r % 2 == 1) continue;   // wheat is sown in denser tufts
                        state.cells.Add(MakeCrop(tr, crop, new Vector3(rx + R(rng, -.04f, .04f), .34f, z + R(rng, -.05f, .05f)), rng));
                    }
                }
                if (crop == Crop.Beans) state.cells.Add(BeanTrellis(tr, new Vector3(bx, .3f, bz), cd - .2f, rng));
            }
        if (blight) root.AddComponent<BlightedPatch>();

        // ---- sign post with need cards that face the camera
        var sign = new GameObject("Sign"); sign.transform.SetParent(tr, false); sign.transform.localPosition = new Vector3(-hx - .6f, 0, -hz - .4f);
        var wood = Mat(new Color(.52f, .37f, .25f), default, .3f);
        Cyl(sign.transform, "SignPost", wood, Vector3.zero, new Vector3(0, 1.0f, 0), .045f, 6);
        Part("SignBoard", box, Mat(new Color(.85f, .78f, .6f)), sign.transform, new Vector3(0, .9f, 0), id, new Vector3(.46f, .26f, .05f));
        var icons = new GameObject("Icons"); icons.transform.SetParent(sign.transform, false); icons.transform.localPosition = new Vector3(0, 1.45f, 0);
        icons.AddComponent<FaceCamera>();
        state.iconWater = Icon(icons.transform, 0); state.iconTill = Icon(icons.transform, 1); state.iconSow = Icon(icons.transform, 2); state.iconCheck = Icon(icons.transform, 3);
        state.sign = sign.transform;

        // ---- water pump (Images 3 & 4) - hand pump beside the plot
        AddWaterPump(tr, new Vector3(hx + 1.1f, 0, hz + .7f), rng);
        // ---- stump with axe stuck in it (Image 3)
        AddStumpWithAxe(tr, new Vector3(-hx - .9f, 0, -hz - .9f), rng);

        return root;
    }

    // ------------------------------------------------------------------ pump & stump helpers
    static void AddWaterPump(Transform parent, Vector3 lp, System.Random rng)
    {
        var pump = new GameObject("WaterPump"); pump.transform.SetParent(parent, false);
        pump.transform.localPosition = lp;
        var iron = Mat(new Color(.55f, .45f, .35f), new Color(.25f, .18f, .12f), .2f, .15f);
        var redIron = Mat(new Color(.62f, .2f, .22f), new Color(.3f, .1f, .12f), .2f, .15f);
        var wood = Mat(new Color(.5f, .38f, .26f));
        var id = Quaternion.identity;
        var box = ChamferBox(.18f);
        // base
        Part("PumpBase", box, iron, pump.transform, new Vector3(0, .12f, 0), id, new Vector3(.45f, .22f, .45f));
        // vertical column
        Part("PumpColumn", box, redIron, pump.transform, new Vector3(0, .62f, 0), id, new Vector3(.12f, 1.05f, .12f));
        // spout
        Cyl(pump.transform, "PumpSpout", iron, new Vector3(.18f, 1.05f, 0), new Vector3(.55f, 1.05f, 0), .04f, 6);
        // handle pivot
        Part("PumpPivot", box, iron, pump.transform, new Vector3(0, 1.55f, 0), id, new Vector3(.10f, .10f, .10f));
        // handle arm (angled up)
        Cyl(pump.transform, "PumpHandle", redIron, new Vector3(0, 1.55f, 0), new Vector3(-.45f, 2.05f, 0), .045f, 6);
        Part("HandleGrip", box, wood, pump.transform, new Vector3(-.45f, 2.12f, 0), id, new Vector3(.05f, .1f, .15f));
        // bucket on the ground
        var bucket = new GameObject("Bucket"); bucket.transform.SetParent(pump.transform, false);
        bucket.transform.localPosition = new Vector3(-.15f, .02f, .3f);
        Part("BucketBody", Ring(12, .82f), wood, bucket.transform, new Vector3(0, .09f, 0), id, new Vector3(.14f, .16f, .14f));
        Part("BucketWater", Disc(3, 0, 10), Mat(new Color(.45f, .7f, .85f)), bucket.transform, new Vector3(0, .02f, 0), id, new Vector3(.12f, 1, .12f));
        Part("BucketHandle", box, iron, bucket.transform, new Vector3(0, .20f, 0), id, new Vector3(.16f, .02f, .02f));
    }

    static void AddStumpWithAxe(Transform parent, Vector3 lp, System.Random rng)
    {
        var stump = new GameObject("Stump"); stump.transform.SetParent(parent, false);
        stump.transform.localPosition = lp;
        var bark = Mat(new Color(.38f, .28f, .20f), default, .35f, .1f);
        var woodCut = Mat(new Color(.72f, .52f, .38f), default, .3f, .12f);
        var iron = Mat(Iron);
        var wood = Mat(new Color(.42f, .28f, .18f));
        var id = Quaternion.identity;
        var box = ChamferBox(.16f);
        // stump
        Part("StumpBase", box, bark, stump.transform, new Vector3(0, .16f, 0), id, new Vector3(.55f, .32f, .55f));
        Part("StumpTop", Disc(rng.Next(), .05f, 10), woodCut, stump.transform, new Vector3(0, .34f, 0), id, new Vector3(.5f, 1, .5f));
        // axe head stuck in the top
        Part("AxeHead", box, iron, stump.transform, new Vector3(0, .40f, .18f), Quaternion.Euler(10, 0, 25), new Vector3(.28f, .06f, .12f));
        Part("AxeBlade", box, iron, stump.transform, new Vector3(0, .40f, .30f), Quaternion.Euler(10, 0, 25), new Vector3(.14f, .14f, .03f));
        // axe handle sticking out
        Cyl(stump.transform, "AxeHandle", wood, new Vector3(0, .40f, .20f), new Vector3(0, .85f, .55f), .035f, 6);
        // little chips around
        for (int i = 0; i < 4; i++)
        {
            float a = (float)(rng.NextDouble() * 6.28), r = (float)(rng.NextDouble() * .3 + .1);
            Part("Chip", Blob(rng.Next(), .15f, 3, 5), woodCut, stump.transform, new Vector3(Mathf.Cos(a) * r, .36f, Mathf.Sin(a) * r), Quaternion.Euler(0, (float)(rng.NextDouble() * 360), 0), new Vector3(.08f, .04f, .08f));
        }
    }

    static GameObject Icon(Transform parent, int kind)
    {
        var g = new GameObject("Icon" + kind); g.transform.SetParent(parent, false);
        var box = ChamferBox(.25f); Quaternion id = Quaternion.identity;
        Part("Card", box, Mat(new Color(.95f, .92f, .82f), new Color(.1f, .09f, .07f), 0, .1f), g.transform, Vector3.zero, id, new Vector3(.5f, .5f, .05f));
        Vector3 f = new Vector3(0, 0, -.04f);   // glyphs sit on the camera-facing side
        switch (kind)
        {
            case 0:   // water drop
                Part("Drop", Blob(31, .02f, 4, 8), Mat(new Color(.3f, .62f, .92f), new Color(.05f, .1f, .18f), 0, .1f), g.transform, f + new Vector3(0, -.04f, 0), id, new Vector3(.11f, .13f, .04f));
                Part("DropTip", ChamferBox(.4f), Mat(new Color(.3f, .62f, .92f), new Color(.05f, .1f, .18f), 0, .1f), g.transform, f + new Vector3(0, .1f, 0), Quaternion.Euler(0, 0, 45), new Vector3(.07f, .07f, .035f));
                break;
            case 1:   // hoe
                Part("HoeShaft", box, Mat(new Color(.6f, .42f, .25f)), g.transform, f + new Vector3(.02f, 0, 0), Quaternion.Euler(0, 0, 25), new Vector3(.04f, .34f, .03f));
                Part("HoeBlade", box, Mat(new Color(.5f, .52f, .56f)), g.transform, f + new Vector3(-.07f, -.15f, 0), Quaternion.Euler(0, 0, 25), new Vector3(.13f, .05f, .03f));
                break;
            case 2:   // seed pouch
                Part("Pouch", Blob(33, .06f, 4, 8), Mat(new Color(.78f, .62f, .38f)), g.transform, f + new Vector3(0, -.03f, 0), id, new Vector3(.12f, .12f, .04f));
                Part("PouchTie", box, Mat(new Color(.5f, .35f, .2f)), g.transform, f + new Vector3(0, .07f, 0), id, new Vector3(.08f, .03f, .035f));
                for (int i = 0; i < 3; i++) Part("Seed", Blob(i, .05f, 3, 5), Mat(new Color(.4f, .3f, .15f)), g.transform, f + new Vector3(-.08f + i * .08f, -.17f, 0), id, Vector3.one * .02f);
                break;
            default:  // tick
                Part("TickA", box, Mat(new Color(.3f, .7f, .32f), new Color(.04f, .12f, .05f), 0, .1f), g.transform, f + new Vector3(-.06f, -.03f, 0), Quaternion.Euler(0, 0, -45), new Vector3(.05f, .15f, .03f));
                Part("TickB", box, Mat(new Color(.3f, .7f, .32f), new Color(.04f, .12f, .05f), 0, .1f), g.transform, f + new Vector3(.05f, .02f, 0), Quaternion.Euler(0, 0, 40), new Vector3(.05f, .27f, .03f));
                break;
        }
        g.SetActive(false);
        return g;
    }

    // ------------------------------------------------------------------ crops
    static FarmPlotState.CropCell MakeCrop(Transform parent, Crop crop, Vector3 lp, System.Random rng)
    {
        var g = new GameObject("Plant_" + crop); g.transform.SetParent(parent, false);
        g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(0, R(rng, 0, 360), 0);
        var gt = g.transform; Quaternion id = Quaternion.identity;
        var cell = new FarmPlotState.CropCell { root = gt, jitter = R(rng, -1, 1) };
        var fruit = new GameObject("Fruit"); fruit.transform.SetParent(gt, false); cell.fruit = fruit.transform; var ft = fruit.transform;
        switch (crop)
        {
            case Crop.Cabbage:
                {
                    float s = R(rng, .24f, .3f);
                    Part("Core", Blob(rng.Next(), .07f), Mat(Cabbage), gt, new Vector3(0, s * .75f, 0), id, Vector3.one * s);
                    for (int i = 0; i < 7; i++)
                        Part("Leaf", Leaf(), Mat(i % 2 == 0 ? CabbageDark : Cabbage), gt, new Vector3(0, .04f, 0), Quaternion.Euler(-26 - (i % 2) * 14, i * 51 + R(rng, -8, 8), 0), Vector3.one * s * R(rng, 1.5f, 1.9f));
                    Part("Head", Blob(rng.Next(), .06f), Mat(new Color(.7f, .85f, .5f)), ft, new Vector3(0, s * .95f, 0), id, Vector3.one * s * 1.12f);
                    break;
                }
            case Crop.Pumpkin:
                for (int i = 0; i < 5; i++) Part("Leaf", Leaf(), Mat(Vine), gt, new Vector3(0, .05f, 0), Quaternion.Euler(-34, i * 72 + R(rng, -10, 10), 0), Vector3.one * R(rng, .34f, .46f));
                Part("Pumpkin", Blob(rng.Next(), .06f, 4, 9), Mat(Pumpkin), ft, new Vector3(.2f, .15f, .08f), Quaternion.Euler(0, R(rng, 0, 360), 0), new Vector3(.3f, .21f, .3f));
                Part("Stem", ChamferBox(.2f), Mat(Vine), ft, new Vector3(.2f, .33f, .08f), Quaternion.Euler(R(rng, -15, 15), 0, R(rng, -15, 15)), new Vector3(.05f, .1f, .05f));
                break;
            case Crop.Turnip:
                for (int i = 0; i < 6; i++) Part("Leaf", Leaf(), Mat(new Color(.42f, .62f, .3f)), gt, new Vector3(0, .05f, 0), Quaternion.Euler(-48, i * 60 + R(rng, -12, 12), 0), Vector3.one * R(rng, .24f, .32f));
                Part("Root", Blob(rng.Next(), .06f), Mat(new Color(.9f, .8f, .86f)), ft, new Vector3(0, .04f, 0), id, new Vector3(.11f, .1f, .11f));
                Part("RootTop", Blob(rng.Next(), .05f, 3, 7), Mat(new Color(.72f, .42f, .62f)), ft, new Vector3(0, .1f, 0), id, new Vector3(.1f, .06f, .1f));
                break;
            case Crop.Carrot:
                for (int i = 0; i < 9; i++) Part("Leaf", Leaf(), Mat(new Color(.36f, .62f, .28f)), gt, new Vector3(0, .04f, 0), Quaternion.Euler(-62 + R(rng, -8, 8), i * 40 + R(rng, -10, 10), 0), new Vector3(.12f, .12f, .46f) * R(rng, .8f, 1.15f));
                Part("Carrot", Blob(rng.Next(), .05f, 3, 6), Mat(new Color(.96f, .55f, .16f)), ft, new Vector3(0, .05f, 0), id, new Vector3(.05f, .12f, .05f));
                Part("CarrotCrown", Ring(6, 0), Mat(new Color(.9f, .48f, .14f)), ft, new Vector3(0, .1f, 0), id, new Vector3(.05f, .03f, .05f));
                break;
            case Crop.Tomato:
                Cyl(gt, "Stake", Mat(Bamboo), Vector3.zero, new Vector3(0, .95f, 0), .018f, 5);
                for (int i = 0; i < 7; i++) Part("Leaf", Leaf(), Mat(new Color(.34f, .58f, .26f)), gt, new Vector3(0, .12f + i * .11f, 0), Quaternion.Euler(-30 + R(rng, -14, 14), i * 97 + R(rng, -20, 20), 0), Vector3.one * R(rng, .24f, .34f));
                for (int i = 0; i < 4; i++) Part("Tomato", Blob(rng.Next(), .04f, 3, 7), Mat(new Color(.86f, .2f, .16f), new Color(.08f, .01f, .01f), .1f, .2f), ft, new Vector3(R(rng, -.12f, .12f), .22f + i * .13f, R(rng, -.12f, .12f)), id, Vector3.one * .07f);
                break;
            case Crop.Wheat:
                {
                    var stalkM = Mat(new Color(.72f, .72f, .36f)); var head = Mat(new Color(.92f, .76f, .3f), default, .3f, .1f);
                    for (int i = 0; i < 11; i++)
                    {
                        float a = R(rng, 0, 6.28f), r = R(rng, 0, .12f), tilt = R(rng, .02f, .12f);
                        Vector3 b0 = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), b1 = b0 + new Vector3(Mathf.Cos(a) * tilt, .78f, Mathf.Sin(a) * tilt);
                        Cyl(gt, "Stalk", stalkM, b0, b1, .011f, 4);
                        Part("Ear", Blob(rng.Next(), .06f, 3, 5), head, ft, b1 + Vector3.up * .04f, Quaternion.Euler(R(rng, -10, 10), 0, R(rng, -10, 10)), new Vector3(.028f, .1f, .028f));
                    }
                    break;
                }
            case Crop.Sunflower:
                Cyl(gt, "Stalk", Mat(Vine), Vector3.zero, new Vector3(0, .95f, 0), .03f, 5);
                for (int i = 0; i < 4; i++) Part("Leaf", Leaf(), Mat(new Color(.3f, .52f, .24f)), gt, new Vector3(0, .2f + i * .15f, 0), Quaternion.Euler(-35, i * 90 + R(rng, -15, 15), 0), Vector3.one * R(rng, .24f, .34f));
                Part("Petals", Disc(rng.Next(), .12f, 14), Mat(new Color(.98f, .8f, .18f), new Color(.1f, .07f, 0), .1f, .2f), ft, new Vector3(0, .98f, .04f), Quaternion.Euler(-62, 0, 0), new Vector3(.3f, 1, .3f));
                Part("Disc", Ring(10, 0), Mat(new Color(.36f, .22f, .12f)), ft, new Vector3(0, .99f, .03f), Quaternion.Euler(-62, 0, 0), new Vector3(.15f, .05f, .15f));
                break;
            default:   // blighted
                for (int i = 0; i < 4; i++) Part("Leaf", Leaf(), Mat(new Color(.45f, .4f, .26f)), gt, new Vector3(0, .04f, 0), Quaternion.Euler(-40 + R(rng, -14, 14), i * 90 + R(rng, -20, 20), 0), Vector3.one * R(rng, .16f, .26f));
                Cyl(gt, "DeadStalk", Mat(new Color(.4f, .32f, .2f)), Vector3.zero, new Vector3(R(rng, -.05f, .05f), .3f, R(rng, -.05f, .05f)), .015f, 4);
                break;
        }
        return cell;
    }

    static FarmPlotState.CropCell BeanTrellis(Transform parent, Vector3 lp, float length, System.Random rng)
    {
        var bam = Mat(Bamboo); var vine = Mat(Vine); Quaternion id = Quaternion.identity;
        var g = new GameObject("BeanFrame"); g.transform.SetParent(parent, false); g.transform.localPosition = lp;
        int pairs = Mathf.Max(2, Mathf.RoundToInt(length / 1.1f));
        for (int i = 0; i < pairs; i++)
        {
            float z = Mathf.Lerp(-length * .5f, length * .5f, i / (float)(pairs - 1));
            Cyl(g.transform, "PoleA", bam, new Vector3(-.3f, 0, z), new Vector3(-.05f, 1.55f, z), .02f, 5);
            Cyl(g.transform, "PoleB", bam, new Vector3(.3f, 0, z), new Vector3(.05f, 1.55f, z), .02f, 5);
        }
        Cyl(g.transform, "RidgePole", bam, new Vector3(0, 1.55f, -length * .5f), new Vector3(0, 1.55f, length * .5f), .018f, 5);
        var vines = new GameObject("Vines"); vines.transform.SetParent(g.transform, false);
        var cell = new FarmPlotState.CropCell { root = vines.transform, jitter = R(rng, -1, 1) };
        int n = Mathf.RoundToInt(length * 11);
        for (int i = 0; i < n; i++)
        {
            float z = R(rng, -length * .5f, length * .5f), h = R(rng, .1f, 1.45f), side = rng.NextDouble() < .5 ? -1 : 1;
            Part("VineLeaf", Leaf(), vine, vines.transform, new Vector3(side * (.3f - h * .16f), h, z), Quaternion.Euler(R(rng, -60, 20), R(rng, 0, 360), R(rng, -30, 30)), Vector3.one * R(rng, .13f, .22f));
        }
        var fruit = new GameObject("Fruit"); fruit.transform.SetParent(vines.transform, false); cell.fruit = fruit.transform;
        for (int i = 0; i < Mathf.RoundToInt(length * 6); i++)
            Part("Pod", ChamferBox(.4f), Mat(new Color(.5f, .75f, .3f)), fruit.transform, new Vector3((rng.NextDouble() < .5 ? -1 : 1) * R(rng, .05f, .22f), R(rng, .3f, 1.2f), R(rng, -length * .5f, length * .5f)), Quaternion.Euler(R(rng, -20, 20), 0, R(rng, -20, 20)), new Vector3(.025f, .14f, .025f));
        return cell;
    }

    // ================================================================== PATH (shares the island road shader)
    static Material pathMat;
    public static Material PathMaterial()
    {
        if (pathMat) return pathMat;
        var sh = Shader.Find("Island/GhibliRoad");
        if (!sh) return Mat(Mud, default, .35f, .05f);
        pathMat = new Material(sh) { name = "VillagePath" };
        pathMat.SetColor("_Tint", new Color(.88f,.70f,.43f));
        pathMat.SetColor("_EdgeColor", new Color(.70f,.62f,.36f));
        pathMat.SetFloat("_ToneStrength", .12f);
        pathMat.SetFloat("_BrushStrength", .10f);
        return pathMat;
    }

    /// <summary>Warm sandy footpath draped over the terrain along a polyline: scalloped verge, worn centre, speckled pebbles
    /// (from the road shader), grass tufts along the edges and the occasional puddle.</summary>
    public static GameObject Path(IList<Vector3> pts, float width, Transform parent, int seed = 1, float puddleChance = .35f)
    {
        var rng = new System.Random(seed);
        var root = Root("Path", pts[0], parent); root.transform.position = Vector3.zero;
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>(); var cols = new List<Color>();
        var tuft = Mat(new Color(.4f, .64f, .3f)); var tuftD = Mat(new Color(.3f, .52f, .24f));
        float acc = 0; Color terr = new Color(.42f, .6f, .27f, 0f);
        for (int s = 0; s < pts.Count - 1; s++)
        {
            Vector3 a = pts[s], b = pts[s + 1];
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / .6f));
            for (int i = 0; i <= n; i++)
            {
                if (s > 0 && i == 0) continue;
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                Vector3 dir = (b - a); dir.y = 0; dir.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, dir);
                float w = width * 1.35f * (.88f + .3f * Mathf.PerlinNoise(acc * .25f, seed));
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 q = p + side * sgn * w * .5f; q = Ground(q) + Vector3.up * .035f;
                    v.Add(q); uv.Add(new Vector2(sgn, acc / 3f)); cols.Add(terr);
                }
                acc += .6f;
                if (v.Count >= 4) { int k = v.Count - 4; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
                if (rng.NextDouble() < puddleChance * .2f) Puddle(p + side * R(rng, -w * .25f, w * .25f), root.transform, rng.Next(), R(rng, .35f, .75f));
                if (rng.NextDouble() < .32f)
                {
                    float sg = rng.NextDouble() < .5 ? -1 : 1; Vector3 tp = p + side * sg * (w * .5f + R(rng, .02f, .2f));
                    var tg = new GameObject("Tuft"); tg.transform.SetParent(root.transform, false); tg.transform.position = Ground(tp);
                    for (int k2 = 0; k2 < 3; k2++) Part("Blade", Leaf(), k2 % 2 == 0 ? tuft : tuftD, tg.transform, Vector3.zero, Quaternion.Euler(-72 + R(rng, -10, 10), k2 * 120 + R(rng, -30, 30), 0), Vector3.one * R(rng, .14f, .24f));
                }
            }
        }
        var mesh = new Mesh { name = "Path" }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, uv); mesh.SetColors(cols); mesh.RecalculateNormals();
        var nn = mesh.normals; if (nn.Length > 0 && nn[0].y < 0) { var tt = mesh.triangles; System.Array.Reverse(tt); mesh.triangles = tt; mesh.RecalculateNormals(); }
        mesh.RecalculateBounds();
        var g = new GameObject("PathSurface"); g.transform.SetParent(root.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = PathMaterial();
        return root;
    }
}
