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
        for (int i = 0; i < 4; i++)                  // fixed-point: follow the slow warp only.
        {                                            // Chasing the fast gully wiggle weaves the path and spikes
                                                     // local grade; the uncompensated gully (±1.3×amp) stays
                                                     // well inside the ledge band, so the path stays smooth.
            float rho = (t.radius - t.rampWidth * 0.5f - shift) / Metric(dr, t.squareness);
            pt = off + d * rho;
            shift = Warp(pt);
        }
        return new Vector2(pt.x, pt.y) * p.worldScale;
    }
}
