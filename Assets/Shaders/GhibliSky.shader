Shader "Ghibli/Sky"
{
    Properties {
        _Top("Top",Color)=(0.075,0.341,0.678,1)
        _Mid("Mid",Color)=(0.376,0.675,0.941,1)
        _Hor("Horizon",Color)=(0.537,0.804,0.969,1)
    }
    SubShader {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _Top,_Mid,_Hor;
            float3 _GhibliSunDir;
            struct A{float4 p:POSITION;}; struct V{float4 cs:SV_POSITION; float3 d:TEXCOORD0;};
            V vert(A a){ V o; o.cs=TransformObjectToHClip(a.p.xyz); o.d=a.p.xyz; return o; }
            half4 frag(V i):SV_Target{
                float3 d = normalize(i.d); float h = d.y;
                float3 c = lerp(_Hor.rgb, _Mid.rgb, smoothstep(0.0,0.32,h));
                c = lerp(c, _Top.rgb, smoothstep(0.24,0.88,h));
                float s = max(dot(d, normalize(_GhibliSunDir)), 0.0);
                c += float3(1.0,0.96,0.82) * (smoothstep(0.9992,0.9998,s)*1.8 + pow(s,22.0)*0.35);
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
