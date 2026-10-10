Shader "Island/FluffyFoliage"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.4,0.45,0.7,1)
        _Bands ("Bands", Float) = 3
        _Sway ("Sway", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Tint; float4 _ShadeColor; float _Bands; float _Sway;
        CBUFFER_END
        struct A { float3 pos : POSITION; float3 n : NORMAL; float4 col : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; float4 col : COLOR; float2 uv : TEXCOORD2; float rot : TEXCOORD3; float fog : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };

        // every quad (4 verts stored at the quad centre, uv = corner) is expanded to face the camera
        float3 Billboard(A i)
        {
            float3 c = TransformObjectToWorld(i.pos);
            float sc = length(GetObjectToWorldMatrix()._m00_m10_m20);
            float3 r = UNITY_MATRIX_V[0].xyz; float3 u = UNITY_MATRIX_V[1].xyz;
            float2 q = (i.uv - 0.5) * 2.0;
            float3 w = c + (r * q.x + u * q.y) * 0.5 * i.uv1.x * sc;
            w.xz += sin(_Time.y * 1.5 + c.x * 0.04 + c.z * 0.04) * _Sway * i.col.a;
            return w;
        }
        float Mask(float2 uv, float rot)
        {
            float2 p = (uv - 0.5) * 2.0;
            float s, co; sincos(rot, s, co);
            p = float2(co * p.x - s * p.y, s * p.x + co * p.y);
            float edge = 1.0 - 0.3 * (0.5 + 0.5 * sin(atan2(p.y, p.x) * 5.0 + rot * 3.0));
            return edge - length(p);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.ws = Billboard(i);
                o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(i.n);
                o.col = i.col; o.uv = i.uv; o.rot = i.uv1.y;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(Mask(i.uv, i.rot));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float t = saturate((dot(normalize(i.n), l.direction) * 0.5 + 0.5) * l.shadowAttenuation);
                t = saturate(floor(t * _Bands) / max(_Bands - 1.0, 1.0));
                float3 baseC = i.col.rgb * _Tint.rgb;
                baseC *= lerp(0.9, 1.12, smoothstep(0.1, 0.9, i.uv.y));
                float3 c = lerp(baseC * _ShadeColor.rgb, baseC * 1.12, t);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex svert
            #pragma fragment sfrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            V svert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 ws = Billboard(i);
                float3 n = TransformObjectToWorldNormal(i.n);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - ws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
            #endif
                o.cs = cs; o.ws = ws; o.n = n; o.col = i.col; o.uv = i.uv; o.rot = i.uv1.y; o.fog = 0;
                return o;
            }
            half4 sfrag(V i) : SV_Target { clip(Mask(i.uv, i.rot)); return 0; }
            ENDHLSL
        }
    }
}