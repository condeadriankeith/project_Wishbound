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
