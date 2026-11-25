using UnityEditor;
using UnityEngine;

/// <summary>Imported FBX materials default to smoothness 0.5, which reads as glass under the painted look.
/// Keep them matte; they still receive sun, point lights and shadows. Metal or glass named materials are left alone.</summary>
public sealed class MatteImportedMaterials : AssetPostprocessor
{
    void OnPostprocessMaterial(Material m)
    {
        if (!m || !m.shader || m.shader.name != "Universal Render Pipeline/Lit") return;
        string n = m.name.ToLowerInvariant();
        if (n.Contains("metal") || n.Contains("glass") || n.Contains("iron") || n.Contains("window") || n.Contains("gold") || n.Contains("water")) return;
        bool brass = n.Contains("brass") || n.Contains("bell");
        m.SetFloat("_Smoothness", brass ? .3f : .08f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_SpecularHighlights", brass ? 1f : 0f);
        m.SetFloat("_EnvironmentReflections", brass ? 1f : 0f);
        if (!brass) { m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
    }
}
