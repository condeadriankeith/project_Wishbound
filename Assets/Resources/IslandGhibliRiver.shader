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
   struct A { float4 p : POSITION; float2 uv : TEXCOORD0; float4 c : COLOR; float2 uv2 : TEXCOORD1; };
   struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 c : COLOR; float3 wp : TEXCOORD1; float fade : TEXCOORD2; };
   float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
   float vn(float2 p)
   {
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
   }
   V vert(A a)
   {
    V o; float3 wp = TransformObjectToWorld(a.p.xyz);
    wp.y += a.c.g * 0.07 * sin(_Time.y * 0.8 + wp.x * 0.15 + wp.z * 0.12);
    o.wp = wp; o.pos = TransformWorldToHClip(wp); o.uv = a.uv; o.c = a.c; o.fade = a.uv2.x; return o;
   }
   half4 frag(V i) : SV_Target
   {
    float t = _Time.y, speed = i.c.r * 14.0, lake = i.c.g, edge = i.c.b, fall = i.c.a;
    float sp = 1.5 + speed * 0.9;
    // real water thickness along the view ray (needs the URP depth texture; WaterBuilder switches it on)
    float2 suv = i.pos.xy / _ScaledScreenParams.xy;
    float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
    float dd = max(0.0, sceneZ - i.pos.w);
    float2 q1, q2;
    if (lake > 0.5) { q1 = i.wp.xz * 0.45 + float2(t * 0.06, t * 0.04); q2 = i.wp.xz * 0.9 + float2(-t * 0.10, t * 0.07); }
    else
    {
     q1 = float2(i.uv.x * 0.9 + 0.6 * sin(i.uv.y * 0.15), (i.uv.y - t * sp) * 0.30 / (1.0 + speed * 0.06));
     q2 = float2(i.uv.x * 1.7 + 7.0 + 0.4 * sin(i.uv.y * 0.3 + t * 0.5), (i.uv.y - t * sp * 1.35) * 0.55);
    }
    float n1 = vn(q1), n2 = vn(q2);
    float n3 = lake > 0.5 ? vn(i.wp.xz * 1.2 + t * 0.1) : vn(float2(i.uv.x * 3.0, (i.uv.y - t * sp * 1.8) * 0.9));
    float dT = saturate(max(dd / 1.5, (1.0 - edge) * 0.65));
    float band = min(floor(dT * 3.0) / 2.0, 1.0);                       // 3 toon depth bands
    half3 col = lerp(_Shallow.rgb, _Deep.rgb, band * 0.9);
    col = lerp(col, _Streak.rgb, step(0.70, n1 * 0.55 + n2 * 0.5) * 0.5);   // ripple streaks
    col = lerp(col, _Streak.rgb, step(0.78, n3) * 0.45 * (1.0 - lake));     // fast thin streaks
    col += step(0.92, vn(i.wp.xz * 3.0 + float2(t * 0.6, -t * 0.4))) * step(0.55, n1) * 0.45;   // sun glints
    // shoreline foam from real depth: lapping band + outward pulsing ring (also wraps rocks and logs)
    float sh = 1.0 - saturate(dd / 0.6);
    float lap = 0.12 * sin(t * 1.6 + i.wp.x * 0.9 + i.wp.z * 0.7);
    float foamShore = step(0.38, sh + (n2 - 0.5) * 0.4 + lap);
    float ring = step(0.88, frac(sh * 2.2 - t * 0.3 + n1 * 0.3)) * step(0.15, sh) * 0.7;
    float foamFall = fall * step(0.42, n1 * 0.5 + n2 * 0.6);
    float foamEdge = step(0.93, edge + (n2 - 0.5) * 0.1);
    float foam = saturate(max(max(max(foamShore, ring), foamFall), foamEdge));
    col = lerp(col, _Foam.rgb, foam);
    float a = lerp(0.40, 0.95, saturate(dd / 1.2));
    a *= 1.0 - smoothstep(0.85, 1.0, edge) * 0.8;
    a = max(a, foam * 0.95); a *= i.fade;
    return half4(col, a);
   }
   ENDHLSL
  }
 }
}
