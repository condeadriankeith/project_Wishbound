using UnityEngine;

/// <summary>Debug/validation data stored on every lot group created by VillageComposer.</summary>
public class VillageLot : MonoBehaviour
{
    public int lot, settlement, doorSide, styleVariant;
    public string role, house;
    public float scale, relief, plinthDrop, uncoveredGap;
    public bool fenced;
}
