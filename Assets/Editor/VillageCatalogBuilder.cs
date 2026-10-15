#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class VillageCatalogBuilder
{
    const string PackPrefabs = "Assets/LowPolyMedievalStarterPack/Prefabs/";
    const string AssetPath = "Assets/Resources/VillageCatalog.asset";

    // id -> (kind, floors, weight)
    static readonly Dictionary<string, (VKind kind, int floors, float weight)> Map = new Dictionary<string, (VKind, int, float)>
    {
        { "SmallMedievalHouse",        (VKind.House, 1, 1.0f) },
        { "MedievalHouse",             (VKind.House, 1, 1.0f) },
        { "TwoStoryMedievalHouse",     (VKind.House, 2, 1.0f) },
        { "TwoStoryMedievalHouse_2",   (VKind.House, 2, 1.0f) },
        { "HayBale",                   (VKind.Prop,  0, 1.0f) },
        { "WoodenBarrel",              (VKind.Prop,  0, 1.0f) },
        { "WoodenCrate",               (VKind.Prop,  0, 1.0f) },
        { "WoodenFence",               (VKind.Fence, 0, 1.0f) },
        { "WoodenGate",                (VKind.Gate,  0, 1.0f) },
        { "SmallRock",                 (VKind.Rock,  0, 1.0f) },
        { "RockCluster",               (VKind.Rock,  0, 1.0f) },
        { "MossyBoulder",              (VKind.Rock,  0, 1.0f) },
        { "RuinedWall",                (VKind.Ruin,  0, 1.0f) },
        { "RuinedArchway",             (VKind.Ruin,  0, 1.0f) },
        { "StoneWall",                 (VKind.Wall,  0, 1.0f) },
        { "CastleWall",                (VKind.Wall,  0, 1.0f) },
        { "CastleTower",               (VKind.Tower, 0, 1.0f) },
    };

    [MenuItem("Island/Village/Build Catalog")]
    public static void Build()
    {
        var cat = AssetDatabase.LoadAssetAtPath<VillageCatalog>(AssetPath);
        if (cat == null)
        {
            cat = ScriptableObject.CreateInstance<VillageCatalog>();
            AssetDatabase.CreateAsset(cat, AssetPath);
        }
        cat.entries.Clear();

        foreach (var kv in Map)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabs + kv.Key + ".prefab");
            if (prefab == null) { Debug.LogWarning("VillageCatalog: missing prefab " + kv.Key); continue; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            var rs = inst.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { Object.DestroyImmediate(inst); continue; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);

            cat.entries.Add(new VillageCatalog.Entry
            {
                id = kv.Key,
                prefab = prefab,
                kind = kv.Value.kind,
                floors = kv.Value.floors,
                weight = kv.Value.weight,
                footprint = new Vector2(b.size.x, b.size.z),
                centerXZ = new Vector2(b.center.x, b.center.z),
                height = b.size.y,
                minY = b.min.y,
            });
            Object.DestroyImmediate(inst);
        }

        EditorUtility.SetDirty(cat);
        AssetDatabase.SaveAssets();
        Debug.Log("VillageCatalog: " + cat.entries.Count + " entries written to " + AssetPath);
    }

    [MenuItem("Island/Village/Rebuild Village")]
    public static void Rebuild() { VillageComposer.RebuildInScene(); Debug.Log(VillageComposer.Report); }
}
#endif
