Shader "Island/GhibliRiver"
{
 Properties
 {
  _Deep ("Deep", Color) = (0.09,0.42,0.60,1)
  _Shallow ("Shallow", Color) = (0.36,0.80,0.80,1)
  _Streak ("Streak", Color) = (0.82,0.97,0.98,1)
  _Foam ("Foam", Color) = (0.97,0.99,1,1)
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
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Deep, _Shallow, _Streak, _Foam;
   CBUFFER_END
   // uv.x = across-river offset (m), uv.y = distance DOWNSTREAM along the channel (m, always increasing toward the sea)
   // uv2.x = fade, uv2.y = river-mouth factor (0 = normal river, 1 = far tip of the fan-out; 0 on lakes)
   struct A { float4 p : POSITION; float2 uv : TEXCOORD0; float4 c : COLOR; float2 uv2 : TEXCOORD1; };
   struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 c : COLOR; float3 wp : TEXCOORD1; float fade : TEXCOORD2; float mouth : TEXCOORD3; };
   float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
   float vn(float2 p)
   {
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
   }
   // River patterns, sampled at a given downstream coordinate y.
   float P1(float x, float y, float speed) { return vn(float2(x * 0.9 + 0.6 * sin(y * 0.15), y * 0.30 / (1.0 + speed * 0.06))); }
   float P2(float x, float y) { return vn(float2(x * 1.7 + 7.0 + 0.4 * sin(y * 0.3), y * 0.55)); }
   float P3(float x, float y) { return vn(float2(x * 3.0, y * 0.9)); }
   float PG(float x, float y) { return vn(float2(x * 2.4 + 3.1, y * 2.4)); }
   float PW(float x, float y) { return vn(float2(x * 1.3 + 11.0, y * 0.5)); }
   V vert(A a)
   {
    V o; float3 wp = TransformObjectToWorld(a.p.xyz);
    wp.y += a.c.g * 0.07 * sin(_Time.y * 0.8 + wp.x * 0.15 + wp.z * 0.12);
    o.wp = wp; o.pos = TransformWorldToHClip(wp); o.uv = a.uv; o.c = a.c; o.fade = a.uv2.x; o.mouth = saturate(a.uv2.y); return o;
   }
   half4 frag(V i) : SV_Target
   {
    float t = _Time.y, speed = i.c.r * 14.0, lake = i.c.g, edge = i.c.b, fall = i.c.a, mouth = i.mouth;
    float sp = 1.5 + speed * 0.9;
    // real water thickness along the view ray (needs the URP depth texture; WaterBuilder switches it on)
    float2 suv = i.pos.xy / _ScaledScreenParams.xy;
    float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
    float dd = max(0.0, sceneZ - i.pos.w);

    // ---- FLOW-MAP SCROLLING --------------------------------------------------------------------------
    // The old "uv.y - t*sp" had a per-pixel speed, so its true velocity was sp/(1 - t*dsp/dy): once t grew,
    // any stretch where speed rises downstream FLIPPED and ran upstream. Here each layer is displaced by a
    // BOUNDED amount (sp * T * phase, phase in 0..1) and two half-cycle-offset layers are cross-faded, so the
    // pattern always travels toward +uv.y (downstream) however the speed varies along the river.
    const float T = 2.2;
    float ph0 = frac(t / T), ph1 = frac(t / T + 0.5);
    float w0 = 1.0 - abs(1.0 - 2.0 * ph0), w1 = 1.0 - w0;
    float d0 = sp * T * ph0, d1 = sp * T * ph1;
    float x = i.uv.x, y = i.uv.y;

    float n1, n2, n3, glint, wispN;
    if (lake > 0.5)
    {
     n1 = vn(i.wp.xz * 0.45 + float2(t * 0.06, t * 0.04));
     n2 = vn(i.wp.xz * 0.9 + float2(-t * 0.10, t * 0.07));
     n3 = vn(i.wp.xz * 1.2 + t * 0.1);
     glint = vn(i.wp.xz * 3.0 + float2(t * 0.6, -t * 0.4));
     wispN = 0.0;
    }
    else
    {
     n1 = P1(x, y - d0, speed) * w0 + P1(x, y - d1, speed) * w1;
     n2 = P2(x, y - d0 * 1.35) * w0 + P2(x, y - d1 * 1.35) * w1;
     n3 = P3(x, y - d0 * 1.8) * w0 + P3(x, y - d1 * 1.8) * w1;
     glint = PG(x, y - d0 * 1.15) * w0 + PG(x, y - d1 * 1.15) * w1;
     wispN = PW(x, y - d0 * 0.6) * w0 + PW(x, y - d1 * 0.6) * w1;
    }
    // the cross-fade averages two noise samples (pulls toward 0.5): re-stretch contrast so thresholds keep their look
    float k = lake > 0.5 ? 1.0 : 1.0 + 0.9 * (1.0 - abs(1.0 - 2.0 * w0)) * 0.0;

    float dT = saturate(max(dd / 1.5, (1.0 - edge) * 0.65));
    // 3 toon depth bands on the river; on the mouth they melt into a continuous gradient so the varying sea floor
    // underneath can never print hard-edged polygons onto the fan
    float bandToon = min(floor(dT * 3.0) / 2.0, 1.0);
    float bandSoft = saturate(0.55 * (1.0 - edge));
    float band = lerp(bandToon, bandSoft, smoothstep(0.0, 0.35, mouth));
    half3 col = lerp(_Shallow.rgb, _Deep.rgb, band * 0.9 * (1.0 - 0.6 * mouth));   // mouth drifts toward the shallow sea tone
    col = lerp(col, _Streak.rgb, step(0.70, n1 * 0.55 + n2 * 0.5) * 0.5);   // ripple streaks
    col = lerp(col, _Streak.rgb, step(0.78, n3) * 0.45 * (1.0 - lake));     // fast thin streaks
    col += step(0.92, glint) * step(0.55, n1) * 0.45;                         // sun glints (ride WITH the current)
     // shoreline foam from real depth: lapping band + outward pulsing ring (also wraps rocks and logs)
     // on the fan these are depth-driven and would trace the sea floor, so they fade out as the mouth opens
     float shoreK = 1.0 - smoothstep(0.05, 0.30, mouth);
     float sh = 1.0 - saturate(dd / 0.9);
     float lap = 0.12 * sin(t * 1.6 + i.wp.x * 0.9 + i.wp.z * 0.7);
     float foamShore = step(0.30, sh + (n2 - 0.5) * 0.45 + lap) * shoreK;
     float ring = step(0.88, frac(sh * 2.2 - t * 0.3 + n1 * 0.3)) * step(0.15, sh) * 0.7 * shoreK;
     float foamFall = fall * step(0.42, n1 * 0.5 + n2 * 0.6);
     float foamEdge = step(0.90, edge + (n2 - 0.5) * 0.3) * shoreK;   // river-bank foam line ends where the channel opens into the sea
     // wispy foam lines peeling off the current as it dissolves into the sea (mid fan only)
     float wisp = step(0.80, wispN) * smoothstep(0.08, 0.45, mouth) * (1.0 - smoothstep(0.65, 0.95, mouth)) * 0.65 * (1.0 - smoothstep(0.35, 0.9, edge));
     float foam = saturate(max(max(max(max(foamShore, ring), foamFall), foamEdge), wisp));
     col = lerp(col, _Foam.rgb, foam);
     // depth-driven opacity on the river; a smooth constant on the mouth (again: don't trace the sea floor)
     float aDepth = lerp(0.22, 0.95, saturate(dd / 1.5));
     float a = lerp(aDepth, 0.62, smoothstep(0.0, 0.35, mouth));
     a *= lerp(1.0, 0.85, mouth);
     // soft side fade: on the fan the sides dissolve to ZERO over a wide band (no straight outline), rivers keep a thin film
     a *= 1.0 - smoothstep(0.55 - 0.45 * mouth, 1.0, edge) * lerp(0.85, 1.0, mouth);
     a = max(a, foam * 0.85 * (1.0 - smoothstep(0.55, 1.0, edge) * mouth)); a *= i.fade;
    return half4(col, a);
   }
   ENDHLSL
  }
 }
}
