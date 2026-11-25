using System.Collections.Generic;
using UnityEngine;

/// <summary>Continuous bridge support and a safe companion corridor, independent of decorative planks.</summary>
public sealed class BridgeWalkSurface : MonoBehaviour
{
    static readonly List<BridgeWalkSurface> surfaces = new List<BridgeWalkSurface>();
    public Vector3[] Centerline { get; private set; }
    public float HalfWidth { get; private set; }
    public float Length { get; private set; }

    public void Configure(Vector3[] points, float width)
    {
        Centerline = (Vector3[])points.Clone();
        HalfWidth = width * .5f;
        Length = 0;
        for (int i=1;i<points.Length;i++) Length += Vector3.Distance(points[i-1], points[i]);
        if(isActiveAndEnabled && !surfaces.Contains(this))surfaces.Add(this);
    }
    void OnEnable() { if (!surfaces.Contains(this)) surfaces.Add(this); }
    void OnDisable() { surfaces.Remove(this); }
    void OnDestroy() { surfaces.Remove(this); }

    public bool Project(Vector3 point, float margin, out Vector3 support)
    {
        support = point;
        if (Centerline == null || Centerline.Length < 2) return false;
        float best = float.PositiveInfinity;
        for (int i=1;i<Centerline.Length;i++)
        {
            Vector3 a = Centerline[i-1], b = Centerline[i];
            Vector3 span = b-a; span.y=0;
            float length2=span.sqrMagnitude;
            if (length2<.0001f) continue;
            Vector3 delta=point-a; delta.y=0;
            float raw=Vector3.Dot(delta,span)/length2;
            if (raw<0 || raw>1) continue;
            Vector3 middle=Vector3.Lerp(a,b,raw);
            Vector3 side=Vector3.Cross(Vector3.up,span.normalized);
            float lateral=Vector3.Dot(point-middle,side);
            float distance=Mathf.Abs(lateral);
            if(distance>HalfWidth+margin || distance>=best)continue;
            best=distance;
            support=middle+side*Mathf.Clamp(lateral,-Mathf.Max(.1f,HalfWidth-margin),Mathf.Max(.1f,HalfWidth-margin));
        }
        return best<float.PositiveInfinity;
    }

    public static bool TrySupport(Vector3 near, out BridgeWalkSurface bridge, out Vector3 support)
    {
        bridge=null;support=near;float nearest=3f;
        foreach(var candidate in surfaces)
        {
            if(!candidate || !candidate.Project(near,0,out var p))continue;
            float height=Mathf.Abs(p.y-near.y);
            if(height>=nearest)continue;
            nearest=height;bridge=candidate;support=p;
        }
        return bridge;
    }
}
