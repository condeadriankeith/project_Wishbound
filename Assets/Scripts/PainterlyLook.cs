using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime controls for the painterly look (Kuwahara brush + ink outlines) rendered by the
/// "WishboundPainterly" Full Screen Pass in PC_Renderer. Quality presets trade brush radius for speed.
/// F8 cycles quality in Play mode (Off / Low / Medium / High).
/// </summary>
[ExecuteAlways]
public class PainterlyLook : MonoBehaviour
{
    public Material material;   // Assets/Art/Look/PainterlyLook.mat
    public enum Quality { Off, Low, Medium, High }
    public Quality quality = Quality.Medium;
    [Range(0, 1)] public float inkStrength = .75f;
    [Range(0, 1)] public float brushStrength = 1f;
    public bool hotkey = true;

    static readonly int Radius = Shader.PropertyToID("_Radius"), Brush = Shader.PropertyToID("_BrushStrength"), Ink = Shader.PropertyToID("_InkStrength");

    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }

    void Update()
    {
        if (hotkey && Application.isPlaying && WishboundInput.Pressed(Key.F8, KeyCode.F8))
        { quality = (Quality)(((int)quality + 1) % 4); Apply(); }
    }

    public void Apply()
    {
        if (!material) return;
        int r = quality == Quality.Off ? 0 : quality == Quality.Low ? 2 : quality == Quality.Medium ? 3 : 5;
        material.SetFloat(Radius, r);
        material.SetFloat(Brush, quality == Quality.Off ? 0 : brushStrength);
        material.SetFloat(Ink, quality == Quality.Off ? 0 : inkStrength);
    }
}
