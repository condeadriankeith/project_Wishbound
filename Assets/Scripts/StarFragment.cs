using System;
using System.Collections;
using UnityEngine;

/// <summary>A miniature copy of the playable star, with its own corona and sparks.</summary>
internal sealed class StarFragment : MonoBehaviour
{
    public Renderer BodyRenderer { get; private set; }

    public static StarFragment Create(Transform parent, Vector3 world, float diameter)
    {
        var root = new GameObject("Star fragment");
        root.SetActive(false);
        root.transform.SetParent(parent, true);
        root.transform.position = world;
        var result = root.AddComponent<StarFragment>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(body.GetComponent<Collider>());
        body.name = "AmberStarBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = Vector3.one * diameter;
        result.BodyRenderer = body.GetComponent<Renderer>();
        var intro = WishboundIntro.Instance;
        var fx = root.AddComponent<StarOrbFX>();
        if (intro)
        {
            var playable = intro.starVisual ? intro.starVisual.GetComponent<StarOrbFX>() : null;
            var sourceBody = intro.starVisual ? intro.starVisual.Find("AmberStarBody") : null;
            if (sourceBody && sourceBody.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh)
                body.GetComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            var orb = intro.starVisual ? intro.starVisual.GetComponent<AmberOrbBody>() : null;
            float ratio = diameter / (orb ? Mathf.Max(.1f,orb.diameter) : .85f);
            fx.haloMaterial = playable && playable.haloMaterial ? playable.haloMaterial : intro.starHaloMaterial;
            if (playable)
            {
                fx.haloSize = playable.haloSize * ratio;
                fx.pulseSpeed = playable.pulseSpeed;
                fx.pulseDepth = playable.pulseDepth;
                fx.sparkColors = playable.sparkColors;
                fx.trailColors = playable.trailColors;
            }
            fx.bodyMaterial = playable && playable.bodyMaterial ? playable.bodyMaterial : intro.starBodyMaterial;
            fx.sparkMaterial = playable && playable.sparkMaterial ? playable.sparkMaterial : intro.sparkMaterial;
        }
        var light = root.AddComponent<Light>();
        light.type = LightType.Point;
        fx.glowLight = light;
        fx.lightIntensity = .40f;
        fx.lightRange = 1.65f;
        fx.radianceScale = .82f;
        fx.particleScale = diameter;
        root.SetActive(true);
        return result;
    }

    public static IEnumerator Merge(Transform fragment, Transform star, Action<float> beat = null, Func<bool> skip = null)
    {
        var orb = star.GetComponentInChildren<AmberOrbBody>();
        var fx = star.GetComponentInChildren<StarOrbFX>();
        var light = StarRadiance.Instance;
        WorldSound.Cue(fragment, "Fragment", 32f);
        Vector3 source = fragment.position;
        Vector3 scale = fragment.localScale;
        Vector3 delta = star.position - source;
        Vector3 side = Vector3.Cross(Vector3.up, delta.normalized);
        if (side.sqrMagnitude < .01f) side = star.right;
        float oldDiameter = orb ? orb.diameter : .85f;
        float oldPower = fx ? fx.FragmentPower : 0f;
        float oldFlare = light ? light.flare : 0f;
        Vector3 Core() => orb && orb.body ? orb.body.position : star.position + Vector3.up * .85f;

        for (float t = 0f; t < 2.35f && !(skip?.Invoke() ?? false); t += Time.deltaTime)
        {
            float x = Mathf.Clamp01(t / 2.35f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .77f, x));
            Vector3 curve = Vector3.up * (.48f * Mathf.Sin(k * Mathf.PI)) +
                side * Mathf.Sin(k * Mathf.PI * 2f) * (.24f * Mathf.Sin(k * Mathf.PI));
            fragment.position = Vector3.Lerp(source, Core(), k) + curve;
            fragment.localScale = scale * (1f - .92f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, .78f, x)));
            if (x >= .78f && fragment.gameObject.activeSelf)
            {
                fragment.gameObject.SetActive(false);
                if (orb) orb.Kick(.12f);
            }
            float absorb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.70f, .96f, x));
            if (orb) orb.diameter = oldDiameter + absorb * .062f;
            if (fx) fx.FragmentPower = oldPower + absorb;
            if (light) light.flare = Mathf.Lerp(oldFlare, 0f, x) + Mathf.Sin(absorb * Mathf.PI) * .45f;
            beat?.Invoke(x);
            yield return null;
        }
        fragment.position = Core();
        fragment.gameObject.SetActive(false);
        if (orb) orb.diameter = oldDiameter + .062f;
        if (fx) fx.FragmentPower = oldPower + 1f;
        if (light) { light.flare = 0f; light.missingPiece = Mathf.Max(0f, light.missingPiece - .25f); }
        beat?.Invoke(1f);
    }
}

