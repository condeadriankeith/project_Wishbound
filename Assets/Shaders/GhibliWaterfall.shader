Shader "Ghibli/Waterfall"
{
    Properties
    {
        _Deep ( "Deep", Color) = (0.07,0.38,0.58,1)
        _Shallow ( "Shallow", Color) = (0.36,0.80,0.80,1)
        _Streak ( "Streak", Color) = (0.82,0.97,0.98,1)
        _Foam ( "Foam", Color) = (0.97,0.99,1,1)
        _Mode ( "Mode: 0 water, 1 wet rock, 2 pool foam", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow, _Streak, _Foam;
                float _Mode;
            CBUFFER_END
            // hemisphere lighting - driven globally by MoodController (same names as Ghibli/ToonLit)
            half4 _HemiSky, _HemiGround; half _HemiIntensity;

            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1; float4 col:COLOR; };
            struct V
            {
                float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1;
                half3 hemi:TEXCOORD2; half fog:TEXCOORD3; float4 col:TEXCOORD4;
            };

            float h21(float2 p){ p = frac(p * float2(123.34,456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3.0-2.0*f);
                return lerp(lerp(h21(i), h21(i+float2(1,0)), f.x), lerp(h21(i+float2(0,1)), h21(i+float2(1,1)), f.x), f.y);
            }

            V vert(A a)
            {
                V o;
                float3 wp = TransformObjectToWorld(a.p.xyz);
                o.pos = TransformWorldToHClip(wp);
                o.uv = a.uv; o.uv1 = a.uv1; o.col = a.col;
                half hk = max(_HemiIntensity, 0.85);
                half3 hsky = any(_HemiSky.rgb) > 0 ? _HemiSky.rgb : half3(0.62,0.76,0.92);
                half3 hgnd = any(_HemiGround.rgb) > 0 ? _HemiGround.rgb : half3(0.34,0.30,0.26);
                o.hemi = lerp(hgnd, hsky, 0.75) * hk;
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;                       // uv.x across 0..1, uv.y: 1 = lip (top) .. 0 = pool (bottom)
                float Lh = max(i.uv1.x, 1.0);           // fall height in metres
                float Lw = max(i.uv1.y, 1.0);           // fall width in metres
                float sup = i.col.a;                    // 0 where the sheet has no rock behind it

                // ---------- plunge-pool foam disc (radial uv) ----------
                if (_Mode > 1.5)
                {
                    float2 d = (uv - 0.5) * 2.0; float r = length(d); float ang = atan2(d.y, d.x);
                    float fn  = vn(float2(ang*3.0 + t*0.25, r*6.0 - t*1.8));
                    float fn2 = vn(float2(ang*7.0 - t*0.40, r*14.0 - t*3.2));
                    float core = smoothstep(0.75, 0.05, r);
                    float blob = step(0.62, fn*0.55 + fn2*0.5) * (1.0 - smoothstep(0.35, 1.0, r));
                    float foam = saturate(core*0.9 + blob);
                    half3 c = lerp(_Shallow.rgb, _Foam.rgb, foam) * i.hemi;
                    float a = (1.0 - smoothstep(0.6, 1.0, r)) * (0.30 + 0.70*foam);
                    return half4(MixFog(c, i.fog), saturate(a) * 0.95);
                }

                float x = uv.x * Lw, y = uv.y * Lh;

                // ---------- wet, darkened rock either side of the fall ----------
                if (_Mode > 0.5)
                {
                    float n = vn(float2(x*1.8, y*0.25));
                    float a = smoothstep(0.0, 0.5, uv.x) * smoothstep(1.0, 0.5, uv.x);
                    a *= (0.55 + 0.45*n) * smoothstep(1.0, 0.94, uv.y) * sup;
                    half3 c = half3(0.16,0.20,0.25) * i.hemi;
                    return half4(MixFog(c, i.fog), saturate(a * 0.55));
                }

                // ---------- falling water sheet ----------
                float spd = 4.5;
                float n1 = vn(float2(x*1.5,       y*0.30 + t*spd*0.55));
                float n2 = vn(float2(x*3.9 + 7.3, y*0.75 + t*spd));
                float n3 = vn(float2(x*0.65,      y*0.10 + t*spd*0.30));
                float s = n1*0.5 + n2*0.3 + n3*0.2;

                // ragged side edges that wobble as they fall
                float er = 0.10 + 0.10 * vn(float2(y*0.8 - t*0.6, 3.7));
                float edge = smoothstep(0.0, er, uv.x) * smoothstep(1.0, 1.0 - er, uv.x);
                float topFade = smoothstep(Lh, Lh - 1.5, y);        // melts into the flowing river at the lip (1.5 m)

                half3 col = lerp(_Shallow.rgb, _Deep.rgb, saturate((1.0 - s) * 0.9 - 0.15));
                col = lerp(col, _Streak.rgb, step(0.62, n1*0.55 + n2*0.5) * 0.6);   // fast bright streaks
                col = lerp(col, _Streak.rgb, step(0.72, n3) * 0.4);                 // broad pale bands

                float crest  = smoothstep(Lh - 2.5, Lh, y);          // ~2.5 m of whitewater at the lip
                float splash = smoothstep(4.0, 0.0, y);              // ~4 m of churn where it lands
                float foam = saturate(max(max(crest*(0.7 + 0.3*n1), splash*(0.55 + 0.45*n2)),
                                          step(0.86, n2*0.6 + n1*0.5) * 0.85));
                col = lerp(col, _Foam.rgb, foam);
                col = col * i.hemi + 0.06;

                float a = edge * topFade * (0.74 + 0.26*s);
                a = max(a, foam * edge * topFade * 0.9);
                return half4(MixFog(col, i.fog), clamp(a * sup, 0.0, 0.95));
            }
            ENDHLSL
        }
    }
}
