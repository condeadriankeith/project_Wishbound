using UnityEngine;

/// <summary>
/// Manages a camera-centred grass trample interaction texture.
/// R = push X (signed, 0.5 = neutral), G = push Z (signed, 0.5 = neutral),
/// B = press amount (0 = untrammeled, 1 = fully pressed).
///
/// The texture follows the camera so only the ~128 m window the player
/// occupies is ever covered. External interactors (NPCs, animals) can call
/// StampAt() to leave their own trample marks.
/// </summary>
[DefaultExecutionOrder(-50)]
public class GrassTrampleSystem : MonoBehaviour
{
    [Header("Texture")]
    [Tooltip("World-space window size in metres. Centred on the camera.")]
    public float windowSize = 128f;

    [Tooltip("Texture resolution. 1024 gives ~12.5 cm per texel.")]
    public int resolution = 1024;

    [Header("Stamp")]
    [Tooltip("Radius of each foot stamp in metres.")]
    public float stampRadius = 0.4f;

    [Tooltip("Strength added per stamp (0-1).")]
    public float stampStrength = 0.9f;

    [Header("Decay")]
    [Tooltip("Time in seconds for a fully pressed blade to spring back upright.")]
    public float recoverySeconds = 7f;

    RenderTexture _trampleRT;
    Material _decayMat;
    Material _stampMat;
    Transform _cameraTransform;
    Transform _playerTransform;

    static readonly int IDTrampleTex    = Shader.PropertyToID("_GrassTrampleTex");
    static readonly int IDTrampleOrigin = Shader.PropertyToID("_GrassTrampleOrigin");
    static readonly int IDTrampleSize   = Shader.PropertyToID("_GrassTrampleSize");
    static readonly int IDDecayFactor   = Shader.PropertyToID("_DecayFactor");
    static readonly int IDStampCenter   = Shader.PropertyToID("_StampCenter");
    static readonly int IDStampRadius   = Shader.PropertyToID("_StampRadius");
    static readonly int IDStampStrength = Shader.PropertyToID("_StampStrength");
    static readonly int IDStampPushDir  = Shader.PropertyToID("_StampPushDir");

    void Awake()
    {
        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGB32, 0)
        {
            sRGB = false
        };
        _trampleRT = new RenderTexture(desc)
        {
            name       = "GrassTrample",
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _trampleRT.Create();
        ClearToNeutral();

        _decayMat = new Material(Shader.Find("Hidden/GrassTrampleDecay")) { name = "TrampleDecay" };
        _stampMat = new Material(Shader.Find("Hidden/GrassTrampleStamp")) { name = "TrampleStamp" };
    }

    void Start()
    {
        var cam = Camera.main;
        if (cam != null) _cameraTransform = cam.transform;

        var fps = FindFirstObjectByType<IslandFPSController>();
        if (fps != null) _playerTransform = fps.transform;

        Shader.SetGlobalTexture(IDTrampleTex, _trampleRT);
    }

    void Update()
    {
        if (_cameraTransform == null) return;

        // Expose current window bounds to all grass shaders
        Vector3 camPos = _cameraTransform.position;
        float   half   = windowSize * 0.5f;
        Shader.SetGlobalVector(IDTrampleOrigin, new Vector4(camPos.x - half, camPos.z - half, 0f, 0f));
        Shader.SetGlobalFloat(IDTrampleSize, windowSize);

        // Decay: exponential fade toward 0 press
        float decay = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(recoverySeconds, 0.1f));
        _decayMat.SetFloat(IDDecayFactor, decay);
        BlitInPlace(_decayMat);

        // Stamp player feet when moving
        if (_playerTransform != null)
        {
            var cc = _playerTransform.GetComponent<CharacterController>();
            Vector3 vel = (cc != null) ? cc.velocity : Vector3.zero;
            var push = new Vector2(vel.x, vel.z);
            if (push.sqrMagnitude > 0.01f) push.Normalize();
            StampAt(_playerTransform.position, push);
        }
    }

    /// <summary>Stamps a trample mark at world position with the given push direction.</summary>
    public void StampAt(Vector3 worldPos, Vector2 pushDir)
    {
        if (_stampMat == null || _trampleRT == null || _cameraTransform == null) return;

        float   half  = windowSize * 0.5f;
        Vector3 cam   = _cameraTransform.position;
        float   u     = (worldPos.x - (cam.x - half)) / windowSize;
        float   v     = (worldPos.z - (cam.z - half)) / windowSize;

        // Clamp: only stamp if inside the window
        if (u < 0f || u > 1f || v < 0f || v > 1f) return;

        _stampMat.SetVector(IDStampCenter,   new Vector4(u, v, 0f, 0f));
        _stampMat.SetFloat(IDStampRadius,    stampRadius / windowSize);
        _stampMat.SetFloat(IDStampStrength,  stampStrength);
        // Pack push direction -1..1 -> 0..1
        _stampMat.SetVector(IDStampPushDir,  new Vector4(pushDir.x * 0.5f + 0.5f,
                                                         pushDir.y * 0.5f + 0.5f, 0f, 0f));
        BlitInPlace(_stampMat);
    }

    // Blit src -> tmp via mat, then copy tmp back to _trampleRT.
    void BlitInPlace(Material mat)
    {
        var tmp = RenderTexture.GetTemporary(_trampleRT.descriptor);
        Graphics.Blit(_trampleRT, tmp, mat);
        Graphics.Blit(tmp, _trampleRT);
        RenderTexture.ReleaseTemporary(tmp);
    }

    void ClearToNeutral()
    {
        // R=0.5 G=0.5 (no direction) B=0 (not pressed) A=1
        var prev = RenderTexture.active;
        RenderTexture.active = _trampleRT;
        GL.Clear(false, true, new Color(0.5f, 0.5f, 0f, 1f));
        RenderTexture.active = prev;
    }

    void OnDestroy()
    {
        if (_trampleRT != null) { _trampleRT.Release(); Destroy(_trampleRT); }
        if (_decayMat  != null) Destroy(_decayMat);
        if (_stampMat  != null) Destroy(_stampMat);
    }
}
