using UnityEngine;

/// <summary>
/// Keeps exactly one active AudioListener in the scene, at the player, facing with the camera, so the game is always audible.
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
    Transform player;
    Camera view;
    AudioListener listener;
    public static Transform Ear { get; private set; }

    void LateUpdate()
    {
        if (!view) return;
        var target = player && player.gameObject.activeInHierarchy && !WishboundIntro.IsPlaying ? player : view.transform;
        transform.SetPositionAndRotation(target.position, view.transform.rotation);
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        AudioListener.pause = false;
        view = Camera.main;
        if (!view) return;
        if (!player) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (!listener) listener = gameObject.AddComponent<AudioListener>();
        Ear = transform;
        listener.enabled = true;
        foreach (var other in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (other != listener && other.enabled) other.enabled = false;
    }
}
