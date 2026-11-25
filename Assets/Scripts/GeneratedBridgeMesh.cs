using UnityEngine;

/// <summary>Releases the generated collision mesh when an island rebuild retires its bridge.</summary>
public sealed class GeneratedBridgeMesh : MonoBehaviour
{
    public Mesh OwnedMesh;
    public Mesh[] VisualMeshes;
    void OnDestroy()
    {
        Release(OwnedMesh);
        if(VisualMeshes!=null)foreach(var mesh in VisualMeshes)Release(mesh);
    }
    static void Release(Mesh mesh) { if(!mesh)return;if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh); }
}
