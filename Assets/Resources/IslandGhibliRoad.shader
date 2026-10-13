Shader "Island/GhibliRoad"
{
    Properties
    {
        _Tint ("Dirt Tint", Color) = (0.83,0.69,0.49,1)
        _EdgeColor ("Edge Tint", Color) = (0.58,0.55,0.34,1)
        _ToneStrength ("Tonal Variation", Range(0,0.5)) = 0.22
        _BrushScale ("Brush Scale", Float) = 0.45
        _BrushStrength ("Brush Strength", Range(0,0.5)) = 0.16
        _RimColor ("Rim Color", Color) = (0.35,0.52,0.75,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.06
        _RimPower ("Rim Power", Float) = 3.2
        _Fade ("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _Tint, _EdgeColor, _RimColor;
            float _ToneStrength, _BrushScale, _BrushStrength, _RimStrength, _RimPower, _Fade;
            float4 _HemiSky, _HemiGround;
            float  _HemiIntensity;

            struct Attributes { float4 posOS : POSITION; float3 nOS : NORMAL; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct Varyings
            {
                float4 posCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1;
                float2 uv : TEXCOORD2; half4 col : COLOR; half fog : TEXCOORD3;
            };

            float hz(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float bn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hz(i), hz(i + float2(1, 0)), f.x), lerp(hz(i + float2(0, 1)), hz(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(v.posOS.xyz);
                o.nWS = TransformObjectToWorldNormal(v.nOS);
                o.posCS = TransformWorldToHClip(o.posWS);
                o.uv = v.uv; o.col = v.col;
                o.fog = ComputeFogFactor(o.posCS.z);
                return o;
            }

            // vertex rgb = the terrain's own vertex colour sampled under the road; vertex a = ford/gravel amount
            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                clip(_Fade - 0.001);
                float2 wp = i.posWS.xz;
                float e = abs(i.uv.x);
                float gravelM = saturate(i.col.a * 2.0);

                // ragged verge, faded by alpha (no hard clip) into the terrain colour
                float lim = 0.84 + (bn(wp * 1.35) - 0.5) * 0.20 + (bn(wp * 4.7 + 9.3) - 0.5) * 0.08;
                float roadA = 1.0 - smoothstep(lim - 0.5, lim, e);
                clip(roadA - 0.004);

                float3 N = normalize(i.nWS);
                N = IS_FRONT_VFACE(face, N, -N);
                float3 terr = i.col.rgb;

                // ---- packed earth with muted tonal variation ----
                float n1 = bn(wp * 0.55), n2 = bn(wp * 1.7 + 4.1), n3 = bn(wp * 0.18 + 11.0);
                float tone = n1 * 0.5 + n2 * 0.25 + n3 * 0.25;
                float3 dirt = _Tint.rgb;
                dirt = lerp(dirt, _EdgeColor.rgb, 0.35 * smoothstep(0.35, 0.8, n3));            // faint mossy drift
                dirt *= (1.0 - _ToneStrength) + 2.0 * _ToneStrength * tone;                   // lighter/darker patches
                dirt = lerp(dirt, dirt * float3(0.80, 0.78, 0.76) + 0.05, gravelM * 0.5);
                dirt *= 1.0 + 0.06 * (1.0 - smoothstep(0.0, 0.6, e));                           // trodden centre
                float rut = 1.0 - smoothstep(0.0, 0.10, abs(e - 0.42));
                dirt *= 1.0 - 0.08 * rut * (0.55 + 0.45 * bn(float2(i.uv.y * 0.55, (i.uv.x < 0 ? -1.0 : 1.0) * 7.0)));
                float br = bn(wp * _BrushScale) * 0.6 + bn(wp * 1.8) * 0.4;
                dirt *= (1.0 - 0.5 * _BrushStrength) + _BrushStrength * br;

                // colour itself eases into the terrain toward the verge so the blend never ghosts
                float3 albedo = lerp(terr, dirt, smoothstep(0.0, 0.75, roadA));

                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float v = saturate(dot(N, L.direction) * 0.5 + 0.5);
                float ramp = v < 0.25 ? 0.3137 : v < 0.5 ? 0.5490 : v < 0.75 ? 0.8039 : 1.0;
                half3 direct = L.color * ramp * L.shadowAttenuation;
                half3 hemi = lerp(_HemiGround.rgb, _HemiSky.rgb, N.y * 0.5 + 0.5) * _HemiIntensity;
                half3 col = albedo * (direct + hemi);
                float3 Vv = normalize(GetWorldSpaceViewDir(i.posWS));
                col += pow(1.0 - saturate(dot(N, Vv)), _RimPower) * _RimColor.rgb * _RimStrength * roadA;
                col = MixFog(col, i.fog);
                return half4(col, roadA);
            }
            ENDHLSL
        }
    }
}
