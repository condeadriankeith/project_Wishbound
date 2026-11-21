using UnityEngine;

/// <summary>
/// Keeps exactly one active AudioListener in the scene, on the main camera, so the game is always audible.
/// Runs automatically in Play (no scene setup needed) and re-checks twice a second in case the camera is swapped or rebuilt.
/// </summary>
public class AudioListenerGuard : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<AudioListenerGuard>() != null) return;
        var go = new GameObject("AudioListenerGuard");
        DontDestroyOnLoad(go);
        go.AddComponent<AudioListenerGuard>();
    }

    float next;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        AudioListener.pause = false;
        var cam = Camera.main;
        if (cam == null) return;
        var mine = cam.GetComponent<AudioListener>();
        if (mine == null) mine = cam.gameObject.AddComponent<AudioListener>();
        mine.enabled = true;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (l != mine && l.enabled) l.enabled = false;   // two listeners spam warnings and can mute audio
    }
}
