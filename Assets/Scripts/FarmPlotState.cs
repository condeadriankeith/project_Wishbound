using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A living farm plot. Walks the cycle Fallow -> Tilled -> Growing -> Ripe -> (harvest) -> Fallow. Soil dries out over
/// time, thirsty crops grow slowly, and the little sign post shows what the plot needs (hoe / seeds / water drop / tick).
/// Settlers read NextNeed() through the village job board and change the state by actually doing the work.
/// </summary>
public class FarmPlotState : MonoBehaviour
{
    public enum Phase { Fallow, Tilled, Growing, Ripe }
    public enum Need { None, Till, Sow, Water, Harvest }

    public Phase phase = Phase.Growing;
    [Range(0, 1)] public float growth, water = .6f;
    public bool blighted;
    public VillageKit.Crop crop;
    public Vector2 size = new Vector2(4, 3);
    public float growSeconds = 220f, waterDrainSeconds = 100f;

    public class CropCell { public Transform root; public Transform fruit; public float jitter; }
    [System.NonSerialized] public readonly List<CropCell> cells = new List<CropCell>();
    [System.NonSerialized] public readonly List<Transform> ridges = new List<Transform>();
    [System.NonSerialized] public readonly List<Renderer> soils = new List<Renderer>();
    [System.NonSerialized] public GameObject iconWater, iconTill, iconSow, iconCheck;
    [System.NonSerialized] public Transform sign;

    MaterialPropertyBlock mpb; float visTimer, shown = -1; Phase shownPhase = (Phase)(-1); Need shownNeed = (Need)(-1); float shownWater = -1;
    System.Random rng;
    static readonly Color Dry = new Color(.46f, .31f, .19f), Wet = new Color(.27f, .18f, .12f);

    public Res YieldRes
    {
        get
        {
            switch (crop) { case VillageKit.Crop.Carrot: return Res.Carrot; case VillageKit.Crop.Tomato: return Res.Tomato; case VillageKit.Crop.Wheat: return Res.Wheat; case VillageKit.Crop.Sunflower: return Res.Sunflower; case VillageKit.Crop.Beans: return Res.Beans; case VillageKit.Crop.Pumpkin: return Res.Pumpkin; case VillageKit.Crop.Turnip: return Res.Turnip; default: return Res.Cabbage; }
        }
    }

    public void Init(int seed)
    {
        rng = new System.Random(seed);
        double r = rng.NextDouble();
        if (blighted) { phase = Phase.Growing; growth = .3f; water = .3f; }
        else if (r < .5) { phase = Phase.Growing; growth = Mathf.Lerp(.1f, .85f, (float)rng.NextDouble()); water = Mathf.Lerp(.15f, .9f, (float)rng.NextDouble()); }
        else if (r < .7) { phase = Phase.Ripe; growth = 1; water = .4f; }
        else if (r < .85) { phase = Phase.Tilled; growth = 0; water = .4f; }
        else { phase = Phase.Fallow; growth = 0; water = .1f; }
        shown = -1; Refresh(true);
    }

    void Start() { if (VillageWorld.I && !VillageWorld.I.plots.Contains(this)) VillageWorld.I.plots.Add(this); }

    void Update()
    {
        if (blighted || cinematicGrowth) return;
        float dt = Time.deltaTime;
        if (phase == Phase.Growing)
        {
            water = Mathf.Max(0, water - dt / waterDrainSeconds);
            growth += dt / growSeconds * (water > .12f ? 1f : .12f);
            if (growth >= 1) { growth = 1; phase = Phase.Ripe; }
        }
        else if (phase == Phase.Tilled || phase == Phase.Fallow) water = Mathf.Max(0, water - dt / (waterDrainSeconds * 2f));
        visTimer -= dt;
        if (visTimer <= 0) { visTimer = .4f; Refresh(false); }
    }

    public bool cinematicGrowth;

    public Need NextNeed()
    {
        if (cinematicGrowth) return Need.None;
        switch (phase)
        {
            case Phase.Fallow: return Need.Till;
            case Phase.Tilled: return Need.Sow;
            case Phase.Growing: return water < .4f ? Need.Water : Need.None;
            default: return Need.Harvest;
        }
    }

    public void CompleteBlessing()
    {
        cinematicGrowth = false; blighted = false; growth = 1f; water = 1f;
        phase = Phase.Ripe; Refresh(true);
    }

    // ---------------------------------------------------------------- work
    public void Till() { phase = Phase.Tilled; growth = 0; water *= .6f; Refresh(true); }
    public void Sow() { phase = Phase.Growing; growth = 0; Refresh(true); }
    public void Water(float amt) { water = Mathf.Clamp01(water + amt); Refresh(true); }
    public int Harvest()
    {
        if (phase != Phase.Ripe || blighted) return 0;
        int q = 3 + (rng ?? new System.Random()).Next(0, 4);
        phase = Phase.Fallow; growth = 0; water *= .5f; Refresh(true); return q;
    }

    // ---------------------------------------------------------------- geometry
    public bool Contains(Vector3 world, float margin)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return Mathf.Abs(l.x) < size.x * .5f + margin && Mathf.Abs(l.z) < size.y * .5f + margin;
    }

    /// <summary>A spot on the walkable apron just outside the bed, on the side nearest 'from'.</summary>
    public Vector3 WorkSpot(Vector3 from, System.Random r)
    {
        Vector3 l = transform.InverseTransformPoint(from);
        bool alongX = Mathf.Abs(l.x) / (size.x * .5f) > Mathf.Abs(l.z) / (size.y * .5f);
        Vector3 spot;
        if (alongX) spot = new Vector3((l.x >= 0 ? 1 : -1) * (size.x * .5f + 1.75f), 0, Mathf.Lerp(-size.y * .35f, size.y * .35f, (float)r.NextDouble()));
        else spot = new Vector3(Mathf.Lerp(-size.x * .35f, size.x * .35f, (float)r.NextDouble()), 0, (l.z >= 0 ? 1 : -1) * (size.y * .5f + 1.75f));
        return VillageKit.Ground(transform.TransformPoint(spot));
    }

    // ---------------------------------------------------------------- visuals
    void Refresh(bool force)
    {
        Need need = blighted ? Need.None : NextNeed();
        bool phaseChanged = phase != shownPhase;
        if (!force && !phaseChanged && need == shownNeed && Mathf.Abs(growth - shown) < .02f && Mathf.Abs(water-shownWater)<.03f) return;
        shownWater = water; shownPhase = phase; shownNeed = need; shown = growth;
        // soil darkens with water
        if (mpb == null) mpb = new MaterialPropertyBlock();
        foreach (var r in soils) { if (!r) continue; r.GetPropertyBlock(mpb); mpb.SetColor("_Color", Color.Lerp(Dry, Wet, Mathf.Clamp01(water * 1.2f))); r.SetPropertyBlock(mpb); }
        // furrow ridges rise when tilled
        float ridge = phase == Phase.Fallow ? .25f : 1f;
        foreach (var rd in ridges) if (rd) { var s = rd.localScale; s.y = Mathf.Lerp(s.y, ridge * ((RidgeBase)rd.GetComponent<RidgeBase>()).h, 1f); rd.localScale = s; }
        // crops scale with growth; fruit on ripe
        float g = phase == Phase.Growing ? Mathf.Lerp(.12f, 1f, growth) : phase == Phase.Ripe ? 1f : 0f;
        foreach (var c in cells)
        {
            if (c.root == null) continue;
            float s = blighted ? Mathf.Max(.5f, g) : g * (1f + c.jitter * .15f);
            c.root.localScale = Vector3.one * s; c.root.gameObject.SetActive(s > .02f);
            if (c.fruit) c.fruit.gameObject.SetActive(phase == Phase.Ripe || (phase == Phase.Growing && growth > .8f));
        }
        if (iconWater) iconWater.SetActive(need == Need.Water);
        if (iconTill) iconTill.SetActive(need == Need.Till);
        if (iconSow) iconSow.SetActive(need == Need.Sow);
        if (iconCheck) iconCheck.SetActive(need == Need.Harvest);
        if (sign) sign.gameObject.SetActive(!blighted);
    }
}

/// <summary>Remembers a furrow ridge's full height so tilling can raise it.</summary>
public class RidgeBase : MonoBehaviour { public float h = 1f; }
