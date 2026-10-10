using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class TerrainColorCameraSetup : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [SerializeField] private float height = 200f;
    [SerializeField] private RenderTexture targetTexture;
    [SerializeField] private string terrainLayerName = "Terrain";

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (terrain == null)
        {
            // auto-find
            terrain = FindFirstObjectByType<Terrain>();
            if (terrain == null) return;
        }

        Camera cam = GetComponent<Camera>();
        Vector3 size = terrain.terrainData.size;
        Vector3 origin = terrain.transform.position;

        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(size.x, size.z) * 0.5f;

        transform.position = new Vector3(
            origin.x + size.x * 0.5f,
            height,
            origin.z + size.z * 0.5f);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        if (targetTexture != null) cam.targetTexture = targetTexture;

        int layer = LayerMask.NameToLayer(terrainLayerName);
        if (layer >= 0) cam.cullingMask = 1 << layer;

        cam.farClipPlane = Mathf.Max(cam.farClipPlane, height + size.y + 10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(cam);
#endif
    }
}
