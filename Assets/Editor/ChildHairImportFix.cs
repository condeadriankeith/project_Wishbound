using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// FBX mirrors can bake reversed winding into these closed hair meshes.
// Repair the surface and its inverted outline shell independently at import.
public sealed class ChildHairImportFix : AssetPostprocessor
{
    public override uint GetVersion() => 1;
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != "Assets/Characters/ChildAnimated/child.fbx" &&
            assetPath != "Assets/Characters/ChildToon/child_export.fbx") return;
        var meshes = new HashSet<Mesh>();
        foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) meshes.Add(r.sharedMesh);
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) meshes.Add(f.sharedMesh);
        foreach (var mesh in meshes)
        {
            if (!mesh || !(mesh.name.StartsWith("hair_") || mesh.name == "scalp_cap")) continue;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var flipped = new HashSet<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var triangles = mesh.GetTriangles(s);
                double volume = 0;
                var center = mesh.bounds.center;
                for (int i = 0; i < triangles.Length; i += 3)
                    volume += Vector3.Dot(vertices[triangles[i]] - center,
                        Vector3.Cross(vertices[triangles[i+1]] - center, vertices[triangles[i+2]] - center));
                if (System.Math.Abs(volume) < 1e-15 || (s == 0 ? volume > 0 : volume < 0)) continue;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int temp = triangles[i+1]; triangles[i+1] = triangles[i+2]; triangles[i+2] = temp;
                    flipped.Add(triangles[i]); flipped.Add(triangles[i+1]); flipped.Add(triangles[i+2]);
                }
                mesh.SetTriangles(triangles, s, false);
            }
            foreach (int i in flipped)
            {
                if (normals.Length == vertices.Length) normals[i] = -normals[i];
                if (tangents.Length == vertices.Length) tangents[i].w = -tangents[i].w;
            }
            if (flipped.Count > 0) { mesh.normals = normals; if (tangents.Length > 0) mesh.tangents = tangents; }
        }
    }
}
