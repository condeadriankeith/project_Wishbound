using System.Collections.Generic;
using UnityEngine;
using static TerrainMath;

/// <summary>
/// Builds the full Whispering Meadow world on top of IslandBuilder terrain.
/// Attach to the same GameObject as IslandBuilder (or any GO in Island scene).
/// Press Play — everything constructs in Start, draws/animates in Update.
/// </summary>
public class IslandWorld : MonoBehaviour
{
    [Header("Quality")]
    public bool fullQuality = true;

    // runtime refs
    Material foliageMat, grassMat, cloudMat, birdMat, oceanMat, skyMat, beamMat, waterfallMat, windowMat;
    readonly Dictionary<string, Material> toonCache = new();
    readonly List<BoatRef> boats = new();
    Transform windmillHub, beamPivot, playerBody;
    Transform[] legL;
    List<Transform> legList = new();
    GameObject playerGO;
    CameraDirector director;
    MoodController mood;
    Light sun;
    bool flyMode;
    float flyTimeSinceLastSpace;
    float flyTimeSinceLastLastSpace;

    // instanced batches (drawn every frame)
    InstancedBatch oakB, cypB, pineB, ivyB, grassB, bodyB, roofB, chimB, winB;
    // birds dynamic
    InstancedBatch birdB;
    struct Bird { public float cx, cz, r, y, a0, spd, ph, s; public int sign; }
    Bird[] birds;
    Matrix4x4[] birdMats; Vector4[] birdCols;
    MaterialPropertyBlock birdMpb;
    Mesh birdMesh;

    // clouds drift
    struct CloudCluster { public Transform root; public float radius; public float angle; public float speed; }
    CloudCluster[] clusters;
    float[] clusterBaseY;

    // boats sail params
    struct BoatRef { public Transform t; public int idx; public bool sailing; public float sc; }

    [Header("Bare island mode: terrain + ocean + sky only")]
    public bool bareIsland = true;

    void Start()
    {
        Scatter.Reseed(1234);
        EnsureShadersAndMaterials();
        EnsureSunSkyOcean();
        EnsureMoodAndCamera();
        if (bareIsland)
        {
            var pmBare = IslandBuilder.Instance ? IslandBuilder.Instance.PathMap : Roads.BuildPathMap();
            BuildWaterfallQuad();
            SetupCameraPresets();
            Debug.Log($"[IslandWorld:BARE] H(0,0)={H(0,0):F1} H(35,25)={H(35,25):F1} H(-175,-135)={H(-175,-135):F1} phase5Buildings={BuildingSystem.Buildings.Count}");
            return;
        }
        var pm = IslandBuilder.Instance ? IslandBuilder.Instance.PathMap : Roads.BuildPathMap();
        BuildVegetation(pm);
        BuildWindmillChapel();
        BuildSpireProps();
        BuildHarborAndBoats();
        BuildCitadel();
        BuildWaterfall();
        BuildCloudsBirdsMotes();
        BuildPlayer();
        SetupCameraPresets();
        Debug.Log($"[IslandWorld] H(0,0)={H(0,0):F1} H(35,25)={H(35,25):F1} H(-175,-135)={H(-175,-135):F1} dz0={Roads.Dz0:F1}");
    }

    // ---------- materials ----------
    Material ToonMat(Color c, float wind = 0, bool vcolor = false, int cull = 2, Color? emission = null)
    {
        string key = $"{c.r:F3}{c.g:F3}{c.b:F3}|{wind}|{vcolor}|{cull}|{emission}";
        if (toonCache.TryGetValue(key, out var m)) return m;
        var sh = Shader.Find("Ghibli/ToonLit");
        if (!sh) { Debug.LogError("Ghibli/ToonLit shader missing"); return new Material(Shader.Find("Universal Render Pipeline/Lit")); }
        m = new Material(sh);
        m.SetColor("_Color", c);
        m.SetColor("_InstColor", Color.white);
        m.SetFloat("_UseVertexColor", vcolor ? 1 : 0);
        m.SetFloat("_WindStrength", wind);
        m.SetFloat("_Cull", cull);
        m.SetColor("_EmissionColor", emission ?? Color.black);
        m.enableInstancing = true;
        toonCache[key] = m;
        return m;
    }

    void EnsureShadersAndMaterials()
    {
        var toon = Shader.Find("Ghibli/ToonLit");
        // Fallback to URP/Lit when custom shader is missing to avoid pink materials
        if (!toon)
        {
            toon = Shader.Find("Universal Render Pipeline/Lit");
            if (!toon) toon = Shader.Find("Standard");
            Debug.LogWarning("[IslandWorld] Ghibli/ToonLit shader not found — using fallback shader. Terrain may look different.");
        }
        foliageMat = new Material(toon); foliageMat.SetColor("_Color", Color.white);
        foliageMat.SetColor("_InstColor", Color.white); foliageMat.SetFloat("_UseVertexColor", 0); foliageMat.enableInstancing = true;
        grassMat = new Material(toon); grassMat.SetColor("_Color", Color.white);
        grassMat.SetColor("_InstColor", Color.white); grassMat.SetFloat("_UseVertexColor", 1);
        grassMat.SetFloat("_WindStrength", 1); grassMat.SetFloat("_Cull", 0); grassMat.enableInstancing = true;
        var csh = Shader.Find("Ghibli/Cloud");
        cloudMat = csh ? new Material(csh) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (csh) cloudMat.SetFloat("_Opacity", 0.62f);
        var bsh = Shader.Find("Ghibli/Bird");
        birdMat = bsh ? new Material(bsh) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        birdMat.enableInstancing = true;
        var osh = Shader.Find("Ghibli/Ocean");
        oceanMat = osh ? new Material(osh) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var ssh = Shader.Find("Ghibli/Sky");
        skyMat = ssh ? new Material(ssh) : null;
        var wsh = Shader.Find("Ghibli/Waterfall");
        waterfallMat = wsh ? new Material(wsh) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        // beams: additive unlit transparent (faint by day, strong by night via MoodController)
        beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        beamMat.SetColor("_BaseColor", new Color(1f, 0.965f, 0.77f, 0.04f));
        beamMat.SetFloat("_Surface", 1); beamMat.SetFloat("_Blend", 2);
        beamMat.SetFloat("_SrcBlend", 1); beamMat.SetFloat("_DstBlend", 1); beamMat.SetFloat("_ZWrite", 0);
        beamMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); beamMat.EnableKeyword("_BLENDMODE_ADD");
        beamMat.renderQueue = 3000;
        // window emissive
        windowMat = ToonMat(Color.black, 0, false, 2, new Color(1f, 0.86f, 0.53f) * 1.5f);
        // terrain material on IslandBuilder
        var ib = GetComponent<IslandBuilder>();
        if (ib)
        {
            var mr = ib.GetComponent<MeshRenderer>();
            var tm = new Material(toon);
            tm.SetColor("_Color", Color.white); tm.SetColor("_InstColor", Color.white);
            tm.SetFloat("_UseVertexColor", 1);
            mr.sharedMaterial = tm;
        }
    }

    void EnsureSunSkyOcean()
    {
        if (skyMat) RenderSettings.skybox = skyMat;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.0011f;
        RenderSettings.fogColor = new Color(0x92 / 255f, 0xc7 / 255f, 0xf0 / 255f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        var g = GameObject.Find("Sun");
        sun = g ? g.GetComponent<Light>() : null;
        if (!sun)
        {
            g = new GameObject("Sun"); sun = g.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft;
        }
        sun.color = new Color(1f, 0.98f, 0.918f); sun.intensity = 1.45f;
        var go = GameObject.Find("Ocean");
        if (!go)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Ocean";
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(9000, 9000, 1);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = oceanMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        else go.GetComponent<MeshRenderer>().sharedMaterial = oceanMat;
    }

    void EnsureMoodAndCamera()
    {
        mood = FindFirstObjectByType<MoodController>();
        if (!mood) mood = gameObject.AddComponent<MoodController>();
        mood.sun = sun; mood.skyMat = skyMat; mood.oceanMat = oceanMat; mood.beamMat = beamMat;
        mood.SnapTo(1); // Golden hour
        var cam = Camera.main;
        if (!cam)
        {
            var cg = new GameObject("Main Camera"); cam = cg.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.fieldOfView = 48; cam.nearClipPlane = 1; cam.farClipPlane = 6000;
        director = FindFirstObjectByType<CameraDirector>();
        if (!director) director = cam.gameObject.AddComponent<CameraDirector>();
        director.cam = cam;
    }

    public void OnIslandLoaded(Mesh islandMesh)
    {
        // Update camera far clip based on island bounds
        var cam = Camera.main;
        if (cam)
        {
            float maxDim = Mathf.Max(islandMesh.bounds.size.x, islandMesh.bounds.size.z);
            cam.farClipPlane = Mathf.Max(6000, maxDim * 2);
        }
    }

    // ---------- helpers ----------
    GameObject PropMesh(Mesh mesh, Color color, Vector3 pos, Quaternion rot, Vector3 scale, string name = "prop", Transform parent = null, float wind = 0, bool vcolor = false)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = scale;
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ToonMat(color, wind, vcolor);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
        return go;
    }
    GameObject PropPrim(PrimitiveType pt, Color color, Vector3 pos, Quaternion rot, Vector3 scale, string name = "prop", Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(pt);
        go.name = name;
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = ToonMat(color);
        var col = go.GetComponent<Collider>(); if (col) Destroy(col);
        return go;
    }

    bool ClearOfOverlook(double x, double z)
    {
        double dx = x + 191, dz = z + 163; // preset-0 camera stands here; keep sightline open
        return dx * dx + dz * dz > 22 * 22;
    }

    // ---------- vegetation ----------
    void BuildVegetation(float[] pm)
    {
        int oakN = fullQuality ? 750 : 350, cypN = fullQuality ? 650 : 320, pineN = fullQuality ? 550 : 260, ivyN = fullQuality ? 850 : 400, grassN = fullQuality ? 16000 : 6000;
        oakB = new InstancedBatch(TreeFactory.Oak(), foliageMat);
        foreach (var p in Scatter.Pick(oakN, (x, z, h) =>
            h > 4.5 && h < 240 && NV(x, z).y > 0.88 && Roads.At(pm, x, z) < 0.12 && ClearOfOverlook(x, z) &&
            System.Math.Sqrt((x - SPIRE_X) * (x - SPIRE_X) + (z - SPIRE_Z) * (z - SPIRE_Z)) > SPIRE_RAD))
        {
            float s = 1.35f + (float)Scatter.R() * 0.85f;
            var rot = Quaternion.Euler((float)(Scatter.R() - .5) * 5.7f, (float)Scatter.R() * 360f, (float)(Scatter.R() - .5) * 5.7f);
            oakB.Add(Matrix4x4.TRS(U(p.x, p.h - 0.2, p.z), rot, new Vector3(s, s * (0.9f + (float)Scatter.R() * 0.3f), s)),
                Color.Lerp(Scatter.Hex(0x3e8623), Scatter.Hex(0x56a62b), (float)Scatter.R()));
        }
        oakB.Bake();

        cypB = new InstancedBatch(TreeFactory.Cypress(), foliageMat);
        foreach (var p in Scatter.Pick(cypN, (x, z, h) =>
            h > 5 && h < 260 && NV(x, z).y > 0.84 && ClearOfOverlook(x, z) && (Roads.At(pm, x, z) > 0.08 || NV(x, z).y < 0.92 ||
              System.Math.Sqrt((x - 20) * (x - 20) + (z + 30) * (z + 30)) < 110)))
        {
            float s = 1.2f + (float)Scatter.R() * 0.7f;
            var rot = Quaternion.Euler((float)(Scatter.R() - .5) * 5.7f, (float)Scatter.R() * 360f, (float)(Scatter.R() - .5) * 5.7f);
            cypB.Add(Matrix4x4.TRS(U(p.x, p.h - 0.2, p.z), rot, new Vector3(s, s * (0.95f + (float)Scatter.R() * 0.4f), s)),
                Color.Lerp(Scatter.Hex(0x235f20), Scatter.Hex(0x387a2d), (float)Scatter.R()));
        }
        cypB.Bake();

        pineB = new InstancedBatch(TreeFactory.Pine(), foliageMat);
        foreach (var p in Scatter.Pick(pineN, (x, z, h) =>
            h > 90 && h < 320 && NV(x, z).y > 0.76 && Roads.At(pm, x, z) < 0.15 && ClearOfOverlook(x, z)))
        {
            float s = 1.4f + (float)Scatter.R() * 0.9f;
            var rot = Quaternion.Euler((float)(Scatter.R() - .5) * 6.9f, (float)Scatter.R() * 360f, (float)(Scatter.R() - .5) * 6.9f);
            pineB.Add(Matrix4x4.TRS(U(p.x, p.h - 0.2, p.z), rot, new Vector3(s, s * (0.9f + (float)Scatter.R() * 0.3f), s)),
                Color.Lerp(Scatter.Hex(0x1e4f2a), Scatter.Hex(0x2d6e3c), (float)Scatter.R()));
        }
        pineB.Bake();

        // ivy on cliff lips
        ivyB = new InstancedBatch(TreeFactory.Ivy(), foliageMat);
        int placed = 0;
        for (int g = 0; placed < ivyN && g < 40000; g++)
        {
            var t = Tiers[(int)(Scatter.R() * Tiers.Length)];
            double a = Scatter.R() * P2;
            if (DA(a, t.th) < 0.8) continue;
            double r = Re(t, a) + (Scatter.R() * 2 - 1) * 3.5;
            double x = t.cx + r * System.Math.Sin(a), z = t.cz - r * System.Math.Cos(a);
            double h = H(x, z);
            var nv = NV(x, z);
            if (nv.y >= 0.62) continue;
            Vector3 pos = U(x, h, z);
            Vector3 nU = new Vector3(nv.x, nv.y, MirrorZ ? -nv.z : nv.z);
            var rot = Quaternion.LookRotation((pos + nU * 6 + new Vector3(0, -2, 0) - pos).normalized, Vector3.up);
            float s = 1.8f + (float)Scatter.R() * 1.6f;
            ivyB.Add(Matrix4x4.TRS(pos + nU * 0.8f, rot, new Vector3(s, 1.5f * s, 0.7f * s)),
                Color.Lerp(Scatter.Hex(0x2f6b1b), Scatter.Hex(0x489429), (float)Scatter.R()));
            placed++;
        }
        ivyB.Bake();

        grassB = new InstancedBatch(TreeFactory.GrassMesh(), grassMat) { castShadows = false };
        foreach (var p in Scatter.Pick(grassN, (x, z, h) => h > 4.2 && Roads.At(pm, x, z) < 0.1 && NV(x, z).y > 0.90))
        {
            float s = (0.9f + (float)Scatter.R() * 0.7f) * 1.4f;
            var rot = Quaternion.Euler(0, (float)Scatter.R() * 360f, 0);
            grassB.Add(Matrix4x4.TRS(U(p.x, p.h - 0.06, p.z), rot, new Vector3(s, s * (0.85f + (float)Scatter.R() * 0.5f), s)),
                Color.Lerp(Scatter.Hex(0x6eb53e), Scatter.Hex(0x458723), (float)Scatter.R()));
        }
        grassB.Bake();
    }

    // ---------- village ----------
    readonly List<Vector2> houseXZ = new();
    bool HouseSpot(double x, double z, double h, float[] pm, double hMin, double hMax)
    {
        if (h < hMin || h > hMax || NV(x, z).y <= 0.92 || Roads.At(pm, x, z) >= 0.1) return false;
        foreach (var q in houseXZ)
        {
            double dx = q.x - x, dz = q.y - z;
            if (dx * dx + dz * dz < 7.5 * 7.5) return false;
        }
        return true;
    }

    void BuildVillage(float[] pm)
    {
        var wallCols = new[] { 0xf4eee1, 0xebe3d3, 0xf6f3e8, 0xdce9f0 };
        var roofCols = new[] { 0xc85332, 0xd47743, 0x3d6e8f, 0x2b857a, 0xd9a441 };
        var bodyMesh = MakeBox(); var roofMesh = TreeFactory.Cone(0.7071f, 1f, 4);
        var chimMesh = MakeBox(); var winMesh = MakeBox();
        // foliageMat clones for instanced colored parts — reuse white base, per-instance color carries tint
        bodyB = new InstancedBatch(bodyMesh, foliageMat);
        roofB = new InstancedBatch(roofMesh, foliageMat);
        chimB = new InstancedBatch(chimMesh, foliageMat);
        winB = new InstancedBatch(winMesh, windowMat);
        int idx = 0;
        // meadow village
        for (int g = 0; houseXZ.Count < 38 && g < 12000; g++)
        {
            double a = Scatter.R() * P2, r = System.Math.Sqrt(Scatter.R()) * 65;
            double x = 20 + r * System.Math.Cos(a), z = -35 + r * System.Math.Sin(a);
            double h = H(x, z);
            if (!HouseSpot(x, z, h, pm, 60, 145)) continue;
            double w = System.Math.Atan2(20 - x, -35 - z) + (Scatter.R() - 0.5) * 0.7;
            float k = 0.85f + (float)Scatter.R() * 0.35f;
            AddHouse(x, h, z, w, k, idx++, wallCols, roofCols);
        }
        // harbor hamlet
        for (int g = 0; houseXZ.Count < 52 && g < 8000; g++)
        {
            double a = Scatter.R() * P2, r = System.Math.Sqrt(Scatter.R()) * 40;
            double x = 10 + r * System.Math.Cos(a), z = -180 + r * System.Math.Sin(a);
            double h = H(x, z);
            if (!HouseSpot(x, z, h, pm, 2.5, 24)) continue;
            double w = System.Math.Atan2(10 - x, -180 - z) + (Scatter.R() - 0.5) * 0.7;
            float k = 0.85f + (float)Scatter.R() * 0.3f;
            AddHouse(x, h, z, w, k, idx++, wallCols, roofCols);
        }
        bodyB.Bake(); roofB.Bake(); chimB.Bake(); winB.Bake();
        BuildSmoke();
    }

    Mesh MakeBox()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return m;
    }

    void AddHouse(double x, double h, double z, double w, float k, int i, int[] wallCols, int[] roofCols)
    {
        houseXZ.Add(new Vector2((float)x, (float)z));
        float yawU = YawToUnity(w);
        var rot = Quaternion.Euler(0, yawU, 0);
        Vector3 base_ = U(x, h, z);
        bodyB.Add(Matrix4x4.TRS(base_ + rot * new Vector3(0, 1.6f, 0), rot, new Vector3(4.6f * k, 3.2f, 3.8f * k)), Scatter.Hex((uint)wallCols[i % 4]));
        var roofRot = rot * Quaternion.Euler(0, 45, 0);
        roofB.Add(Matrix4x4.TRS(base_ + rot * new Vector3(0, 4.3f, 0), roofRot, new Vector3(5.8f * k, 2.6f, 5.0f * k)), Scatter.Hex((uint)roofCols[(3 * i + 1) % 5]));
        Vector3 coff = rot * new Vector3((float)(System.Math.Cos(w) * 1.3 + System.Math.Sin(w) * 0.6), 4.8f, (float)(-System.Math.Sin(w) * 1.3 + System.Math.Cos(w) * 0.6));
        // note: offset already rotated; simpler: use local offset via rot
        chimB.Add(Matrix4x4.TRS(base_ + coff, rot, new Vector3(0.7f, 1.8f, 0.7f)), Scatter.Hex(0x8a837c));
        float d = 1.95f * k;
        foreach (float j in new[] { -1.2f, 1.2f })
        {
            Vector3 wo = rot * new Vector3((float)(System.Math.Sin(w) * d + System.Math.Cos(w) * j), 1.8f, (float)(System.Math.Cos(w) * d - System.Math.Sin(w) * j));
            winB.Add(Matrix4x4.TRS(base_ + wo, rot, new Vector3(0.85f, 0.9f, 0.15f)), Color.white);
        }
    }

    void BuildSmoke()
    {
        if (houseXZ.Count == 0) return;
        var go = new GameObject("ChimneySmoke");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.maxParticles = 400; main.startLifetime = 8.3f; main.startSize = 1.2f; main.startSpeed = 0;
        main.startColor = new Color(0.94f, 0.96f, 0.97f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize3D = false;
        var emission = ps.emission; emission.rateOverTime = 40;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.5f;
        var vel = ps.velocityOverLifetime; vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(1.2f); vel.y = new ParticleSystem.MinMaxCurve(2.2f); vel.z = new ParticleSystem.MinMaxCurve(0.4f);
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(0.5f, 2.6f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0.45f, 0), new GradientAlphaKey(0f, 1) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = SoftAdditiveMat(new Color(0.94f, 0.96f, 0.97f, 0.45f));
        // place at village center; particles drift up globally (approximation of per-chimney emitters)
        double cx = 0, cz = 0; foreach (var q in houseXZ) { cx += q.x; cz += q.y; }
        cx /= houseXZ.Count; cz /= houseXZ.Count;
        go.transform.position = U(cx, H(cx, cz) + 6, cz);
    }

    // ---------- windmill + chapel ----------
    void BuildWindmillChapel()
    {
        double wx = 52, wz = -22; float wh = Hf(wx, wz);
        var root = new GameObject("Windmill"); root.transform.position = U(wx, wh, wz);
        var baseMesh = TreeFactory.Cyl(2.4f, 3.4f, 9.5f, 8);
        PropMesh(baseMesh, Scatter.Hex(0xf5f3ea), root.transform.position + new Vector3(0, 4.75f, 0), Quaternion.identity, Vector3.one, "Base", root.transform);
        PropMesh(TreeFactory.Cone(2.6f, 3.2f, 8), Scatter.Hex(0xb55335), root.transform.position + new Vector3(0, 10.8f, 0), Quaternion.identity, Vector3.one, "Cap", root.transform);
        var hub = new GameObject("Hub"); hub.transform.SetParent(root.transform, false);
        hub.transform.localPosition = new Vector3(0, 8.8f, -2.7f);
        windmillHub = hub.transform;
        var armMesh = MakeBox(); var sailMesh = MakeBox();
        for (int q = 0; q < 4; q++)
        {
            var arm = new GameObject($"Arm{q}"); arm.transform.SetParent(hub.transform, false);
            arm.transform.localRotation = Quaternion.Euler(0, 0, q * 90);
            PropMesh(armMesh, Scatter.Hex(0x4d3826), Vector3.zero, Quaternion.identity, new Vector3(0.25f, 8.5f, 0.15f), $"ArmM{q}", arm.transform);
            PropMesh(sailMesh, Scatter.Hex(0xfdfaf2), new Vector3(0.65f, 1.2f, 0), Quaternion.identity, new Vector3(1.2f, 6.5f, 0.05f), $"Sail{q}", arm.transform);
            // fix local positions: PropMesh used world pos; reparent correctly
            arm.transform.localPosition = Vector3.zero;
        }
        // chapel at (12,h,-45) yaw 0.45
        double chx = 12, chz = -45; float chh = Hf(chx, chz);
        var ch = new GameObject("Chapel"); ch.transform.position = U(chx, chh, chz);
        ch.transform.rotation = Quaternion.Euler(0, YawToUnity(0.45), 0);
        PropPrim(PrimitiveType.Cube, Scatter.Hex(0xf4eee2), ch.transform.position + ch.transform.rotation * new Vector3(0, 3.25f, 0), ch.transform.rotation, new Vector3(9, 6.5f, 14), "Nave", ch.transform);
        var naveRoof = PropMesh(TreeFactory.Cone(7.2f, 4.5f, 4), Scatter.Hex(0x3d6682), ch.transform.position + ch.transform.rotation * new Vector3(0, 8.2f, 0), ch.transform.rotation * Quaternion.Euler(0, 45, 0), new Vector3(1, 1, 1.5f), "NaveRoof", ch.transform);
        PropPrim(PrimitiveType.Cube, Scatter.Hex(0xeae2d4), ch.transform.position + ch.transform.rotation * new Vector3(5.5f, 8, -4.5f), ch.transform.rotation, new Vector3(4.2f, 16, 4.2f), "BellTower", ch.transform);
        PropMesh(TreeFactory.Cone(3.0f, 6.5f, 8), Scatter.Hex(0xc45638), ch.transform.position + ch.transform.rotation * new Vector3(5.5f, 8 + 11.2f, -4.5f), ch.transform.rotation, Vector3.one, "SpireRoof", ch.transform);
    }

    // ---------- spire props ----------
    void BuildSpireProps()
    {
        double tx = SPIRE_X + 9, tz = SPIRE_Z + 7; float th = Hf(tx, tz);
        var trav = new GameObject("Traveler"); trav.transform.position = U(tx, th, tz);
        Vector3 lookSrc = new Vector3(10, 120, -10);
        Vector3 tp = trav.transform.position;
        Vector3 lookU = U(lookSrc.x, lookSrc.y, lookSrc.z);
        Vector3 fdir = lookU - tp; fdir.y = 0; if (fdir.sqrMagnitude < 0.001f) fdir = Vector3.forward;
        trav.transform.rotation = Quaternion.LookRotation(fdir.normalized, Vector3.up);
        PropMesh(TreeFactory.Cyl(0.35f, 0.45f, 1.4f, 8), Scatter.Hex(0x1f4e79), tp + trav.transform.rotation * new Vector3(0, 1.2f, 0), trav.transform.rotation, Vector3.one, "Body", trav.transform);
        PropPrim(PrimitiveType.Sphere, Scatter.Hex(0xf5caa2), tp + trav.transform.rotation * new Vector3(0, 2.15f, 0), Quaternion.identity, Vector3.one * 0.72f, "Head", trav.transform);
        PropPrim(PrimitiveType.Cube, Scatter.Hex(0x8b4513), tp + trav.transform.rotation * new Vector3(0, 1.3f, 0.4f), trav.transform.rotation, new Vector3(0.65f, 0.85f, 0.45f), "Pack", trav.transform);
        // lone tree
        double lx = SPIRE_X - 10, lz = SPIRE_Z - 6; float lh = Hf(lx, lz);
        var lt = new GameObject("LoneTree"); lt.transform.position = U(lx, lh, lz);
        PropMesh(TreeFactory.Cyl(0.5f, 0.8f, 6.5f, 7), Scatter.Hex(0x5a3e28), lt.transform.position + new Vector3(0, 3.25f, 0), Quaternion.Euler(0, 0, 0.25f), Vector3.one, "Trunk", lt.transform);
        PropMesh(TreeFactory.Lobe(new Vector3(3.8f, 3.8f, 3.8f)), Scatter.Hex(0x3d8c22), lt.transform.position + new Vector3(1.5f, 7.2f, 0.2f), Quaternion.identity, new Vector3(1.2f, 0.9f, 1.2f), "Crown", lt.transform);
    }

    // ---------- harbor + boats ----------
    void BuildHarborAndBoats()
    {
        double dz0 = Roads.Dz0;
        PropPrim(PrimitiveType.Cube, Scatter.Hex(0x936e45), U(15, 1.4, dz0 - 18), Quaternion.identity, new Vector3(6.5f, 0.5f, 52), "PierDeck");
        var pileMesh = TreeFactory.Cyl(0.35f, 0.35f, 7, 8);
        for (int i = 0; i <= 11; i++) foreach (float s in new[] { -2.8f, 2.8f })
            PropMesh(pileMesh, Scatter.Hex(0x5a3e24), U(15 + s, -0.6, dz0 + 6 - i * 4.5), Quaternion.identity, Vector3.one, "Piling");
        var defs = new (double x, double z, float sc, bool sail)[] {
            (25, dz0 - 12, 1.2f, false), (6, dz0 - 32, 1.1f, false), (120, -280, 1.8f, true), (-180, -320, 1.6f, true) };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            var g = new GameObject($"Boat{i}");
            g.transform.position = U(d.x, 0.05, d.z); g.transform.localScale = Vector3.one * d.sc;
            PropPrim(PrimitiveType.Cube, Scatter.Hex(0x6e4325), g.transform.position, g.transform.rotation, new Vector3(3.0f, 1.3f, 9.2f), "Hull", g.transform);
            PropMesh(TreeFactory.Cyl(0.12f, 0.16f, 12, 6), Scatter.Hex(0x4a2c16), g.transform.position + new Vector3(0, 6.0f, 0), Quaternion.identity, Vector3.one, "Mast", g.transform);
            var sail = PropMesh(TreeFactory.Cone(5.2f, 5.2f, 4), Scatter.Hex(0xfff6e5), g.transform.position + new Vector3(0, 7.2f, 0.5f), Quaternion.identity, new Vector3(1, 1, 0.1f), "Sail", g.transform);
            sail.GetComponent<MeshRenderer>().sharedMaterial = ToonMat(Scatter.Hex(0xfff6e5), 0, false, 0);
            boats.Add(new BoatRef { t = g.transform, idx = i, sailing = d.sail, sc = d.sc });
        }
    }

    // ---------- citadel ----------
    void BuildCitadel()
    {
        double cx = Tiers[3].cx, cz = Tiers[3].cz; float yt = Hf(cx, cz);
        var g = new GameObject("Citadel"); g.transform.position = U(cx, yt, cz);
        var stone = Scatter.Hex(0xf2f2e9);
        PropPrim(PrimitiveType.Cube, stone, g.transform.position + new Vector3(0, 7, 0), Quaternion.identity, new Vector3(22, 14, 22), "Body", g.transform);
        foreach (float bx in new[] { -9.5f, 0f, 9.5f }) foreach (float bz in new[] { -9.5f, 0f, 9.5f })
        {
            if (bx == 0 && bz == 0) continue;
            PropPrim(PrimitiveType.Cube, stone, g.transform.position + new Vector3(bx, 15.3f, bz), Quaternion.identity, new Vector3(3.2f, 2.6f, 3.2f), "Bat", g.transform);
        }
        PropMesh(TreeFactory.Cyl(5.2f, 5.8f, 14, 16), stone, g.transform.position + new Vector3(0, 21, 0), Quaternion.identity, Vector3.one, "Tower", g.transform);
        PropMesh(TreeFactory.Cyl(3.8f, 3.8f, 4.5f, 16), Scatter.Hex(0xfff0a8), g.transform.position + new Vector3(0, 30.25f, 0), Quaternion.identity, Vector3.one, "Beacon", g.transform);
        PropMesh(TreeFactory.Cone(5.5f, 8.5f, 16), Scatter.Hex(0x6e96be), g.transform.position + new Vector3(0, 36.75f, 0), Quaternion.identity, Vector3.one, "Roof", g.transform);
        // beams pivot
        var piv = new GameObject("Beams"); piv.transform.position = g.transform.position + new Vector3(0, 30.25f, 0);
        beamPivot = piv.transform;
        var beamMesh = TreeFactory.Cone(12, 180, 16);
        for (int k = 0; k < 2; k++)
        {
            var b = new GameObject($"Beam{k}");
            b.transform.SetParent(piv.transform, false);
            b.transform.localRotation = Quaternion.Euler(90, 0, k * 180);
            b.transform.localPosition = Quaternion.Euler(0, k * 180, 0) * new Vector3(0, 0, 90);
            var mf = b.AddComponent<MeshFilter>(); mf.sharedMesh = beamMesh;
            var mr = b.AddComponent<MeshRenderer>(); mr.sharedMaterial = beamMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        mood.beamMat = beamMat;
    }

    // ---------- waterfall (quad only; mist is particles and stays disabled in bare mode) ----------
void BuildWaterfallQuad()
    {
        // Legacy hard-coded quad removed: it floated in mid-air because it ignored the terrain.
        // Waterfalls are now generated by WaterBuilder from the carved river channels, so they always
        // start on the river lip and follow the cliff down to the plunge pool.
        var old = GameObject.Find("Waterfall");
        if (old) Destroy(old);
    }

void BuildWaterfall()
    {
        BuildWaterfallQuad();   // just cleans up any legacy object; real falls + mist come from WaterBuilder
    }

    // ---------- clouds, birds, motes ----------
    void BuildCloudsBirdsMotes()
    {
        var defs = new (double x, double y, double z, float sc, int n)[] {
            (-780, 150, -560, 1.8f, 24), (860, 160, -480, 1.9f, 26), (-720, 110, 720, 1.7f, 22),
            (750, 95, 780, 1.8f, 24), (50, 420, -60, 1.5f, 20) };
        clusters = new CloudCluster[defs.Length];
        var sphereMesh = MakeSphere();
        for (int ci = 0; ci < defs.Length; ci++)
        {
            var d = defs[ci];
            var root = new GameObject($"Cloud{ci}"); root.transform.position = U(d.x, 0, d.z);
            float ang = Mathf.Atan2((float)d.x, (float)d.z);
            float rad = Mathf.Sqrt((float)(d.x * d.x + d.z * d.z));
            float spd = 0.8f + (float)Scatter.R() * 1.2f;
            for (int i = 0; i < d.n; i++)
            {
                int tier = i < 0.35 * d.n ? 0 : i < 0.7 * d.n ? 1 : 2;
                float[] br = { 20, 16, 12 }, bv = { 10, 8, 6 };
                float r = (br[tier] + (float)Scatter.R() * bv[tier]) * d.sc;
                Vector3 lp = new Vector3(
                    ((float)Scatter.R() - 0.5f) * (tier == 2 ? 60 : 100) * d.sc,
                    (tier * 20 + (float)Scatter.R() * 10) * d.sc * 0.85f + (float)d.y,
                    ((float)Scatter.R() - 0.5f) * (tier == 2 ? 55 : 90) * d.sc);
                var s = new GameObject($"Puff{i}");
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = lp - root.transform.position + new Vector3(0, (float)d.y, 0);
                s.transform.localPosition = lp; s.transform.position = root.transform.position + lp;
                s.transform.localScale = Vector3.one * r * 2;
                var mf = s.AddComponent<MeshFilter>(); mf.sharedMesh = sphereMesh;
                var mr = s.AddComponent<MeshRenderer>(); mr.sharedMaterial = cloudMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            clusters[ci] = new CloudCluster { root = root.transform, radius = rad, angle = ang, speed = spd };
        }
        // birds
        float ytTop = Hf(35, 25);
        int nB = fullQuality ? 45 : 20;
        birds = new Bird[nB]; birdMats = new Matrix4x4[nB]; birdCols = new Vector4[nB];
        birdMesh = TreeFactory.BirdMesh();
        birdB = new InstancedBatch(birdMesh, birdMat) { castShadows = false };
        birdMpb = new MaterialPropertyBlock();
        for (int i = 0; i < nB; i++)
        {
            bool high = i < 26;
            float cx = high ? 35 : 15, cz = high ? 25 : -160;
            float r = high ? 80 + (float)Scatter.R() * 120 : 45 + (float)Scatter.R() * 70;
            float y = high ? ytTop + 45 + (float)Scatter.R() * 80 : 18 + (float)Scatter.R() * 45;
            float spd = (high ? 0.16f : 0.26f) * (0.8f + (float)Scatter.R() * 0.4f);
            int sign = Scatter.R() > 0.5 ? 1 : -1;
            birds[i] = new Bird { cx = cx, cz = cz, r = r, y = y, a0 = (float)Scatter.R() * Mathf.PI * 2, spd = spd, ph = (float)Scatter.R() * 10, s = 2.0f, sign = sign };
            float g = 0.88f + (float)Scatter.R() * 0.12f;
            birdCols[i] = new Vector4(g, g, g, 1);
        }
        // motes
        var mg = new GameObject("Motes");
        var ps = mg.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.maxParticles = fullQuality ? 2400 : 800; main.startLifetime = 1e6f; main.startSpeed = 0;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = 1.2f; main.startColor = new Color(1f, 0.94f, 0.72f, 0.5f);
        var em = ps.emission; em.rateOverTime = 0;
        ps.Emit(fullQuality ? 2400 : 800);
        var parts = new ParticleSystem.Particle[fullQuality ? 2400 : 800];
        int got = ps.GetParticles(parts);
        for (int i = 0; i < got; i++)
        {
            double x = (Scatter.R() * 2 - 1) * 320, z = (Scatter.R() * 2 - 1) * 320;
            double h = H(x, z); if (h < 4) h = 4;
            parts[i].position = U(x, h + 2 + Scatter.R() * 32, z);
            parts[i].startSize = 0.8f + (float)(parts[i].position.x * 7 % 1) * 1.5f;
            if (parts[i].startSize < 0.5f) parts[i].startSize = 1f;
            parts[i].startLifetime = 1e6f; parts[i].remainingLifetime = 1e6f;
        }
        ps.SetParticles(parts, got);
        var noise = ps.noise; noise.enabled = true; noise.strength = 5; noise.frequency = 0.08f; noise.scrollSpeed = 0.35f;
        var pr = mg.GetComponent<ParticleSystemRenderer>();
        pr.material = SoftAdditiveMat(new Color(1f, 0.94f, 0.72f, 0.5f));
    }

    static Texture2D softDot;
    public static Material SoftAdditiveMat(Color c)
    {
        if (!softDot)
        {
            softDot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                softDot.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
            softDot.Apply();
        }
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        m.SetTexture("_BaseMap", softDot);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2);
        m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 1); m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
        m.renderQueue = 3000;
        return m;
    }

    Mesh MakeSphere()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return m;
    }

    // ---------- player ----------
    void BuildPlayer()
    {
        double sx = 20, sz = -35;
        playerGO = new GameObject("Explorer");
        playerGO.transform.position = U(sx, H(sx, sz), sz);
        playerBody = new GameObject("Body").transform; playerBody.SetParent(playerGO.transform, false);
        playerBody.localScale = Vector3.one * 1.6f;
        PropMesh(TreeFactory.Cyl(0.36f, 0.44f, 1.1f, 8), Scatter.Hex(0xd9534a), Vector3.zero, Quaternion.identity, Vector3.one, "Torso", playerBody).transform.localPosition = new Vector3(0, 1.15f, 0);
        PropPrim(PrimitiveType.Sphere, Scatter.Hex(0xf2c9a0), new Vector3(0, 1.95f, 0), Quaternion.identity, Vector3.one * 0.7f, "Head", playerBody).transform.localPosition = new Vector3(0, 1.95f, 0);
        PropMesh(TreeFactory.Cyl(0.42f, 0.42f, 0.1f, 10), Scatter.Hex(0xf2c14e), Vector3.zero, Quaternion.identity, Vector3.one, "Brim", playerBody).transform.localPosition = new Vector3(0, 2.2f, 0);
        PropMesh(TreeFactory.Cone(0.3f, 0.4f, 8), Scatter.Hex(0xf2c14e), Vector3.zero, Quaternion.identity, Vector3.one, "Cone", playerBody).transform.localPosition = new Vector3(0, 2.45f, 0);
        PropPrim(PrimitiveType.Cube, Scatter.Hex(0x3a6ea5), new Vector3(0, 1.25f, 0.5f), Quaternion.identity, new Vector3(0.7f, 0.8f, 0.35f), "Pack", playerBody).transform.localPosition = new Vector3(0, 1.25f, 0.5f);
        foreach (float ex in new[] { -0.18f, 0.18f })
            PropPrim(PrimitiveType.Sphere, Color.black, new Vector3(ex, 2.0f, -0.32f), Quaternion.identity, Vector3.one * 0.1f, "Eye", playerBody).transform.localPosition = new Vector3(ex, 2.0f, -0.32f);
        legList.Clear();
        foreach (float lx in new[] { -0.2f, 0.2f })
        {
            var hip = new GameObject("Hip"); hip.transform.SetParent(playerGO.transform, false); hip.transform.localPosition = new Vector3(lx * 1.6f, 0.65f * 1.6f, 0);
            var leg = PropPrim(PrimitiveType.Cube, Scatter.Hex(0x2f3b52), Vector3.zero, Quaternion.identity, new Vector3(0.25f, 0.65f, 0.3f), "Leg", hip.transform);
            leg.transform.localPosition = new Vector3(0, -0.3f, 0);
            legList.Add(hip.transform);
        }
        if (director) director.player = playerGO.transform;
    }

    // ---------- camera presets ----------
    void SetupCameraPresets()
    {
        float ytTop = Hf(35, 25); double dz0 = Roads.Dz0;
        Vector3[] P = {
            U(SPIRE_X - 16, SPIRE_TOP_Y + 12, SPIRE_Z - 28),
            U(58, 118, -6), U(42, 28, dz0 + 45), U(35 + 55, ytTop + 65, 25 + 55), U(-480, 260, -420) };
        Vector3[] T = {
            U(10, 130, -10), U(20, 95, -35), U(15, 6, dz0 - 18), U(35, ytTop + 24, 25), U(0, 110, 20) };
        director.SetOrbitFrom(bareIsland ? P[4] : P[0], bareIsland ? T[4] : T[0]);
        Vector3[] TP = { U(-191,190,-163), U(30,45,-210), U(180,135,-50), U(90,210,120), U(-40,280,90), U(-240,220,-110) };
        Vector3[] TT = { U(10,130,-10), U(15,10,-180), U(20,100,-35), U(25,190,30), U(35,ytTop+15,25), U(0,120,0) };
        director.SetTourPath(TP, TT);
    }

    // per-frame ----------
    void Update()
    {
        oakB?.Draw(); cypB?.Draw(); pineB?.Draw(); ivyB?.Draw(); grassB?.Draw();
        bodyB?.Draw(); roofB?.Draw(); chimB?.Draw(); winB?.Draw();
        float t = Time.time;
        if (windmillHub) windmillHub.localRotation = Quaternion.Euler(0, 0, -t * 0.85f * Mathf.Rad2Deg * 0.05f - t * 0.85f * 10f * 0.02f);
        if (windmillHub) windmillHub.localRotation = Quaternion.Euler(0, 0, -t * 0.85f * Mathf.Rad2Deg);
        if (beamPivot) beamPivot.rotation = Quaternion.Euler(0, t * 0.6f * Mathf.Rad2Deg, 0);
        // birds
        if (birds != null && birdB != null)
        {
            for (int i = 0; i < birds.Length; i++)
            {
                var b = birds[i];
                float a = b.a0 + t * b.spd * b.sign;
                Vector3 ps = U(b.cx + b.r * System.Math.Cos(a), b.y + System.Math.Sin(0.4f * t + b.ph) * 6, b.cz + b.r * System.Math.Sin(a));
                Vector3 fwd = new Vector3((float)(-System.Math.Sin(a) * b.sign), 0, (float)(System.Math.Cos(a) * b.sign));
                if (MirrorZ) fwd.z = -fwd.z;
                var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(0, 0, -b.sign * 0.3f * Mathf.Rad2Deg * 0.05f);
                birdMats[i] = Matrix4x4.TRS(ps, rot, Vector3.one * b.s);
            }
            birdMpb ??= new MaterialPropertyBlock();
            birdMpb.SetVectorArray("_InstColor", birdCols);
            Graphics.DrawMeshInstanced(birdMesh, 0, birdMat, birdMats, birds.Length, birdMpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
        // boats
        foreach (var b in boats)
        {
            if (!b.sailing)
            {
                var p = b.t.position; p.y = 0.05f + Mathf.Sin(t * 1.3f + b.idx) * 0.14f;
                b.t.position = p;
                b.t.rotation = Quaternion.Euler(0, b.t.rotation.eulerAngles.y, Mathf.Sin(t * 0.9f + b.idx) * 0.035f * Mathf.Rad2Deg);
            }
            else
            {
                float a = t * 0.02f + b.idx * 2;
                double xs = 260 * System.Math.Sin(a), zs = -260 * System.Math.Cos(a);
                b.t.position = U(xs, 0.05 + Mathf.Sin(t * 1.3f + b.idx) * 0.14f, zs);
                double yaw = System.Math.Atan2(System.Math.Cos(a), System.Math.Sin(a));
                b.t.rotation = Quaternion.Euler(0, YawToUnity(yaw), Mathf.Sin(t * 0.9f + b.idx) * 0.035f * Mathf.Rad2Deg);
            }
        }
        // clouds drift
        if (clusters != null)
            for (int i = 0; i < clusters.Length; i++)
            {
                var c = clusters[i];
                c.angle += c.speed * 0.0003f * 60 * Time.deltaTime;
                c.root.position = new Vector3(Mathf.Sin(c.angle) * c.radius, c.root.position.y, (MirrorZ ? -1 : 1) * Mathf.Cos(c.angle) * c.radius);
                clusters[i] = c;
            }
        // explorer walk / fly (Play mode)
        if (director && director.mode == CameraDirector.Mode.Play && playerGO)
        {
            // Double-tap Space to toggle fly mode.
            // flyTimeSinceLastSpace counts up each frame; resets on each Space press.
            flyTimeSinceLastSpace += Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                // A second press within 0.5 s of the first counts as a double-tap.
                if (flyTimeSinceLastSpace < 0.5f)
                {
                    flyMode = !flyMode;
                    director.EnterPlay(flyMode);
                    Debug.Log(flyMode ? "Fly mode enabled" : "Fly mode disabled");
                }
                // Reset the inter-press timer on every Space press.
                flyTimeSinceLastSpace = 0f;
            }

            if (flyMode)
            {
                // First-person fly controls
                float forward = Input.GetAxisRaw("Vertical");
                float right = Input.GetAxisRaw("Horizontal");
                float up = 0f;
                
                // Space to go up, Left Ctrl to go down
                if (Input.GetKey(KeyCode.Space)) up += 1f;
                if (Input.GetKey(KeyCode.LeftControl)) up -= 1f;

                float L = Mathf.Min(1, Mathf.Abs(forward) + Mathf.Abs(right));
                float yaw = director.PlayYaw;
                if (L > 0.05f)
                {
                    float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
                    Vector3 dir = new Vector3(-s * forward + c * right, up, -c * forward - s * right).normalized;
                    float speed = 24f * L;
                    playerGO.transform.position += dir * speed * Time.deltaTime;
                    float tgt = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    playerGO.transform.rotation = Quaternion.Slerp(playerGO.transform.rotation, Quaternion.Euler(0, tgt, 0), Mathf.Min(1, Time.deltaTime * 12));
                }
            }
            else
            {
                // Original explorer walk
                Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                float L = Mathf.Min(1, input.magnitude);
                float yaw = director.PlayYaw;
                if (L > 0.05f)
                {
                    float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
                    Vector3 dir = new Vector3(-s * input.y + c * input.x, 0, -c * input.y - s * input.x).normalized;
                    float speed = (Input.GetKey(KeyCode.LeftShift) ? 32f : 18f) * L;
                    playerGO.transform.position += dir * speed * Time.deltaTime;
                    float tgt = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    playerGO.transform.rotation = Quaternion.Slerp(playerGO.transform.rotation, Quaternion.Euler(0, tgt, 0), Mathf.Min(1, Time.deltaTime * 12));
                    for (int li = 0; li < legList.Count; li++)
                        legList[li].localRotation = Quaternion.Euler(Mathf.Sin(t * 12f + li * Mathf.PI) * 0.8f * Mathf.Rad2Deg * 0.4f, 0, 0);
                }
                double hx = playerGO.transform.position.x, hz = MirrorZ ? -playerGO.transform.position.z : playerGO.transform.position.z;
                float ground = Hf(hx, hz);
                Vector3 pp = playerGO.transform.position;
                pp.y += (ground - pp.y) * (1 - Mathf.Exp(-Time.deltaTime * 20f));
                playerGO.transform.position = pp;
            }
        }
    }

    // fly mode state is declared at the top of the class (lines 26-28); removed duplicates.

    // simple UI hooks for buttons
    public void FlyToPreset(int i)
    {
        float ytTop = Hf(35, 25); double dz0 = Roads.Dz0;
        Vector3[] P = {
            U(SPIRE_X - 16, SPIRE_TOP_Y + 12, SPIRE_Z - 28),
            U(58, 118, -6), U(42, 28, dz0 + 45), U(35 + 55, ytTop + 65, 25 + 55), U(-480, 260, -420) };
        Vector3[] T = {
            U(10, 130, -10), U(20, 95, -35), U(15, 6, dz0 - 18), U(35, ytTop + 24, 25), U(0, 110, 20) };
        i = Mathf.Clamp(i, 0, 4);
        director.FlyTo(P[i], T[i]);
    }

    void BuildPhase5Village(float[] pm)
    {
        // Ensure the phase 5 settlement-layout data is built (positions, yaws,
        // footprints, floor counts). We no longer use ExteriorDetailSystem's
        // output here -- the real modular kit's door/window wall prefabs
        // already carry correct openings, so there's nothing for an abstract
        // "detail" layer to add on top without risking misalignment.
        if (!BuildingSystem.Built) BuildingSystem.Build();

        // Create a parent object for the village
        var villageRoot = new GameObject("Phase5Village");

        // Iterate over each building, replacing each one with a real modular
        // house built from Assets/FantasyMedievalTown_LITE/Prefabs/ (see
        // ModularHouseBuilder for the grid math). NOTE: this intentionally
        // ignores building.floors for geometry -- the LITE kit has no
        // second-story wall pieces, so every building becomes a correct
        // single-story house rather than a stacked/stretched approximation.
        // Footprints are snapped to whole 2.5m grid cells (clamped 2-4 cells
        // per axis) so every house is built from flush, non-overlapping
        // modules with no gaps, no overlaps, and no floating pieces.
        foreach (var building in BuildingSystem.Buildings)
        {
            if (building == null) continue;

            var (cols, rows) = ModularHouseBuilder.SnapFootprintToGrid(building.footprint);

            // Deterministic per-building variety: door on a chosen edge/slot,
            // a handful of windows scattered on the rest. Seeded by building
            // id so layout is stable across rebuilds in the same session.
            var rng = new System.Random(building.id * 7919 + 13);
            int doorEdge = rng.Next(4);
            int doorEdgeLen = (doorEdge % 2 == 0) ? cols : rows;
            int doorIdx = rng.Next(doorEdgeLen);
            int windowCount = Mathf.Clamp((cols * 2 + rows * 2) / 2, 1, 6);

            var hp = new ModularHouseBuilder.HouseParams
            {
                cols = cols,
                rows = rows,
                doorEdge = doorEdge,
                doorIndexOnEdge = doorIdx,
                windowCount = windowCount,
                seed = building.id
            };

            var buildingGo = ModularHouseBuilder.Build(
                $"Building_{building.id}",
                // Model space -> Unity space: Z is mirrored (TerrainMath.MirrorZ) and yaw flips sign.
                // Height comes from the RENDERED mesh (Hm), not the analytic field (Hf), so houses sit flush.
                TerrainMath.U(building.position.x, TerrainMath.Hm(building.position.x, building.position.z), building.position.z),
                TerrainMath.YawToUnity(building.yaw),
                hp);
            buildingGo.transform.SetParent(villageRoot.transform, true);
        }
    }
}
