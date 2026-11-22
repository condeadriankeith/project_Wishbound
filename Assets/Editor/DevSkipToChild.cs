#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only dev shortcut: Island > Dev > Play (skip intro to child meeting).
/// Enters Play mode, skips the opening (the same path as F1), waits for the hand-over to the player, then drops the star
/// a few metres from the hiding child so ChildConversation starts by itself. Nothing here ships in a build.
/// </summary>
[InitializeOnLoad]
public static class DevSkipToChild
{
    const string Key = "DevSkipToChild.Armed";
    static int stage;
    static double stageStart;
    static FieldInfo skipField;

    static DevSkipToChild()
    {
        EditorApplication.playModeStateChanged -= OnMode;
        EditorApplication.playModeStateChanged += OnMode;
        if (EditorApplication.isPlaying && SessionState.GetBool(Key, false)) Arm();
    }

    [MenuItem("Island/Dev/Play (skip intro to child meeting)")]
    static void Go()
    {
        SessionState.SetBool(Key, true);
        if (EditorApplication.isPlaying) Arm(); else EditorApplication.isPlaying = true;
    }

    static void OnMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) Arm();
        if (s == PlayModeStateChange.ExitingPlayMode) { Disarm(); }
    }

    static void Arm()
    {
        stage = 0; stageStart = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("[DevSkipToChild] armed");
    }

    static void Disarm()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(Key, false);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Disarm(); return; }
        double elapsed = EditorApplication.timeSinceStartup - stageStart;
        if (elapsed > 120) { Debug.LogWarning("[DevSkipToChild] gave up at stage " + stage); Disarm(); return; }

        switch (stage)
        {
            case 0: // let the intro's coroutine get going, then request the skip
                if (WishboundIntro.Instance && WishboundIntro.IsPlaying && elapsed > 1.5)
                {
                    if (skipField == null) skipField = typeof(WishboundIntro).GetField("skipRequested", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (skipField != null) skipField.SetValue(WishboundIntro.Instance, true);
                    Debug.Log("[DevSkipToChild] intro skip requested");
                    stage = 1; stageStart = EditorApplication.timeSinceStartup;
                }
                else if (!WishboundIntro.IsPlaying && elapsed > 4) { stage = 1; stageStart = EditorApplication.timeSinceStartup; } // intro disabled: nothing to skip
                break;

            case 1: // wait for the hand-over to finish
                if (!WishboundIntro.IsPlaying) { stage = 2; stageStart = EditorApplication.timeSinceStartup; }
                break;

            case 2: // wait for the child to be placed behind their tree, then put the star next to them
                var enc = ChildEncounter.Instance;
                if (enc && enc.Child && enc.Child.gameObject.activeInHierarchy && !enc.HasMet && enc.HidePos != Vector3.zero)
                {
                    var player = Object.FindFirstObjectByType<IslandThirdPersonController>();
                    if (!player) return;
                    Vector3 away = enc.HidePos - enc.Trunk; away.y = 0; away = away.sqrMagnitude > .01f ? away.normalized : Vector3.forward;
                    Vector3 p = enc.HidePos + away * 5.5f;
                    if (Physics.Raycast(p + Vector3.up * 30, Vector3.down, out var hit, 80, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * .6f;
                    Vector3 toChild = enc.HidePos - p; toChild.y = 0;
                    player.Teleport(p, Quaternion.LookRotation(toChild).eulerAngles.y);
                    Debug.Log("[DevSkipToChild] star placed " + Vector3.Distance(p, enc.HidePos).ToString("F1") + " m from the child");
                    Disarm();
                }
                break;
        }
    }
}
#endif
