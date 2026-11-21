// Small still water (ponds, wells, troughs, flooded courtyards): cel depth gradient from seafoam turquoise at the
// edges to deep teal, crisp hand-drawn foam rings where it meets stones and banks, cel caustics, twinkling glints,
// and the Star / lanterns reflected. Depth comes from the URP depth texture, so edges follow whatever is underneath.
Shader "Wishbound/ToonWater"
{
    Properties
    {
        _Shallow ("Shallow (edges)", Color) = (0.42, 0.85, 0.78, 1)
        _Mid ("Mid", Color) = (0.14, 0.52, 0.55, 1)
        _Deep ("Deep", Color) = (0.05, 0.2, 0.3, 1)
        _DepthRange ("Depth range (m)", Float) = 1.4
        _Foam ("Foam", Color) = (0.95, 0.98, 1, 1)
        _FoamWidth ("Foam width (m)", Float) = 0.18
        _CausticStrength ("Caustics", Range(0, 1)) = 0.45
        _GlintStrength ("Glints", Range(0, 3)) = 1.4
        _Opacity ("Opacity", Range(0, 1)) = 0.86
        _Glow ("Glow (fragment under water)", Color) = (0, 0, 0, 1)
        [Toggle] _RadialDepth ("Radial depth (flat disc ponds: deep centre, shallow rim)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "WishboundWaterFX.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Mid, _Deep, _Foam, _Glow; float _DepthRange, _FoamWidth, _CausticStrength, _GlintStrength, _Opacity, _RadialDepth;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; half fog : TEXCOORD1; float2 op : TEXCOORD2; };
            float h(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + float2(1, 1)), f.x), f.y); }
            V vert(A a)
            {
                V o; float3 wp = TransformObjectToWorld(a.p.xyz);
                wp.y += sin(_Time.y * .9 + wp.x * .7 + wp.z * .5) * .01;
                o.op = a.p.xz; o.wp = wp; o.pos = TransformWorldToHClip(wp); o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float2 suv = i.pos.xy / _ScaledScreenParams.xy;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float dd = max(0, sceneZ - i.pos.w);                        // water thickness along the view ray
                if (_RadialDepth > .5) dd = min(dd + 10 * step(dd, .001), (1 - saturate(length(i.op))) * _DepthRange * 1.6);   // flat ponds: depth from the rim inward
                float k = saturate(dd / _DepthRange);
                // cel depth bands: turquoise rim -> mid -> deep teal
                float band = k < .22 ? 0 : k < .55 ? .5 : 1;
                float3 col = band < .25 ? _Shallow.rgb : band < .75 ? _Mid.rgb : _Deep.rgb;
                col = lerp(col, lerp(_Shallow.rgb, _Deep.rgb, k), .25);    // a little painterly blend between bands
                // ripple normal (soft) for glints and reflections
                float2 p = i.wp.xz;
                float3 n = normalize(float3((vn(p * 1.6 + t * .35) - .5) * .5, 1, (vn(p * 1.6 - t * .3 + 7) - .5) * .5));
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                // caustics brightest in the mid shallows
                col += WaterCaustics(p, t, 1.3) * _CausticStrength * (1 - smoothstep(.2, .9, k)) * float3(.65, 1, .9) * .5;
                // sky fresnel
                col = lerp(col, float3(.62, .82, .95), pow(1 - saturate(dot(n, v)), 4) * .35);
                // sun glints + the Star / lanterns
                Light sun = GetMainLight();
                col += WaterGlints(p, n, v, sun.direction, t, 3.2) * _GlintStrength * sun.color * saturate(sun.direction.y * 3);
                col += WaterPointLights(i.wp, n, v, i.pos, p, t);
                col += _Glow.rgb * (1 - k * .6);
                // crisp hand-drawn foam: a lapping ring hugging every edge / stone / piling
                float lap = .5 + .5 * sin(t * 1.4 + p.x * 1.3 + p.y * .9);
                float foam = step(dd, _FoamWidth * (.7 + .5 * lap) * (.8 + .4 * vn(p * 4 + t * .2)));
                float ring = step(.93, frac((1 - saturate(dd / (_FoamWidth * 3))) * 2 - t * .25)) * step(dd, _FoamWidth * 3) * .3;
                col = lerp(col, _Foam.rgb, saturate(foam + ring));
                float a = saturate(lerp(.45, _Opacity, smoothstep(0, .35, k)) + foam);
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
