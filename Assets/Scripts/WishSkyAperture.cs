using System.Collections.Generic;
using UnityEngine;

/// <summary>Layered, world-space light drawn into the sky for the fragment farewell.</summary>
internal sealed class WishSkyAperture : MonoBehaviour
{
    const int Segments = 80;
    readonly List<Mesh> meshes = new List<Mesh>();
    Material additive;
    Transform opening, glint;
    float aperture;

    public static WishSkyAperture Create(Transform parent, Vector3 position, Vector3 forward)
    {
        var root = new GameObject("Sky aperture");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        var effect = root.AddComponent<WishSkyAperture>();
        effect.Build();
        return effect;
    }

    void Build()
    {
        var shader = Shader.Find("Wishbound/Atmosphere Additive");
        if (!shader) return;
        additive = new Material(shader);
        opening = new GameObject("Opening stars and corona").transform;
        opening.SetParent(transform, false);
        // Fixed stars emerge gradually through a diffuse band of skyglow.
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var random = new System.Random(430);
        for (int i = 0; i < 18; i++)
        {
            float x = (float)random.NextDouble()*30f-15f;
            float y = x*.24f + (float)random.NextDouble()*3f-1.5f;
            Diamond(vertices,colors,indices,new Vector3(x,y,.13f),5f,2f,new Color(.25f,.38f,.65f,.035f));
        }
        for (int i = 0; i < 74; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * 9.5f;
            var p = new Vector3(Mathf.Cos(a)*distance,Mathf.Sin(a)*distance,.08f);
            float size = Mathf.Lerp(.035f,.19f,(float)random.NextDouble());
            Diamond(vertices,colors,indices,p,size,size,new Color(.65f,.87f,1.7f,.92f));
        }
        MakeMesh("Revealed starfield", opening, vertices, colors, indices);
        vertices.Clear(); colors.Clear(); indices.Clear();
        Diamond(vertices,colors,indices,Vector3.zero,.77f,.77f,new Color(1.4f,1.6f,2.9f,1));
        Diamond(vertices,colors,indices,Vector3.zero,2.4f,.19f,new Color(.86f,1.3f,2.7f,1));
        Diamond(vertices,colors,indices,Vector3.zero,.19f,2.4f,new Color(.86f,1.3f,2.7f,1));
        glint = MakeMesh("Farewell twinkle",transform,vertices,colors,indices);
        SetOpen(0f);
        SetGlint(0f);
    }

    void Ring(float inner, float crest, float outer, Color color, float depth)
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        float[] radii = { inner, crest, outer };
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            float variation = 1f + .035f*Mathf.Sin(a*7f) + .021f*Mathf.Sin(a*13f+.7f);
            Vector3 v = new Vector3(Mathf.Cos(a),Mathf.Sin(a),0) * (11f * variation);
            for (int j = 0; j < 3; j++)
            {
                vertices.Add(v * radii[j] + Vector3.forward * depth);
                colors.Add(new Color(color.r,color.g,color.b,j == 1 ? color.a : 0f));
            }
            for (int j = 0; j < 2; j++)
            {
                int a0 = i * 3 + j, b = (i+1)%Segments * 3 + j;
                triangles.AddRange(new[] { a0,b,a0+1,a0+1,b,b+1 });
            }
        }
        MakeMesh("Sky corona",opening,vertices,colors,triangles);
    }

    static void Diamond(List<Vector3> v,List<Color> colors,List<int> triangles,Vector3 p,float x,float y,Color tint)
    {
        int start = v.Count;
        v.Add(p); v.Add(p+Vector3.right*x); v.Add(p+Vector3.up*y); v.Add(p-Vector3.right*x); v.Add(p-Vector3.up*y);
        colors.Add(tint);
        for (int i=0;i<4;i++)
        {
            colors.Add(new Color(tint.r,tint.g,tint.b,0));
            triangles.AddRange(new[] { start,start+i+1,start+(i+1)%4+1 });
        }
    }

    Transform MakeMesh(string name,Transform parent,List<Vector3> vertices,List<Color> colors,List<int> triangles)
    {
        var item = new GameObject(name);
        item.transform.SetParent(parent,false);
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles,0);
        item.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = additive;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshes.Add(mesh);
        return item.transform;
    }

    public void SetOpen(float amount)
    {
        if (!opening) return;
        aperture = Mathf.Clamp01(amount);
        opening.gameObject.SetActive(aperture > .001f);
        opening.localScale = new Vector3(1.8f,.86f,1);
        additive.SetColor("_Tint",new Color(1,1,1,Mathf.SmoothStep(0,1,aperture)));
    }

    public void Shimmer(float amount)
    {
        if (!opening) return;
        SetOpen(aperture);
        additive.SetColor("_Tint",new Color(1,1,1,aperture*(.97f+.03f*amount*Mathf.Sin(Time.time))));
    }

    public void SetGlint(float amount)
    {
        if (!glint) return;
        if (amount > .005f) additive.SetColor("_Tint",Color.white);
        glint.gameObject.SetActive(amount > .005f);
        glint.localScale = Vector3.one * (amount * 1.5f);
    }

    public LineRenderer MakeWake(Vector3 start, bool streak = false)
    {
        var line = new GameObject(streak ? "Lightspeed streak" : "Starlight ascent trail").AddComponent<LineRenderer>();
        line.transform.SetParent(transform,false);
        line.sharedMaterial = additive;
        line.alignment = LineAlignment.View;
        line.positionCount = 2;
        line.SetPosition(0,start);
        line.SetPosition(1,start);
        line.widthCurve = AnimationCurve.Linear(0f,.04f,1f,1f);
        line.startColor = new Color(.27f,.57f,2f,0f);
        line.endColor = streak ? new Color(.65f,1.2f,2.1f,.7f) : new Color(1.15f,1.75f,3f,.95f);
        line.numCapVertices = 4;
        return line;
    }

    void OnDestroy()
    {
        foreach (var mesh in meshes) if (mesh) Destroy(mesh);
        if (additive) Destroy(additive);
    }
}

