Shader "Ghibli/Ocean"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.09, 0.42, 0.60, 1)
        _Shallow ("Shallow", Color) = (0.36, 0.80, 0.80, 1)
        _Streak ("Streak", Color) = (0.82, 0.97, 0.98, 1)
        _Foam ("Foam", Color) = (0.97, 0.99, 1, 1)
        _FoamIntensity ("Foam Intensity", Range(0,2)) = 1.0
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
            half4 _Deep, _Shallow, _Streak, _Foam;
            float _FoamIntensity;
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
            // Same toon language as Island/GhibliRiver: 3 depth bands, ripple streaks, sun glints, stepped shore foam.
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
                float dist = length(wS - camS);
                // detail fades with distance so the far sea is a calm flat band (no shimmer / aliasing, no hard horizon)
                float detail = 1.0 - Sm(250.0, 900.0, dist);

                float dT = Sm(0.0, 9.0, depth);
                float band = min(floor(dT * 3.0) / 2.0, 1.0);
                half3 col = lerp(_Shallow.rgb, _Deep.rgb, band * 0.9);

                float n1 = vn(p * 0.30 + float2(t * 0.18, t * 0.11));
                float n2 = vn(p * 0.65 + float2(-t * 0.14, t * 0.09));
                float n3 = vn(p * 1.10 + float2(t * 0.22, -t * 0.12));
                float gl = vn(p * 2.4 + float2(t * 0.55, -t * 0.35));
                col = lerp(col, _Streak.rgb, step(0.70, n1 * 0.55 + n2 * 0.5) * 0.5 * detail);
                col = lerp(col, _Streak.rgb, step(0.78, n3) * 0.35 * detail);
                col += step(0.92, gl) * step(0.55, n1) * 0.45 * detail;

                float sh = 1.0 - saturate(depth / 2.0);
                float lap = 0.12 * sin(t * 1.6 + p.x * 0.9 + p.y * 0.7);
                float foamShore = step(0.30, sh + (n2 - 0.5) * 0.45 + lap);
                float ring = step(0.88, frac(sh * 2.2 - t * 0.3 + n1 * 0.3)) * step(0.15, sh) * 0.7;
                float foam = saturate(max(foamShore, ring)) * _FoamIntensity;
                col = lerp(col, _Foam.rgb, saturate(foam));

                float a = max(lerp(0.55, 0.96, Sm(0.0, 6.0, depth)), foam * 0.85);
                float d = dist * _GhibliFogDensity;
                col = lerp(col, _GhibliFogColor.rgb, 1.0 - exp(-d * d));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
