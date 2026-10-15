using UnityEngine;

[CreateAssetMenu(fileName = "IslandParams", menuName = "Island/Island Params")]
public class IslandParams : ScriptableObject
{
    [Header("Terrain container")]
    public int heightmapRes = 4097;      // 2^n+1. Use 1025 while iterating, 4097 for final
    public float terrainSize = 1200f;    // square, metres
    public float seabedDepth = 60f;      // terrain origin sits this far below sea level
    public float terrainHeight = 520f;   // must exceed seabedDepth + summit (≈476)
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
