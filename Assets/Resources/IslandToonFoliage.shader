Shader "Island/ToonFoliage"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.38,0.42,0.62,1)
        _Bands ("Bands", Float) = 3
        _Sway ("Sway", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _ShadeColor;
                float _Bands;
                float _Sway;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; float4 col : COLOR; float fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                ws.xz += sin(_Time.y * 1.4 + ws.x * 0.04 + ws.z * 0.04) * _Sway * i.col.a;
                o.ws = ws;
                o.cs = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.n);
                o.col = i.col;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float3 n = normalize(i.n);
                float t = saturate((dot(n, l.direction) * 0.5 + 0.5) * l.shadowAttenuation);
                t = saturate(floor(t * _Bands) / max(_Bands - 1.0, 1.0));
                float3 baseC = i.col.rgb * _Tint.rgb;
                float3 c = lerp(baseC * _ShadeColor.rgb, baseC * 1.1, t);
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}