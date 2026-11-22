using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Removes scattered grass / flowers from the places the village is built on after the vegetation exists:
/// farm plots, pond, well, footpaths, and the footprints of village props (lamp posts, woodpiles, shrine, fences...).
/// Waits until both the vegetation and the village are ready, then strips instances inside each footprint once.
/// </summary>
public class VillageGrassClearer : MonoBehaviour
{
    [Tooltip("Extra metres around every prop / plot footprint.")] public float margin = 0.45f;
    [Tooltip("Extra metres around the pond edge and well.")] public float waterMargin = 1.0f;
    [Tooltip("Half-width (m) cleared along footpaths.")] public float pathHalfWidth = 1.25f;
    [Tooltip("Metres cleared around every house footprint (doorstep, porch).")] public float houseMargin = 1.1f;
    [Tooltip("Ignore renderers wider than this (ground slabs, big terrain pieces).")] public float maxPropSize = 14f;
    public bool cleared { get; private set; }

    struct Box { public float x0, x1, z0, z1, y0, y1; }
    readonly List<Box> boxes = new List<Box>();
    readonly List<Vector3> circles = new List<Vector3>();        // x, z, radius
    readonly List<Vector3> pathPts = new List<Vector3>();
    const float Cell = 4f;
    Dictionary<long, List<int>> boxGrid = new Dictionary<long, List<int>>();
    Dictionary<long, List<int>> pathGrid = new Dictionary<long, List<int>>();
    Dictionary<long, List<int>> circleGrid = new Dictionary<long, List<int>>();

    IEnumerator Start()
    {
        IslandStylizedGrass grass = null; IslandStylizedFlowers flowers = null;
        while (true)
        {
            grass = FindFirstObjectByType<IslandStylizedGrass>(); flowers = FindFirstObjectByType<IslandStylizedFlowers>();
            bool g = grass == null || !grass.enabled || grass.Ready;
            bool f = flowers == null || !flowers.enabled || flowers.Ready;
            var vd = VillageDressing.Instance;
            if (g && f && vd != null && vd.Built) break;
            yield return null;
        }
        yield return null;   // let the settlers finish registering their shared places
        Gather(VillageDressing.Instance);
        Index();
        int n = 0;
        if (grass != null && grass.Ready) n += grass.ClearWhere(Inside);
        if (flowers != null && flowers.Ready) n += flowers.ClearWhere(Inside);
        cleared = true;
        Debug.Log("[VillageGrassClearer] removed " + n + " grass/flower instances under " + boxes.Count + " footprints, " + circles.Count + " circles, " + pathPts.Count + " path points");
    }

    void Gather(VillageDressing vd)
    {
        // every house (and so its interior) lives inside its combined renderer bounds: no grass there
        foreach (var lg in FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if (!lg.name.Contains("House")) continue;
            var rs = lg.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue;
            var hb = rs[0].bounds; for (int i = 1; i < rs.Length; i++) hb.Encapsulate(rs[i].bounds);
            AddBox(hb, houseMargin); GrassExclusions.AddInterior(hb);
        }
        foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if ((!tr.name.Contains("MedievalHouse") && tr.name != "WeatheredCottage") || (tr.parent && tr.parent.name.Contains("MedievalHouse"))) continue;
            var rs = tr.GetComponentsInChildren<MeshRenderer>(true); if (rs.Length == 0) continue;
            Bounds bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
            AddBox(bounds, houseMargin); GrassExclusions.AddInterior(bounds);
        }
        foreach (Transform child in vd.transform)
        {
            string n = child.name;
            if (n == "Path")
            {
                foreach (Transform c in child)
                {
                    var mf = c.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh) { var v = mf.sharedMesh.vertices; for (int i = 0; i < v.Length; i += 3) pathPts.Add(c.TransformPoint(v[i])); }
                }
                continue;
            }
            if (n == "Fishpond")
            {
                float r = (SettlerCommunity.Instance ? SettlerCommunity.Instance.PondRadius : 3.5f) + waterMargin;
                circles.Add(new Vector3(child.position.x, child.position.z, r));
                // the pond's own renderers can be larger than the nominal radius
                foreach (var rd in child.GetComponentsInChildren<Renderer>()) { var b = rd.bounds; circles.Add(new Vector3(b.center.x, b.center.z, Mathf.Max(b.extents.x, b.extents.z) + waterMargin)); }
                continue;
            }
            foreach (var rd in child.GetComponentsInChildren<Renderer>())
            {
                if (rd is ParticleSystemRenderer || rd is TrailRenderer || rd is LineRenderer) continue;
                var b = rd.bounds;
                if (b.size.x > maxPropSize || b.size.z > maxPropSize) continue;
                AddBox(b, margin);
            }
        }
        if (vd.Well) circles.Add(new Vector3(vd.Well.position.x, vd.Well.position.z, 1.6f + waterMargin * .5f));
        if (SettlerCommunity.Instance != null)
            foreach (var p in SettlerCommunity.Instance.Plots) foreach (var rd in p.GetComponentsInChildren<Renderer>()) AddBox(rd.bounds, margin + .15f);
    }

    void AddBox(Bounds b, float m) { boxes.Add(new Box { x0 = b.min.x - m, x1 = b.max.x + m, z0 = b.min.z - m, z1 = b.max.z + m, y0 = b.min.y - 1.2f, y1 = b.max.y + 0.6f }); }

    static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    static int C(float v) => Mathf.FloorToInt(v / Cell);

    void Index()
    {
        for (int i = 0; i < boxes.Count; i++)
            for (int cx = C(boxes[i].x0); cx <= C(boxes[i].x1); cx++) for (int cz = C(boxes[i].z0); cz <= C(boxes[i].z1); cz++) Add(boxGrid, cx, cz, i);
        for (int i = 0; i < circles.Count; i++)
            for (int cx = C(circles[i].x - circles[i].z); cx <= C(circles[i].x + circles[i].z); cx++) for (int cz = C(circles[i].y - circles[i].z); cz <= C(circles[i].y + circles[i].z); cz++) Add(circleGrid, cx, cz, i);
        for (int i = 0; i < pathPts.Count; i++) { var p = pathPts[i]; Add(pathGrid, C(p.x), C(p.z), i); }
    }
    static void Add(Dictionary<long, List<int>> g, int cx, int cz, int i) { long k = Key(cx, cz); if (!g.TryGetValue(k, out var l)) g[k] = l = new List<int>(); l.Add(i); }

    bool Inside(Vector3 p)
    {
        int cx = C(p.x), cz = C(p.z);
        if (boxGrid.TryGetValue(Key(cx, cz), out var bl))
            foreach (int i in bl) { var b = boxes[i]; if (p.x >= b.x0 && p.x <= b.x1 && p.z >= b.z0 && p.z <= b.z1 && p.y >= b.y0 && p.y <= b.y1) return true; }
        if (circleGrid.TryGetValue(Key(cx, cz), out var cl))
            foreach (int i in cl) { var c = circles[i]; float dx = p.x - c.x, dz = p.z - c.y; if (dx * dx + dz * dz < c.z * c.z) return true; }
        float pw = pathHalfWidth, pw2 = pw * pw;
        for (int ox = -1; ox <= 1; ox++) for (int oz = -1; oz <= 1; oz++)
            if (pathGrid.TryGetValue(Key(cx + ox, cz + oz), out var pl))
                foreach (int i in pl) { var q = pathPts[i]; float dx = p.x - q.x, dz = p.z - q.z; if (dx * dx + dz * dz < pw2 && Mathf.Abs(p.y - q.y) < 1.5f) return true; }
        return false;
    }
}
