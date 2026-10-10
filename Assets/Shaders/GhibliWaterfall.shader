Shader "Ghibli/Waterfall"
{
    Properties { _ColorA("Top", Color)=(0.62,0.86,0.98,1) }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A{ float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V{ float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A a){ V o; o.cs=TransformObjectToHClip(a.p.xyz); o.uv=a.uv; return o; }
            float h(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float nn(float2 p){ float2 i=floor(p),f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(h(i),h(i+float2(1,0)),f.x), lerp(h(i+float2(0,1)),h(i+float2(1,1)),f.x), f.y); }
            half4 frag(V i):SV_Target {
                float2 uv = i.uv; float t = _Time.y;
                float edge = smoothstep(0.0,0.18,uv.x) * smoothstep(1.0,0.82,uv.x);
                float s = nn(float2(uv.x*24.0, uv.y*3.5 + t*2.8))*0.6 + nn(float2(uv.x*55.0, uv.y*7.0 + t*4.5))*0.4;
                float a = edge * smoothstep(0.2,0.65,s+0.25) * (0.6 + 0.4*smoothstep(0.0,0.25,uv.y));
                return half4(lerp(float3(0.62,0.86,0.98), 1.0, s), a * 0.88);
            }
            ENDHLSL
        }
    }
}
