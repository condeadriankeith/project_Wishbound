using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and caches one real dithered-occlusion material PER SUBMESH PER SOURCE MATERIAL, used by
/// TreeOcclusionFader to render an occluding tree see-through (Method 1: dithered transparency driven
/// by a camera-to-player raycast) instead of fully hiding it or moving the camera.
///
/// Each dither material samples the SAME base-color texture + tint as the real package material it
/// stands in for (Shader Graphs/Layered for bark, Shader Graphs/Leaves for foliage), via
/// Island/TreeOcclusionDither (Assets/Resources/IslandTreeOcclusionDither.shader) - a hand-written
/// Opaque + Alpha-Clip shader whose clip threshold comes from a real Dither node pattern (the exact 4x4
/// Bayer matrix Shader Graph's own Dither node generates), swept by _DitherFade. This is the same
/// "Dither -> Alpha Clip Threshold" wiring Method 1 describes; it's hand-written here only because the
/// package's Shader Graphs/Layered material has no alpha/cutout path at all to attach a Dither node to,
/// and Shader Graphs/Leaves exposes no tunable clip threshold (its _AlphaClip is a fixed on/off).
/// </summary>
public static class GhibliDitherMaterials
{
    static Shader ditherShader;
    static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();   // source material -> its dither stand-in

    /// <summary>Returns one dither material per submesh of the batch, matching batch.mats 1:1.</summary>
    public static Material[] For(GhibliBatch batch)
    {
        if (batch == null || batch.mats == null) return null;
        var result = new Material[batch.mats.Length];
        for (int i = 0; i < batch.mats.Length; i++) result[i] = ForSource(batch.mats[i]);
        return result;
    }

    static Material ForSource(Material src)
    {
        if (src == null) return null;
        Material m;
        if (cache.TryGetValue(src, out m)) return m;

        if (ditherShader == null) ditherShader = Resources.Load<Shader>("IslandTreeOcclusionDither");
        if (ditherShader == null) { Debug.LogWarning("[GhibliDitherMaterials] Island/TreeOcclusionDither shader not found in Resources."); return null; }

        m = new Material(ditherShader);
        Texture baseTex = null; Color tint = Color.white;

        // Shader Graphs/Leaves: single base-color texture + tint, already alpha-tested - the easy case.
        if (src.HasProperty("_Texture2D"))
        {
            baseTex = src.GetTexture("_Texture2D");
            if (src.HasProperty("_Color")) tint = src.GetColor("_Color");
            m.SetFloat("_UseTexture", baseTex != null ? 1f : 0f);
        }
        // Shader Graphs/Layered (bark/trunk): multi-layer blend with no single "the" texture; use its
        // primary (red) layer's base-color texture + tint as a faithful stand-in rather than flattening
        // to a plain colour - this is still the real wood texture, just without the other 3 blended layers.
        else if (src.HasProperty("_ZL_Base_Color"))
        {
            baseTex = src.GetTexture("_ZL_Base_Color");
            if (src.HasProperty("_ZL_Tint")) tint = src.GetColor("_ZL_Tint");
            if (tint.a <= 0.001f) tint.a = 1f;   // some package tints carry a=0 (unused by the multiply-blend graph); our shader needs a=1
            m.SetFloat("_UseTexture", baseTex != null ? 1f : 0f);
        }
        else
        {
            m.SetFloat("_UseTexture", 0f);
        }

        if (baseTex != null) m.SetTexture("_BaseMap", baseTex);
        m.SetColor("_Tint", tint);
        m.SetFloat("_DitherFade", 0f);
        m.enableInstancing = false;   // drawn one at a time via DrawSingleInstance

        cache[src] = m;
        return m;
    }

    public static void SetFade(Material[] mats, float fade)
    {
        if (mats == null) return;
        for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i].SetFloat("_DitherFade", fade);
    }
}
