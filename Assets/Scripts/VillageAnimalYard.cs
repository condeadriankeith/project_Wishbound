using UnityEngine;

public static class VillageAnimalYard
{
    public static bool Build(VillageDressing village, Vector3 centre, int seed)
    {
        var cow = Resources.Load<GameObject>("VillageAnimals/Cow");
        var sheep = Resources.Load<GameObject>("VillageAnimals/Sheep");
        var cat = Resources.Load<GameObject>("VillageAnimals/Cat");
        var hen = Resources.Load<GameObject>("VillageAnimals/Hen");
        if (!cow || !sheep || !cat || !hen)
        {
            Debug.LogWarning("[VillageAnimals] Farm animal prefabs are missing.");
            return false;
        }

        bool yardBuilt=false;
        const float footprint = 7.5f;
        for (int i = 0; i < 240; i++)
        {
            float angle = i * 2.399963f;
            float radius = Mathf.Lerp(15f, 67f, i / 239f);
            Vector3 point = VillageKit.Ground(centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
            if (!village.Clear(point, footprint) || !village.Gentle(point, footprint) ||
                WaterProbe.Near(point, footprint, 2f)) continue;
            bool road = false;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 sample = point + new Vector3(x * 5f, 0, z * 4f);
                    if (Surface.RoadPathAt(sample.x, TerrainMath.MirrorZ ? -sample.z : sample.z) > .20) road = true;
                }
            if (road) continue;

            village.Claim(point, footprint);
            var yard = VillageKit.KitRoot("Village Animal Yard", point, village.transform).transform;
            var toVillage = centre - point;
            toVillage.y = 0;
            yard.rotation = Quaternion.LookRotation(toVillage);
            var cowPen = Pen(yard, "Cow Pen", new Vector3(-2.7f, 0, 0), new Vector2(2.5f, 3.1f), seed);
            var sheepPen = Pen(yard, "Sheep Paddock", new Vector3(2.7f, 0, 0), new Vector2(2.5f, 3.1f), seed + 10);
            for (int j = 0; j < 2; j++)
                Spawn(cowPen, cow, VillageAnimalMotion.Species.Cow, new Vector3(j * 2.35f - 1.175f, 0, .1f), new Vector2(.12f, .58f), 1, seed + j);
            for (int j = 0; j < 3; j++)
                Spawn(sheepPen, sheep, VillageAnimalMotion.Species.Sheep, new Vector3(j == 2 ? 1.1f : -1.05f, 0, j == 0 ? -1.15f : j == 1 ? 1.15f : .15f), new Vector2(.24f, .25f), .88f + .07f * j, seed + 20 + j);
            for (int j = 0; j < 4; j++)
                Spawn(yard, hen, VillageAnimalMotion.Species.Hen, new Vector3((j - 1.5f) * 1.6f, 0, 4.3f), new Vector2(.75f, 1.7f), .64f + j * .045f, seed + 31 + j);
            Spawn(yard, cat, VillageAnimalMotion.Species.Cat, new Vector3(-4.2f, 0, 4.65f), new Vector2(2.2f, 1.2f), .77f, seed + 45);
            Spawn(yard, cat, VillageAnimalMotion.Species.Cat, new Vector3(4.2f, 0, 4.65f), new Vector2(2.2f, 1.2f), .66f, seed + 46);
            yardBuilt=true;
            break;
        }
        var pastures = new System.Collections.Generic.List<Vector3>();
        foreach (var route in RoadBuild.Roads)
        {
            for (int sample=1;sample<=5 && pastures.Count<14;sample++)
            {
                int index=(route.x.Length-1)*sample/6;
                if (index+1>=route.x.Length) continue;
                Vector3 path=TerrainMath.U(route.x[index],route.prof[index],route.z[index]);
                Vector3 next=TerrainMath.U(route.x[index+1],route.prof[index+1],route.z[index+1]);
                Vector3 side=Vector3.Cross(Vector3.up,(next-path).normalized);
                foreach (float bank in new[]{1f,-1f})
                {
                    var at=VillageKit.Ground(path+side*bank*9f);
                    if (at.y<TerrainProbe.SeaLevel+1 || pastures.Exists(p=>(p-at).sqrMagnitude<38*38) ||
                        !village.Clear(at,5f) || !village.Gentle(at,4f) || WaterProbe.Near(at,5f,2f)) continue;
                    var pasture=VillageKit.KitRoot("Trail pasture",at,village.transform).transform;
                    pasture.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(next-path,Vector3.up));
                    int siteSeed=seed+100+pastures.Count*17;
                    bool highland=at.y>55f;
                    Spawn(pasture,highland?sheep:cow,highland?VillageAnimalMotion.Species.Sheep:VillageAnimalMotion.Species.Cow,
                        Vector3.left*1.8f,new Vector2(2.4f,4.6f),.92f,siteSeed,true);
                    Spawn(pasture,sheep,VillageAnimalMotion.Species.Sheep,Vector3.right*2f,new Vector2(2.4f,4.6f),.86f,siteSeed+1,true);
                    Spawn(pasture,hen,VillageAnimalMotion.Species.Hen,Vector3.forward*2f,new Vector2(2.8f,4.4f),.87f,siteSeed+2,true);
                    pastures.Add(at);
                    break;
                }
            }
        }
        Debug.Log($"[VillageAnimals] Yard built={yardBuilt}, trail pastures={pastures.Count}");
        return yardBuilt || pastures.Count>0;
    }

    static Transform Pen(Transform yard, string name, Vector3 at, Vector2 half, int seed)
    {
        var pen = new GameObject(name).transform;
        pen.SetParent(yard, false);
        pen.localPosition = at;
        Vector3 Point(float x, float z) => pen.TransformPoint(new Vector3(x, 0, z));
        VillageKit.Fence(new[] { Point(-.91f, half.y), Point(-half.x, half.y), Point(-half.x, -half.y),
            Point(half.x, -half.y), Point(half.x, half.y), Point(.91f, half.y) }, pen, seed);
        var gate = VillageKit.KitRoot("Braced Timber Gate", Point(0, half.y), pen).transform;
        gate.rotation = pen.rotation;
        var wood = VillageKit.Mat(VillageKit.Timber);
        var dark = VillageKit.Mat(VillageKit.TimberDark);
        Mesh bevel = VillageKit.ChamferBox(.12f);
        for (int i = 0; i < 2; i++)
            VillageKit.Piece(gate, "Gate rail", bevel, wood, new Vector3(0, .38f + i * .43f, 0),
                Vector3.zero, new Vector3(1.68f, .13f, .12f), true);
        VillageKit.Piece(gate, "Gate brace", bevel, dark, new Vector3(0, .59f, .06f),
            new Vector3(0, 0, 16), new Vector3(1.65f, .10f, .08f));
        VillageKit.Trough(Point(0, -2.55f), pen, seed);
        return pen;
    }

    static void Spawn(Transform parent, GameObject prefab, VillageAnimalMotion.Species species, Vector3 home,
        Vector2 range, float scale, int seed, bool openRange=false)
    {
        var actor = new GameObject(species.ToString());
        actor.transform.SetParent(parent, false);
        actor.transform.position = VillageKit.Ground(parent.TransformPoint(home));
        actor.transform.localRotation = Quaternion.Euler(0,
            species == VillageAnimalMotion.Species.Cow ? (home.x > 0f ? 175f : 5f) : seed % 360, 0);
        var visual = Object.Instantiate(prefab, actor.transform).transform;
        visual.localScale *= scale;
        actor.AddComponent<VillageAnimalMotion>().Configure(species, visual, home, range, seed, openRange);
    }
}
