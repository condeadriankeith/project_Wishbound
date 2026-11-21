// Occlusion see-through feature removed. Plain opaque lit shader - trees render normally,
// no transparency, no camera->avatar hole, no dithering. Kept name/properties for material
// compatibility with existing assets that reference this shader.
Shader "Island/TreeOcclusionDither"
{
    Properties
    {
        _BaseMap ("Base Color", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _DitherFade ("Unused (feature removed)", Range(0,1)) = 0
        [Toggle] _UseTexture ("Use texture (off = flat tint, for bark layers with no single base map)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _DitherFade;
                float _UseTexture;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; float2 uv : TEXCOORD2; float fog : TEXCOORD4; };

            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                o.ws = ws;
                o.cs = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.n);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 texCol = _UseTexture > 0.5
                    ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv)
                    : half4(1,1,1,1);
                half3 albedo = texCol.rgb * _Tint.rgb;

                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float3 n = normalize(i.n);
                float ndotl = saturate(dot(n, l.direction) * 0.5 + 0.5);
                float shade = lerp(0.55, 1.0, ndotl) * lerp(0.6, 1.0, l.shadowAttenuation);
                half3 c = albedo * shade;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
