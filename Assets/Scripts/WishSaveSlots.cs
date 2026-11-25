using System;
using System.Collections.Generic;
using UnityEngine;

internal static class WishSaveSlots
{
    [Serializable]
    internal sealed class Progress
    {
        public List<string> flags = new List<string>();
        public bool hasPosition;
        public Vector3 position;
        public float yaw;
        public float spectrum;
        public int alignment;
        public bool Has(string flag) => flags.Contains(flag);
        public string Chapter => Has("TetherEnding") ? "Returned to the sky" :
            Has("RootsEnding") ? "At home on the island" :
            Has("GardenFragmentRecovered") ? "The summit pond" :
            Has("ParcelFragmentRecovered") ? "The kitchen garden" :
            Has("ChoseGatherFragments") ? "The village parcels" :
            Has("ChoseLiveAmongHumans") ? "The island walk" :
            Has("ChildTookHand") ? "With the child" : "Starfall";
    }

    static string Prefix = "Wishbound.Save.";
    public static int Active => Mathf.Clamp(PlayerPrefs.GetInt(Prefix+"Active",1),1,3);
    public static Progress Read(int slot)
    {
        string key = Prefix+slot;
        if (PlayerPrefs.HasKey(key)) return JsonUtility.FromJson<Progress>(PlayerPrefs.GetString(key));
        if (slot != 1 || PlayerPrefs.HasKey(Prefix+"Migrated")) return null;
        var legacy = new Progress();
        foreach (var flag in new[]{"ChildHeardStar","ChildTookHand","WarmthSeen","CottageChoiceMade","ChoseGatherFragments","ChoseLiveAmongHumans","ParcelFragmentRecovered","GardenFragmentRecovered","SummitFragmentRecovered","TetherEnding","RootsEnding"})
            if (PlayerPrefs.GetInt("Wishbound.Flag."+flag)==1) legacy.flags.Add(flag);
        if (legacy.flags.Count==0 && !PlayerPrefs.HasKey("Wishbound.IntroSeen")) return null;
        legacy.alignment = PlayerPrefs.GetInt("Wishbound.Alignment");
        legacy.spectrum = PlayerPrefs.GetFloat("Wishbound.Spectrum");
        return legacy;
    }
    public static void Write(int slot,Progress save)
    {
        if (slot==1) PlayerPrefs.SetInt(Prefix+"Migrated",1);
        PlayerPrefs.SetString(Prefix+slot,JsonUtility.ToJson(save));
        PlayerPrefs.Save();
    }
    public static void Select(int slot)
    {
        if (slot<1 || slot>3) throw new ArgumentOutOfRangeException(nameof(slot));
        if (Read(slot)==null) Write(slot,new Progress());
        PlayerPrefs.SetInt(Prefix+"Active",slot);
        PlayerPrefs.Save();
    }
    public static void Reset(int slot)
    {
        if (slot==1) PlayerPrefs.SetInt(Prefix+"Migrated",1);
        PlayerPrefs.DeleteKey(Prefix+slot);
        PlayerPrefs.Save();
    }
}
