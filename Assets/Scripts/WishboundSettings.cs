using UnityEngine;

/// <summary>Small persistent settings surface shared by title and gameplay.</summary>
public static class WishboundSettings
{
    public static float Volume => PlayerPrefs.GetFloat("Wishbound.Volume", .8f);
    public static float Sensitivity => PlayerPrefs.GetFloat("Wishbound.Sensitivity", .15f);
    public static void Apply(IslandThirdPersonController player)
    {
        AudioListener.volume = Volume;
        if (PlayerPrefs.HasKey("Wishbound.Fullscreen")) Screen.fullScreen = PlayerPrefs.GetInt("Wishbound.Fullscreen") != 0;
        if (player) { player.lookSensitivity = Sensitivity; if (player.isoRig) player.isoRig.mouseYawSensitivity = Sensitivity; }
    }
    public static void SetVolume(float value) { PlayerPrefs.SetFloat("Wishbound.Volume", Mathf.Clamp01(value)); AudioListener.volume = Volume; PlayerPrefs.Save(); }
    public static void SetSensitivity(float value, IslandThirdPersonController player)
    { PlayerPrefs.SetFloat("Wishbound.Sensitivity", Mathf.Clamp(value,.04f,.4f)); Apply(player); PlayerPrefs.Save(); }
    public static void SetFullscreen(bool value) { Screen.fullScreen = value; PlayerPrefs.SetInt("Wishbound.Fullscreen",value?1:0); PlayerPrefs.Save(); }
}