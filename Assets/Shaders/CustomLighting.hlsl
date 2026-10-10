#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

// Main light data for Shader Graph (URP).
// Node function name: MainLight  (Shader Graph appends the _float suffix itself).
void MainLight_float(float3 WorldPos, out float3 Direction, out float3 Color,
                     out float DistanceAtten, out float ShadowAtten)
{
#if defined(SHADERGRAPH_PREVIEW)
    Direction     = float3(0.5, 0.5, 0.0);
    Color         = float3(1.0, 1.0, 1.0);
    DistanceAtten = 1.0;
    ShadowAtten   = 1.0;
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    Light mainLight    = GetMainLight(shadowCoord);
    Direction     = mainLight.direction;
    Color         = mainLight.color;
    DistanceAtten = mainLight.distanceAttenuation;
    ShadowAtten   = mainLight.shadowAttenuation;
#endif
}

// Half-precision variant
void MainLight_half(half3 WorldPos, out half3 Direction, out half3 Color,
                    out half DistanceAtten, out half ShadowAtten)
{
#if defined(SHADERGRAPH_PREVIEW)
    Direction     = half3(0.5, 0.5, 0.0);
    Color         = half3(1.0, 1.0, 1.0);
    DistanceAtten = 1.0;
    ShadowAtten   = 1.0;
#else
    half4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    Light mainLight   = GetMainLight(shadowCoord);
    Direction     = mainLight.direction;
    Color         = mainLight.color;
    DistanceAtten = mainLight.distanceAttenuation;
    ShadowAtten   = mainLight.shadowAttenuation;
#endif
}
#endif
