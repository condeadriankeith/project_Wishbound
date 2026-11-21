// Shared stylised water effects for Wishbound (ocean, river, waterfall, ponds).
//   WaterCaustics  - cel-shaded caustic patches (thresholded animated voronoi), brighter in the shallows
//   WaterGlints    - sparkling sun glints: a jittered grid of tiny stars that twinkle when they face the sun
//   WaterPointLights - the Star, lanterns and hearths reflect on the surface as banded highlights + glints
// Include after Lighting.hlsl (for the point lights) and add these pragmas to the pass:
//   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
//   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#ifndef WISHBOUND_WATER_FX
#define WISHBOUND_WATER_FX

float2 WfxHash2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }

// distance to the nearest and second-nearest animated cell point
float2 WfxVoronoi(float2 p, float t)
{
    float2 i = floor(p), f = frac(p); float d1 = 8, d2 = 8;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y); float2 o = WfxHash2(i + g);
        o = .5 + .45 * sin(t + 6.2831 * o);
        float d = length(g + o - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return float2(d1, d2);
}

// Cel caustic network: bright where cells meet. scale ~ 0.6-1.5 per metre. Returns 0..1 bands.
float WaterCaustics(float2 p, float t, float scale)
{
    float2 v1 = WfxVoronoi(p * scale, t * .8);
    float2 v2 = WfxVoronoi(p * scale * 1.7 + 3.1, -t * .6);
    float e = (1 - smoothstep(0.0, .14, v1.y - v1.x)) * .7 + (1 - smoothstep(0.0, .1, v2.y - v2.x)) * .45;
    return step(.42, e) * .7 + step(.75, e) * .3;   // two hard bands, painterly not photoreal
}

// Twinkling glints. n = surface normal, v = view dir (to camera), l = light dir (to light).
float WaterGlints(float2 p, float3 n, float3 v, float3 l, float t, float density)
{
    float2 cell = floor(p * density); float2 f = frac(p * density);
    float2 r = WfxHash2(cell);
    float2 c = .2 + .6 * r;
    float d = length(f - c);
    float tw = pow(saturate(sin(t * (2 + r.x * 3) + r.y * 40)), 6);
    float facing = pow(saturate(dot(reflect(-l, n), v)), 4);
    return smoothstep(.09, .0, d) * tw * facing * step(.55, r.x);
}

#if defined(UNIVERSAL_LIGHTING_INCLUDED)
// Point / spot lights (the Star, lanterns, hearths) reflected on the water: soft cel highlight + extra glints.
float3 WaterPointLights(float3 posWS, float3 n, float3 v, float4 posCS, float2 glintP, float t)
{
    float3 acc = 0;
    #if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = posWS; inputData.normalWS = n; inputData.viewDirectionWS = v;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(posCS);
    half4 shadowMask = half4(1, 1, 1, 1);
    AmbientOcclusionFactor ao; ao.indirectAmbientOcclusion = 1; ao.directAmbientOcclusion = 1;
    uint count = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light l = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        float s = pow(saturate(dot(reflect(-l.direction, n), v)), 24);
        acc += l.color * l.distanceAttenuation * (step(.35, s) * .6 + step(.75, s) * .6 + .12 + WaterGlints(glintP, n, v, l.direction, t, 5) * 2);
    }
    #endif
    LIGHT_LOOP_BEGIN(count)
        Light l2 = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        float s2 = pow(saturate(dot(reflect(-l2.direction, n), v)), 24);
        acc += l2.color * l2.distanceAttenuation * (step(.35, s2) * .6 + step(.75, s2) * .6 + .12 + WaterGlints(glintP, n, v, l2.direction, t, 5) * 2);
    LIGHT_LOOP_END
    #endif
    return acc;
}
#endif

#endif
