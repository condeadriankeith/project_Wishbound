using UnityEngine;
public class PerfProbe : MonoBehaviour
{
    public static string Last = "no data";
    float acc, worst; int n; float t0;
    FrameTiming[] ft = new FrameTiming[1];
    void Update()
    {
        float d = Time.unscaledDeltaTime; acc += d; n++; if (d > worst) worst = d;
        FrameTimingManager.CaptureFrameTimings();
        if (Time.realtimeSinceStartup - t0 >= 2f && n > 0)
        {
            uint got = FrameTimingManager.GetLatestTimings(1, ft);
            double cpu = got > 0 ? ft[0].cpuMainThreadFrameTime : 0, rt = got > 0 ? ft[0].cpuRenderThreadFrameTime : 0, gpu = got > 0 ? ft[0].gpuFrameTime : 0;
            Last = "fps=" + (n / acc).ToString("F1") + " avg=" + (acc / n * 1000f).ToString("F1") + "ms worst=" + (worst * 1000f).ToString("F0") + "ms | cpuMain=" + cpu.ToString("F1") + " cpuRender=" + rt.ToString("F1") + " gpu=" + gpu.ToString("F1");
            acc = 0; n = 0; worst = 0; t0 = Time.realtimeSinceStartup;
        }
    }
}