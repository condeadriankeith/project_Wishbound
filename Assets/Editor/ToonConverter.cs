#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Converts the Low Poly Medieval Starter Pack materials to the Island/GhibliToon shader (keeps colours/textures).</summary>
public static class ToonConverter
{
    const string PackFolder = "Assets/LowPolyMedievalStarterPack";

    [MenuItem("Island/Village/Apply Toon Shader To Pack")]
    public static void ApplyMenu() { Debug.Log(Apply()); }

    public static string Apply()
    {
        var toon = Shader.Find("Island/GhibliToon");
        if (toon == null) return "ToonConverter: shader Island/GhibliToon not found.";

        int converted = 0, already = 0;
        var guids = AssetDatabase.FindAssets("t:Material", new[] { PackFolder });
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            if (m.shader == toon) { already++; continue; }

            Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
            Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);

            m.shader = toon;
            m.SetColor("_BaseColor", col);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Cull", 2f);
            EditorUtility.SetDirty(m);
            converted++;
        }
        AssetDatabase.SaveAssets();
        return "ToonConverter: converted " + converted + " materials, " + already + " already toon.";
    }
}
#endif
