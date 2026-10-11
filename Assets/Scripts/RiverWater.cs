using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River water that DRAPES over the real terrain mesh: every vertex is CAPPED to just above the ground,
/// so it can never float or climb banks. Edges tuck UNDER rising banks (hidden by the depth test) instead
/// of riding up them; near-vertical falls hide the ribbon entirely (wet rock + foam + mist sell the drop);
/// the source collapses to a point and fades in; the sea mouth rides AT sea level.
/// Banks are found by marching outwards, and rocks/pebbles chain along the waterline.
/// </summary>
public static class RiverWater
{
    /// <summary>Height of the rendered mesh. All water/rock placement must use this, never the analytic field.</summary>
    public static float GH(double x, double z) => TerrainMath.Hm(x, z);

    // walk outwards until the ground climbs 1.1 m above the channel floor (a bank) or maxW is reached
    static double March(double x, double z, double dx, double dz, double th0, double maxW)
    {
        for (double d = 0.5; d <= maxW; d += 0.5) if (GH(x + dx * d, z + dz * d) > th0 + 1.1) return Math.Max(0.4, d - 0.3);
        return maxW;
    }

    static void Smooth(double[] e) { for (int p = 0; p < 2; p++) { var c = (double[])e.Clone(); for (int m = 1; m < e.Length - 1; m++) e[m] = (c[m - 1] + 2 * c[m] + c[m + 1]) / 4; } }
    static double Sm01(double u) { u = u < 0 ? 0 : u > 1 ? 1 : u; return u * u * (3 - 2 * u); }

    public static void Build(List<Vector3> V, List<Vector2> UV, List<Vector2> UV2, List<Color> C, List<int> T, out RiverCarve.Channel longest)
    {
        longest = null; RiverRocks.Clear();
        const int CS = 9; // cross-section vertices
        foreach (var c in RiverCarve.Channels)
        {
            int M = c.x.Length; if (M < 2) continue;
            if (longest == null || c.s[M - 1] > longest.s[longest.x.Length - 1]) longest = c;
            var rnd = new System.Random(1337 + c.river * 131);
            var X = new List<double>(c.x); var Z = new List<double>(c.z); var S = new List<double>(c.s); var HW = new List<double>(c.hw);
            var SP = new List<float>(); var FL = new List<float>(); var FD = new List<float>(); var WS = new List<double>();
            for (int m = 0; m < M; m++)
            {
                int a = Math.Max(0, m - 1), b = Math.Min(M - 1, m + 1);
                double ds = Math.Max(0.01, c.s[b] - c.s[a]), sl = Math.Max(0, (c.bed[a] - c.bed[b]) / ds);
                SP.Add((float)Math.Min(14, 1.2 + sl * 8)); FL.Add((float)Math.Min(1, Math.Max(0, (sl - 0.6) / 1.0)));
                float ramp = c.lake ? 1f : Mathf.SmoothStep(0, 1, m / 12f);   // spring: fades in from a point
                FD.Add(ramp); WS.Add(c.lake ? 1.0 : Mathf.Lerp(0.05f, 1f, ramp));
            }
            bool toSea = c.bed[M - 1] < 1.0;
            if (toSea)   // reaches the sea: fan out into a fading mouth
            {
                int q = Math.Max(0, M - 4); double dx = c.x[M - 1] - c.x[q], dz = c.z[M - 1] - c.z[q], dl = Math.Sqrt(dx * dx + dz * dz);
                if (dl > 1e-6)
                {
                    dx /= dl; dz /= dl; int K = 8;
                    for (int p = 1; p <= K; p++) { X.Add(c.x[M - 1] + dx * 4 * p); Z.Add(c.z[M - 1] + dz * 4 * p); S.Add(c.s[M - 1] + 4 * p); HW.Add(c.hw[M - 1] * (1 + 0.3 * p)); SP.Add(SP[M - 1] * 0.7f); FL.Add(0); FD.Add(1f - (float)p / K); WS.Add(1.0); }
                }
            }
            int N = X.Count;
            var th = new double[N]; var yc = new double[N]; var eL = new double[N]; var eR = new double[N]; var tx = new double[N]; var tz = new double[N]; var casc = new double[N];
            for (int m = 0; m < N; m++) { th[m] = GH(X[m], Z[m]); yc[m] = Math.Max(0.05, th[m] + 0.32); }
            for (int p = 0; p < 3; p++) { var cc = (double[])yc.Clone(); for (int m = 1; m < N - 1; m++) yc[m] = (cc[m - 1] + 2 * cc[m] + cc[m + 1]) / 4; }
            for (int m = 0; m < N; m++) yc[m] = Math.Max(yc[m], th[m] + 0.12);
            // cascade mask from raw centreline grade: near-vertical spans hide the ribbon (wet rock + mist sell the fall)
            for (int m = 0; m < N; m++)
            {
                int a = Math.Max(0, m - 1), b = Math.Min(N - 1, m + 1);
                double grade = (th[a] - th[b]) / Math.Max(0.01, S[b] - S[a]);
                casc[m] = Sm01((grade - 1.0) / 1.0);
            }
            Smooth(casc); Smooth(casc);
            for (int m = 0; m < N; m++) { FD[m] *= (float)(1 - casc[m]); WS[m] *= 1 - 0.35 * casc[m]; }
            for (int m = 0; m < N; m++)
            {
                int a = Math.Max(0, m - 1), b = Math.Min(N - 1, m + 1);
                double ax = X[b] - X[a], az = Z[b] - Z[a], al = Math.Sqrt(ax * ax + az * az); if (al < 1e-6) { ax = 1; az = 0; al = 1; }
                tx[m] = ax / al; tz[m] = az / al;
                double maxW = (m < M ? HW[m] * 1.15 + 0.5 : HW[m] * 1.8 + 1) * WS[m] * (1 - 0.35 * FL[m]);
                eR[m] = March(X[m], Z[m], -tz[m], tx[m], th[m], maxW);
                eL[m] = March(X[m], Z[m], tz[m], -tx[m], th[m], maxW);
            }
            Smooth(eL); Smooth(eR);
            int baseI = V.Count;
            for (int m = 0; m < N; m++)
            {
                for (int k = 0; k < CS; k++)
                {
                    double a2 = (k - (CS - 1) / 2.0) / ((CS - 1) / 2.0), off = a2 < 0 ? a2 * eL[m] : a2 * eR[m];
                    double px = X[m] - tz[m] * off, pz = Z[m] + tx[m] * off, tk = GH(px, pz);
                    // CAP ONLY: centre rides just above the bed, edges tuck to a thin film. Rising banks bury
                    // the edge (depth test hides it) instead of growing a vertical curtain. Never floats.
                    double cap = tk + (0.42 - 0.28 * Math.Abs(a2));
                    double y = Math.Min(yc[m] - 0.26 * a2 * a2, cap);
                    if (tk < 0.25) y = Math.Max(y, 0.14);   // estuary / sea mouth rides at sea level, never dives
                    V.Add(TerrainMath.U(px, y, pz)); UV.Add(new Vector2((float)off, (float)S[m])); UV2.Add(new Vector2(FD[m], (float)casc[m]));
                    C.Add(new Color(SP[m] / 14f, 0, (float)Math.Abs(a2), FL[m]));
                }
                if (m < N - 1)
                {
                    for (int k = 0; k < CS - 1; k++) { int i0 = baseI + m * CS + k, i1 = i0 + 1, i2 = i0 + CS, i3 = i2 + 1; T.Add(i0); T.Add(i2); T.Add(i1); T.Add(i1); T.Add(i2); T.Add(i3); }
                    // no phantom water: queryable only where the ribbon is actually visible
                    float fdAvg = (FD[m] + FD[m + 1]) * 0.5f, cascAvg = (float)((casc[m] + casc[m + 1]) * 0.5);
                    if (fdAvg > 0.08f && cascAvg < 0.7f)
                        WaterField.Add(new WaterField.Seg { x0 = (float)X[m], z0 = (float)Z[m], x1 = (float)X[m + 1], z1 = (float)Z[m + 1], y0 = (float)yc[m], y1 = (float)yc[m + 1], w0 = (float)((eL[m] + eR[m]) * 0.5), w1 = (float)((eL[m + 1] + eR[m + 1]) * 0.5), v0 = SP[m], v1 = SP[m + 1] });
                }
                // waterline pebble CHAINS + bank rocks: dense at the water's edge, biased to outer bends
                if (m < M && FD[m] > 0.25f)
                {
                    int mn = Math.Min(N - 1, m + 1);
                    double turn = tx[m] * tz[mn] - tz[m] * tx[mn];
                    int outer = turn >= 0 ? 1 : -1;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double ext = side > 0 ? eR[m] : eL[m];
                        int nPeb = 1 + (rnd.NextDouble() < 0.6 ? 1 : 0) + (side == outer && rnd.NextDouble() < 0.5 ? 1 : 0);
                        for (int qp = 0; qp < nPeb; qp++)
                        {
                            double off = side * ext * (0.92 + 0.08 * qp + rnd.NextDouble() * 0.14);
                            RiverRocks.Add(X[m] - tz[m] * off + (rnd.NextDouble() - 0.5) * 0.6, Z[m] + tx[m] * off + (rnd.NextDouble() - 0.5) * 0.6,
                                (float)(0.05 + 0.11 * rnd.NextDouble() * rnd.NextDouble() + 0.05 * FL[m]), rnd);
                        }
                        if (rnd.NextDouble() < (0.28 + 0.35 * FL[m]) * (side == outer ? 1.6 : 1.0))
                        {
                            double off = side * ext * (1.05 + rnd.NextDouble() * 0.6);
                            RiverRocks.Add(X[m] - tz[m] * off, Z[m] + tx[m] * off, (float)((0.15 + 0.5 * Math.Pow(rnd.NextDouble(), 2.2)) * (1 + FL[m])), rnd);
                        }
                    }
                    if (rnd.NextDouble() < 0.025 && FL[m] < 0.3)
                    {
                        double off = (rnd.NextDouble() - 0.5) * (eL[m] + eR[m]) * 0.6;
                        RiverRocks.Add(X[m] - tz[m] * off, Z[m] + tx[m] * off, (float)(0.3 + 0.3 * rnd.NextDouble()), rnd);
                    }
                }
            }
            int m0 = -1;
            for (int m = 0; m <= M; m++)
            {
                bool st = m < M && FL[m] > 0.4f;
                if (st && m0 < 0) m0 = m;
                if (!st && m0 >= 0)
                {
                    int e = m - 1;
                    if (yc[m0] - yc[e] > 4)
                    {
                        WaterBuilder.Mist(X[e], yc[e], Z[e], (float)HW[e], (float)(yc[m0] - yc[e]));
                        for (int q = 0; q < 5; q++)   // plunge-pool boulders at the falls base
                        {
                            double an = rnd.NextDouble() * 6.2832, rr = HW[e] + 1.5 + rnd.NextDouble() * 2.5;
                            RiverRocks.Add(X[e] + Math.Cos(an) * rr, Z[e] + Math.Sin(an) * rr, (float)(0.4 + rnd.NextDouble() * 0.7), rnd);
                        }
                    }
                    m0 = -1;
                }
            }
        }
    }
}
