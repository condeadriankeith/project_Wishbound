using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared island bridge construction using the authored ferry deck's wood, pins, posts and rope.</summary>
public static class IslandDeckBridge
{
    public static GameObject Build(string name, Vector3[] path, float width, Transform parent)
    {
        if(path==null || path.Length<2)throw new System.ArgumentException("Bridge needs two or more centerline points");
        var prefab=Resources.Load<GameObject>("VerticalSlice/FerryPier");
        if(!prefab)throw new System.InvalidOperationException("Missing authored FerryPier prefab");
        width=Mathf.Max(2.2f,width);
        path=Approaches(path);
        var root=new GameObject(name);root.transform.SetParent(parent,false);
        var surface=root.AddComponent<BridgeWalkSurface>();surface.Configure(path,width*.84f);
        var distances=new float[path.Length];
        for(int i=1;i<path.Length;i++)distances[i]=distances[i-1]+Vector3.Distance(path[i-1],path[i]);
        float length=distances[distances.Length-1];
        int modules=Mathf.Max(1,Mathf.CeilToInt(length/3.2f));
        float moduleLength=length/modules;
        for(int i=0;i<modules;i++)
        {
            Vector3 a=At(path,distances,i*moduleLength),b=At(path,distances,(i+1)*moduleLength);
            var module=Object.Instantiate(prefab,root.transform);
            module.name="Authored deck module "+i;
            module.transform.rotation=Quaternion.LookRotation((b-a).normalized,Vector3.up);
            module.transform.position=(a+b)*.5f-module.transform.up*.49f;
            module.transform.localScale=new Vector3(width/2.35f,1,Vector3.Distance(a,b)/3.2f);
            foreach(var collider in module.GetComponentsInChildren<Collider>())
            {
                collider.enabled=false;
                if(Application.isPlaying)Object.Destroy(collider);else Object.DestroyImmediate(collider);
            }
            // Extend the existing beveled piling meshes to support the hand ropes.
            foreach(var filter in module.GetComponentsInChildren<MeshFilter>())
                if(filter.name.StartsWith("Pier piling"))filter.transform.localScale=Vector3.Scale(filter.transform.localScale,new Vector3(1,2.5f,1));
        }
        var vertices=new Vector3[path.Length*2];var triangles=new int[(path.Length-1)*6];
        for(int i=0;i<path.Length;i++)
        {
            Vector3 forward=path[Mathf.Min(i+1,path.Length-1)]-path[Mathf.Max(i-1,0)];forward.y=0;
            Vector3 right=Vector3.Cross(Vector3.up,forward.normalized)*width*.5f;
            vertices[i*2]=root.transform.InverseTransformPoint(path[i]-right);
            vertices[i*2+1]=root.transform.InverseTransformPoint(path[i]+right);
            if(i==path.Length-1)continue;
            int v=i*2,t=i*6;
            triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;
            triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;
        }
        var mesh=new Mesh {name=name+" continuous walk surface"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
        var floor=root.AddComponent<MeshCollider>();floor.sharedMesh=mesh;
        var owned=root.AddComponent<GeneratedBridgeMesh>();owned.OwnedMesh=mesh;
        var groups=new Dictionary<Material,List<CombineInstance>>();
        var oldModules=new List<GameObject>();
        foreach(Transform module in root.transform)
        {
            oldModules.Add(module.gameObject);
            foreach(var filter in module.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {
                    var material=renderer.sharedMaterials[sub];
                    if(!groups.TryGetValue(material,out var combines)){combines=new List<CombineInstance>();groups.Add(material,combines);}
                    combines.Add(new CombineInstance {mesh=filter.sharedMesh,subMeshIndex=sub,transform=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
            }
        }
        var visualMeshes=new List<Mesh>();
        foreach(var pair in groups)
        {
            var combined=new Mesh {name=name+" "+pair.Key.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            combined.CombineMeshes(pair.Value.ToArray(),true,true);
            var batch=new GameObject("Deck detail - "+pair.Key.name,typeof(MeshFilter),typeof(MeshRenderer));batch.transform.SetParent(root.transform,false);
            batch.GetComponent<MeshFilter>().sharedMesh=combined;batch.GetComponent<MeshRenderer>().sharedMaterial=pair.Key;
            visualMeshes.Add(combined);
        }
        owned.VisualMeshes=visualMeshes.ToArray();
        foreach(var module in oldModules){module.SetActive(false);if(Application.isPlaying)Object.Destroy(module);else Object.DestroyImmediate(module);}
        var rope=Resources.Load<Material>("VerticalSlice/Ivory");
        for(int side=-1;side<=1;side+=2)
        {
            var rail=new GameObject("Continuous rope rail");rail.transform.SetParent(root.transform,false);
            var line=rail.AddComponent<LineRenderer>();line.sharedMaterial=rope;line.widthMultiplier=.045f;line.numCornerVertices=2;line.positionCount=path.Length;
            for(int i=0;i<path.Length;i++)
            {
                Vector3 f=path[Mathf.Min(i+1,path.Length-1)]-path[Mathf.Max(i-1,0)];f.y=0;
                line.SetPosition(i,path[i]+Vector3.Cross(Vector3.up,f.normalized)*side*width*.46f+Vector3.up*.82f);
            }
            for(int i=1;i<path.Length;i++)
            {
                Vector3 delta=path[i]-path[i-1];
                var barrier=new GameObject("Smooth side barrier");barrier.transform.SetParent(root.transform,false);
                barrier.transform.rotation=Quaternion.LookRotation(delta,Vector3.up);
                barrier.transform.position=(path[i]+path[i-1])*.5f+barrier.transform.right*side*width*.48f+Vector3.up*.45f;
                var box=barrier.AddComponent<BoxCollider>();box.size=new Vector3(.08f,.9f,delta.magnitude+.03f);
            }
        }
        return root;
    }

    static Vector3 At(Vector3[] path,float[] distances,float value)
    {
        for(int i=1;i<path.Length;i++)if(value<=distances[i])return Vector3.Lerp(path[i-1],path[i],Mathf.InverseLerp(distances[i-1],distances[i],value));
        return path[path.Length-1];
    }

    static Vector3[] Approaches(Vector3[] original)
    {
        var points=new List<Vector3>(original);
        for(int end=0;end<2;end++)
        {
            int index=end==0?0:original.Length-1;
            Vector3 bank=original[index];
            if(!TerrainProbe.TryGetGroundY(bank.x,bank.z,out float floor) || bank.y-floor<.18f)continue;
            Vector3 away=bank-original[end==0?1:original.Length-2];away.y=0;away.Normalize();
            float run=Mathf.Clamp((bank.y-floor)/.12f,2f,8f);
            Vector3 landing=bank+away*run;
            if(!TerrainProbe.TryGetGroundY(landing.x,landing.z,out float landingY))continue;
            landing.y=landingY+.06f;
            int count=Mathf.CeilToInt(run/.4f);
            var ramp=new List<Vector3>();
            for(int i=0;i<count;i++)ramp.Add(Vector3.Lerp(landing,bank,(float)i/count));
            if(end==0)points.InsertRange(0,ramp);
            else{ramp.Reverse();points.AddRange(ramp);}
        }
        return points.ToArray();
    }

    public static GameObject Between(string name,Vector3 a,Vector3 b,float width,Transform parent,float arch=.25f)
    {
        int segments=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(a,b)/.6f));
        var path=new Vector3[segments+1];
        for(int i=0;i<=segments;i++){float t=(float)i/segments;path[i]=Vector3.Lerp(a,b,t)+Vector3.up*(.06f+Mathf.Sin(t*Mathf.PI)*arch);}
        return Build(name,path,width,parent);
    }
}
