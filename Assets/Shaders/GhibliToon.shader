Shader "Island/GhibliToon"
{
    // Village/prop variant of the project's Ghibli/ToonLit look: same 4-band half-Lambert ramp, same brush-noise albedo
    // variation, same global hemisphere ambient (_HemiSky/_HemiGround/_HemiIntensity) and cool rim - so buildings sit in the
    // same painted world as the terrain and trees. Adds: texture/colour input, full pass set (shadows/depth), additional
    // lights (interior lamps) and soft ground-contact darkening.
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        [Header(Ramp)]
        _Softness("Band Edge Softness", Range(0.005, 0.2)) = 0.035
        _Ambient("Ambient (fallback if no global hemisphere)", Range(0, 1.5)) = 0.75

        [Header(Painterly)]
        _Saturation("Saturation", Range(0, 2)) = 1.05
        _BrushScale("Brush Scale", Float) = 0.45
        _BrushStrength("Brush Strength", Range(0, 0.5)) = 0.18
        _GroundDark("Ground Contact Darkening", Range(0, 1)) = 0.28
        _GroundFade("Ground Darkening Height", Range(0.2, 6)) = 1.4

        [Header(Rim)]
        _RimColor("Rim Colour", Color) = (0.35,0.52,0.75,1)
        _RimStrength("Rim Strength", Range(0, 1)) = 0.14
        _RimPower("Rim Power", Float) = 3.2

        [HDR] _EmissionColor("Emission", Color) = (0,0,0,0)
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Softness;
        half _Ambient;
        half _Saturation;
        float _BrushScale;
        half _BrushStrength;
        half _GroundDark;
        half _GroundFade;
        half4 _RimColor;
        half _RimStrength;
        half _RimPower;
        half4 _EmissionColor;
        half _Cull;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _HemiSky, _HemiGround;
            float  _HemiIntensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float  baseY      : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float hz(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float bn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hz(i), hz(i + float2(1, 0)), f.x), lerp(hz(i + float2(0, 1)), hz(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.baseY = TransformObjectToWorld(float3(0, 0, 0)).y;   // pivot height == ground contact for placed props
                return o;
            }

            // same stepped ramp as Ghibli/ToonLit (0.31 / 0.55 / 0.80 / 1.0), with a small anti-aliased edge
            half ToonRamp(half v)
            {
                half s = _Softness;
                return 0.3137
                     + (0.5490 - 0.3137) * smoothstep(0.25 - s, 0.25 + s, v)
                     + (0.8039 - 0.5490) * smoothstep(0.50 - s, 0.50 + s, v)
                     + (1.0000 - 0.8039) * smoothstep(0.75 - s, 0.75 + s, v);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 N = normalize(i.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 albedo = tex.rgb;
                half lum = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = saturate(lerp(lum.xxx, albedo, _Saturation));

                // painterly brush variation, projected so walls and roofs both get it (not just top-down)
                float br = bn(float2(i.positionWS.x + i.positionWS.z, i.positionWS.y) * _BrushScale * 1.4) * 0.6
                         + bn(i.positionWS.xz * 1.8 + i.positionWS.y * 0.7) * 0.4;
                albedo *= (1.0 - 0.5 * _BrushStrength) + _BrushStrength * br;

                // soft darkening at the base of every object so houses/props sit in the ground
                half h = saturate((i.positionWS.y - i.baseY) / _GroundFade);
                albedo *= lerp(1.0 - _GroundDark, 1.0, h);

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half v = saturate(dot(N, L.direction) * 0.5 + 0.5);
                half3 direct = L.color * ToonRamp(v) * L.shadowAttenuation;

                half3 hemi = (_HemiIntensity > 0.001)
                    ? lerp(_HemiGround.rgb, _HemiSky.rgb, N.y * 0.5 + 0.5) * _HemiIntensity
                    : SampleSH(N) * _Ambient;

                half3 col = albedo * (direct + hemi) + _EmissionColor.rgb;

                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalWS = N;
                    inputData.viewDirectionWS = V;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    half4 shadowMask = half4(1, 1, 1, 1);
                    AmbientOcclusionFactor aoFactor;
                    aoFactor.indirectAmbientOcclusion = 1;
                    aoFactor.directAmbientOcclusion = 1;

                    uint pixelLightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                        Light l = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                        col += albedo * l.color * ToonRamp(saturate(dot(N, l.direction) * 0.5 + 0.5)) * l.distanceAttenuation * l.shadowAttenuation;
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light l2 = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                        col += albedo * l2.color * ToonRamp(saturate(dot(N, l2.direction) * 0.5 + 0.5)) * l2.distanceAttenuation * l2.shadowAttenuation;
                    LIGHT_LOOP_END
                }
                #endif

                col += pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimColor.rgb * _RimStrength;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V ShadowVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V DepthVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half DepthFrag(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V DNVert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 DNFrag(V i) : SV_Target
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 n = normalize(i.normalWS);
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 r = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(r), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
