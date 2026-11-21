using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum Res { Cabbage, Beans, Pumpkin, Turnip, Fish, Wood, Bread, Water, Carrot, Tomato, Wheat, Sunflower }

public static class ResInfo
{
    public static bool IsProduce(Res r) => r == Res.Cabbage || r == Res.Beans || r == Res.Pumpkin || r == Res.Turnip || r == Res.Carrot || r == Res.Tomato || r == Res.Wheat || r == Res.Sunflower;
    public static string Name(Res r) => r.ToString().ToLower();
    public static Color Colour(Res r)
    {
        switch (r)
        {
            case Res.Carrot: return new Color(.95f,.48f,.13f);
            case Res.Tomato: return new Color(.85f,.22f,.16f);
            case Res.Wheat: return new Color(.9f,.73f,.35f);
            case Res.Sunflower: return new Color(.98f,.8f,.2f);
            case Res.Cabbage: return new Color(.5f, .72f, .36f);
            case Res.Beans: return new Color(.36f, .6f, .28f);
            case Res.Pumpkin: return new Color(.92f, .54f, .18f);
            case Res.Turnip: return new Color(.86f, .74f, .82f);
            case Res.Fish: return new Color(.6f, .72f, .82f);
            case Res.Wood: return new Color(.5f, .35f, .22f);
            case Res.Bread: return new Color(.85f, .62f, .32f);
            default: return new Color(.3f, .55f, .7f);
        }
    }
}

/// <summary>A place goods are kept (storehouse, market stall). Visuals listen to onChanged.</summary>
public class Stockpile : MonoBehaviour
{
    public string label = "store";
    public Vector3 dock;                                   // where settlers stand to load / unload
    public readonly Dictionary<Res, int> stock = new Dictionary<Res, int>();
    public System.Action onChanged;
    public int Count(Res r) => stock.TryGetValue(r, out var n) ? n : 0;
    public int Total { get { int t = 0; foreach (var kv in stock) t += kv.Value; return t; } }
    public int ProduceTotal { get { int t = 0; foreach (var kv in stock) if (ResInfo.IsProduce(kv.Key)) t += kv.Value; return t; } }
    public void Add(Res r, int n) { stock[r] = Count(r) + n; onChanged?.Invoke(); }
    public bool Take(Res r, int n) { if (Count(r) < n) return false; stock[r] = Count(r) - n; onChanged?.Invoke(); return true; }
    /// <summary>Take up to max of the most plentiful item; returns what was taken.</summary>
    public bool TakeMost(out Res res, out int qty, int max)
    {
        res = Res.Cabbage; qty = 0; int best = 0;
        foreach (var kv in stock) if (kv.Value > best) { best = kv.Value; res = kv.Key; }
        if (best <= 0) return false;
        qty = Mathf.Min(best, max); stock[res] = best - qty; onChanged?.Invoke(); return true;
    }
    public Vector3 Dock => dock == Vector3.zero ? transform.position : dock;
}

/// <summary>A crate / bundle lying on the ground waiting to be hauled.</summary>
public class Pickup : MonoBehaviour
{
    public Res res; public int qty = 1; public bool claimed;
    void OnEnable() { if (VillageWorld.I && !VillageWorld.I.pickups.Contains(this)) VillageWorld.I.pickups.Add(this); }
    void OnDestroy() { if (VillageWorld.I) VillageWorld.I.pickups.Remove(this); }
}

/// <summary>One unit of work on the village board.</summary>
public class Job
{
    public enum Kind { Till, Sow, Water, Harvest, Haul, FetchCart, ContinueHaul, Restock, Chop }
    public Kind kind; public float priority = 1f; public SettlerBrain claimedBy; public bool done;
    public FarmPlotState plot; public Pickup pickup; public Stockpile from, to; public HandCart cart; public Vector3 where; public float posted;
    public Res res; public int qty;
    public bool Open => !done && claimedBy == null;
    public override string ToString() => kind + (plot ? " " + plot.name : "") + (pickup ? " " + pickup.res : "");
}

/// <summary>
/// The village as a shared model every settler can read: resources and stockpiles, the job board (farm chores,
/// hauling, relays, restocking), parked carts, and a navigation grid with A* so settlers know where they are really
/// going (around houses, fences, trees and water, preferring paths, avoiding trampling crops).
/// </summary>
public class VillageWorld : MonoBehaviour
{
    public static VillageWorld I { get; private set; }
    public static VillageWorld Ensure()
    {
        if (I) return I;
        var g = new GameObject("VillageWorld"); return g.AddComponent<VillageWorld>();
    }

    public readonly List<FarmPlotState> plots = new List<FarmPlotState>();
    public readonly List<Stockpile> piles = new List<Stockpile>();
    public readonly List<HandCart> carts = new List<HandCart>();
    public readonly List<Pickup> pickups = new List<Pickup>();
    public readonly List<Job> jobs = new List<Job>();
    public Stockpile Storehouse, Market;
    public WellRig Well;
    public Transform ChopBlock;
    public Vector3 CartShed;              // where empty carts belong
    public int coins;                     // the village purse, for the market
    public bool NavReady { get; private set; }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    IEnumerator Start()
    {
        while (IslandBuilder.HGrid == null) yield return null;
        while (VillageDressing.Instance == null || !VillageDressing.Instance.Built) yield return null;
        yield return null; yield return null;
        SetupEconomy();
        yield return BuildNav();
        StartCoroutine(Tick());
    }

    void SetupEconomy()
    {
        var vd = VillageDressing.Instance;
        Well = vd.Well ? vd.Well.GetComponent<WellRig>() : null;
        foreach (var p in vd.GetComponentsInChildren<FarmPlotState>()) if (!plots.Contains(p)) plots.Add(p);
        Vector3 hub = vd.Well ? vd.Well.position : vd.transform.position;
        // Choose clear ground for stations, rather than placing supplies inside houses.
        Vector3 Site(float angle, float distance) {
            for (int i=0;i<40;i++) {
                Vector3 p = VillageKit.Ground(hub + Quaternion.Euler(0,angle+i*19,0)*Vector3.forward*(distance+i*.2f));
                if (p.y>1.3f && !Physics.CheckCapsule(p+Vector3.up*.7f,p+Vector3.up*2,.65f,~0,QueryTriggerInteraction.Ignore)) return p;
            }
            return VillageKit.Ground(hub+Vector3.right*distance);
        }
        Stockpile Station(string title, Vector3 at) {
            var g = VillageKit.KitRoot(title,at,transform);
            var pile=g.AddComponent<Stockpile>(); pile.label=title; pile.dock=at+Vector3.forward*1.5f; piles.Add(pile);
            VillageKit.Piece(g.transform,"Timber counter",VillageKit.ChamferBox(),VillageKit.Mat(VillageKit.Timber),new Vector3(0,.45f,0),Vector3.zero,new Vector3(2,.9f,.9f));
            var display=new GameObject("Supplies"); display.transform.SetParent(g.transform,false);
            pile.onChanged=()=> {
                foreach(Transform t in display.transform) { t.gameObject.SetActive(false); Destroy(t.gameObject); }
                int k=0; foreach(var pair in pile.stock) for(int n=0;n<Mathf.Min(pair.Value,4);n++) {
                    if(k>=18) break;
                    VillageKit.Piece(display.transform,ResInfo.Name(pair.Key),VillageKit.Blob(k+13,.12f),VillageKit.Mat(ResInfo.Colour(pair.Key)),new Vector3((k%6-2.5f)*.28f,1+(k/6)*.22f,0),Vector3.zero,Vector3.one*.17f); k++;
                }
            };
            return pile;
        }
        if(!Storehouse) Storehouse=Station("Village store",Site(45,8));
        if(!Market) Market=Station("Produce exchange",Site(220,10));
        Storehouse.Add(Res.Bread,16); Storehouse.Add(Res.Cabbage,6); Market.Add(Res.Bread,8);
        CartShed=Site(100,12);
        if(carts.Count==0) for(int i=0;i<3;i++) VillageKitV2.Cart(Site(100+i*25,12+i*2),transform,810+i);
        ChopBlock=VillageKit.KitRoot("Chopping station",Site(290,14),transform).transform;
        VillageKit.Piece(ChopBlock,"Stump",VillageKit.Ring(10,0),VillageKit.Mat(VillageKit.Timber),Vector3.zero,Vector3.zero,new Vector3(.55f,.65f,.55f));
        SpawnPickup(Res.Wood,5,Site(20,15)); SpawnPickup(Res.Pumpkin,6,Site(160,18));
    }

    // ================================================================== job board
    public Job Post(Job.Kind k, float priority, Vector3 where, FarmPlotState plot = null, Pickup pk = null, Stockpile from = null, Stockpile to = null, HandCart cart = null, Res res = Res.Cabbage, int qty = 0)
    {
        var j = new Job { kind = k, priority = priority, where = where, plot = plot, pickup = pk, from = from, to = to, cart = cart, res = res, qty = qty, posted = Time.time };
        jobs.Add(j); return j;
    }
    public bool HasJob(Job.Kind k, Object target)
    {
        foreach (var j in jobs)
        {
            if (j.done || j.kind != k) continue;
            if (target == null) return true;
            if ((j.plot && j.plot == target) || (j.pickup && j.pickup == target) || (j.cart && j.cart == target)) return true;
        }
        return false;
    }
    public void Finish(Job j) { if (j != null) j.done = true; }
    public void Abandon(Job j) { if (j != null && !j.done) j.claimedBy = null; }

    IEnumerator Tick()
    {
        var wait = new WaitForSeconds(1.2f);
        while (true)
        {
            yield return wait;
            jobs.RemoveAll(j => j.done || (j.claimedBy == null && j.pickup == null && j.kind == Job.Kind.Haul));
            // stale claims (settler gone / gave up)
            foreach (var j in jobs) if (j.claimedBy && j.claimedBy.CurrentJob != j) j.claimedBy = null;

            foreach (var p in plots)
            {
                if (!p || p.blighted) continue;
                switch (p.NextNeed())
                {
                    case FarmPlotState.Need.Till: if (!HasJob(Job.Kind.Till, p)) Post(Job.Kind.Till, .8f, p.transform.position, p); break;
                    case FarmPlotState.Need.Sow: if (!HasJob(Job.Kind.Sow, p)) Post(Job.Kind.Sow, .9f, p.transform.position, p); break;
                    case FarmPlotState.Need.Water: if (!HasJob(Job.Kind.Water, p)) Post(Job.Kind.Water, 1f + (1 - p.water), p.transform.position, p); break;
                    case FarmPlotState.Need.Harvest: if (!HasJob(Job.Kind.Harvest, p)) Post(Job.Kind.Harvest, 1.4f, p.transform.position, p); break;
                }
            }
            foreach (var pk in pickups)
            {
                if (!pk || pk.claimed || HasJob(Job.Kind.Haul, pk)) continue;
                var dest = DestinationFor(pk.res); if (!dest) continue;
                Post(Job.Kind.Haul, 1.2f, pk.transform.position, null, pk, null, dest, null, pk.res, pk.qty);
            }
            if (Storehouse && Market && Storehouse.ProduceTotal >= 4 && Market.ProduceTotal < 7 && !HasJob(Job.Kind.Restock, null))
                Post(Job.Kind.Restock, 1f, Storehouse.Dock, null, null, Storehouse, Market);
            foreach (var c in carts)
            {
                if (!c || !c.Parked || c.reserved) continue;
                if (c.HasCargo) {
                    if (!HasJob(Job.Kind.ContinueHaul, c) && Time.time - c.parkedAt > 3f)
                        Post(Job.Kind.ContinueHaul, 1.8f, c.HitchPoint, cart:c, to:DestinationFor(c.cargoRes), res:c.cargoRes, qty:c.cargoQty);
                    continue;
                }
                if (HasJob(Job.Kind.FetchCart, c)) continue;
                if (Flat(c.transform.position - CartShed).sqrMagnitude > 7f * 7f && Time.time - c.parkedAt > 25f) Post(Job.Kind.FetchCart, .6f, c.transform.position, null, null, null, null, c);
            }
            if (ChopBlock && Storehouse && !HasJob(Job.Kind.Chop, null) && Storehouse.Count(Res.Wood) < 10 && CountPickups(Res.Wood) < 2)
                Post(Job.Kind.Chop, .55f, ChopBlock.position, null, null, null, Storehouse);
        }
    }

    int CountPickups(Res r) { int n = 0; foreach (var p in pickups) if (p && p.res == r) n++; return n; }

    public Stockpile DestinationFor(Res r)
    {
        if (r == Res.Fish) return Market ? Market : Storehouse;
        return Storehouse ? Storehouse : Market;
    }

    public HandCart NearestFreeCart(Vector3 near, float range)
    {
        HandCart best = null; float bd = range * range;
        foreach (var c in carts) { if (!c || !c.Parked || c.reserved || c.HasCargo) continue; float d = Flat(c.transform.position - near).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
        return best;
    }

    public Pickup SpawnPickup(Res r, int qty, Vector3 at)
    {
        var g = VillageKitV2.Crate(r, qty, at, transform); return g ? g.GetComponent<Pickup>() : null;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    // ================================================================== navigation grid
    const float Cell = .8f;
    int nx, nz; Vector3 origin; bool[] walk; float[] gy; float[] cost;
    readonly Collider[] buf = new Collider[24];

    IEnumerator BuildNav()
    {
        var vd = VillageDressing.Instance;
        Vector3 centre = vd.Well ? vd.Well.position : vd.transform.position;
        float half = 78f;
        nx = nz = Mathf.CeilToInt(half * 2 / Cell);
        origin = new Vector3(centre.x - half, 0, centre.z - half);
        walk = new bool[nx * nz]; gy = new float[nx * nz]; cost = new float[nx * nz];
        int foliage = LayerMask.NameToLayer("TreeOcclusion"); int mask = ~0; if (foliage >= 0) mask &= ~(1 << foliage);
        int done = 0;
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                Vector3 p = CellCentre(x, z);
                Vector3 g = VillageKit.Ground(p); gy[i] = g.y; cost[i] = 1f;
                bool ok = g.y > 1.25f && VillageKit.GroundNormal(g).y > .82f;
                if (ok)
                {
                    int n = Physics.OverlapCapsuleNonAlloc(g + Vector3.up * .55f, g + Vector3.up * 1.5f, .32f, buf, mask, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n && ok; k++)
                    {
                        var c = buf[k];
                        if (c is CharacterController || c.GetComponentInParent<HandCart>() || c.GetComponentInParent<SettlerBrain>()) continue;
                        if (c.GetComponent<IslandBuilder>()) continue; // Only the actual island is terrain; fences and houses block routes.
                        ok = false;
                    }
                }
                walk[i] = ok;
                if ((++done % 450) == 0) yield return null;
            }
        // keep a little distance from walls and trunks
        var pad = new float[nx * nz];
        for (int z = 1; z < nz - 1; z++) for (int x = 1; x < nx - 1; x++)
            {
                int i = z * nx + x; if (!walk[i]) continue;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (!walk[(z + dz) * nx + x + dx]) { pad[i] = .6f; dz = 2; break; }
            }
        for (int i = 0; i < cost.Length; i++) cost[i] += pad[i];
        // paths are easier going; crops are not to be trampled
        foreach (var p in RoadPoints()) MarkRoad(p);
        foreach (var pl in plots) if (pl) MarkPlot(pl);
        NavReady = true;
        int open = 0; foreach (var w in walk) if (w) open++;
        Debug.Log("[VillageWorld] nav grid " + nx + "x" + nz + ", " + open + " walkable cells");
    }

    IEnumerable<Vector3> RoadPoints()
    {
        var vd = VillageDressing.Instance; if (!vd) yield break;
        foreach (Transform t in vd.transform)
        {
            if (t.name != "Path") continue;
            foreach (Transform c in t)
            {
                var mf = c.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var v = mf.sharedMesh.vertices; for (int i = 0; i < v.Length; i += 2) yield return c.TransformPoint(v[i]);
            }
        }
        if (RoadNet.Paths != null)
            foreach (var road in RoadNet.Paths)
            {
                if (road.x == null) continue;
                for (int i = 0; i < road.x.Length; i++) yield return TerrainMath.U(road.x[i], 0, road.z[i]);
            }
    }

    void MarkRoad(Vector3 p)
    {
        int cx = Mathf.FloorToInt((p.x - origin.x) / Cell), cz = Mathf.FloorToInt((p.z - origin.z) / Cell);
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                int i = z * nx + x; if (walk[i]) cost[i] = Mathf.Min(cost[i], .55f);
            }
    }

    void MarkPlot(FarmPlotState pl)
    {
        for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x; if (!walk[i]) continue;
                if (pl.Contains(CellCentre(x, z), .35f)) cost[i] += 4f;
            }
    }

    public void AddPlotCost(FarmPlotState pl) { if (NavReady) MarkPlot(pl); }

    Vector3 CellCentre(int x, int z) => new Vector3(origin.x + (x + .5f) * Cell, 0, origin.z + (z + .5f) * Cell);
    bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz;
    public bool Walkable(Vector3 p)
    {
        if (!NavReady) return true;
        int x = Mathf.FloorToInt((p.x - origin.x) / Cell), z = Mathf.FloorToInt((p.z - origin.z) / Cell);
        return InGrid(x, z) && walk[z * nx + x];
    }

    int Nearest(Vector3 p)
    {
        int cx = Mathf.FloorToInt((p.x - origin.x) / Cell), cz = Mathf.FloorToInt((p.z - origin.z) / Cell);
        if (InGrid(cx, cz) && walk[cz * nx + cx]) return cz * nx + cx;
        int best = -1; float bd = float.MaxValue;
        for (int r = 1; r <= 10; r++)
        {
            for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                    int x = cx + dx, z = cz + dz; if (!InGrid(x, z) || !walk[z * nx + x]) continue;
                    float d = dx * dx + dz * dz; if (d < bd) { bd = d; best = z * nx + x; }
                }
            if (best >= 0) return best;
        }
        return -1;
    }

    // ---------------------------------------------------------------- A*
    float[] gScore; int[] came; byte[] state;
    readonly List<int> heapN = new List<int>(); readonly List<float> heapF = new List<float>();
    void Push(int n, float f)
    {
        heapN.Add(n); heapF.Add(f); int i = heapN.Count - 1;
        while (i > 0) { int p = (i - 1) / 2; if (heapF[p] <= heapF[i]) break; Swap(i, p); i = p; }
    }
    int Pop()
    {
        int top = heapN[0]; int last = heapN.Count - 1;
        heapN[0] = heapN[last]; heapF[0] = heapF[last]; heapN.RemoveAt(last); heapF.RemoveAt(last);
        int i = 0, c = heapN.Count;
        while (true)
        {
            int l = i * 2 + 1, r = l + 1, m = i;
            if (l < c && heapF[l] < heapF[m]) m = l;
            if (r < c && heapF[r] < heapF[m]) m = r;
            if (m == i) break; Swap(i, m); i = m;
        }
        return top;
    }
    void Swap(int a, int b) { var n = heapN[a]; heapN[a] = heapN[b]; heapN[b] = n; var f = heapF[a]; heapF[a] = heapF[b]; heapF[b] = f; }

    /// <summary>Route from a to b as a short list of waypoints (the last one is b). Falls back to a straight line.</summary>
    public List<Vector3> FindPath(Vector3 a, Vector3 b)
    {
        var res = new List<Vector3>();
        int s = NavReady ? Nearest(a) : -1, g = NavReady ? Nearest(b) : -1;
        if (s < 0 || g < 0) return res;
        if (s == g) { if (Walkable(b)) res.Add(b); return res; }
        int n = nx * nz;
        if (gScore == null || gScore.Length != n) { gScore = new float[n]; came = new int[n]; state = new byte[n]; }
        System.Array.Clear(state, 0, n);
        heapN.Clear(); heapF.Clear();
        int gx = g % nx, gz = g / nx;
        gScore[s] = 0; came[s] = -1; state[s] = 1; Push(s, 0);
        bool found = false; int guard = 0;
        while (heapN.Count > 0 && guard++ < 40000)
        {
            int cur = Pop(); if (state[cur] == 2) continue; state[cur] = 2;
            if (cur == g) { found = true; break; }
            int cx = cur % nx, cz = cur / nx;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int x = cx + dx, z = cz + dz; if (!InGrid(x, z)) continue;
                    int ni = z * nx + x; if (!walk[ni] || state[ni] == 2 || Mathf.Abs(gy[ni]-gy[cur])>.45f) continue;
                    if (dx != 0 && dz != 0 && (!walk[cz * nx + x] || !walk[z * nx + cx])) continue;   // no corner cutting
                    float step = (dx != 0 && dz != 0 ? 1.414f : 1f) * (cost[ni] + cost[cur]) * .5f;
                    float ng = gScore[cur] + step;
                    if (state[ni] == 1 && ng >= gScore[ni]) continue;
                    gScore[ni] = ng; came[ni] = cur; state[ni] = 1;
                    float h = Mathf.Sqrt((x - gx) * (x - gx) + (z - gz) * (z - gz)) * .55f;
                    Push(ni, ng + h);
                }
        }
        if (!found) return res;
        var cells = new List<int>(); for (int c = g; c != -1; c = came[c]) cells.Add(c); cells.Reverse();
        // string-pull: skip cells while the straight line stays on easy ground
        res.Add(new Vector3(origin.x+(s%nx+.5f)*Cell,gy[s],origin.z+(s/nx+.5f)*Cell));
        int idx = 0;
        while (idx < cells.Count - 1)
        {
            int far = idx + 1;
            for (int j = cells.Count - 1; j > idx + 1; j--) if (LineEasy(cells[idx], cells[j])) { far = j; break; }
            int ci = cells[far]; res.Add(new Vector3(origin.x + ((ci % nx) + .5f) * Cell, gy[ci], origin.z + ((ci / nx) + .5f) * Cell));
            idx = far;
        }
        if (Walkable(b)) res.Add(b);
        return res;
    }

    bool LineEasy(int a, int b)
    {
        int x0 = a % nx, z0 = a / nx, x1 = b % nx, z1 = b / nx;
        int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)); if (steps == 0) return true;
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps; int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), z = Mathf.RoundToInt(Mathf.Lerp(z0, z1, t));
            int ci = z * nx + x; if (!walk[ci] || cost[ci] > 1.7f) return false;
            // also keep one cell of slack on both sides so we don't scrape corners
            int ox = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0) ? 0 : 1, oz = 1 - ox;
            if (!InGrid(x + ox, z + oz) || !walk[(z + oz) * nx + x + ox] || !InGrid(x - ox, z - oz) || !walk[(z - oz) * nx + x - ox]) return false;
        }
        return true;
    }

    /// <summary>A random walkable spot near p (for wandering with intent: always somewhere reachable).</summary>
    public bool RandomWalkable(Vector3 near, float radius, System.Random rng, out Vector3 spot)
    {
        spot = near;
        if (!NavReady) return false;
        for (int i = 0; i < 24; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2, d = radius * (.3f + .7f * (float)rng.NextDouble());
            var p = near + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
            int cx = Mathf.FloorToInt((p.x - origin.x) / Cell), cz = Mathf.FloorToInt((p.z - origin.z) / Cell);
            if (!InGrid(cx, cz)) continue; int ci = cz * nx + cx;
            if (walk[ci] && cost[ci] < 2f) { spot = new Vector3(p.x, gy[ci], p.z); return true; }
        }
        return false;
    }
}
