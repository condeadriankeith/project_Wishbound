using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using static TerrainMath;

public class MeshSubdivider : EditorWindow
{
    [MenuItem("Tools/Island/Subdivide Island Mesh (Smooth Cliffs)")]
    public static void SubdivideIslandMesh()
    {
        var ib = FindFirstObjectByType<IslandBuilder>();
        if (ib == null)
        {
            Debug.LogError("[MeshSubdivider] No IslandBuilder in scene.");
            return;
        }

        var mf = ib.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("[MeshSubdivider] Island mesh not built yet.");
            return;
        }

        var originalMesh = mf.sharedMesh;
        
        // Subdivide mesh using edge-split on steep normals (cliffs)
        var subdividedMesh = SubdivideWithCliffSmoothing(originalMesh, 2, 0.7f);
        
        // Save as new asset
        string savePath = "Assets/BakedIslandData";
        if (!AssetDatabase.IsValidFolder(savePath))
            AssetDatabase.CreateFolder("Assets", "BakedIslandData");

        string meshPath = $"{savePath}/IslandMesh_Subdivided.asset";
        var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existingMesh != null)
        {
            EditorUtility.CopySerialized(subdividedMesh, existingMesh);
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.CreateAsset(subdividedMesh, meshPath);
        }

        // Also update the runtime mesh
        mf.sharedMesh = subdividedMesh;
        var mc = ib.GetComponent<MeshCollider>();
        if (mc) mc.sharedMesh = subdividedMesh;

        // Rebuild heightmap at higher resolution
        RebuildHeightMap(ib, subdividedMesh);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MeshSubdivider] Subdivided mesh saved to {meshPath} (verts: {subdividedMesh.vertexCount})");
    }

    [MenuItem("Tools/Island/Rebake All With Subdivision")]
    public static void RebakeAllWithSubdivision()
    {
        SubdivideIslandMesh();
        // Re-run bakers after subdivision
        IslandBaker.BakeAll();
    }

    static Mesh SubdivideWithCliffSmoothing(Mesh source, int iterations, float cliffThreshold)
    {
        Mesh current = source;
        
        for (int iter = 0; iter < iterations; iter++)
        {
            current = SubdivideOnce(current, cliffThreshold);
        }
        
        return current;
    }

    static Mesh SubdivideOnce(Mesh source, float cliffThreshold)
    {
        var verts = source.vertices;
        var norms = source.normals;
        var colors = source.colors;
        var tris = source.triangles;
        var uv = source.uv;

        var newVerts = new List<Vector3>(verts);
        var newNorms = new List<Vector3>(norms);
        var newColors = colors.Length > 0 ? new List<Color>(colors) : null;
        var newUV = uv.Length > 0 ? new List<Vector2>(uv) : null;

        var edgeMidpoints = new Dictionary<long, int>();
        
        System.Func<int, int, int> GetMidpoint = (a, b) =>
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (edgeMidpoints.TryGetValue(key, out int idx))
                return idx;

            Vector3 va = verts[a], vb = verts[b];
            Vector3 na = norms[a], nb = norms[b];
            Vector3 midPos = (va + vb) * 0.5f;
            Vector3 midNorm = Vector3.Lerp(na, nb, 0.5f).normalized;
            
            // Detect cliff: if normals differ significantly, smooth the edge
            float normalDiff = 1.0f - Vector3.Dot(na, nb);
            if (normalDiff > cliffThreshold)
            {
                // Smooth the midpoint normal toward vertical for cliffs
                midNorm = Vector3.Lerp(midNorm, Vector3.up, 0.5f).normalized;
            }
            
            Color midCol = Color.white;
            if (newColors != null) midCol = Color.Lerp(colors[a], colors[b], 0.5f);
            Vector2 midUV = Vector2.zero;
            if (newUV != null) midUV = Vector2.Lerp(uv[a], uv[b], 0.5f);

            int midIdx = newVerts.Count;
            newVerts.Add(midPos);
            newNorms.Add(midNorm);
            if (newColors != null) newColors.Add(midCol);
            if (newUV != null) newUV.Add(midUV);
            
            edgeMidpoints[key] = midIdx;
            return midIdx;
        };

        var newTris = new List<int>();
        
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];

            int ab = GetMidpoint(a, b);
            int bc = GetMidpoint(b, c);
            int ca = GetMidpoint(c, a);

            // Create 4 triangles from 1
            newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
            newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
            newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
            newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
        }

        var mesh = new Mesh
        {
            name = source.name + "_Subdivided",
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(newVerts);
        mesh.SetNormals(newNorms);
        if (newColors != null) mesh.SetColors(newColors);
        if (newUV != null) mesh.SetUVs(0, newUV);
        mesh.SetTriangles(newTris, 0);
        mesh.RecalculateBounds();
        
        return mesh;
    }

    static void RebuildHeightMap(IslandBuilder ib, Mesh mesh)
    {
        int V = Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount));
        if (V * V != mesh.vertexCount)
        {
            // Not a regular grid, use the existing HeightTex resolution
            V = 256;
        }
        
        var pos = mesh.vertices;
        var colors = mesh.colors;
        
        var tex = new Texture2D(V, V, TextureFormat.RHalf, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "IslandHeight"
        };
        
        var pixels = new Color[V * V];
        for (int j = 0; j < V; j++)
        {
            for (int i = 0; i < V; i++)
            {
                int k = j * V + i;
                float h = pos[k].y;
                pixels[k] = new Color(h / 300f, 0, 0, 1); // Normalized height
            }
        }
        
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        ib.HeightTex = tex;
        Shader.SetGlobalTexture("_GhibliHeightMap", tex);
    }
}