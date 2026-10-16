using UnityEngine;
using static TerrainMath;

/// <summary>F3 cycles overlay: off / height / slope bands / distance-to-sea. Top = north (+Unity Z).</summary>
public class WorldFieldDebug : MonoBehaviour
{
    public int mode = 0;            // 0 off, 1 height, 2 slope bands, 3 sea distance, 4 surface, 5 roads
    public float size = 380f;
    public bool debugHotkeyEnabled = false;   // F3 overlay is off by default (no UI in the game view)
    Texture2D[] tex = new Texture2D[7];
    GUIStyle st;

    void Start() { WorldField.Build(); Debug.Log(WorldField.SelfTest()); }

    static Color SurfCol(SurfaceType t)
    {
        switch (t)
        {
            case SurfaceType.Sand: return new Color(0.94f, 0.88f, 0.7f);
            case SurfaceType.Grass: return new Color(0.2f, 0.62f, 0.2f);
            case SurfaceType.Field: return new Color(0.7f, 0.8f, 0.3f);
            case SurfaceType.Dirt: return new Color(0.55f, 0.38f, 0.2f);
            case SurfaceType.DirtPath: return new Color(0.8f, 0.45f, 0.15f);
            case SurfaceType.Scree: return new Color(0.5f, 0.5f, 0.52f);
            case SurfaceType.CliffWall: return Color.white;
            default: return new Color(0.1f, 0.25f, 0.5f);
        }
    }

    Texture2D Get(int m)
    {
        if (tex[m] != null) return tex[m];
        int G = WorldField.G;
        var t = new Texture2D(G, G, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[G * G];
        for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                int k = j * G + i; float h = WorldField.HeightGrid[k], s = WorldField.SlopeGrid[k];
                Color c;
                if (h <= WorldField.SeaLevel) c = new Color(0.1f, 0.25f, 0.5f);
                else if (m == 1) c = Color.Lerp(new Color(0.2f, 0.6f, 0.2f), Color.white, Mathf.Clamp01(h / WorldField.MaxHeight));
                else if (m == 2) c = s < 6f ? new Color(0.2f, 0.75f, 0.25f) : s < 14f ? new Color(0.9f, 0.85f, 0.2f) : s < 38f ? new Color(0.95f, 0.5f, 0.15f) : new Color(0.85f, 0.1f, 0.1f);
                else if (m == 6) c = ZoneMap.ColorAt(k); else if (m == 4) { double gx = -E + i * WorldField.Cell, gz = -E + j * WorldField.Cell; c = SurfCol(Surface.Classify(gx, gz, h, Mathf.Cos(s * Mathf.Deg2Rad), Surface.PathAt(gx, gz))); }
                else if (m == 5) c = Color.Lerp(new Color(0.12f, 0.14f, 0.12f), new Color(0.55f, 0.6f, 0.55f), Mathf.Clamp01(h / Mathf.Max(1f, WorldField.MaxHeight)));
                else c = Color.Lerp(Color.black, Color.cyan, Mathf.Clamp01(WorldField.SeaDist[k] / Mathf.Max(1f, WorldField.MaxSeaDist)));
                px[(G - 1 - j) * G + i] = c;   // flip so top = +Unity Z
            }
        if (m == 5) PaintRoads(px, G);
        if (m == 6) ZoneMap.Paint(px);
        t.SetPixels(px); t.Apply(); return tex[m] = t;
    }

    // paint the road graph (spine orange, branches yellow) + water crossings (cyan bridges / white fords)
    void PaintRoads(Color[] px, int G)
    {
        RoadNet.Build();
        foreach (var p in RoadNet.Paths)
        {
            Color col = p.main ? new Color(1f, 0.5f, 0.05f) : new Color(1f, 0.9f, 0.1f);
            for (int n = 0; n < p.x.Length; n++)
            {
                int i = Mathf.RoundToInt((float)((p.x[n] + E) / WorldField.Cell));
                int j = Mathf.RoundToInt((float)((p.z[n] + E) / WorldField.Cell));
                if (i < 0 || j < 0 || i >= G || j >= G) continue;
                px[(G - 1 - j) * G + i] = col;
            }
        }
        foreach (var r in RoadBuild.Roads)
            foreach (var c in r.crosses)
            {
                Color col = c.bridge ? Color.cyan : Color.white;
                for (int q = c.i0; q <= c.i1 && q < r.x.Length; q++)
                {
                    int i = Mathf.RoundToInt((float)((r.x[q] + E) / WorldField.Cell));
                    int j = Mathf.RoundToInt((float)((r.z[q] + E) / WorldField.Cell));
                    if (i < 0 || j < 0 || i >= G || j >= G) continue;
                    px[(G - 1 - j) * G + i] = col;
                }
            }
    }

    void OnDrawGizmos() { if (mode == 6) ZoneMap.DrawGizmos(); }
    void Update()
    {
        if (debugHotkeyEnabled) { try { if (Input.GetKeyDown(KeyCode.F3)) { mode = (mode + 1) % 7; tex = new Texture2D[7]; } } catch { } }
    }

    void OnGUI()
    {
        if (mode == 0) return;
        if (st == null) st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
        var r = new Rect(Screen.width - size - 10, 10, size, size);
        GUI.DrawTexture(r, Get(mode));
        var cam = Camera.main;
        string info = "F3 mode: " + new[] { "off", "height", "slope (green<6, yellow<14, orange<38, red cliff)", "dist to sea", "surface (sand/grass/field/dirt/path/scree/cliff)", "roads (orange spine, yellow branch, cyan bridge, white ford)", ZoneMap.Legend() }[mode];
        if (cam != null)
        {
            double x, z; WorldField.FromWorld(cam.transform.position, out x, out z);
            float fx = (float)((x + E) / (2 * E)), fy = (float)((E + z) / (2 * E)) ; // z source -> top = -z
            float gy = (float)((E - (-z)) / (2 * E));
            GUI.DrawTexture(new Rect(r.x + fx * size - 4, r.y + (1f - (float)((-z + E) / (2 * E))) * size - 4, 8, 8), Texture2D.whiteTexture);
            info += $"\nH {WorldField.Height(x, z):F1} m  slope {WorldField.SlopeDeg(x, z):F1}\u00b0  cliff {WorldField.CliffMask(x, z):F2}  sea {WorldField.DistToSea(x, z):F0} m";
        }
        GUI.Label(new Rect(r.x, r.yMax + 2, size + 200, 60), info, st);
    }
}
