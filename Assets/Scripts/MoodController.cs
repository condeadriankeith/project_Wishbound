using UnityEngine;
using static TerrainMath;

[System.Serializable]
public class Mood
{
    public Color top, mid, hor, fog, sc, hs, hg, dp, sh, gl;
    public float si, hi, ex, bm; public Vector3 sd;
    public Mood Clone() => (Mood)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(Mood));
}

[ExecuteAlways]
public class MoodController : MonoBehaviour
{
    public Light sun; public Material skyMat, oceanMat; public Material beamMat;
    public Mood[] moods = new Mood[4];
    public int index;
    public string[] moodNames = { "Day", "Golden", "Dusk", "Night" };
    public string CurrentName => moodNames != null && moodNames.Length > 0 ? moodNames[index % moodNames.Length] : "Day";

    Mood cur;
    Vector3 sunSrc = new Vector3(-0.52f, 0.68f, 0.51f).normalized;

    static Color C(uint hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);

    void Awake() { EnsureMoods(); if (cur == null) cur = moods[0].Clone(); }

    void EnsureMoods()
    {
        if (moods == null || moods.Length != 4 || moods[0] == null) BuildDefaults();
    }

    void BuildDefaults()
    {
        moods = new Mood[4];
        moods[0] = new Mood { top = C(0x1357ad), mid = C(0x60acf0), hor = C(0x89cdf7), fog = C(0x92c7f0), sc = C(0xfffaea), hs = C(0xdcf0ff), hg = C(0x5b7f4e), dp = C(0x004178), sh = C(0x0a9ed6), gl = C(0xfff4d0), si = 1.45f, hi = 1.15f, ex = 1.25f, bm = 0.04f, sd = new Vector3(-0.52f, 0.68f, 0.51f).normalized };
        moods[1] = new Mood { top = C(0x2e5c94), mid = C(0xf5a867), hor = C(0xffcb8a), fog = C(0xf3bf8c), sc = C(0xffba6a), hs = C(0xffd5b0), hg = C(0x5a6341), dp = C(0x093863), sh = C(0x1b94a8), gl = C(0xffb566), si = 1.65f, hi = 0.85f, ex = 1.2f, bm = 0.18f, sd = new Vector3(-0.85f, 0.28f, 0.44f).normalized };
        moods[2] = new Mood { top = C(0x161c44), mid = C(0x764282), hor = C(0xff7b6a), fog = C(0xb57088), sc = C(0xff8662), hs = C(0x7f7fb2), hg = C(0x32354c), dp = C(0x061432), sh = C(0x225d80), gl = C(0xff8866), si = 1.15f, hi = 0.65f, ex = 1.35f, bm = 0.55f, sd = new Vector3(-0.92f, 0.11f, 0.32f).normalized };
        moods[3] = new Mood { top = C(0x040817), mid = C(0x0c1733), hor = C(0x16274d), fog = C(0x101c38), sc = C(0x9fb5e0), hs = C(0x25355e), hg = C(0x121a2b), dp = C(0x020712), sh = C(0x081b36), gl = C(0xffe080), si = 0.35f, hi = 0.42f, ex = 1.45f, bm = 0.95f, sd = new Vector3(-0.3f, 0.8f, 0.5f).normalized };
    }

    public void Next() { EnsureMoods(); index = (index + 1) % moods.Length; }
    public void SnapTo(int i) { EnsureMoods(); index = ((i % moods.Length) + moods.Length) % moods.Length; cur = moods[index].Clone(); sunSrc = moods[index].sd.normalized; ApplyImmediate(); }

    void Update()
    {
        EnsureMoods();
        if (cur == null) { cur = moods[index].Clone(); sunSrc = moods[index].sd.normalized; }
        var m = moods[index];
        float dt = Application.isPlaying ? Time.deltaTime : 0.05f;
        float k = 1 - Mathf.Exp(-dt * 2f);
        cur.top = Color.Lerp(cur.top, m.top, k); cur.mid = Color.Lerp(cur.mid, m.mid, k); cur.hor = Color.Lerp(cur.hor, m.hor, k);
        cur.fog = Color.Lerp(cur.fog, m.fog, k); cur.sc = Color.Lerp(cur.sc, m.sc, k);
        cur.hs = Color.Lerp(cur.hs, m.hs, k); cur.hg = Color.Lerp(cur.hg, m.hg, k);
        cur.dp = Color.Lerp(cur.dp, m.dp, k); cur.sh = Color.Lerp(cur.sh, m.sh, k); cur.gl = Color.Lerp(cur.gl, m.gl, k);
        cur.si = Mathf.Lerp(cur.si, m.si, k); cur.hi = Mathf.Lerp(cur.hi, m.hi, k);
        cur.ex = Mathf.Lerp(cur.ex, m.ex, k); cur.bm = Mathf.Lerp(cur.bm, m.bm, k);
        sunSrc = Vector3.Lerp(sunSrc, m.sd.normalized, k).normalized;
        ApplyImmediate();
    }

    void ApplyImmediate()
    {
        Vector3 sunU = MirrorZ ? new Vector3(sunSrc.x, sunSrc.y, -sunSrc.z) : sunSrc;
        if (skyMat) { skyMat.SetColor("_Top", cur.top); skyMat.SetColor("_Mid", cur.mid); skyMat.SetColor("_Hor", cur.hor); }
        Shader.SetGlobalVector("_GhibliSunDir", sunU);
        RenderSettings.fogColor = cur.fog; RenderSettings.fogDensity = 0.0011f;
        RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fog = true;
        if (sun) { sun.color = cur.sc; sun.intensity = cur.si; sun.transform.rotation = Quaternion.LookRotation(-sunU); }
        Shader.SetGlobalVector("_HemiSky", (Vector4)(Vector3)(Vector4)cur.hs);
        Shader.SetGlobalColor("_HemiSky", cur.hs); Shader.SetGlobalColor("_HemiGround", cur.hg);
        Shader.SetGlobalFloat("_HemiIntensity", cur.hi);
        // Keep Unity's BUILT-IN ambient synced to the same mood data. The custom _Hemi* globals above only
        // feed our own Ghibli/ToonLit-family shaders (trees, grass); imported package meshes -- e.g. the
        // GHIBLI rock prefabs, which use their own Shader Graphs/Layered material -- read Unity's standard
        // RenderSettings.ambient* instead and never see _HemiSky/_HemiGround at all. Without this, those
        // meshes are lit by whatever ambient was last set manually (stale, mood-independent), which is why
        // rocks were going pitch-black on their shadow side once the sun moved off a manually-tuned angle.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = cur.hs * cur.hi;
        RenderSettings.ambientEquatorColor = Color.Lerp(cur.hg, cur.hs, 0.5f) * cur.hi;
        RenderSettings.ambientGroundColor = cur.hg * cur.hi;
        if (oceanMat) { oceanMat.SetColor("_CDeep", cur.dp); oceanMat.SetColor("_CShal", cur.sh); }
        Shader.SetGlobalVector("_GhibliSunDirSrc", sunSrc);
        Shader.SetGlobalColor("_GhibliFogColor", cur.fog);
        Shader.SetGlobalFloat("_GhibliFogDensity", 0.0011f);
        Shader.SetGlobalFloat("_GhibliE", (float)E);
        Shader.SetGlobalFloat("_GhibliMirrorZ", MirrorZ ? 1f : 0f);
        if (beamMat)
        {
            var bc = beamMat.color; bc.a = cur.bm; beamMat.color = bc;
            if (beamMat.HasProperty("_Opacity")) beamMat.SetFloat("_Opacity", cur.bm);
        }
    }

    public Mood CurrentMood() { EnsureMoods(); return moods[index]; }
    public Vector3 SunUnityDir()
    {
        EnsureMoods();
        var s = moods[index].sd.normalized;
        return MirrorZ ? new Vector3(s.x, s.y, -s.z) : s;
    }
}
