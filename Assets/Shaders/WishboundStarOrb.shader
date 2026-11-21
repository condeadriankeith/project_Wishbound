// The star character's body: a hot, living core with drifting plasma bands and a cyan/magenta corona rim
// (the same colours as the comet it fell as). Unlit + HDR so URP bloom carries the glow.
Shader "Wishbound/StarOrb"
{
    Properties
    {
        [HDR] _CoreColor ("Core (hot centre)", Color) = (4.0, 3.3, 2.2, 1)
        [HDR] _MidColor ("Body", Color) = (2.2, 1.05, 0.28, 1)
        [HDR] _BandColor ("Plasma bands", Color) = (3.2, 1.9, 0.7, 1)
        [HDR] _RimA ("Corona A (cyan)", Color) = (0.35, 2.6, 3.4, 1)
        [HDR] _RimB ("Corona B (magenta)", Color) = (3.0, 0.55, 2.6, 1)
        _CorePower ("Core falloff", Range(0.5, 8)) = 2.2
        _RimPower ("Rim falloff", Range(0.5, 8)) = 2.6
        _RimIntensity ("Rim intensity", Range(0, 4)) = 1.15
        _BandScale ("Band scale", Range(0.5, 12)) = 3.4
        _BandSpeed ("Band speed", Range(0, 3)) = 0.45
        _BandAmount ("Band amount", Range(0, 1)) = 0.55
        _Intensity ("Overall intensity", Range(0, 4)) = 1
        _Pulse ("Pulse (driven by script)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StarOrb"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor, _MidColor, _BandColor, _RimA, _RimB;
                half _CorePower, _RimPower, _RimIntensity, _BandScale, _BandSpeed, _BandAmount, _Intensity, _Pulse;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float3 op : TEXCOORD2; half fog : TEXCOORD3; };

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wp = p.positionWS; o.op = i.pos.xyz;
                o.n = TransformObjectToWorldNormal(i.n); o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float h3(float3 p) { p = frac(p * 0.3183099 + .1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vn(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h3(i), h3(i + float3(1,0,0)), f.x), lerp(h3(i + float3(0,1,0)), h3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h3(i + float3(0,0,1)), h3(i + float3(1,0,1)), f.x), lerp(h3(i + float3(0,1,1)), h3(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.n), Vd = normalize(GetWorldSpaceViewDir(i.wp));
                float ndv = saturate(dot(N, Vd));
                float t = _Time.y * _BandSpeed;
                // domain-warped plasma bands flowing over the surface
                float3 q = i.op * _BandScale;
                float warp = vn(q + float3(0, t, t * .7));
                float bands = sin((q.y + warp * 2.4 + vn(q * 1.9 - t) * 1.2) * 3.1 + t * 2.0) * .5 + .5;
                bands = smoothstep(.35, .95, bands) * _BandAmount;

                float core = pow(ndv, _CorePower);
                half3 c = lerp(_MidColor.rgb, _BandColor.rgb, bands);
                c = lerp(c, _CoreColor.rgb, saturate(core * (1 + _Pulse * .6)));

                // corona rim, hue swirling between cyan and magenta around the silhouette
                float rim = pow(1 - ndv, _RimPower);
                float ang = atan2(N.y - Vd.y, N.x - Vd.x);
                float hueMix = sin(ang * 2 + _Time.y * 1.3 + vn(i.op * 4 + t) * 3) * .5 + .5;
                half3 rimC = lerp(_RimA.rgb, _RimB.rgb, hueMix);
                c = c * (1 + _Pulse * .35) + rimC * rim * _RimIntensity * (1 + _Pulse * .5);
                c *= _Intensity;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
