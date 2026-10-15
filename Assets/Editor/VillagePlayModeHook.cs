#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs "Island > Village > Rebuild Village" automatically every time you press Play.
/// It fires just before the editor leaves edit mode, so the freshly built Phase5Village
/// is part of the scene that enters play mode. Toggle it via Island > Village > Rebuild On Play.
/// </summary>
[InitializeOnLoad]
public static class VillagePlayModeHook
{
    const string PrefKey = "VillagePlayModeHook.Enabled";
    const string MenuPath = "Island/Village/Rebuild On Play";

    static bool Enabled
    {
        get { return EditorPrefs.GetBool(PrefKey, true); }   // on by default
        set { EditorPrefs.SetBool(PrefKey, value); }
    }

    static VillagePlayModeHook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode || !Enabled) return;
        try
        {
            VillageCatalogBuilder.Rebuild();
        }
        catch (System.Exception e)
        {
            Debug.LogError("VillagePlayModeHook: Rebuild Village failed - " + e);
        }
    }

    [MenuItem(MenuPath)]
    static void Toggle() { Enabled = !Enabled; }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
#endif
