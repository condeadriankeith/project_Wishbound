using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Stones along banks, shallows, lake shore and waterfalls, now using the GHIBLI package rock prefabs
/// (Rock_01..11) via GhibliBatch GPU instancing instead of procedural geometry.
/// Public API (Clear/Add/Build) is UNCHANGED so callers (RiverWater.cs, WaterBuilder.cs) need no edits:
/// placement positions, counts and per-call scale requests are identical to the previous implementation.
/// Package rocks have no per-vertex wet/sand/scree tinting (fixed prefab materials), so the old
/// location-based RockLook() colour logic now only picks WHICH rock piece to use (larger piece for larger
/// scale request) rather than colouring a procedural mesh; piece choice is deterministic per spawn (seed).
/// Previous implementation: RiverRocks.cs.bak_pre_ghibli2.txt (and .bak_pre_ghibli.txt before that)
/// Docs: Assets/Docs/GHIBLI_NATURE_INTEGRATION.md
/// </summary>
public static class RiverRocks
{
    struct R { public Vector3 p; public float s, yaw; public int seed; public Vector3 normal; }
    static readonly List<R> rocks = new List<R>();

    public static void Clear() { rocks.Clear(); bridgeSegs = null; }

    // Cached bridge exclusion zones (deck centreline segment + half-width + clearance), built once per
    // Clear() cycle from RoadBuild's already-solved crossings so rocks never spawn under/through a bridge deck.
    struct BridgeSeg { public double x0, z0, x1, z1, halfW; }
    static List<BridgeSeg> bridgeSegs;

    static void BuildBridgeSegs()
    {
        bridgeSegs = new List<BridgeSeg>();
        foreach (var r in RoadBuild.Roads)
            foreach (var c in r.crosses)
            {
                if (!c.bridge) continue;
                // half the span width plus a couple of metres so rocks don't spawn right at the abutments either
                double halfW = c.width * 0.5 + 2.0;
                for (int i = c.i0; i < c.i1; i++)
                    bridgeSegs.Add(new BridgeSeg { x0 = r.x[i], z0 = r.z[i], x1 = r.x[i + 1], z1 = r.z[i + 1], halfW = halfW });
            }
    }

    static bool NearBridge(double x, double z)
    {
        if (bridgeSegs == null) BuildBridgeSegs();
        foreach (var seg in bridgeSegs)
        {
            double vx = seg.x1 - seg.x0, vz = seg.z1 - seg.z0, l2 = vx * vx + vz * vz;
            double t = l2 > 1e-9 ? ((x - seg.x0) * vx + (z - seg.z0) * vz) / l2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double dx = x - (seg.x0 + t * vx), dz = z - (seg.z0 + t * vz);
            if (dx * dx + dz * dz < seg.halfW * seg.halfW) return true;
        }
        return false;
    }

    public static void Add(double x, double z, float s, System.Random rnd)
    {
        if (NearBridge(x, z)) return;   // keep stones clear of bridge decks/abutments
        float th = RiverWater.GH(x, z);
        // local ground normal at the drop point, so the rock can be tilted to sit flush with sloped banks
        // instead of always sitting dead-level (which digs into the uphill side / floats on the downhill side).
        Vector3 n = TerrainMath.NV(x, z);
        rocks.Add(new R { p = TerrainMath.U(x, th + 0.1f * s, z), s = s, yaw = (float)(rnd.NextDouble() * 6.2832), seed = rnd.Next(), normal = n });
    }

    public static void Build(Transform parent)
    {
        if (rocks.Count == 0) return;
        var set = GhibliNatureSet.Load();
        if (set == null || set.rocks.Count == 0)
        {
            Debug.LogError("[RiverRocks] GhibliNatureSet rocks missing (run menu Island/Ghibli/Build Nature Set); skipping " + rocks.Count + " stones.");
            rocks.Clear();
            return;
        }

        // sort rock pieces by height so we can size-match: small scale request -> small piece, big -> big piece
        var pieces = new List<GhibliPiece>(set.rocks);
        pieces.Sort((a, b) => (a.maxY - a.minY).CompareTo(b.maxY - b.minY));
        int np = pieces.Count;

        var lists = new List<Matrix4x4>[np];
        for (int i = 0; i < np; i++) lists[i] = new List<Matrix4x4>();

        // scale range actually requested across all Add() calls, for normalizing into a piece index
        float minS = float.MaxValue, maxS = float.MinValue;
        foreach (var r in rocks) { if (r.s < minS) minS = r.s; if (r.s > maxS) maxS = r.s; }
        float range = Mathf.Max(0.0001f, maxS - minS);

        foreach (var r in rocks)
        {
            var rg = new System.Random(r.seed);
            // deterministic piece pick: base index from size-matching, +/-1 jitter from seed for variety
            float t = (r.s - minS) / range;                 // 0..1
            int baseIdx = Mathf.Clamp(Mathf.RoundToInt(t * (np - 1)), 0, np - 1);
            int jitter = rg.Next(-1, 2);
            int pi = Mathf.Clamp(baseIdx + jitter, 0, np - 1);

            var piece = pieces[pi];
            float pieceHeight = Mathf.Max(0.01f, piece.maxY - piece.minY);
            // normalize the chosen piece so its height roughly matches the old procedural rock's target size
            // (old rocks were ~2x their 's' parameter tall at the peak vertex); keep a mild random variance.
            float targetHeight = r.s * 1.7f;
            float scale = (targetHeight / pieceHeight) * (0.85f + 0.3f * (float)rg.NextDouble());

            // Tilt the rock to follow the local ground slope instead of always sitting dead-level: this is
            // what was causing rocks to dig into the uphill side / hover over the downhill side on banks and
            // slopes (image: rocks on a steep shoreline bank with visible gaps underneath). Blend 75% toward
            // the surface normal rather than fully conforming - fully-aligned angular rock meshes can look like
            // they're glued flat against a slope; a partial tilt reads as "resting on" the ground naturally.
            Quaternion yawRot = Quaternion.Euler(0f, r.yaw * Mathf.Rad2Deg, 0f);
            Quaternion slopeRot = Quaternion.FromToRotation(Vector3.up, r.normal);
            Quaternion tilt = Quaternion.Slerp(Quaternion.identity, slopeRot, 0.75f);
            var rot = tilt * yawRot;
            // package rock pivots sit at mesh bounds min (same convention as trees/shrubs); offset down by minY*scale
            // along the TILTED up-axis (not world-up) so the rock's base still meets the tilted ground, not floats.
            Vector3 pos = r.p - (rot * Vector3.up) * (piece.minY * scale);
            lists[pi].Add(Matrix4x4.TRS(pos, rot, Vector3.one * scale));
        }

        var batches = new List<GhibliBatch>();
        for (int i = 0; i < np; i++)
        {
            if (lists[i].Count == 0) continue;
            var pc = pieces[i];
            var b = new GhibliBatch(pc.mesh, pc.materials) { shadows = UnityEngine.Rendering.ShadowCastingMode.On };
            b.Build(lists[i], 48f, new Vector3(pc.MaxXZ * 2f, pc.maxY * 2f, pc.MaxXZ * 2f));
            batches.Add(b);
        }

        var go = new GameObject("GhibliRiverRocks");
        go.transform.SetParent(parent, false);
        var driver = go.AddComponent<GhibliRockDriver>();
        driver.batches = batches;

        Debug.Log("[RiverRocks] " + rocks.Count + " stones placed using GHIBLI package rocks (" + np + " piece variants).");
        rocks.Clear();
    }
}

/// <summary>Tiny per-frame draw driver for GhibliBatch-rendered stones (RiverRocks has no MonoBehaviour of its own).</summary>
public class GhibliRockDriver : MonoBehaviour
{
    public List<GhibliBatch> batches;
    public float maxRenderDistance = 160f;
    readonly Plane[] planes = new Plane[6];

    void Update()
    {
        if (batches == null || batches.Count == 0) return;
        var cam = Camera.main; if (cam == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 cp = cam.transform.position;
        foreach (var b in batches) b.Draw(cp, planes, maxRenderDistance, maxRenderDistance, 0f);
    }
}
