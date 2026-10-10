Shader "Ghibli/Ocean"
{
    Properties
    {
        _CDeep ("Deep", Color) = (0.0, 0.255, 0.471, 1)
        _CShal ("Shallow", Color) = (0.039, 0.620, 0.839, 1)
        _CShore ("Shore", Color) = (0.322, 0.965, 0.816, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_GhibliHeightMap); SAMPLER(sampler_GhibliHeightMap);
            half4 _CDeep, _CShal, _CShore;
            float3 _GhibliSunDirSrc;
            float4 _GhibliFogColor;
            float  _GhibliFogDensity;
            float  _GhibliE;
            float  _GhibliMirrorZ;
            struct A { float4 pos:POSITION; };
            struct V { float4 posCS:SV_POSITION; float3 wsU:TEXCOORD0; };
            float Sm(float a, float b, float x){ float t=saturate((x-a)/(b-a)); return t*t*(3.0-2.0*t); }
            float hs(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float vn(float2 p){
                float2 i=floor(p), f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(hs(i),hs(i+float2(1,0)),f.x), lerp(hs(i+float2(0,1)),hs(i+float2(1,1)),f.x), f.y);
            }
            V vert(A a){
                V o; float3 w = TransformObjectToWorld(a.pos.xyz);
                o.wsU = w; o.posCS = TransformWorldToHClip(w); return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float zS = _GhibliMirrorZ > 0.5 ? -1.0 : 1.0;
                float3 wS = float3(i.wsU.x, i.wsU.y, i.wsU.z * zS);
                float3 camS = float3(_WorldSpaceCameraPos.x, _WorldSpaceCameraPos.y, _WorldSpaceCameraPos.z * zS);
                float2 p = wS.xz;
                float2 uv = (p + _GhibliE) / (2.0 * _GhibliE);
                float inb = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float hgt = SAMPLE_TEXTURE2D_LOD(_GhibliHeightMap, sampler_GhibliHeightMap, uv, 0).r;
                float depth = lerp(18.0, 2.0 - hgt, inb);
                float3 n = normalize(float3(
                    -(cos(p.x*0.08 + t*0.8)*0.06 + cos((p.x+p.y)*0.05 + t*0.5)*0.03) * 2.0,
                    1.0,
                    -(cos(p.y*0.09 - t*0.6)*0.07 + cos((p.x+p.y)*0.05 + t*0.5)*0.03) * 2.0));
                float3 col = lerp(_CShal.rgb, _CDeep.rgb, Sm(1.5, 14.0, depth));
                col = lerp(_CShore.rgb, col, Sm(0.0, 3.2, depth));
                col += vn(p*0.25 + float2(t*0.2, -t*0.15)) * vn(p*0.4 - float2(t*0.15, t*0.2)) * 0.15 * (1.0 - Sm(1.0, 7.0, depth));
                float wob = 0.5 + 0.5 * sin(t*0.7 + depth*1.8);
                float foam = max(
                    Sm(2.8*(0.35+0.25*wob), 0.0, depth) * Sm(0.35, 0.65, vn(p*0.7 + t*0.12) + (1.0 - depth/2.8)*0.5),
                    Sm(0.18 + 0.12*sin(t*1.0), 0.0, depth));
                float3 v = normalize(camS - wS);
                float3 sunS = normalize(_GhibliSunDirSrc);
                float sp = smoothstep(0.5, 0.58, pow(max(dot(reflect(-sunS, n), v), 0.0), 80.0));
                col = lerp(col, float3(0.58,0.82,0.98), pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.4) + sp * 0.85;
                col = lerp(col, 1.0, foam * 0.92);
                float a = max(lerp(0.48, 0.96, Sm(0.0, 6.0, depth)), foam);
                float d = length(wS - camS) * _GhibliFogDensity;
                col = lerp(col, _GhibliFogColor.rgb, 1.0 - exp(-d * d));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
