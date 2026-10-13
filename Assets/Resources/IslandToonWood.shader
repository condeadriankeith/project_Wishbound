Shader "Island/ToonWood"
{
    // Props that live next to the terrain (timber bridges, road stones). Lighting is a straight copy of
    // Ghibli/ToonLit - same 4-step ramp, same main-light shadows, same hemisphere ambient, same rim and the
    // same painterly brush noise - so a bridge sits in the world instead of looking pasted on.
    // Extras: procedural wood grain (UV0 = metres along / across the longest edge of each face) and a
    // wet + mossy lower band driven by vertex alpha (the road builder writes 1 at the waterline, 0 above it).
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Grain ("Wood Grain (0 = stone)", Range(0,1)) = 1
        _Stone ("Masonry courses", Range(0,1)) = 0
        _GrainStrength ("Grain Strength", Range(0,0.6)) = 0.30
        _BrushScale ("Brush Scale", Float) = 0.45
        _BrushStrength ("Brush Strength", Range(0,0.5)) = 0.24
        _WetColor ("Wet Tint", Color) = (0.52,0.55,0.50,1)
        _MossColor ("Moss", Color) = (0.30,0.46,0.20,1)
        _MossAmount ("Moss Amount", Range(0,1)) = 0.55
        _RimColor ("Rim Color", Color) = (0.35,0.52,0.75,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.14
        _RimPower ("Rim Power", Float) = 3.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 _Tint, _WetColor, _MossColor, _RimColor;
            float _Stone, _Grain, _GrainStrength, _BrushScale, _BrushStrength, _MossAmount, _RimStrength, _RimPower;
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

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 N = normalize(i.nWS);
                N = IS_FRONT_VFACE(face, N, -N);

                // ---- base colour: per-board vertex tint x procedural grain ----
                float3 albedo = _Tint.rgb * i.col.rgb;
                float2 g = i.uv;
                float grain = bn(float2(g.x * 0.55, g.y * 10.0)) * 0.55
                            + bn(float2(g.x * 1.60, g.y * 26.0)) * 0.30
                            + bn(float2(g.x * 3.10 + 7.0, g.y * 5.0)) * 0.15;
                float gm = (1.0 - 0.5 * _GrainStrength) + _GrainStrength * grain;
                albedo *= lerp(1.0, gm, _Grain);
                // cartoon grain: warped growth lines + occasional knots (UV x = along the board)
                float warp = bn(g * float2(0.7, 2.2)) * 2.2 + bn(g * float2(2.0, 5.0)) * 0.6;
                float gl = abs(frac(g.y * 2.6 + warp) - 0.5);
                float lineM = 1.0 - smoothstep(0.025, 0.075, gl);
                float2 kc = floor(g * float2(0.55, 1.1));
                float2 kf = frac(g * float2(0.55, 1.1)) - (0.3 + 0.4 * float2(hz(kc), hz(kc + 3.1)));
                float kd = length(kf * float2(1.6, 1.0));
                float knot = step(0.82, hz(kc + 9.7)) * (1.0 - smoothstep(0.05, 0.16, kd)) * (0.5 + 0.5 * sin(kd * 60.0));
                albedo *= 1.0 - _Grain * (lineM * 0.20 + knot * 0.32);

                // same painterly brush variation the terrain uses
                float br = bn(i.posWS.xz * _BrushScale + i.posWS.y * 0.2) * 0.6 + bn(i.posWS.xz * 1.8) * 0.4;
                albedo *= (1.0 - 0.5 * _BrushStrength) + _BrushStrength * br;

                // ---- masonry: running-bond courses (walls) / flagstones (tops), per-block tone, mortar, chips, creeping moss ----
                if (_Stone > 0.5)
                {
                    float3 Tn = normalize(cross(N, float3(0, 1, 0)) + float3(1e-4, 0, 0));
                    bool topf = N.y > 0.7;
                    float2 sq = topf ? i.posWS.xz : float2(dot(i.posWS, Tn), i.posWS.y);
                    float rowH = topf ? 0.5 : 0.27;
                    float row = floor(sq.y / rowH);
                    float blkL = lerp(0.45, 0.85, hz(float2(row, 9.1)));
                    float cx = (sq.x + hz(float2(row, 3.7)) * 2.0) / blkL;
                    float cid = floor(cx);
                    float2 fr = float2(frac(cx), frac(sq.y / rowH));
                    float edge = min(min(fr.x, 1.0 - fr.x) * blkL, min(fr.y, 1.0 - fr.y) * rowH);
                    float bid = hz(float2(cid + row * 17.0, row));
                    albedo *= (0.84 + 0.26 * bid) * (0.9 + 0.2 * bn(sq * 9.0));
                    albedo *= 1.0 - (1.0 - smoothstep(0.012, 0.05, edge)) * 0.4;
                    float lic = smoothstep(0.55, 0.85, bn(i.posWS.xz * 1.7 + i.posWS.y * 2.3));
                    albedo = lerp(albedo, _MossColor.rgb, lic * 0.20 * (1.0 - 0.6 * N.y));
                }

                // sun-bleached upward faces (decks, rail caps)
                albedo *= 1.0 + 0.07 * saturate(N.y);

                // ---- waterline: damp darkening, then moss creeping up from the bottom ----
                float wet = saturate(i.col.a);
                float mossN = smoothstep(0.35, 0.75, bn(i.posWS.xz * 2.3 + i.posWS.y * 3.1));
                albedo = lerp(albedo, albedo * _WetColor.rgb * 1.6, wet * 0.75);
                albedo = lerp(albedo, _MossColor.rgb * (0.75 + 0.25 * grain), wet * wet * mossN * _MossAmount);
                // animated water-light caustics on damp masonry / timber just above the waterline
                float cau = bn(i.posWS.xz * 2.8 + float2(_Time.y * 0.5, -_Time.y * 0.35)) * bn(i.posWS.xz * 4.1 - _Time.y * 0.4 + i.posWS.y * 2.0);
                albedo += cau * wet * wet * 0.2 * float3(0.75, 0.95, 1.0);

                // ---- same toon lighting as the terrain ----
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float v = saturate(dot(N, L.direction) * 0.5 + 0.5);
                float ramp = v < 0.25 ? 0.3137 : v < 0.5 ? 0.5490 : v < 0.75 ? 0.8039 : 1.0;
                half3 direct = L.color * ramp * L.shadowAttenuation;
                half3 hemi = lerp(_HemiGround.rgb, _HemiSky.rgb, N.y * 0.5 + 0.5) * _HemiIntensity;
                half3 col = albedo * (direct + hemi);
                float3 Vv = normalize(GetWorldSpaceViewDir(i.posWS));
                col += pow(1.0 - saturate(dot(N, Vv)), _RimPower) * _RimColor.rgb * _RimStrength;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
