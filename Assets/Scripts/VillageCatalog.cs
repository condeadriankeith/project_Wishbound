using System;
using System.Collections.Generic;
using UnityEngine;

public enum VKind { House, Prop, Fence, Gate, Rock, Ruin, Wall, Tower }

/// <summary>
/// Measured catalog of the Low Poly Medieval Starter Pack. Built by the editor menu
/// "Island/Village/Build Catalog" (see VillageCatalogBuilder) and loaded from Resources at runtime,
/// so VillageComposer places things using REAL footprints/pivots instead of guessed numbers.
/// </summary>
[CreateAssetMenu(fileName = "VillageCatalog", menuName = "Island/Village Catalog")]
public class VillageCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id;
        public GameObject prefab;
        public VKind kind;
        public Vector2 footprint;   // x,z extent in metres at scale 1
        public Vector2 centerXZ;    // bounds centre relative to the pivot (local x,z)
        public float height;        // total height at scale 1
        public float minY;          // lowest point relative to pivot (<0 means pivot is above the base)
        public int floors;          // houses only
        public float weight = 1f;
    }

    public List<Entry> entries = new List<Entry>();

    static VillageCatalog cached;
    public static VillageCatalog Load()
    {
        if (cached == null) cached = Resources.Load<VillageCatalog>("VillageCatalog");
        return cached;
    }

    public List<Entry> Of(VKind k) { return entries.FindAll(e => e.kind == k && e.prefab != null); }
    public Entry Get(string id) { return entries.Find(e => e.id == id && e.prefab != null); }
}
