Shader "Ghibli/Cloud"
{
    Properties { _Opacity("Opacity", Range(0,1)) = 0.62 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Opacity;
            float3 _GhibliSunDir;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A{ float4 p:POSITION; float3 n:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V{ float4 cs:SV_POSITION; float3 nw:TEXCOORD0; float3 wp:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a){ V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a,o);
                float3 w = TransformObjectToWorld(a.p.xyz); o.wp=w;
                o.nw = TransformObjectToWorldNormal(a.n); o.cs=TransformWorldToHClip(w); return o; }
            half4 frag(V i):SV_Target{
                UNITY_SETUP_INSTANCE_ID(i);
                float3 nW = normalize(i.nw);
                float3 vD = normalize(_WorldSpaceCameraPos - i.wp);
                float3 sunDir = normalize(_GhibliSunDir);
                float l = dot(nW, sunDir) * 0.5 + 0.5;
                float b = smoothstep(0.40,0.56,l)*0.55 + smoothstep(0.68,0.80,l)*0.45;
                float3 c = lerp(float3(0.68,0.78,0.88), 1.0, b);
                c += pow(1.0 - saturate(dot(nW, vD)), 3.0) * 0.15;
                float rim = smoothstep(0.0, 0.4, saturate(dot(nW, vD)));
                half4 ic = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
                c *= max(ic.rgb, half3(0.001,0.001,0.001));
                return half4(c, _Opacity * rim * ic.a);
            }
            ENDHLSL
        }
    }
}
