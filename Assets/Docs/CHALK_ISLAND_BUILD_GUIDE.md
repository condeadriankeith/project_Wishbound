# Bare Chalk Island — Complete Unity Build Guide

A walkable, true-to-life-scale (1 unit = 1 metre) stepped white-chalk island built from the
reference painting: a grassy base ring with sandy rim, four stacked chalk drums with spiral
grass ramps, and a summit pillar — bare terrain only, every level connected by continuous
walkable ground. First-person ready (1.8 m player, real human speeds).

> Provenance: this document describes the island as actually built and verified in this
> project (`Assets/Scenes/ChalkIsland.unity`). Every number in §1 and §10 is measured from
> the final build, not estimated. Code listings are the exact final scripts.

---

## 0. What you are building

### Painting → Unity reading

| Painting feature | Interpretation |
|---|---|
| Round grassy base ring with sandy rim | Meadow shelf (+4 m) and beach ring at sea level |
| Four stacked white-chalk drums, each narrower | 4 tiers: vertical chalk cliffs with grass terraces on top |
| Green ribbon spiralling up the cliff faces | One ledge ramp per tier, carved into the cliff, each starting where the last one ended |
| Vertical streaking on the chalk | Gullies (vertical grooves) in the cliff outline + a streaked chalk texture (top-down UV projection streaks vertical walls for free) |
| Blocky white tower with spire | Summit pillar (tier 4, squarish footprint), plain chalk with a flat top |
| Village, clouds, birds | **Excluded** — bare island. Only sea + sky so it doesn't float in a void |

### Scale contract

- 1 Unity unit = 1 metre. Player is 1.8 m tall (eye 1.65 m).
- Island ≈ 780 m across at the waterline, summit **+412 m** (≈ 3.8× Dover's White Cliffs).
- The painting is taller/thinner (height ≈ 0.9 × width) than this build (≈ 0.53 × width) —
  deliberate: at the painting's proportions the ramps would exceed 25% grade. See §12.

---

## 1. Numbers at a glance (all metres, measured)

| Item | Value |
|---|---|
| Sea level | y = 0 |
| Island diameter at waterline | ≈ 780 |
| Meadow shelf height | +4 |
| Summit (tier 4 top) | **+412** |
| Ramp widths | 24 / 22 / 20 / 12 |
| Ramp max grades (validator, 1200 samples each) | **9.9% / 13.1% / 14.8% / 14.5%** (all < 15%) |
| Total ramp length | **3926 m** (≈ 5 km with terrace walks) |
| Walk time, spawn → summit | ~47 min at 1.4 m/s · ~22 min jogging at 3 m/s |
| Spawn | (269, 4.9, −188), facing the island centre (yaw −55°) |
| Summit mark | (6, 412.5, −6) |

### Tier table (final, as built)

| Tier | Outer radius R | Rise ΔH | Top height | Ramp width w | Ramp start (deg from +X) | Sweep | Terrace above |
|---|---|---|---|---|---|---|---|
| 0 Meadow | 360 (beach 410, seabed 540) | — | 4 | — | — | — | — |
| 1 Chalk wall 1 | 300 | 146 | 150 | 24 | −35 | 330° | ≈ 71 |
| 2 Chalk wall 2 | 205 | 130 | 280 | 22 | −105 | 330° | ≈ 48 |
| 3 Chalk wall 3 | 135 | 90 | 370 | 20 | −175 | 330° | ≈ 53 |
| 4 Summit pillar | 66 (squarish, rot 20°) | 42 | 412 | 12 | −245 | 340° | plateau |

Each ramp ends ≈ 100° clockwise from where the next begins: one continuous clockwise
spiral with a terrace stroll between ramps.

### Grade rule (resize safely with this)

```text
grade ≈ 1.064 × ΔH / ( 2π × (R − w/2) × sweep/360° )
```

Keep ≤ 15% for comfortable walking. **Uniform `worldScale` scaling keeps grades identical.**
Only *independent* height-vs-radius scaling changes them. Note the formula assumes a smooth
spiral: outline warp that pulls the path inward tightens the circle and raises local grade
(§13, “17% on the pillar”).

---

## 2. Geometry: how the levels connect

```text
 y (m)
 412 ┤                        ┌──────┐          summit plateau
 370 ┤                 ┌──────┘      └──────┐   terrace 3
 280 ┤          ┌──────┘                    └──────┐   terrace 2
 150 ┤   ┌──────┘                                  └──────┐   terrace 1
   4 ┤───┘                                                └────── meadow
   0 ┼ ~~~~~~~~~~~~~~~~~~~~~~~~ sea ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
```

### The ledge-ramp trick (the core of the whole build)

Each ramp is a **ledge band** of width *w* just inside a tier's outer edge. Its height is a
function of angle: `E(θ)` runs 0 → 1 along a 330–340° clockwise sweep with eased
(flat) ends:

- **Bottom**: ledge height = base level → emerges flush from the ground, no step.
  The 8–10 m “mouth” wall beside the start is the natural cliff end (visible in-game as a
  thin chalk blade next to the ramp mouth — intended).
- **Top**: E = 1 → ledge band and terrace are the *same* height → the ramp widens into
  the terrace with no seam or lip.
- **Everything is one heightfield** `h(x, z)` → no mesh seams, no floating pieces, no gaps.
  Unity Terrain supplies collider, LOD and NavMesh support. Limit: no overhangs/caves
  (add rock meshes later if wanted).

---

## 3. Project setup

1. **Unity 6 (6000.x), URP template.** Active Input Handling must include the new Input
   System (this project uses *Both*; the controller below uses `UnityEngine.InputSystem`).
2. Package Manager → install: **Mathematics** (1.3.x), **AI Navigation** (2.0.x).
   Input System ships with the template. (Terrain Tools optional.)
3. Quality (URP asset): Shadow Distance ~1000, 4 cascades, soft shadows.
4. Folders:
   ```text
   Assets/Island/Scripts/   IslandParams.cs  IslandField.cs  IslandFPSController.cs  ReachabilityTest.cs
   Assets/Island/Editor/    IslandSetup.cs  IslandGenerator.cs  IslandValidator.cs
   Assets/Island/Terrain/   params + TerrainData + layers + volume profile (created by setup)
   Assets/Island/Materials/ Sea.mat, ChalkSky.mat (created during build)
   Assets/Island/Textures/  procedural layer textures (created by setup)
   Assets/Scenes/ChalkIsland.unity
   ```

---

## 4. Code (final, as built — copy verbatim)

### 4.1 `Assets/Island/Scripts/IslandParams.cs`

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "IslandParams", menuName = "Island/Island Params")]
public class IslandParams : ScriptableObject
{
    [Header("Terrain container")]
    public int heightmapRes = 4097;      // 2^n+1. Use 1025 while iterating, 4097 for final
    public float terrainSize = 1200f;    // square, metres
    public float seabedDepth = 60f;      // terrain origin sits this far below sea level
    public float terrainHeight = 520f;   // must exceed seabedDepth + summit (≈472)
    public int seed = 1337;
    [Min(0.1f)] public float worldScale = 1f;   // uniform scale. Grow terrainSize/terrainHeight too

    [Header("Meadow / beach / seabed")]
    public float meadowHeight = 4f;
    public float meadowRadius = 360f;
    public float beachRadius = 410f;
    public float seabedRadius = 540f;
    public float seabedFloor = 48f;

    [System.Serializable]
    public class Tier
    {
        public string name;
        public float radius = 100f;          // outer cliff radius (metric units)
        public float rise = 100f;            // height gained
        public float rampWidth = 20f;
        public float rampStartDeg = 0f;      // angle where ramp begins, deg from +X, CCW positive
        public float rampSweepDeg = 330f;    // ramp climbs CLOCKWISE (angle decreases)
        public float mouthLength = 10f;      // length of the cliff end-wall beside the ramp start
        public float squareness = 2f;        // 2 = circle, 4 = blocky pillar
        public float rotationDeg = 0f;
        public Vector2 offset;               // centre offset (organic, non-concentric look)
        public float gullyAmp = 4f;          // vertical-groove depth in the cliff outline
        public float gullyWavelength = 20f;
        public float talusHeight = 3f;       // rubble apron at cliff foot
        public float talusLength = 10f;
    }

    public Tier[] tiers = new Tier[]
    {
        new Tier{ name="Chalk Wall 1", radius=300, rise=146, rampWidth=24, rampStartDeg=-35,  gullyAmp=5, gullyWavelength=24, talusHeight=4, talusLength=14, offset=new Vector2(0,0) },
        new Tier{ name="Chalk Wall 2", radius=205, rise=130, rampWidth=22, rampStartDeg=-105, gullyAmp=5, gullyWavelength=22, talusHeight=4, talusLength=12, offset=new Vector2(10,6) },
        new Tier{ name="Chalk Wall 3", radius=135, rise=90,  rampWidth=20, rampStartDeg=-175, gullyAmp=4, gullyWavelength=18, talusHeight=3, talusLength=10, offset=new Vector2(-8,8) },
        new Tier{ name="Summit Pillar", radius=66, rise=42,  rampWidth=12, rampStartDeg=-245, rampSweepDeg=340, gullyAmp=2, gullyWavelength=12, talusHeight=2, talusLength=6,  squareness=4, rotationDeg=20, mouthLength=8, offset=new Vector2(6,-6) },
    };
}
```

### 4.2 `Assets/Island/Scripts/IslandField.cs` — the whole island as one function

Design points that keep grades ≤ 15% (all learned from failed validator runs, §13):

- `Warp` is ±10 m, not ±16 m: warp pulling the path inward tightens the spiral and
  raises local grade as `1/rho`.
- `RampPoint` follows the **slow warp only**. Chasing the fast gully wiggle wove ~40 m
  of extra path into the pillar ramp and spiked local grade to 24%. The uncompensated
  gully (±1.3×amp ≤ 6.8 m) always stays inside the ledge band (half-width ≥ 6 m).
- Micro rock roughness and rolling ground noise are **fully suppressed on the ledge band**
  (`1 − ledgeF`, damp floor 0.97) but kept on faces/terraces. At 1200 samples per ramp,
  even ±0.1 m of sub-metre noise reads as 20%+ grade spikes.

```csharp
using Unity.Mathematics;
using UnityEngine;

/// Pure math, thread-safe. Height(x,z) = metres above sea level.
public class IslandField
{
    const float TwoPi = 6.2831853f;
    readonly IslandParams p;
    readonly float2 o1, o2, o3;

    public IslandField(IslandParams p)
    {
        this.p = p;
        var rng = new System.Random(p.seed);
        float2 R() => new float2((float)rng.NextDouble() * 500f, (float)rng.NextDouble() * 500f);
        o1 = R(); o2 = R(); o3 = R();
    }

    public float Height(float wx, float wz) => Height(wx, wz, out _);

    /// rampMask: 0 = ordinary ground, ~1 = on a ramp ledge (used to keep props off the path)
    public float Height(float wx, float wz, out float rampMask)
    {
        float s = p.worldScale;
        float2 pos = new float2(wx, wz) / s;
        float r = math.length(pos);
        rampMask = 0f;

        float warp = Warp(pos);
        float h = BaseProfile(r + warp);
        if (r > 430f) return h * s;                 // open sea: skip tiers (speed)

        float damp = 1f;
        for (int k = 0; k < p.tiers.Length; k++)
            h += TierThickness(pos, p.tiers[k], k, warp, ref damp);

        // gentle rolling ground, damped on ramps so grades stay exact
        float land = math.smoothstep(0f, 3f, h);
        h += land * damp * (0.9f * noise.snoise(pos / 80f + o3)
                          + 0.2f * noise.snoise(pos / 14f + o3 * 1.7f));
        rampMask = 1f - damp;
        return h * s;
    }

    float Warp(float2 pos) =>
        10f * noise.snoise(pos * 0.0045f + o1) + 4f * noise.snoise(pos * 0.014f + o2);

    float BaseProfile(float r)
    {
        if (r < p.beachRadius)
            return math.lerp(p.meadowHeight, -2f, math.smoothstep(p.meadowRadius, p.beachRadius, r));
        return math.lerp(-2f, -p.seabedFloor, math.smoothstep(p.beachRadius, p.seabedRadius, r));
    }

    float Gully(float theta, IslandParams.Tier t, int k)
    {
        float2 cs = new float2(math.cos(theta), math.sin(theta));   // sampled on a circle: no seam at ±180°
        return t.gullyAmp * noise.snoise(cs * (t.radius / t.gullyWavelength) + o1 * (k + 1))
             + 0.3f * t.gullyAmp * noise.snoise(cs * (t.radius / (t.gullyWavelength * 0.27f)) + o2 * (k + 1));
    }

    static float Metric(float2 v, float n)
    {
        if (n <= 2.01f) return math.length(v);
        return math.pow(math.pow(math.abs(v.x), n) + math.pow(math.abs(v.y), n), 1f / n);
    }

    float TierThickness(float2 pos, IslandParams.Tier t, int k, float warp, ref float damp)
    {
        float2 q = pos - new float2(t.offset.x, t.offset.y);
        float a = math.radians(t.rotationDeg);
        float c = math.cos(a), sn = math.sin(a);
        float2 qr = new float2(c * q.x + sn * q.y, -sn * q.x + c * q.y);
        float theta = math.atan2(q.y, q.x);

        const float soft = 1.6f;                                          // wall transition width
        float rm0 = Metric(qr, t.squareness) + warp + Gully(theta, t, k);
        float ledgeF0 = math.smoothstep(-soft, soft, t.radius - rm0)
                      - math.smoothstep(-soft, soft, t.radius - t.rampWidth - rm0);
        float micro = (0.35f * noise.snoise(pos / 4f + k * 17.3f)
                    + 0.15f * noise.snoise(pos / 1.3f + k * 3.1f))
                    * (1f - ledgeF0);    // rock roughness on the faces only; the walkable ledge stays clean
        float rm = rm0 + micro;

        float outer = math.smoothstep(-soft, soft, t.radius - rm);
        float inner = math.smoothstep(-soft, soft, t.radius - t.rampWidth - rm);
        float E = RampE(theta, t);

        float thick = t.rise * (outer * E + inner * (1f - E));
        float talus = t.talusHeight * (1f - outer)
                    * math.exp(-math.max(rm - t.radius, 0f) / t.talusLength)
                    * math.saturate(t.rise * E / 30f);                    // no rubble where the ramp starts

        float ledge = outer - inner;   // full ledge band, end to end (terraces: outer-inner = 0, unaffected)
        damp = math.min(damp, 1f - 0.97f * ledge);
        return thick + talus;
    }

    static float Repeat(float v, float len) => v - math.floor(v / len) * len;

    static float RampE(float theta, IslandParams.Tier t)
    {
        float start = math.radians(t.rampStartDeg);
        float sweep = math.radians(t.rampSweepDeg);
        float d = Repeat(start - theta, TwoPi);                 // clockwise distance from ramp start
        if (d <= sweep) return EaseEnds(d / sweep);
        float md = TwoPi - d;                                   // anticlockwise distance back to start
        return math.smoothstep(0f, t.mouthLength / t.radius, md);   // the cliff end-wall ("mouth")
    }

    static float EaseEnds(float u)    // constant grade in the middle, flat at both ends
    {
        const float e = 0.06f;
        float v = 1f / (1f - e);
        if (u < e) return v * u * u / (2f * e);
        if (u > 1f - e) { float w = 1f - u; return 1f - v * w * w / (2f * e); }
        return v * (e * 0.5f + (u - e));
    }

    /// Centre-line of tier k's ramp in world metres. u: 0 = bottom, 1 = top.
    public Vector2 RampPoint(int k, float u)
    {
        var t = p.tiers[k];
        float theta = math.radians(t.rampStartDeg - u * t.rampSweepDeg);
        float2 d = new float2(math.cos(theta), math.sin(theta));
        float a = math.radians(t.rotationDeg);
        float c = math.cos(a), sn = math.sin(a);
        float2 dr = new float2(c * d.x + sn * d.y, -sn * d.x + c * d.y);
        float2 off = new float2(t.offset.x, t.offset.y);

        float shift = 0f; float2 pt = off;
        for (int i = 0; i < 4; i++)                  // fixed-point: follow the slow warp only (see note above)
        {
            float rho = (t.radius - t.rampWidth * 0.5f - shift) / Metric(dr, t.squareness);
            pt = off + d * rho;
            shift = Warp(pt);
        }
        return new Vector2(pt.x, pt.y) * p.worldScale;
    }
}
```

### 4.3 `Assets/Island/Editor/IslandGenerator.cs`

Heightmap and alphamap arrays are indexed `[z, x]` — that is what Unity expects.
At 4097² generation takes ~2 minutes; use 1025 while iterating (seconds).

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class IslandGenerator
{
    public static IslandParams FindParams()
    {
        var g = AssetDatabase.FindAssets("t:IslandParams");
        return g.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<IslandParams>(AssetDatabase.GUIDToAssetPath(g[0]));
    }

    [MenuItem("Tools/Island/1. Generate Terrain")]
    public static void Generate()
    {
        var p = FindParams();
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (p == null || terrain == null) { Debug.LogError("Need an IslandParams asset and a Terrain in the scene."); return; }

        var td = terrain.terrainData;
        td.heightmapResolution = p.heightmapRes;
        td.size = new Vector3(p.terrainSize, p.terrainHeight, p.terrainSize);
        int n = td.heightmapResolution;
        float step = p.terrainSize / (n - 1);
        float half = p.terrainSize * 0.5f;
        var field = new IslandField(p);
        var hm = new float[n, n];                      // [z, x]

        EditorUtility.DisplayProgressBar("Island", "Sampling heightfield…", 0.1f);
        Parallel.For(0, n, zi =>
        {
            float z = zi * step - half;
            for (int xi = 0; xi < n; xi++)
                hm[zi, xi] = math.saturate((field.Height(xi * step - half, z) + p.seabedDepth) / p.terrainHeight);
        });
        td.SetHeights(0, 0, hm);
        terrain.transform.position = new Vector3(-half, -p.seabedDepth, -half);   // island centred on world (0,0)

        EditorUtility.DisplayProgressBar("Island", "Painting layers…", 0.7f);
        PaintSplat(td, hm, p);
        EditorUtility.ClearProgressBar();
        EditorUtility.SetDirty(td);
        Debug.Log("Island generated.");
    }

    // Terrain layers must be assigned in this order: 0 Grass, 1 Chalk, 2 Sand, 3 Seabed
    static void PaintSplat(TerrainData td, float[,] hm, IslandParams p)
    {
        if (td.terrainLayers == null || td.terrainLayers.Length < 4)
        { Debug.LogWarning("Assign 4 Terrain Layers (Grass, Chalk, Sand, Seabed), then run Generate again."); return; }

        const int ar = 2048;
        td.alphamapResolution = ar;
        int n = hm.GetLength(0);
        float cell = p.terrainSize / (n - 1), half = p.terrainSize * 0.5f;
        var am = new float[ar, ar, 4];

        Parallel.For(0, ar, zi =>
        {
            int hz = Mathf.Clamp(Mathf.RoundToInt(zi * (n - 1f) / (ar - 1f)), 1, n - 2);
            for (int xi = 0; xi < ar; xi++)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt(xi * (n - 1f) / (ar - 1f)), 1, n - 2);
                float h  = hm[hz, hx] * p.terrainHeight - p.seabedDepth;
                float dx = (hm[hz, hx + 1] - hm[hz, hx - 1]) * p.terrainHeight / (2f * cell);
                float dz = (hm[hz + 1, hx] - hm[hz - 1, hx]) * p.terrainHeight / (2f * cell);
                float slope = math.degrees(math.atan(math.sqrt(dx * dx + dz * dz)));

                float2 w = new float2(hx * cell - half, hz * cell - half);
                float patch = math.saturate(noise.snoise(w / 9f) * 1.6f);       // grass tufts clinging to the chalk

                float chalk = math.smoothstep(50f, 66f, slope);
                chalk *= 1f - 0.65f * patch * (1f - math.smoothstep(58f, 70f, slope));
                float seabed = math.smoothstep(-0.5f, -2.5f, h);
                float sand = (1f - math.smoothstep(0.8f, 2.2f, h)) * (1f - seabed) * (1f - chalk);
                float grass = math.max(0f, 1f - chalk - seabed - sand);
                float sum = chalk + seabed + sand + grass + 1e-5f;
                am[zi, xi, 0] = grass / sum; am[zi, xi, 1] = chalk / sum;
                am[zi, xi, 2] = sand / sum;  am[zi, xi, 3] = seabed / sum;
            }
        });
        td.SetAlphamaps(0, 0, am);
    }

    [MenuItem("Tools/Island/2. Scatter Shrubs (optional)")]
    public static void ScatterShrubs()
    {
        var p = FindParams(); var terrain = Object.FindFirstObjectByType<Terrain>(); var td = terrain.terrainData;
        if (td.treePrototypes.Length == 0) { Debug.LogError("Add a shrub prefab as a Tree prototype first."); return; }
        var f = new IslandField(p);
        var rng = new System.Random(p.seed + 7);
        var list = new List<TreeInstance>();
        float half = p.terrainSize * 0.5f;
        for (int i = 0; i < 80000 && list.Count < 6000; i++)
        {
            float wx = ((float)rng.NextDouble() * 2f - 1f) * half;
            float wz = ((float)rng.NextDouble() * 2f - 1f) * half;
            float h = f.Height(wx, wz, out float ramp);
            if (h < 6f || ramp > 0.05f) continue;                     // not on beach, not on a ramp ledge
            float u = (wx + half) / p.terrainSize, v = (wz + half) / p.terrainSize;
            if (td.GetSteepness(u, v) > 28f) continue;
            list.Add(new TreeInstance
            {
                prototypeIndex = rng.Next(td.treePrototypes.Length),
                position = new Vector3(u, 0, v),
                widthScale = 0.8f + (float)rng.NextDouble() * 0.7f,
                heightScale = 0.8f + (float)rng.NextDouble() * 0.7f,
                color = Color.white, lightmapColor = Color.white,
                rotation = (float)rng.NextDouble() * 6.283f
            });
        }
        td.SetTreeInstances(list.ToArray(), true);
        Debug.Log($"Placed {list.Count} shrubs.");
    }

    [MenuItem("Tools/Island/3. Fill Grass Detail (optional)")]
    public static void FillGrass()
    {
        var terrain = Object.FindFirstObjectByType<Terrain>(); var td = terrain.terrainData;
        if (td.detailPrototypes.Length == 0) { Debug.LogError("Add a grass Detail prototype first."); return; }
        const int res = 2048;
        td.SetDetailResolution(res, 32);
        float[,,] am = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
        int aw = td.alphamapWidth;
        var d = new int[res, res];
        var rng = new System.Random(1);
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float g = am[z * aw / res, x * aw / res, 0];
                d[z, x] = (g > 0.6f && rng.NextDouble() < 0.55) ? 6 : 0;
            }
        td.SetDetailLayer(0, 0, 0, d);
    }
}
#endif
```

### 4.4 `Assets/Island/Editor/IslandValidator.cs`

Run after every parameter change. Green→red ramp overlays draw in the Scene view
(green = gentle, red = steep). All four lines below must read OK with grade < 15%.

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class IslandValidator
{
    [MenuItem("Tools/Island/4. Validate Walkability")]
    public static void Run()
    {
        var p = IslandGenerator.FindParams();
        var f = new IslandField(p);
        float baseH = p.meadowHeight;
        float total = 0f;

        for (int k = 0; k < p.tiers.Length; k++)
        {
            var t = p.tiers[k];
            const int N = 1200;
            float len = 0f, maxGrade = 0f;
            Vector2 prev = f.RampPoint(k, 0);
            float prevH = f.Height(prev.x, prev.y), h0 = prevH, h1 = prevH;

            for (int i = 1; i <= N; i++)
            {
                Vector2 c = f.RampPoint(k, i / (float)N);
                float h = f.Height(c.x, c.y);
                float d = Vector2.Distance(prev, c);
                len += d;
                float g = d > 0.01f ? Mathf.Abs(h - prevH) / d : 0f;
                maxGrade = Mathf.Max(maxGrade, g);
                Debug.DrawLine(new Vector3(prev.x, prevH + 1f, prev.y), new Vector3(c.x, h + 1f, c.y),
                               Color.Lerp(Color.green, Color.red, g / 0.2f), 120f);   // Scene view overlay
                prev = c; prevH = h; h1 = h;
            }
            total += len;

            float expStart = baseH, expEnd = baseH + t.rise;
            bool joinOK = Mathf.Abs(h0 - expStart) < 2f && Mathf.Abs(h1 - expEnd) < 2f;
            Debug.Log($"[{t.name}] length {len:F0} m | max grade {maxGrade * 100f:F1}% | start {h0:F1} (exp {expStart:F1}) | end {h1:F1} (exp {expEnd:F1}) | {(joinOK ? "OK" : "JOIN MISMATCH")}" +
                      (maxGrade > 0.18f ? "  <-- TOO STEEP" : ""));
            baseH = expEnd;

            if (k > 0)   // nesting margin: child footprint must sit fully inside parent's terrace
            {
                var par = p.tiers[k - 1];
                float ext = t.radius * (t.squareness > 2.01f ? Mathf.Pow(2f, 0.5f - 1f / t.squareness) : 1f)
                          + (t.offset - par.offset).magnitude + 1.3f * (t.gullyAmp + par.gullyAmp) + 3f;
                float room = par.radius - par.rampWidth;
                Debug.Log($"   nesting {par.name} -> {t.name}: needs {ext:F0} < {room:F0}  {(ext < room ? "OK" : "OVERLAP: shrink child or enlarge parent")}");
            }
        }

        var s = f.RampPoint(0, 0) * 1.12f;
        var o = p.tiers[p.tiers.Length - 1].offset * p.worldScale;
        Debug.Log($"Total ramp length {total:F0} m | Spawn ≈ ({s.x:F0}, {f.Height(s.x, s.y) + 1f:F1}, {s.y:F0}) | Summit ≈ ({o.x:F0}, {f.Height(o.x, o.y) + 1f:F1}, {o.y:F0})");
    }
}
#endif
```

Expected console output (the acceptance bar):

```text
[Chalk Wall 1] length 1679 m | max grade 9.9%  | start 4.0   (exp 4.0)   | end 150.0 (exp 150.0) | OK
[Chalk Wall 2] length 1135 m | max grade 13.1% | start 150.0 (exp 150.0) | end 280.0 (exp 280.0) | OK
   nesting Chalk Wall 1 -> Chalk Wall 2: needs 233 < 276  OK
[Chalk Wall 3] length 732 m  | max grade 14.8% | start 280.0 (exp 280.0) | end 370.0 (exp 370.0) | OK
   nesting Chalk Wall 2 -> Chalk Wall 3: needs 168 < 183  OK
[Summit Pillar] length 380 m | max grade 14.5% | start 370.0 (exp 370.0) | end 412.0 (exp 412.0) | OK
   nesting Chalk Wall 3 -> Summit Pillar: needs 109 < 115  OK
Total ramp length 3926 m | Spawn ≈ (269, 4.9, -189) | Summit ≈ (6, 412.5, -6)
```

### 4.5 `Assets/Island/Scripts/IslandFPSController.cs` (new Input System)

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class IslandFPSController : MonoBehaviour
{
    public Transform cam;
    public float walk = 1.4f, jog = 3.0f, sprint = 5.5f;   // real human speeds (m/s)
    public float lookSens = 0.12f, gravity = -9.81f, jumpHeight = 0.6f;
    CharacterController cc; float pitch; float vy;

    void Awake() { cc = GetComponent<CharacterController>(); Cursor.lockState = CursorLockMode.Locked; }

    void Update()
    {
        var kb = Keyboard.current; var ms = Mouse.current;
        if (kb == null || ms == null) return;

        Vector2 look = ms.delta.ReadValue() * lookSens;
        transform.Rotate(0, look.x, 0);
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        cam.localEulerAngles = new Vector3(pitch, 0, 0);

        Vector2 m = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
        float speed = kb.leftShiftKey.isPressed ? sprint : kb.leftCtrlKey.isPressed ? walk : jog;
        Vector3 move = transform.right * m.x + transform.forward * m.y;
        if (move.sqrMagnitude > 1f) move.Normalize();

        if (cc.isGrounded && vy < 0) vy = -2f;
        if (cc.isGrounded && kb.spaceKey.wasPressedThisFrame) vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
        vy += gravity * Time.deltaTime;

        cc.Move((move * speed + Vector3.up * vy) * Time.deltaTime);
        if (transform.position.y < -3f) transform.position = new Vector3(0, 600f, 0);   // replace with your respawn
    }
}
```

### 4.6 `Assets/Island/Scripts/ReachabilityTest.cs`

```csharp
using UnityEngine;
using UnityEngine.AI;

public class ReachabilityTest : MonoBehaviour
{
    public Transform from, to;     // from = spawn, to = summit (positions printed by the validator)
    [ContextMenu("Run")]
    void Run()
    {
        NavMesh.SamplePosition(from.position, out var a, 10f, NavMesh.AllAreas);
        NavMesh.SamplePosition(to.position, out var b, 10f, NavMesh.AllAreas);
        var path = new NavMeshPath();
        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
        Debug.Log($"Path status: {path.status} | corners: {path.corners.Length}");
    }
}
```

### 4.7 `Assets/Island/Editor/IslandSetup.cs` (one-click asset scaffolding)

Creates the params asset, Terrain + TerrainData, and four procedural TerrainLayers
(grass `#7DB544` 5 m, chalk `#F4F1EA` 14 m with vertical streaks, sand `#D9C9A0` 4 m,
seabed `#6FA38B` 8 m). Replace the generated textures with Quixel/ambientCG surfaces
for the final look (§6).

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class IslandSetup
{
    [MenuItem("Tools/Island/0. Setup Scene Assets")]
    public static void Setup()
    {
        IslandParams p = IslandGenerator.FindParams();
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<IslandParams>();
            p.heightmapRes = 1025;   // iterate fast; raise to 4097 for final
            AssetDatabase.CreateAsset(p, "Assets/Island/Terrain/IslandParams.asset");
        }

        Terrain terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null)
        {
            TerrainData td = new TerrainData();
            td.heightmapResolution = 1025;
            td.size = new Vector3(p.terrainSize, p.terrainHeight, p.terrainSize);
            AssetDatabase.CreateAsset(td, "Assets/Island/Terrain/ChalkTerrain.asset");
            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "ChalkIsland";
            terrain = go.GetComponent<Terrain>();
        }

        EnsureLayers(terrain);

        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 1;
        terrain.basemapDistance = 2000;
        terrain.detailObjectDistance = 120;
        terrain.treeDistance = 500;
        terrain.treeBillboardDistance = 150;

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        Debug.Log("Island setup done: params + terrain + 4 layers assigned.");
    }

    static void EnsureLayers(Terrain terrain)
    {
        string[] names = { "Grass", "Chalk", "Sand", "Seabed" };
        Color[] cols = {
            new Color(0x7D / 255f, 0xB5 / 255f, 0x44 / 255f),
            new Color(0xF4 / 255f, 0xF1 / 255f, 0xEA / 255f),
            new Color(0xD9 / 255f, 0xC9 / 255f, 0xA0 / 255f),
            new Color(0x6F / 255f, 0xA3 / 255f, 0x8B / 255f),
        };
        float[] tiles = { 5f, 14f, 4f, 8f };
        var layers = new TerrainLayer[4];
        for (int i = 0; i < 4; i++)
        {
            string texPath = "Assets/Island/Textures/T_" + names[i] + ".asset";
            string layPath = "Assets/Island/Terrain/Layer_" + names[i] + ".terrainlayer";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { tex = MakeTexture(cols[i], i); AssetDatabase.CreateAsset(tex, texPath); }
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layPath);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, layPath); }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(tiles[i], tiles[i]);
            layer.smoothness = i == 0 ? 0.1f : 0.05f;
            EditorUtility.SetDirty(layer);
            layers[i] = layer;
        }
        terrain.terrainData.terrainLayers = layers;
        EditorUtility.SetDirty(terrain.terrainData);
    }

    static Texture2D MakeTexture(Color c, int kind)
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGB24, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        var rng = new System.Random(1000 + kind);
        float[] colHash = new float[S];
        for (int x = 0; x < S; x++) colHash[x] = (float)rng.NextDouble();
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = (float)rng.NextDouble() - 0.5f;
                float v = 1f + n * 0.10f;
                if (kind == 1)   // chalk: faint vertical streaks + grey-pink mottling
                    v *= 1f + (colHash[x] - 0.5f) * 0.10f + ((x * 7 + y * 13) % 5 == 0 ? -0.03f : 0f);
                if (kind == 0)   // grass: fine painterly grain (no tiling pattern)
                    v *= 1f + (n * 0.5f + (colHash[(x * 13 + y * 7) % S] - 0.5f) * 0.5f) * 0.12f;
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(c.r * v + (kind == 1 && (x + y) % 11 == 0 ? 0.02f : 0f)),
                    Mathf.Clamp01(c.g * v),
                    Mathf.Clamp01(c.b * v + (kind == 1 && (x * 3 + y) % 13 == 0 ? 0.015f : 0f))));
            }
        tex.Apply(true);
        return tex;
    }
}
#endif
```

---

## 5. Step-by-step build order

| # | Step | Result / time |
|---|---|---|
| 1 | New URP project (or open this one). Install **Mathematics** + **AI Navigation**. Create folders + the 7 scripts above | 15 min |
| 2 | New scene `Assets/Scenes/ChalkIsland.unity`. Run **Tools → Island → 0. Setup Scene Assets** (params at 1025, terrain, 4 layers) | Seconds |
| 3 | Run **1. Generate Terrain**. Fly around the Scene view: 4 tiers, spiral ledges, summit pillar | Seconds at 1025 |
| 4 | Run **4. Validate Walkability**. Demand the §4.4 output: all OK, grades < 15% | Fix via §13 before texturing |
| 5 | Import real textures (§6) or keep procedural; set `heightmapRes = 4097`, regenerate | ~2 min one-off |
| 6 | Water + sun + sky + fog + ACES (§8) | 10 min |
| 7 | Player at spawn (§9). Add NavMeshSurface (voxel override 0.5), bake, run chained reachability (§10) | Bake ~1 min |
| 8 | Optional: shrubs / grass detail (menu 2–3, need prototypes) | Dressing |
| 9 | Walk spawn → summit, run the QA checklist (§10) | ~25 min jogging |

---

## 6. Texturing (the chalk look)

Top-down UV projection means a vertical wall samples one texel column top-to-bottom →
**automatic vertical streaking**, like the painting. Choose textures accordingly.

| Layer | Tile | Notes |
|---|---|---|
| 0 Grass | 5 m | Yellow-green `#7DB544` (shadow `#4A8A2E`), smoothness 0.1 |
| 1 Chalk | 14 m | Off-white `#F4F1EA` (shadow `#E8D9D4`), fine grain, faint vertical streaks, **no horizontal bricks/cracks** (they stretch). Smoothness 0.05 |
| 2 Sand | 4 m | Pale beige `#D9C9A0` |
| 3 Seabed | 8 m | Sandy with green hint |

Free sources: Fab / Quixel Megascans, ambientCG, Poly Haven.

**Automatic splat rules** (`PaintSplat`): slope 50–66° → chalk (9 m noise lets grass cling
to moderate slopes); height 0.8–2.2 m → sand beach; below waterline → seabed; else grass.

---

## 7. Terrain inspector settings

| Setting | Value | Why |
|---|---|---|
| Draw Instanced | On | Performance |
| **Pixel Error** | **1** | Higher values melt vertical cliffs at distance |
| Base Map Dist. | 2000 | Cliffs stay textured far away |
| Detail Distance | 120 | Grass cost |
| Tree Distance / Billboard Start | 500 / 150 | |
| Shadow Distance (URP asset) | 1000, 4 cascades, soft | Cliff shading sells the scale |

---

## 8. Surroundings

- **Sea**: Plane 40 × 40 km at y = 0, URP/Lit transparent (`_Surface` 1, alpha-blend,
  render queue 3000), `#1F7FC0` at 0.93 alpha. Seabed (−48 m) + deep colour hides the
  terrain's square edge.
- **Sun**: Directional, rotation (52°, −35°, 0), warm white, 1.3, soft shadows.
- **Sky**: gradient skybox, deep blue `#1E6FC4` overhead → pale `#8EC5EE` horizon
  (built-in `Skybox/Procedural` works; this build uses a two-stop gradient shader).
- **Fog**: exponential-squared, 0.0002, `#C8DCEC`.
- **Post**: global Volume, ACES tone mapping (a mild bloom 0.2 + slight saturation lift
  gets closer to the painting).

---

## 9. Player and first-person scale

1. Empty **Player** + **CharacterController**: Height 1.8, Radius 0.3, Center (0, 0.9, 0),
   Step Offset 0.35, Slope Limit 40, Skin Width 0.03.
2. Child **Main Camera** at (0, 1.65, 0), FOV 70, near 0.1, far 20000.
3. `IslandFPSController`, camera assigned. Place at spawn **(269, 9, −188)**, yaw −55°.
   (Y = 9 lets it settle onto the ~5 m meadow.)
4. Scale props (temporary): 1.8 m capsule + door-size box at the first cliff. If the
   146 m wall doesn't feel absurd next to the door, something is mis-scaled.

Speeds: walk 1.4 · jog 3.0 (default) · sprint 5.5 m/s — the climb takes as long as it
really would.

---

## 10. Cohesion QA checklist (measured)

| Junction | Check | Result |
|---|---|---|
| Meadow → ramp 1 | Ledge flush, no step | start 4.0 = exp 4.0 OK |
| Ramp tops → terraces | Merge, no seam/lip | ends 150.0 / 280.0 / 370.0 / 412.0, all OK |
| Terrace → next ramp | Walkable ring between tiers | nesting 233<276, 168<183, 109<115, all OK |
| Ramp 4 → plateau | Flat summit | plateau on NavMesh at (6, 412, −6) |
| Whole route | Spawn reaches summit | chained NavMesh legs **all PathComplete** |

**NavMesh bake:** `NavMeshSurface` on its own GameObject, `collectObjects = All`
(deactivate Sea + Player first so the ocean doesn't bake as walkable), agent defaults,
`overrideVoxelSize`, voxel 0.5. **Caveat:** one 5 km `CalculatePath` returns `PathPartial`
(A* node budget) — prove continuity with chained leg queries (spawn→T1 top→…→plateau),
each of which is PathComplete.

---

## 11. Performance

- 4097² Terrain is fine on mid/high desktop. Weaker targets: 2049² (0.59 m cells).
- Keep Pixel Error at 1 rather than lowering resolution — silhouettes degrade first.
- Shrubs ≤ 6000, grass cull 120 m. Single directional light; bake ambient if needed.

---

## 12. Taller variant (closer to the painting's 0.9 ratio)

1. Multiply every `rise` ×1.4 (204/182/126/59 — recompute, don't guess) and `terrainHeight` → 700.
2. Grades land ≈ 13–18%: hiking-grade but legal; re-run the validator. Raise Slope Limit
   to 45 if the controller slips.
3. Or widen radii instead (longer ramps, same grades, bigger island).
4. Uniform `worldScale` never changes grades — only independent height/radius edits do.

---

## 13. Troubleshooting (every row earned during this build)

| Symptom | Cause / fix |
|---|---|
| Validator 20–24% on the pillar | Path chased gully wiggle (+40 m weave). Fix: warp-only `RampPoint` (§4.2). First measured 24.3% → 14.5% |
| Grades 17–18% everywhere | Sub-metre noise at 1200 samples reads as grade. Fix: zero micro + 0.97 damp floor **on the ledge only** |
| Still >15% on a tier | Warp tightens the spiral (`grade ∝ 1/rho`). Fix: raise R / lower ΔH / sweep ≤ 345° (pillar: 66 / 42 / 340°) |
| `JOIN MISMATCH` | Tier overlaps parent's ramp band → shrink child radius/offset |
| Single NavMesh query `PathPartial` | A* node budget on 5 km routes → use chained leg queries |
| Cliffs near-black from one side | You're on the shadow side; sun (52,−35) lights the spawn-facing slopes. Shoot hero views from the lit side |
| Neon grass | Linear-space lighting; ACES + real albedo textures calm it. Procedural placeholders run vivid |
| `Input.GetAxis` errors | New Input System project — use the provided controller, never legacy `Input` |
| Slow iteration | `heightmapRes = 1025` until final, then 4097 |
| Terrain edge visible | Deepen sea colour/alpha or enlarge `terrainSize` |

---

## 14. Extending later

- **Village/tower:** place at meadow ring + summit plateau; use `rampMask` (1 = on path)
  to keep props off ledges.
- **Overhangs/caves:** rock meshes over the heightfield (it can't do them natively).
- **Moods/seasons:** swap layers + sky only; geometry never changes.
- **More islands:** reuse `IslandField` with a new `seed`.
