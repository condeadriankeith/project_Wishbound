using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Debug/validation data stored on every lot group created by VillageComposer.</summary>
public class VillageLot : MonoBehaviour
{
    public int lot, settlement, doorSide, styleVariant;
    public string role, house;
    public float scale, relief, plinthDrop, uncoveredGap;
    public bool fenced;
}

/// <summary>
/// Phase 5 village composer for the Low Poly Medieval Starter Pack.
///
/// Reads the existing, validated data (ZoneMap lots -> SettlementMap massing/anchors -> PadGrade pads -> LaneNet doors)
/// and turns each lot into a small, terrain-conforming compound:
///   * the house is picked per lot (weighted by settlement role, floors, lot fit, and what its neighbours already use),
///     scaled to the lot, rotated so its door (+Z) faces the lane door, and pushed flush to the lane-facing side;
///   * terrain is sampled on the RENDERED mesh (TerrainMath.Hm) over the whole footprint; if the ground is not level a
///     procedural stone plinth follows the terrain down so nothing can float;
///   * yards get role-specific props (hay, barrels, crates, rocks), optional fences that follow the slope, and a gate
///     in front of the door; plazas and anchors (well, shrine, mill, tower + ruins) are dressed too.
/// Everything is seeded (Seed + lot index) so a rebuild is identical, while every lot differs from its neighbours.
/// MODEL SPACE conventions (same as SettlementMap/LaneNet): forward=(sin yaw, cos yaw), right=(cos yaw,-sin yaw), Z is
/// mirrored into Unity space by TerrainMath.U.
/// </summary>
public static class VillageComposer
{
    public static int Seed = 20260;
    public static string Report = "";

    const double FrontYard = 1.8;   // gap between lot front edge (fence/gate) and the house face when fenced

    struct Obb { public double x, z, hw, hd, yaw; }
    struct HouseRec { public string id; public double x, z; }

    sealed class YardCtx
    {
        public int lot; public Zone role; public Transform parent; public System.Random rng;
        public double cx, cz, face, fx, fz, rx, rz, halfLat, halfDepth, gateU;
        public Obb house;
        public readonly List<Vector2> placed = new List<Vector2>();
    }

    static VillageCatalog cat;
    static List<VillageCatalog.Entry> houseEntries;
    static VillageCatalog.Entry fenceE, gateE;
    static HashSet<long> laneCells;
    static List<SettlementMap.PublicCircle> publicCircles;
    static readonly List<Obb> obbs = new List<Obb>();
    static readonly List<HouseRec> houseRecs = new List<HouseRec>();
    static readonly Dictionary<long, Material> variantCache = new Dictionary<long, Material>();
    static readonly Dictionary<string, int> perType = new Dictionary<string, int>();
    static readonly List<string> notes = new List<string>();
    static Material plinthMat;
    static int nHouse, nFence, nGate, nProp, nPlinth, nSkippedLots, nSkippedProps, nAnchors, nFenced;
    static float maxRelief, maxUncovered; static double sumScale;

    // ---------- small helpers ----------
    static double Sn(double a) { return System.Math.Sin(a); }
    static double Cs(double a) { return System.Math.Cos(a); }
    static float G(double x, double z) { return TerrainMath.Hm(x, z); }
    static Vector3 UP(double x, float y, double z) { return TerrainMath.U(x, y, z); }
    static Quaternion Face(double yaw) { return Quaternion.LookRotation(TerrainMath.U(Sn(yaw), 0, Cs(yaw)), Vector3.up); }
    static long Key(int ix, int iz) { return ((long)ix << 32) ^ (long)(uint)iz; }
    static float Rf(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

    static bool Blocked(double x, double z)
    {
        if (laneCells.Contains(Key((int)System.Math.Floor(x), (int)System.Math.Floor(z)))) return true;
        if (RoadGrade.MaskAt(x, z) > 0.15) return true;
        if (RiverCarve.WetAt(x, z) > 0.25) return true;
        return G(x, z) < 1.0f;
    }

    static float Slope(double x, double z)
    {
        float dx = G(x + 0.5, z) - G(x - 0.5, z), dz = G(x, z + 0.5) - G(x, z - 0.5);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static bool FreeOfBuildings(double x, double z, double r)
    {
        foreach (var o in obbs)
            if (SettlementMap.ObbCircleOverlap(o.x, o.z, o.hw, o.hd, o.yaw, x, z, r, 0.4)) return false;
        return true;
    }

    // ---------- entry points ----------
    public static void RebuildInScene()
    {
        var old = GameObject.Find("Phase5Village");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("Phase5Village");
        Build(root.transform);
    }

    public static void Build(Transform parent)
    {
        if (!LoadCatalog()) return;
        EnsureData();
        ResetStats();

        foreach (var s in SettlementMap.Settlements)
        {
            var sg = new GameObject("S" + s.id + "_" + s.role + "_" + s.name.Replace(' ', '_'));
            sg.transform.SetParent(parent, false);
            int styleA = (s.id * 5 + 1) % 7 + 1, styleB = (s.id * 3 + 4) % 7 + 1;   // palette variants this settlement favours
            foreach (int li in s.lotIdx)
            {
                if (li < 0 || li >= SettlementMap.Lots.Length || SettlementMap.Lots[li].dropped) continue;
                BuildLot(li, s, sg.transform, styleA, styleB);
            }
            Dress(s, sg.transform);
        }
        Report = MakeReport();
    }

    static bool LoadCatalog()
    {
        cat = VillageCatalog.Load();
        if (cat == null || cat.entries.Count == 0)
        {
            Debug.LogError("VillageComposer: VillageCatalog missing. Run menu Island > Village > Build Catalog first.");
            return false;
        }
        houseEntries = cat.Of(VKind.House);
        fenceE = cat.Get("WoodenFence"); gateE = cat.Get("WoodenGate");
        return houseEntries.Count > 0;
    }

    static void EnsureData()
    {
        var ib = Object.FindFirstObjectByType<IslandBuilder>();
        if (IslandBuilder.HGrid == null && ib != null) ib.Build();
        if (!SettlementMap.Built) SettlementMap.Build();
        if (PadGrade.Pads.Count == 0) PadGrade.Build();
        if (!LaneNet.Built) LaneNet.Build();

        laneCells = new HashSet<long>();
        foreach (var lane in LaneNet.Lanes)
            for (int i = 0; i < lane.x.Length; i++)
            {
                int cx = (int)System.Math.Floor(lane.x[i]), cz = (int)System.Math.Floor(lane.z[i]);
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) laneCells.Add(Key(cx + a, cz + b));
            }
        publicCircles = SettlementMap.PublicCircles();
    }

    static void ResetStats()
    {
        obbs.Clear(); houseRecs.Clear(); perType.Clear(); notes.Clear();
        nHouse = nFence = nGate = nProp = nPlinth = nSkippedLots = nSkippedProps = nAnchors = nFenced = 0;
        maxRelief = 0f; maxUncovered = 0f; sumScale = 0;
    }

    static void Count(string id) { nProp++; perType[id] = perType.ContainsKey(id) ? perType[id] + 1 : 1; }

    // ---------- lots / houses ----------
    static void BuildLot(int li, SettlementMap.Settlement s, Transform parent, int styleA, int styleB)
    {
        var m = SettlementMap.Lots[li];
        var door = (LaneNet.Doors != null && li < LaneNet.Doors.Length) ? LaneNet.Doors[li] : default(LaneNet.Door);
        var rng = new System.Random(Seed * 7919 + li * 104729 + 17);

        int side = door.ok ? Mathf.Clamp(door.side, 0, 3) : 0;
        double face = m.yaw + side * System.Math.PI * 0.5;          // direction the door points (model space)
        bool alongD = side % 2 == 0;
        double halfDepth = (alongD ? m.footprintD : m.footprintW) * 0.5;
        double halfLat = (alongD ? m.footprintW : m.footprintD) * 0.5;
        double fx = Sn(face), fz = Cs(face), rx = Cs(face), rz = -Sn(face);

        float pFence = s.role == Zone.Farm ? 0.85f : s.role == Zone.Hamlet ? 0.55f : s.role == Zone.Summit ? 0.3f : 0.2f;
        bool fence = rng.NextDouble() < pFence && halfDepth * 2 > 9.5 && halfLat * 2 > 8.0;

        float scale;
        var e = ChooseHouse(s, m.floors, m.x, m.z, halfLat * 2, halfDepth * 2 - (fence ? FrontYard : 0), rng, out scale);
        if (e == null && fence) { fence = false; e = ChooseHouse(s, m.floors, m.x, m.z, halfLat * 2, halfDepth * 2, rng, out scale); }
        if (e == null) { nSkippedLots++; notes.Add("lot " + li + " too small for any house"); return; }

        double frontYard = fence ? FrontYard : 0.0;
        double hw = e.footprint.x * 0.5 * scale, hd = e.footprint.y * 0.5 * scale;

        // door lines up with the lane end point; face sits flush on the lane-facing side of the lot
        double doorLat = door.ok ? (door.x - m.x) * rx + (door.z - m.z) * rz : 0.0;
        double slack = System.Math.Max(0.0, halfLat - hw - 0.15);
        double lat = System.Math.Max(-slack, System.Math.Min(slack, doorLat));
        double hcx = m.x + fx * (halfDepth - frontYard - hd) + rx * lat;
        double hcz = m.z + fz * (halfDepth - frontYard - hd) + rz * lat;

        var lotGO = new GameObject("Lot_" + li);
        lotGO.transform.SetParent(parent, false);

        float relief, gap; bool plinth; Obb obb;
        PlaceBuilding(e, lotGO.transform, hcx, hcz, face, scale, rng, styleA, styleB, out relief, out plinth, out gap, out obb);

        var vl = lotGO.AddComponent<VillageLot>();
        vl.lot = li; vl.settlement = s.id; vl.role = s.role.ToString(); vl.house = e.id; vl.scale = scale;
        vl.relief = relief; vl.doorSide = side; vl.fenced = fence; vl.uncoveredGap = gap; vl.plinthDrop = plinth ? relief : 0f;

        nHouse++; sumScale += scale; if (fence) nFenced++;
        perType[e.id] = perType.ContainsKey(e.id) ? perType[e.id] + 1 : 1;
        houseRecs.Add(new HouseRec { id = e.id, x = hcx, z = hcz });

        var y = new YardCtx
        {
            lot = li, role = s.role, parent = lotGO.transform, rng = rng, cx = m.x, cz = m.z, face = face,
            fx = fx, fz = fz, rx = rx, rz = rz, halfLat = halfLat, halfDepth = halfDepth, house = obb,
            gateU = System.Math.Max(-(halfLat - 1.4), System.Math.Min(halfLat - 1.4, doorLat)),
        };
        Yard(y, fence);
    }

    static float Affinity(Zone role, string id)
    {
        bool small = id == "SmallMedievalHouse", mid = id == "MedievalHouse", two = id == "TwoStoryMedievalHouse";
        switch (role)
        {
            case Zone.Farm:    return small ? 1.6f : mid ? 1.1f : two ? 0.35f : 0.5f;
            case Zone.Harbour: return small ? 0.6f : mid ? 1.0f : two ? 1.3f : 1.2f;
            case Zone.Hamlet:  return small ? 0.9f : mid ? 1.1f : 1.0f;
            default:           return small ? 1.4f : mid ? 1.0f : 0.5f;
        }
    }

    static VillageCatalog.Entry ChooseHouse(SettlementMap.Settlement s, int floors, double cx, double cz, double availLat, double availDepth, System.Random rng, out float scale)
    {
        scale = 1f;
        var cand = new List<VillageCatalog.Entry>(); var ws = new List<float>(); var fits = new List<float>(); float total = 0f;
        int want = Mathf.Min(floors, 2);
        foreach (var e in houseEntries)
        {
            float fit = (float)System.Math.Min(1.25, System.Math.Min(availLat * 0.96 / e.footprint.x, availDepth * 0.96 / e.footprint.y));
            if (fit < 0.8f) continue;
            float w = e.weight * Affinity(s.role, e.id);
            w *= (e.floors == want) ? 1.6f : 0.55f;                                         // match the planned floor count
            w *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(Mathf.Abs(fit - 1.05f) / 0.35f));      // prefer houses that fill the lot without distortion
            int near = 0;
            foreach (var r in houseRecs)
                if (r.id == e.id) { double dx = r.x - cx, dz = r.z - cz; if (dx * dx + dz * dz < 38.0 * 38.0) near++; }
            w *= Mathf.Pow(0.4f, near);                                                      // neighbours should differ
            cand.Add(e); ws.Add(w); fits.Add(fit); total += w;
        }
        if (cand.Count == 0) return null;

        float roll = (float)rng.NextDouble() * total; int pick = cand.Count - 1;
        for (int i = 0; i < cand.Count; i++) { roll -= ws[i]; if (roll <= 0f) { pick = i; break; } }

        bool big = floors >= 3 && cand[pick].floors == 2;
        scale = Mathf.Min(fits[pick], big ? Rf(rng, 1.1f, 1.25f) : Rf(rng, 0.96f, 1.16f));
        return cand[pick];
    }

    /// <summary>Places a building flush on the terrain: samples the rendered mesh over the footprint and adds a plinth where needed.</summary>
    static GameObject PlaceBuilding(VillageCatalog.Entry e, Transform parent, double cx, double cz, double face, float s, System.Random rng,
                                    int styleA, int styleB, out float relief, out bool plinth, out float uncoveredGap, out Obb obb)
    {
        double fx = Sn(face), fz = Cs(face), rx = Cs(face), rz = -Sn(face);
        double hw = e.footprint.x * 0.5 * s, hd = e.footprint.y * 0.5 * s;

        float mn = float.MaxValue, mx = float.MinValue;
        for (int a = -3; a <= 3; a++)
            for (int b = -3; b <= 3; b++)
            {
                double u = a / 3.0 * hw, v = b / 3.0 * hd;
                float h = G(cx + rx * u + fx * v, cz + rz * u + fz * v);
                if (h < mn) mn = h; if (h > mx) mx = h;
            }
        relief = mx - mn; if (relief > maxRelief) maxRelief = relief;

        float yBase = mn + Mathf.Min(0.5f * relief, 0.5f);       // never hover: the high side sinks in, the low side gets a plinth
        plinth = yBase - mn > 0.05f;

        var rot = Face(face);
        Vector3 pivot = UP(cx, yBase, cz) - rot * new Vector3(e.centerXZ.x * s, 0f, e.centerXZ.y * s) + Vector3.up * (-e.minY * s);
        if (plinth) { MakePlinth(parent, cx, cz, face, hw + 0.12, hd + 0.12, yBase); nPlinth++; }

        var go = Object.Instantiate(e.prefab, pivot, rot, parent);
        go.name = e.id; go.transform.localScale = Vector3.one * s;
        ApplyVariant(go, rng, styleA, styleB);

        var bc = go.AddComponent<BoxCollider>();
        bc.center = new Vector3(e.centerXZ.x, e.height * 0.5f + e.minY, e.centerXZ.y);
        bc.size = new Vector3(e.footprint.x, e.height, e.footprint.y);

        // truthful check: lowest rendered point vs the terrain under the footprint (a plinth covers any gap)
        float lowest = float.MaxValue;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) lowest = Mathf.Min(lowest, r.bounds.min.y);
        uncoveredGap = plinth ? 0f : Mathf.Max(0f, lowest - mn);
        if (uncoveredGap > maxUncovered) maxUncovered = uncoveredGap;

        obb = new Obb { x = cx, z = cz, hw = hw, hd = hd, yaw = face };
        obbs.Add(obb);
        return go;
    }

    // ---------- foundation mesh that follows the terrain ----------
    static Material PlinthMat()
    {
        if (plinthMat == null)
        {
            var toon = Shader.Find("Island/GhibliToon");
            plinthMat = new Material(toon != null ? toon : Shader.Find("Universal Render Pipeline/Lit")) { name = "VillagePlinth" };
            plinthMat.SetColor("_BaseColor", new Color(0.50f, 0.47f, 0.44f));
        }
        return plinthMat;
    }

    static void AddQuad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 tmp = b; b = d; d = tmp; }
        int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
    }

    static void AddTri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), Vector3.up) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
        int i = v.Count; v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    static void MakePlinth(Transform parent, double cx, double cz, double face, double hw, double hd, float yTop)
    {
        double fx = Sn(face), fz = Cs(face), rx = Cs(face), rz = -Sn(face);
        double[,] corner = { { -1, -1 }, { -1, 1 }, { 1, 1 }, { 1, -1 } };
        var ring = new List<double[]>();
        for (int k = 0; k < 4; k++)
        {
            double u0 = corner[k, 0] * hw, v0 = corner[k, 1] * hd, u1 = corner[(k + 1) % 4, 0] * hw, v1 = corner[(k + 1) % 4, 1] * hd;
            double len = System.Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
            int n = System.Math.Max(2, (int)System.Math.Ceiling(len / 1.2));
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n, u = u0 + (u1 - u0) * t, v = v0 + (v1 - v0) * t;
                ring.Add(new[] { cx + rx * u + fx * v, cz + rz * u + fz * v });
            }
        }

        Vector3 origin = UP(cx, yTop, cz);
        var verts = new List<Vector3>(); var tris = new List<int>();
        int N = ring.Count;
        for (int i = 0; i < N; i++)
        {
            var p0 = ring[i]; var p1 = ring[(i + 1) % N];
            Vector3 t0 = UP(p0[0], yTop, p0[1]) - origin, t1 = UP(p1[0], yTop, p1[1]) - origin;
            Vector3 b0 = UP(p0[0], Mathf.Min(G(p0[0], p0[1]) - 0.3f, yTop - 0.1f), p0[1]) - origin;
            Vector3 b1 = UP(p1[0], Mathf.Min(G(p1[0], p1[1]) - 0.3f, yTop - 0.1f), p1[1]) - origin;
            Vector3 outward = new Vector3((t0.x + t1.x) * 0.5f, 0f, (t0.z + t1.z) * 0.5f);
            AddQuad(verts, tris, t0, t1, b1, b0, outward);
            AddTri(verts, tris, Vector3.zero, t0, t1);
        }

        var mesh = new Mesh { name = "Plinth" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject("Foundation");
        go.transform.SetParent(parent, false);
        go.transform.position = origin;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = PlinthMat();
    }

    // ---------- material variation ----------
    static void ApplyVariant(GameObject go, System.Random rng, int a, int b)
    {
        double roll = rng.NextDouble();
        int v = roll < 0.35 ? a : roll < 0.65 ? b : rng.Next(0, 8);   // 0 = original palette
        if (v == 0) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var src = r.sharedMaterials; var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = VariantOf(src[i], v);
            r.sharedMaterials = dst;
        }
    }

    static Material VariantOf(Material m, int v)
    {
        if (m == null) return null;
        long key = ((long)m.GetInstanceID() << 4) ^ (long)v;
        Material c;
        if (variantCache.TryGetValue(key, out c) && c != null) return c;
        var n = new Material(m); n.name = m.name + "_v" + v;
        if (m.HasProperty("_BaseColor"))
        {
            Color col = m.GetColor("_BaseColor"); float h, s, val;
            Color.RGBToHSV(col, out h, out s, out val);
            var rr = new System.Random(v * 7331 + (int)(h * 1000) + (int)(val * 1000));
            bool accent = s > 0.35f && val > 0.2f;                       // roofs / coloured accents shift more than stone and timber
            float dh = ((float)rr.NextDouble() * 2f - 1f) * (accent ? 0.07f : 0.012f);
            float dv = ((float)rr.NextDouble() * 2f - 1f) * (accent ? 0.12f : 0.07f);
            float ds = ((float)rr.NextDouble() * 2f - 1f) * 0.12f;
            n.SetColor("_BaseColor", Color.HSVToRGB(Mathf.Repeat(h + dh, 1f), Mathf.Clamp01(s * (1f + ds)), Mathf.Clamp01(val * (1f + dv))));
        }
        variantCache[key] = n;
        return n;
    }

    // ---------- yards ----------
    static bool InsideHouse(YardCtx y, double x, double z, double pad)
    {
        double dx = x - y.house.x, dz = z - y.house.z;
        double u = dx * Cs(y.house.yaw) - dz * Sn(y.house.yaw);
        double v = dx * Sn(y.house.yaw) + dz * Cs(y.house.yaw);
        return System.Math.Abs(u) < y.house.hw + pad && System.Math.Abs(v) < y.house.hd + pad;
    }

    static bool TryPoint(YardCtx y, double inset, double clear, out double px, out double pz)
    {
        px = pz = 0;
        for (int t = 0; t < 16; t++)
        {
            double u = (y.rng.NextDouble() * 2 - 1) * (y.halfLat - inset), v = (y.rng.NextDouble() * 2 - 1) * (y.halfDepth - inset);
            double x = y.cx + y.rx * u + y.fx * v, z = y.cz + y.rz * u + y.fz * v;
            if (InsideHouse(y, x, z, clear) || Blocked(x, z) || Slope(x, z) > 0.6f) continue;
            bool near = false;
            foreach (var p in y.placed) { double dx = p.x - x, dz = p.y - z; if (dx * dx + dz * dz < 1.0) { near = true; break; } }
            if (near) continue;
            px = x; pz = z; return true;
        }
        return false;
    }

    static GameObject Put(VillageCatalog.Entry e, Transform parent, double x, double z, float s, double faceYaw, float sink, float lift)
    {
        float fp = Mathf.Max(e.footprint.x, e.footprint.y) * s * 0.35f;
        float h = G(x, z);
        if (fp > 0.25f) { h = Mathf.Min(h, G(x + fp, z)); h = Mathf.Min(h, G(x - fp, z)); h = Mathf.Min(h, G(x, z + fp)); h = Mathf.Min(h, G(x, z - fp)); }
        var go = Object.Instantiate(e.prefab, UP(x, h - e.minY * s - sink + lift, z), Face(faceYaw), parent);
        go.name = e.id; go.transform.localScale = Vector3.one * s;
        return go;
    }

    static void Cluster(YardCtx y, string id, int count, float spread, double inset)
    {
        var e = cat.Get(id); if (e == null) return;
        double px, pz;
        if (!TryPoint(y, inset, 0.8, out px, out pz)) { nSkippedProps++; return; }
        for (int i = 0; i < count; i++)
        {
            double ang = y.rng.NextDouble() * 6.283, d = i == 0 ? 0 : spread * (0.6 + y.rng.NextDouble() * 0.5);
            double x = px + Cs(ang) * d, z = pz + Sn(ang) * d;
            if (i > 0 && (InsideHouse(y, x, z, 0.4) || Blocked(x, z))) continue;
            Put(e, y.parent, x, z, Rf(y.rng, 0.9f, 1.2f), y.rng.NextDouble() * 6.283, 0.02f, 0f);
            y.placed.Add(new Vector2((float)x, (float)z)); Count(id);
        }
    }

    static void Hay(YardCtx y, double inset)
    {
        var e = cat.Get("HayBale"); if (e == null) return;
        double px, pz;
        if (!TryPoint(y, inset, 1.0, out px, out pz)) { nSkippedProps++; return; }
        int n = y.rng.Next(1, 4); double baseYaw = y.rng.NextDouble() * 6.283; float s = Rf(y.rng, 0.95f, 1.2f);
        for (int i = 0; i < (n == 3 ? 2 : n); i++)
        {
            double off = (n == 1 ? 0 : (i == 0 ? -0.45 : 0.45)) * s;
            double x = px + Cs(baseYaw) * off, z = pz + Sn(baseYaw) * off;
            Put(e, y.parent, x, z, s, baseYaw + y.rng.NextDouble() * 0.3, 0.02f, 0f); Count("HayBale");
        }
        if (n == 3) { Put(e, y.parent, px, pz, s, baseYaw + 0.4, 0.02f, 0.72f * s); Count("HayBale"); }
        y.placed.Add(new Vector2((float)px, (float)pz));
    }

    static void Rocks(YardCtx y, int count)
    {
        var rocks = cat.Of(VKind.Rock); if (rocks.Count == 0) return;
        for (int i = 0; i < count; i++)
        {
            double px, pz;
            if (!TryPoint(y, 0.4, 1.0, out px, out pz)) { nSkippedProps++; continue; }
            var e = rocks[y.rng.Next(rocks.Count)];
            Put(e, y.parent, px, pz, Rf(y.rng, 0.8f, 1.7f), y.rng.NextDouble() * 6.283, 0.1f, 0f);
            y.placed.Add(new Vector2((float)px, (float)pz)); Count(e.id);
        }
    }

    static void Yard(YardCtx y, bool fence)
    {
        if (fence) Fence(y);
        double inset = fence ? 0.9 : 0.5;
        var r = y.rng; int barrels, crates, hay, rocks;
        switch (y.role)
        {
            case Zone.Farm:    hay = r.Next(1, 3); barrels = r.Next(0, 3); crates = r.Next(0, 2); rocks = r.Next(0, 3); break;
            case Zone.Harbour: hay = 0; barrels = r.Next(1, 4); crates = r.Next(1, 4); rocks = r.Next(0, 2); break;
            case Zone.Hamlet:  hay = r.Next(0, 2); barrels = r.Next(0, 3); crates = r.Next(0, 3); rocks = r.Next(0, 3); break;
            default:           hay = 0; barrels = r.Next(0, 2); crates = r.Next(0, 2); rocks = r.Next(1, 4); break;
        }
        for (int i = 0; i < hay; i++) Hay(y, inset);
        for (int i = 0; i < barrels; i++) Cluster(y, "WoodenBarrel", r.Next(1, 3), 0.7f, inset);
        for (int i = 0; i < crates; i++) Cluster(y, "WoodenCrate", r.Next(1, 4), 0.65f, inset);
        Rocks(y, rocks);
    }

    static void Fence(YardCtx y)
    {
        if (fenceE == null) return;
        double uh = y.halfLat - 0.25, vh = y.halfDepth - 0.25;
        double[,] c = { { -1, 1 }, { 1, 1 }, { 1, -1 }, { -1, -1 } };    // front-left, front-right, back-right, back-left
        bool gateDone = false;
        for (int k = 0; k < 4; k++)
        {
            double u0 = c[k, 0] * uh, v0 = c[k, 1] * vh, u1 = c[(k + 1) % 4, 0] * uh, v1 = c[(k + 1) % 4, 1] * vh;
            double len = System.Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
            int n = System.Math.Max(1, (int)System.Math.Round(len / fenceE.footprint.x));
            for (int i = 0; i < n; i++)
            {
                double t0 = (double)i / n, t1 = (double)(i + 1) / n, tm = (t0 + t1) * 0.5;
                double um = u0 + (u1 - u0) * tm, vm = v0 + (v1 - v0) * tm;
                double mx = y.cx + y.rx * um + y.fx * vm, mz = y.cz + y.rz * um + y.fz * vm;

                if (k == 0 && System.Math.Abs(um - y.gateU) < 0.95)
                {
                    if (!gateDone && gateE != null) { PlaceSegment(y, gateE, y.gateU - 0.6, vh, y.gateU + 0.6, vh, 1.35f); gateDone = true; nGate++; }
                    continue;
                }
                if (InsideHouse(y, mx, mz, 0.3) || Blocked(mx, mz) || Slope(mx, mz) > 0.9f) { nSkippedProps++; continue; }
                PlaceSegment(y, fenceE, u0 + (u1 - u0) * t0, v0 + (v1 - v0) * t0, u0 + (u1 - u0) * t1, v0 + (v1 - v0) * t1, 0f);
                nFence++;
            }
        }
    }

    /// <summary>One fence/gate piece between two yard-local points, stretched to fit and tilted to follow the slope.</summary>
    static void PlaceSegment(YardCtx y, VillageCatalog.Entry e, double ua, double va, double ub, double vb, float forcedScaleX)
    {
        double ax = y.cx + y.rx * ua + y.fx * va, az = y.cz + y.rz * ua + y.fz * va;
        double bx = y.cx + y.rx * ub + y.fx * vb, bz = y.cz + y.rz * ub + y.fz * vb;
        Vector3 pa = UP(ax, G(ax, az), az), pb = UP(bx, G(bx, bz), bz);
        Vector3 d = pb - pa; float len = d.magnitude; if (len < 0.2f) return;
        Vector3 dn = d / len, z = Vector3.Cross(dn, Vector3.up);
        if (z.sqrMagnitude < 1e-6f) return;
        z.Normalize();
        Quaternion rot = Quaternion.LookRotation(z, Vector3.Cross(z, dn));   // local X runs along the segment, Y follows the slope
        var go = Object.Instantiate(e.prefab, (pa + pb) * 0.5f + Vector3.up * (-e.minY - 0.03f), rot, y.parent);
        go.name = e.id;
        go.transform.localScale = new Vector3(forcedScaleX > 0f ? forcedScaleX : len / e.footprint.x, 1f, 1f);
    }

    // ---------- plazas and public anchors ----------
    static void Dress(SettlementMap.Settlement s, Transform parent)
    {
        var g = new GameObject("Public"); g.transform.SetParent(parent, false);
        var rng = new System.Random(Seed * 31 + s.id * 977 + 5);
        if (s.anchors != null)
            foreach (var a in s.anchors)
            {
                double toPlaza = System.Math.Atan2(s.plazaX - a.x, s.plazaZ - a.z);
                switch (a.kind)
                {
                    case "well": Well(a, g.transform, rng); break;
                    case "tower": Tower(a, g.transform, rng, toPlaza); break;
                    case "chapel":
                    case "monument": Shrine(a, g.transform, rng, toPlaza); break;
                    case "mill": Mill(a, g.transform, rng, toPlaza); break;
                }
                nAnchors++;
            }
        Plaza(s, g.transform, rng);
    }

    static void Well(SettlementMap.Anchor a, Transform p, System.Random rng)
    {
        var rock = cat.Get("SmallRock"); if (rock == null) return;
        for (int i = 0; i < 9; i++)
        {
            double ang = i * 6.2832 / 9 + rng.NextDouble() * 0.2;
            Put(rock, p, a.x + Cs(ang) * 0.95, a.z + Sn(ang) * 0.95, Rf(rng, 0.38f, 0.5f), rng.NextDouble() * 6.283, 0.05f, 0f); Count("SmallRock");
        }
        for (int i = 0; i < 5; i++)
        {
            double ang = i * 6.2832 / 5 + 0.3;
            Put(rock, p, a.x + Cs(ang) * 0.78, a.z + Sn(ang) * 0.78, Rf(rng, 0.3f, 0.4f), rng.NextDouble() * 6.283, 0.05f, 0.3f); Count("SmallRock");
        }
        var bar = cat.Get("WoodenBarrel");
        double ba = rng.NextDouble() * 6.283, bx = a.x + Cs(ba) * 1.7, bz = a.z + Sn(ba) * 1.7;
        if (bar != null && !Blocked(bx, bz)) { Put(bar, p, bx, bz, 1f, rng.NextDouble() * 6.283, 0.02f, 0f); Count("WoodenBarrel"); }
    }

    static void Shrine(SettlementMap.Anchor a, Transform p, System.Random rng, double toPlaza)
    {
        var arch = cat.Get("RuinedArchway"); if (arch == null) return;
        Put(arch, p, a.x, a.z, Rf(rng, 1.05f, 1.25f), toPlaza, 0.05f, 0f); Count("RuinedArchway");
        var rocks = cat.Of(VKind.Rock);
        for (int i = 0; i < 3 && rocks.Count > 0; i++)
        {
            double ang = rng.NextDouble() * 6.283, d = Rf(rng, 2.2f, 3.4f), x = a.x + Cs(ang) * d, z = a.z + Sn(ang) * d;
            if (Blocked(x, z) || !FreeOfBuildings(x, z, 0.6)) continue;
            var e = rocks[rng.Next(rocks.Count)]; Put(e, p, x, z, Rf(rng, 0.8f, 1.4f), rng.NextDouble() * 6.283, 0.1f, 0f); Count(e.id);
        }
    }

    static void Tower(SettlementMap.Anchor a, Transform p, System.Random rng, double toPlaza)
    {
        var e = cat.Get("CastleTower"); if (e == null) return;
        float rel, gap; bool pl; Obb ob;
        PlaceBuilding(e, p, a.x, a.z, toPlaza, 1.0f, rng, 0, 0, out rel, out pl, out gap, out ob);
        perType["CastleTower"] = perType.ContainsKey("CastleTower") ? perType["CastleTower"] + 1 : 1;

        // fallen walls, an arch and rubble around the base
        var ruins = cat.Of(VKind.Ruin); var rocks = cat.Of(VKind.Rock);
        int want = rng.Next(4, 7), placed = 0;
        for (int t = 0; t < 40 && placed < want; t++)
        {
            double ang = rng.NextDouble() * 6.283, d = Rf(rng, 6.5f, 13f), x = a.x + Cs(ang) * d, z = a.z + Sn(ang) * d;
            if (Blocked(x, z) || Slope(x, z) > 0.5f || !FreeOfBuildings(x, z, 2.0)) continue;
            var pick = (rng.NextDouble() < 0.6 && ruins.Count > 0) ? ruins[rng.Next(ruins.Count)] : (rocks.Count > 0 ? rocks[rng.Next(rocks.Count)] : null);
            if (pick == null) break;
            Put(pick, p, x, z, Rf(rng, 0.9f, 1.4f), rng.NextDouble() * 6.283, 0.08f, 0f); Count(pick.id); placed++;
        }
    }

    static void Mill(SettlementMap.Anchor a, Transform p, System.Random rng, double toPlaza)
    {
        var e = cat.Get("TwoStoryMedievalHouse"); if (e == null) return;
        float sc = 1.1f; double hw = e.footprint.x * 0.5 * sc, hd = e.footprint.y * 0.5 * sc;
        // smart fallback: try the anchor, then rings around it, until the footprint is clear of lots, circles and lanes
        double[] radii = { 0, 3, 5, 7, 10, 13, 17 };
        foreach (double r in radii)
            for (int k = 0; k < (r == 0 ? 1 : 12); k++)
            {
                double ang = k * 0.5236 + rng.NextDouble() * 0.2, x = a.x + Cs(ang) * r, z = a.z + Sn(ang) * r;
                if (!AreaFree(x, z, hw + 1.0, hd + 1.0, toPlaza)) continue;
                float rel, gap; bool pl; Obb ob;
                PlaceBuilding(e, p, x, z, toPlaza, sc, rng, 0, 0, out rel, out pl, out gap, out ob);
                perType["Mill(TwoStory)"] = perType.ContainsKey("Mill(TwoStory)") ? perType["Mill(TwoStory)"] + 1 : 1;
                return;
            }
        notes.Add("mill anchor at " + a.x.ToString("F0") + "," + a.z.ToString("F0") + " had no free footprint; skipped");
    }

    static bool AreaFree(double x, double z, double hw, double hd, double yaw)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var o in obbs)
            if (SettlementMap.ObbOverlap(x, z, hw, hd, yaw, o.x, o.z, o.hw, o.hd, o.yaw, 0.8)) return false;
        foreach (var c in publicCircles)
            if (SettlementMap.ObbCircleOverlap(x, z, hw, hd, yaw, c.x, c.z, c.r, 0.5)) return false;
        double fx = Sn(yaw), fz = Cs(yaw), rx = Cs(yaw), rz = -Sn(yaw);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                double px = x + rx * hw * i + fx * hd * j, pz = z + rz * hw * i + fz * hd * j;
                if (Blocked(px, pz)) return false;
                float hh = G(px, pz); if (hh < lo) lo = hh; if (hh > hi) hi = hh;
            }
        return hi - lo < 1.6f;
    }

    static void Plaza(SettlementMap.Settlement s, Transform p, System.Random rng)
    {
        if (!s.hasPlaza) return;
        int n = s.plazaR < 5.5 ? rng.Next(1, 3) : rng.Next(3, 7);
        for (int i = 0; i < n; i++)
        {
            double ang = rng.NextDouble() * 6.283, r = s.plazaR * Rf(rng, 0.55f, 0.88f);
            double x = s.plazaX + Cs(ang) * r, z = s.plazaZ + Sn(ang) * r;
            if (Blocked(x, z) || !FreeOfBuildings(x, z, 0.8)) continue;
            double roll = rng.NextDouble();
            string id = (s.role == Zone.Farm && roll < 0.4) ? "HayBale" : roll < 0.7 ? "WoodenBarrel" : "WoodenCrate";
            var e = cat.Get(id); if (e == null) continue;
            Put(e, p, x, z, Rf(rng, 0.95f, 1.2f), rng.NextDouble() * 6.283, 0.02f, 0f); Count(id);
        }
    }

    // ---------- report ----------
    static string MakeReport()
    {
        int overlaps = 0;
        for (int i = 0; i < obbs.Count; i++)
            for (int j = i + 1; j < obbs.Count; j++)
                if (SettlementMap.ObbOverlap(obbs[i].x, obbs[i].z, obbs[i].hw, obbs[i].hd, obbs[i].yaw, obbs[j].x, obbs[j].z, obbs[j].hw, obbs[j].hd, obbs[j].yaw, 0.0)) overlaps++;

        var sb = new StringBuilder();
        sb.AppendLine("VillageComposer: " + nHouse + " houses (" + nFenced + " fenced), " + nPlinth + " plinths, " + nFence + " fence + " + nGate + " gates, "
                      + nProp + " props, " + nAnchors + " anchors, skipped lots=" + nSkippedLots + " skipped placements=" + nSkippedProps);
        sb.AppendLine("  grounding: max footprint relief=" + maxRelief.ToString("F2") + "m, max UNCOVERED gap under a house=" + maxUncovered.ToString("F3") + "m (plinths cover the rest)");
        sb.AppendLine("  building overlaps=" + overlaps + "  avg house scale=" + (nHouse > 0 ? (sumScale / nHouse).ToString("F2") : "n/a"));
        var parts = new List<string>(); foreach (var kv in perType) parts.Add(kv.Key + "=" + kv.Value);
        parts.Sort(); sb.AppendLine("  by type: " + string.Join(", ", parts));
        foreach (var n in notes) sb.AppendLine("  note: " + n);
        return sb.ToString();
    }
}
