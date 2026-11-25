using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays the VillageKit out around a hamlet at runtime (the island and its houses are generated at Play):
///   a well hub at the open heart of the hamlet, packed-dirt paths with puddles from every house to the well,
///   lantern posts along them, terraced farm plots (fenced or stone-walled, a blighted patch or two on the
///   outskirts), barrels and woodpiles tucked against the houses, and a fishpond on open low ground.
/// Deterministic for a given seed. The intro's well vignette uses this well when it exists.
/// </summary>
public class VillageDressing : MonoBehaviour
{
    public static VillageDressing Instance { get; private set; }

    [Tooltip("Centre of the hamlet to dress (defaults to the intro's Shot2_Village marker).")]
    public Transform hamletCentre;
    public float houseRadius = 60f;
    public int seed = 4127;
    [Header("Amounts")]
    public int farmPlots = 7;
    public int blightedPlots = 2;
    public bool fishpond = true;

    public Transform Well { get; private set; }
    public Renderer WellWater { get; private set; }
    public bool Built { get; private set; }

    readonly List<(Vector3 c, float r)> occupied = new List<(Vector3, float)>();
    readonly List<(Vector3 a, Vector3 b, float halfWidth)> paths = new();
    System.Random rng;
    float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

    void Awake() { Instance = this; }

    IEnumerator Start()
    {
        // wait for the island mesh + colliders to exist
        while (IslandBuilder.HGrid == null) yield return null;
        yield return null; yield return null;
        Build();
    }

    [ContextMenu("Rebuild dressing")]
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        Well = null;
        WellWater = null;
        VillageWorld.Ensure();
        occupied.Clear(); paths.Clear(); rng = new System.Random(seed);
        Physics.SyncTransforms();
        var cottage = FindFirstObjectByType<CottageChoiceScene>();
        if (cottage && cottage.cottageOverride != Vector3.zero) Claim(cottage.cottageOverride, 10f);
        Vector3 centre = hamletCentre ? hamletCentre.position : new Vector3(10, 6, 318);
        var houses = FindHouses(centre, houseRadius);
        foreach (var h in houses) { var b = Bounds(h); occupied.Add((Flat(b.center), Mathf.Max(b.extents.x, b.extents.z) + 1.5f)); }

        // ---- well hub at the most open spot near the hamlet's middle
        Vector3 mid = centre;
        if (houses.Count > 0) { mid = Vector3.zero; foreach (var h in houses) mid += h.position; mid /= houses.Count; }
        bool hasWell = TryFindOpen(mid, 4.2f, 0, 36, out Vector3 wellAt);
        if (hasWell)
        {
            Well = VillageKit.Well(wellAt, transform, seed).transform;
            var water = Well.Find("Water");
            WellWater = water ? water.GetComponent<Renderer>() : null;
            Claim(wellAt, 4.2f);
            TryPlace(wellAt + new Vector3(4.25f, 0, 3.2f), .85f,
                p => VillageKit.LanternPost(p, transform, seed + 1));
            TryPlace(wellAt + new Vector3(-4.1f, 0, -3.4f), .85f,
                p => VillageKit.LanternPost(p, transform, seed + 2));
        }

        // ---- paths from each door-side to the well, lanterns along the longer ones
        var houseRoutes = new List<(Transform house, Vector3 door, Vector3 bend, Vector3 end, Vector3 direction)>();
        for (int i = 0; hasWell && i < houses.Count; i++)
        {
            var h = houses[i];
            var b = Bounds(h);
            Vector3 dir = Flat(wellAt - b.center).normalized;
            if (dir.sqrMagnitude < .001f) continue;
            Vector3 door = b.center + dir * (Mathf.Max(b.extents.x, b.extents.z) + .8f);
            Vector3 end = wellAt - dir * 2.6f;
            if (Vector3.Distance(door, end) < 2) continue;
            Vector3 bend = Vector3.Lerp(door, end, .5f) + Vector3.Cross(Vector3.up, dir) * R(-2.5f, 2.5f);
            float width = R(1.4f, 2f);
            VillageKit.Path(new[] { door, bend, end }, width, transform, seed + 10 + i, .45f, .0008f * i);
            paths.Add((Flat(door), Flat(bend), width * .8f));
            paths.Add((Flat(bend), Flat(end), width * .8f));
            houseRoutes.Add((h, door, bend, end, dir));
        }
        int pi = 0;
        foreach (var (h, door, bend, end, dir) in houseRoutes)
        {
            var b = Bounds(h);
            if (Vector3.Distance(door, end) > 14)
                TryPlace(bend + Vector3.Cross(Vector3.up, dir) * 2.7f, .85f,
                    p => VillageKit.LanternPost(p, transform, seed + 30 + pi));
            // a lamp post beside every front door (the door is on the house model's local +Z side), arm reaching over the doorstep
            {
                Vector3 front = h.forward; front.y = 0; front.Normalize();
                Vector3 doorSide = Vector3.Cross(Vector3.up, front);
                float ext = Mathf.Max(b.extents.x, b.extents.z);
                Vector3 lp = b.center + front * (ext + 1.1f) + doorSide * 1.7f;
                TryPlace(lp + front * 1.1f, .85f,
                    p => VillageKit.LanternPost(p, transform, seed + 900 + pi, Quaternion.LookRotation(-doorSide).eulerAngles.y));
            }
            // rain barrel under the eave and a woodpile against a side wall
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float houseRadius = Mathf.Max(b.extents.x, b.extents.z);
            TryPlace(b.center + side * (houseRadius + 2.25f), .55f,
                p => VillageKit.RainBarrel(p, transform, seed + 50 + pi));
            if (pi % 2 == 0)
                TryPlace(b.center - dir * (houseRadius + 3.2f), 1.45f,
                    p => VillageKit.Woodpile(p, Quaternion.LookRotation(dir).eulerAngles.y, transform, seed + 70 + pi));
            if (rng.NextDouble() < .5)
                TryPlace(door + dir * 1.9f + side * 2.3f, .45f,
                    p => VillageKit.Basket(p, transform, seed + 90 + pi, rng.NextDouble() < .5));
            pi++;
        }

        // ---- terraced farm plots on open, gentle ground around the outskirts
        var crops = new[] { VillageKit.Crop.Cabbage, VillageKit.Crop.Beans, VillageKit.Crop.Pumpkin, VillageKit.Crop.Turnip, VillageKit.Crop.Carrot, VillageKit.Crop.Tomato, VillageKit.Crop.Wheat, VillageKit.Crop.Sunflower };
        int placed = 0, blighted = 0; var plots = new List<(Vector3 p, float yaw, Vector2 size)>();
        for (int attempt = 0; attempt < 400 && placed < Mathf.Max(farmPlots, 10); attempt++)
        {
            float a = R(0, Mathf.PI * 2), d = R(14, 46);
            Vector3 p = mid + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
            Vector2 size = new Vector2(R(3.6f, 6.5f), R(3f, 5f));
            float rad = size.magnitude * .5f + 1.2f;
            if (!Clear(p, rad) || !Gentle(p, rad)) continue;
            bool outskirts = placed >= 8 && d > 34 && blighted < blightedPlots;
            var crop = outskirts ? VillageKit.Crop.Blighted : crops[placed % crops.Length];
            float yaw = Quaternion.LookRotation(Flat(mid - p)).eulerAngles.y + R(-12, 12);
            if (!FitsFarmTerrain(p, yaw, size)) continue;
            bool stone = rng.NextDouble() < .4;
            var plotRoot = VillageKit.FarmPlot(p, yaw, size, crop, stone, transform, seed + 200 + placed);
            if (plotRoot && crop != VillageKit.Crop.Blighted)
            {
                var state = plotRoot.GetComponent<FarmPlotState>() ?? plotRoot.AddComponent<FarmPlotState>();
                state.size = size;
                state.crop = crop;
                state.Init(seed + 200 + placed);
                if (VillageWorld.I && !VillageWorld.I.plots.Contains(state)) VillageWorld.I.plots.Add(state);
            }
            else if (plotRoot && crop == VillageKit.Crop.Blighted)
            {
                var state = plotRoot.GetComponent<FarmPlotState>() ?? plotRoot.AddComponent<FarmPlotState>();
                state.size = size; state.blighted = true; state.Init(seed + 200 + placed);
            }
            if (!stone)
            {
                // split-rail fence a little outside the plot, open on the side facing the village
                var q = Quaternion.Euler(0, yaw, 0); float fx = size.x * .5f + 1f, fz = size.y * .5f + 1f;
                VillageKit.Fence(new[] { p + q * new Vector3(-fx * .3f, 0, fz), p + q * new Vector3(-fx, 0, fz), p + q * new Vector3(-fx, 0, -fz), p + q * new Vector3(fx, 0, -fz), p + q * new Vector3(fx, 0, fz), p + q * new Vector3(fx * .3f, 0, fz) }, transform, seed + 300 + placed);
            }
            if (rng.NextDouble() < .45)
                TryPlace(p + Quaternion.Euler(0, yaw, 0) * new Vector3(size.x * .5f + 1.75f, 0, 0), .42f,
                    at => VillageKit.Basket(at, transform, seed + 350 + placed, true));
            if (crop == VillageKit.Crop.Blighted) blighted++;
            Claim(p, rad + 1); placed++;
            plots.Add((p, yaw, size));
        }
        // Dry-stone terrace walls removed: hamlets keep their open, grassy edges.

        // ---- farm dressing: scarecrows, a tipped wheelbarrow, clay pots along the borders
        for (int i = 0; i < plots.Count; i++)
        {
            var (pp, yaw, size) = plots[i]; var q = Quaternion.Euler(0, yaw, 0);
            if (i % 3 == 0) VillageKitExtras.Scarecrow(pp + q * new Vector3(0, 0, size.y * .5f - .6f), yaw + 180, transform, seed + 600 + i);
            float outsidePlot = size.magnitude * .5f + 2.2f;
            if (i % 3 == 1) TryPlace(pp + q * new Vector3(-outsidePlot - .65f, 0, size.y * .3f), .6f,
                p => VillageKitExtras.ClayPots(p, transform, seed + 620 + i));
            if (i == 2) TryPlace(pp + q * new Vector3(outsidePlot + 1.35f, 0, -1f), 1.3f,
                p => VillageKitExtras.Wheelbarrow(p, yaw + 70, transform));
        }
        // ---- a clothesline in the open and a small shrine by the well
        {
            Vector3 dir = Quaternion.Euler(0, R(0, 180), 0) * Vector3.forward;
            if (TryFindOpen(mid + new Vector3(8, 0, -10), 3f, 0, 18, out var lineCentre))
            {
                VillageKitExtras.Clothesline(lineCentre - dir * 2.25f, lineCentre + dir * 2.25f, transform, seed + 700);
                Claim(lineCentre, 3f);
            }
            if (hasWell && TryFindOpen(wellAt + new Vector3(-5, 0, 3), 1.5f, 0, 8, out var sh))
            {
                VillageKitExtras.Shrine(sh, Quaternion.LookRotation(Flat(wellAt - sh)).eulerAngles.y, transform, seed + 710);
                Claim(sh, 1.5f);
            }
        }
        // ---- lanterns along the island road where it passes the hamlet
        LanternsAlongRoads(mid, 75f, 22f);

        // ---- fishpond on open low ground
        if (fishpond)
            for (int attempt = 0; attempt < 200; attempt++)
            {
                float a = R(0, Mathf.PI * 2), d = R(22, 55); float radius = R(3.2f, 4.5f);
                Vector3 p = mid + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                if (!Clear(p, radius + 2) || !Gentle(p, radius + 1)) continue;
                Vector3 across = Quaternion.Euler(0, R(0, 180), 0) * Vector3.right;
                VillageKit.Pond(p, radius, transform, across, seed + 500);
                Claim(p, radius + 2);
                // a footbridge across it, and a net rack on the bank
                VillageKitExtras.Footbridge(p - across * (radius + .9f), p + across * (radius + .9f), transform);
                Vector3 bank = p + Vector3.Cross(Vector3.up, across) * (radius + 3.7f);
                TryPlace(bank, 1.5f, at => VillageKitExtras.NetRack(at, Quaternion.LookRotation(Flat(p - at)).eulerAngles.y, transform));
                break;
            }
        DressCommunityFarms(mid, houses, crops);
        if (TryFindOpen(mid + Vector3.right*7,1.65f,0,24,out var hearthAt))
        {
            Claim(hearthAt,1.65f);
            VillageCraft.Hearth(hearthAt,transform);
        }
        VillageAnimalYard.Build(this, wellAt, seed + 1600);
        Built = true;
    }

    const int CommunityPlotLimit = 8;

    void DressCommunityFarms(Vector3 hamlet, List<Transform> hamletHouses, VillageKit.Crop[] crops)
    {
        var nearby = new HashSet<Transform>(hamletHouses);
        var outlying = FindHouses(hamlet, float.PositiveInfinity);
        outlying.RemoveAll(h => nearby.Contains(h) || (h.position - hamlet).sqrMagnitude < houseRadius * houseRadius);
        outlying.Sort((a, b) =>
        {
            int elevation = a.position.y.CompareTo(b.position.y);
            if (elevation != 0) return elevation;
            int east = a.position.x.CompareTo(b.position.x);
            return east != 0 ? east : a.position.z.CompareTo(b.position.z);
        });
        foreach (var house in outlying)
        {
            var bounds = Bounds(house);
            Claim(bounds.center, Mathf.Max(bounds.extents.x, bounds.extents.z) + 1.5f);
        }

        var visited = new HashSet<Transform>();
        int planted = 0;
        const float clusterRadius = 30f;
        foreach (var house in outlying)
        {
            if (planted >= CommunityPlotLimit) break;
            if (visited.Contains(house)) continue;
            var cluster = new List<Transform>();
            foreach (var candidate in outlying)
                if (!visited.Contains(candidate) && (candidate.position - house.position).sqrMagnitude < clusterRadius * clusterRadius)
                    cluster.Add(candidate);
            if (cluster.Count < 2) continue;

            Vector3 centre = Vector3.zero;
            foreach (var member in cluster)
            {
                centre += member.position;
                visited.Add(member);
            }
            centre /= cluster.Count;

            int target = Mathf.Min(cluster.Count >= 4 ? 2 : 1, CommunityPlotLimit - planted);
            var size = new Vector2(3.8f, 3.2f);
            float footprint = size.magnitude * .5f + 1.2f;
            for (int attempt = 0; attempt < 72 && target > 0; attempt++)
            {
                float angle = attempt * 2.399963f;
                float distance = Mathf.Lerp(7f, 24f, attempt / 71f);
                Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                p = VillageKit.Ground(p);
                if (Mathf.Abs(p.y - centre.y) > 4f) continue;
                float yaw = Quaternion.LookRotation(Flat(centre - p)).eulerAngles.y;
                if (!FitsFarmTerrain(p, yaw, size) || WaterProbe.Near(p, footprint, 2f) || !Clear(p, footprint)) continue;
                int plotSeed = seed + 1200 + planted;
                var plot = VillageKit.FarmPlot(p, yaw, size, crops[planted % crops.Length], true, transform, plotSeed);
                var state = plot.GetComponent<FarmPlotState>();
                state.Init(plotSeed);
                if (VillageWorld.I) VillageWorld.I.plots.Add(state);
                Claim(p, footprint + 1f);
                planted++;
                target--;
            }
        }
        Debug.Log($"[VillageDressing] {planted} community farm plots placed outside the hamlet.");
    }

    static bool FitsFarmTerrain(Vector3 point, float yaw, Vector2 size)
    {
        var rotation = Quaternion.Euler(0, yaw, 0);
        VillageKit.FarmGroundRange(point, rotation, size, out float low, out float high);
        float diagonal = (size + Vector2.one * 1.2f).magnitude;
        if (low < 1.2f || high - low > diagonal * FarmPlots.MaxSlope + .12f) return false;

        // Check the shoulder as well as the centre so terraces do not cover the road.
        Vector2 half = size * .5f + Vector2.one * .6f;
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 p = point + rotation * new Vector3(half.x * x, 0, half.y * z);
                if (Surface.RoadPathAt(p.x, TerrainMath.MirrorZ ? -p.z : p.z) > .25) return false;
            }
        return true;
    }

    void LanternsAlongRoads(Vector3 around, float radius, float spacing)
    {
        var placedAt = new List<Vector3>();
        foreach (var road in RoadNet.Paths)
        {
            if (road.x == null) continue;
            float acc = spacing;
            for (int i = 1; i < road.x.Length; i++)
            {
                Vector3 a = TerrainMath.U(road.x[i - 1], 0, road.z[i - 1]), b = TerrainMath.U(road.x[i], 0, road.z[i]);
                acc += Vector3.Distance(Flat(a), Flat(b));
                if (acc < spacing || Vector3.Distance(Flat(b), Flat(around)) > radius) continue;
                Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * 2.6f * (i % 2 == 0 ? 1 : -1);
                Vector3 p = b + side;
                bool near = false; foreach (var q in placedAt) if (Vector3.Distance(q, p) < spacing * .7f) { near = true; break; }
                if (near || !Clear(p, .8f)) continue;
                VillageKit.LanternPost(p, transform, seed + 800 + placedAt.Count); Claim(p, 1f); placedAt.Add(p); acc = 0;
            }
        }
    }

    // ---------------------------------------------------------------- placement helpers
    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    public void Claim(Vector3 p, float r) { occupied.Add((Flat(p), r)); }

    void TryPlace(Vector3 point, float radius, System.Action<Vector3> place)
    {
        if (!Clear(point, radius)) return;
        place(point);
        Claim(point, radius);
    }

    public bool Clear(Vector3 p, float r)
    {
        Vector3 f = Flat(p);
        foreach (var o in occupied) if (Vector3.Distance(f, o.c) < r + o.r) return false;
        foreach (var path in paths)
        {
            Vector3 segment = path.b - path.a;
            if (segment.sqrMagnitude < .001f) continue;
            float t = Mathf.Clamp01(Vector3.Dot(f - path.a, segment) / segment.sqrMagnitude);
            if (Vector3.Distance(f, path.a + segment * t) < r + path.halfWidth) return false;
        }
        Vector3 g = VillageKit.Ground(p);
        if (g.y < 1.2f) return false;   // keep out of the sea / river beds
        // nothing solid standing there (trees, rocks, buildings)
        int foliage = LayerMask.NameToLayer("TreeOcclusion"); int mask = ~0; if (foliage >= 0) mask &= ~(1 << foliage);
        var hits = Physics.OverlapCapsule(g + Vector3.up * .6f, g + Vector3.up * 4f, r * .9f, mask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits) if (!h.GetComponent<IslandBuilder>()) return false;
        // no tree canopy overhead either (canopy proxies live on the TreeOcclusion layer)
        if (foliage >= 0 && Physics.CheckCapsule(g + Vector3.up * 2f, g + Vector3.up * 14f, r * .8f, 1 << foliage, QueryTriggerInteraction.Collide)) return false;
        if (Physics.SphereCast(g + Vector3.up * 30f, r * .7f, Vector3.down, out var top, 26f, ~0, QueryTriggerInteraction.Collide) && !(top.collider is MeshCollider tm && !tm.convex) && !top.collider.GetComponentInParent<VillageKitPiece>()) return false;
        return true;
    }

    public bool Gentle(Vector3 p, float r)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4; float y = VillageKit.Ground(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r).y;
            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
        }
        return hi - lo < r * .22f + .3f;
    }

    public bool TryFindOpen(Vector3 near, float r, float minD, float maxD, out Vector3 result)
    {
        for (int ring = 0; ring < 30; ring++)
        {
            float d = Mathf.Lerp(minD, maxD, ring / 29f);
            for (int s = 0; s < 12; s++)
            {
                float a = s * Mathf.PI / 6 + ring * .37f;
                Vector3 p = near + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                if (Clear(p, r) && Gentle(p, r)) { result = p; return true; }
            }
        }
        result = near;
        return false;
    }

    static List<Transform> FindHouses(Vector3 around, float radius)
    {
        var list = new List<Transform>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            var n = t.name;
            if (!(n.Contains("MedievalHouse") || n.Contains("MedievalHo"))) continue;
            if (t.parent && (t.parent.name.Contains("MedievalHouse") || t.parent.name.Contains("MedievalHo"))) continue;
            if ((t.position - around).sqrMagnitude > radius * radius) continue;
            list.Add(t);
        }
        return list;
    }

    static Bounds Bounds(Transform t)
    {
        var b = new Bounds(t.position, Vector3.zero); bool init = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds); }
        return b;
    }
}
