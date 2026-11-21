// Painterly look for Wishbound: Kuwahara brush smoothing + Sobel ink outlines (depth + normals).
// Used by a URP "Full Screen Pass Renderer Feature" (PC_Renderer → WishboundPainterly), injected before
// post-processing so bloom / tonemapping / tilt-shift still apply on top.
Shader "Wishbound/Painterly"
{
    Properties
    {
        [IntRange] _Radius ("Brush radius (px)", Range(0, 6)) = 3
        _BrushStrength ("Brush strength", Range(0, 1)) = 1
        _InkColor ("Ink colour", Color) = (0.11, 0.09, 0.14, 1)
        _InkStrength ("Ink strength", Range(0, 1)) = 0.75
        _InkThickness ("Ink thickness (px)", Range(0.5, 3)) = 1.1
        _DepthSensitivity ("Depth edge sensitivity", Range(0, 40)) = 12
        _NormalSensitivity ("Normal edge sensitivity", Range(0, 4)) = 1.2
        _InkFadeStart ("Ink fade start (m)", Float) = 60
        _InkFadeEnd ("Ink fade end (m)", Float) = 220
        _PaperGrain ("Paper grain", Range(0, 0.1)) = 0.025
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off
        Pass
        {
            Name "Painterly"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _Radius, _BrushStrength, _InkStrength, _InkThickness, _DepthSensitivity, _NormalSensitivity, _InkFadeStart, _InkFadeEnd, _PaperGrain;
            float4 _InkColor;

            float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }

            // classic 4-quadrant Kuwahara: pick the mean of the least-varying quadrant -> flat painted patches, kept edges
            float3 Kuwahara(float2 uv, int r)
            {
                float2 px = (1.0 / _ScreenParams.xy);
                float3 m[4]; float3 s[4];
                [unroll] for (int k = 0; k < 4; k++) { m[k] = 0; s[k] = 0; }
                int2 offs[4] = { int2(-1, -1), int2(1, -1), int2(-1, 1), int2(1, 1) };
                float n = (r + 1) * (r + 1);
                [unroll] for (int q = 0; q < 4; q++)
                {
                    [loop] for (int j = 0; j <= r; j++)
                    [loop] for (int i = 0; i <= r; i++)
                    {
                        float3 c = Src(uv + float2(i * offs[q].x, j * offs[q].y) * px);
                        m[q] += c; s[q] += c * c;
                    }
                }
                float best = 1e9; float3 result = Src(uv);
                [unroll] for (int q2 = 0; q2 < 4; q2++)
                {
                    float3 mean = m[q2] / n;
                    float3 var = abs(s[q2] / n - mean * mean);
                    float v = var.r + var.g + var.b;
                    if (v < best) { best = v; result = mean; }
                }
                return result;
            }

            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            float Edge(float2 uv, out float depth)
            {
                float2 px = (1.0 / _ScreenParams.xy) * _InkThickness;
                depth = EyeDepth(uv);
                float d[9]; float3 nrm[9]; int idx = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 p = uv + float2(x, y) * px;
                    d[idx] = EyeDepth(p); nrm[idx] = SampleSceneNormals(p); idx++;
                }
                // Sobel on depth, normalised by distance so far objects don't all turn to ink
                float gx = (d[2] + 2 * d[5] + d[8]) - (d[0] + 2 * d[3] + d[6]);
                float gy = (d[6] + 2 * d[7] + d[8]) - (d[0] + 2 * d[1] + d[2]);
                float de = sqrt(gx * gx + gy * gy) / max(depth, .5) * _DepthSensitivity;
                float3 ngx = (nrm[2] + 2 * nrm[5] + nrm[8]) - (nrm[0] + 2 * nrm[3] + nrm[6]);
                float3 ngy = (nrm[6] + 2 * nrm[7] + nrm[8]) - (nrm[0] + 2 * nrm[1] + nrm[2]);
                float ne = sqrt(dot(ngx, ngx) + dot(ngy, ngy)) * _NormalSensitivity;
                return saturate(smoothstep(.35, 1, de) + smoothstep(.6, 1.6, ne));
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 src = Src(uv);
                int r = (int)_Radius;
                float3 col = r > 0 ? lerp(src, Kuwahara(uv, r), _BrushStrength) : src;

                float depth;
                float edge = Edge(uv, depth);
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 0.00001;
                #else
                    bool sky = raw >= 0.99999;
                #endif
                float fade = 1 - smoothstep(_InkFadeStart, _InkFadeEnd, depth);
                float ink = sky ? 0 : edge * _InkStrength * fade;
                // ink takes on a little of the paint underneath, like a wet brush line
                float3 inkCol = lerp(_InkColor.rgb, col * .35, .3);
                col = lerp(col, inkCol, ink);

                // faint paper grain
                col *= 1 + (Hash(uv * _ScreenParams.xy) - .5) * _PaperGrain;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
