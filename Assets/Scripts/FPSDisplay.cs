using UnityEngine;

public class FPSDisplay : MonoBehaviour
{
    [SerializeField] private float updateInterval = 0.5f;

    private float accumulated;
    private int frames;
    private float timeLeft;
    private float fps;

    private void Start() => timeLeft = updateInterval;

    private void Update()
    {
        timeLeft -= Time.unscaledDeltaTime;
        if (Time.unscaledDeltaTime > 0f)
            accumulated += 1f / Time.unscaledDeltaTime;
        frames++;

        if (timeLeft <= 0f)
        {
            fps = frames > 0 ? accumulated / frames : 0f;
            timeLeft = updateInterval;
            accumulated = 0f;
            frames = 0;
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 200, 30), $"FPS: {fps:0}");
    }
}
