// Camera-facing additive glow disc for the star / comet. Put it on a Quad; the vertex shader billboards it.
Shader "Wishbound/StarHalo"
{
    Properties
    {
        [HDR] _Inner ("Inner", Color) = (2.4, 1.5, 0.6, 1)
        [HDR] _Outer ("Outer", Color) = (0.6, 0.5, 1.6, 1)
        _Falloff ("Falloff", Range(0.5, 8)) = 2.2
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Rays ("Ray streaks", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Inner, _Outer; half _Falloff, _Intensity, _Rays;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float sx = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 wp = centre + (right * i.pos.x + up * i.pos.y) * sx;
                o.pos = TransformWorldToHClip(wp); o.uv = i.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 d = i.uv * 2 - 1; float r = length(d);
                float g = pow(saturate(1 - r), _Falloff);
                float a = atan2(d.y, d.x);
                float rays = pow(saturate(abs(sin(a * 3 + _Time.y * .25)) * abs(cos(a * 2 - _Time.y * .17))), 6) * saturate(1 - r) * _Rays;
                half3 c = lerp(_Outer.rgb, _Inner.rgb, saturate(g * 1.6)) * (g + rays);
                return half4(c * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
