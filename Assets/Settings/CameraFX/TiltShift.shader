Shader "Hidden/Island/TiltShift"
{
    Properties
    {
        _FocusY ("Focus line (screen Y, 0.5 = centre)", Range(0, 1)) = 0.5
        _BandHalfHeight ("Sharp band half-height (screen height)", Range(0, 0.5)) = 0.09
        _Falloff ("Transition height (screen height)", Range(0.01, 0.8)) = 0.28
        _MaxBlurRadius ("Max blur radius (px at 1080p)", Range(0, 64)) = 26
        _Angle ("Tilt angle (degrees)", Range(-45, 45)) = 0
        _Saturation ("Saturation", Range(0.5, 2)) = 1.1
        _BokehBoost ("Highlight bloom in blur", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            Name "TiltShift"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define SAMPLE_COUNT 80

            float _IslandTiltShiftEnabled;   // global, set by IslandCameraFX
            float _FocusY, _BandHalfHeight, _Falloff, _MaxBlurRadius, _Angle, _Saturation, _BokehBoost;

            // Circle of confusion in pixels for a given screen position: zero inside the sharp band, ramping to max.
            float Coc(float2 uv, float aspect, float2 n)
            {
                float2 q = float2((uv.x - 0.5) * aspect, uv.y - 0.5);
                float d = dot(q, n) - (_FocusY - 0.5);
                float t = saturate((abs(d) - _BandHalfHeight) / max(_Falloff, 1e-4));
                t = t * t * (3.0 - 2.0 * t);
                return t * _MaxBlurRadius * (_ScreenParams.y / 1080.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float3 col = src.rgb;

                if (_IslandTiltShiftEnabled > 0.5)
                {
                    float aspect = _ScreenParams.x / _ScreenParams.y;
                    float ang = radians(_Angle);
                    float2 n = float2(-sin(ang), cos(ang));
                    float R = Coc(uv, aspect, n);

                    if (R > 0.35)
                    {
                        float2 texel = 1.0 / _ScreenParams.xy;
                        float ign = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                        float rot = ign * 6.2831853;

                        float3 acc = min(col, 32.0);
                        float wsum = 1.0;

                        [loop]
                        for (int i = 0; i < SAMPLE_COUNT; i++)
                        {
                            float r = sqrt((i + 0.5) / SAMPLE_COUNT);
                            float a = i * 2.39996323 + rot;
                            float distPx = r * R;
                            float2 suv = uv + float2(cos(a), sin(a)) * distPx * texel;

                            // scatter-as-gather: a sample only contributes if its own blur reaches this pixel,
                            // so the sharp band never bleeds into the blur and vice versa (no halos).
                            float w = saturate(Coc(suv, aspect, n) - distPx + 1.0);

                            float3 s = min(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, suv, 0).rgb, 32.0);
                            float lum = dot(s, float3(0.2126, 0.7152, 0.0722));
                            w *= 1.0 + min(lum, 8.0) * _BokehBoost;

                            acc += s * w;
                            wsum += w;
                        }
                        col = acc / wsum;
                    }

                    float l = dot(col, float3(0.2126, 0.7152, 0.0722));
                    col = lerp(l.xxx, col, _Saturation);
                }

                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
