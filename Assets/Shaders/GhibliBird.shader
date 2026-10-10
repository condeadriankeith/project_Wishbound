Shader "Ghibli/Bird"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A{ float4 p:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V{ float4 cs:SV_POSITION; half fog:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a){
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a,o);
                float3 p = a.p.xyz;
                float ph = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).r * 50.0;
                p.y += a.uv.x * sin(_Time.y * 9.0 + ph) * 1.1;
                float3 w = TransformObjectToWorld(p);
                o.cs = TransformWorldToHClip(w);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i):SV_Target{
                UNITY_SETUP_INSTANCE_ID(i);
                half3 c = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).rgb;
                c = MixFog(c, i.fog);
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
