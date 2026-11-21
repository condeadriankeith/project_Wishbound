using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interior root for an Interior_* scene. The rooms are hand-laid in InteriorDesigns and BAKED into the scene as
/// ordinary GameObjects (Tools > Wishbound > Bake Interiors) so they can be edited by hand. If a scene has not
/// been baked yet (no children), the design is built at load as a fallback.
/// </summary>
[DefaultExecutionOrder(-100)]
public class InteriorBuilder : MonoBehaviour
{
    public enum Style { MedievalHouse, SmallMedievalHouse, TwoStoryMedievalHouse, TwoStoryMedievalHouse_2 }
    public Style style;
    public int seed = 3;

    void Awake() { if (transform.childCount == 0) Build(); }

    [ContextMenu("Rebuild (destroys hand edits)")]
    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject); else DestroyImmediate(transform.GetChild(i).gameObject);
        }
        InteriorDesigns.Build(transform, style, seed);
    }
}


