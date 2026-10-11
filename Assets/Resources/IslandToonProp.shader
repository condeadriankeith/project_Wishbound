Shader "Island/ToonProp"
{
 SubShader
 {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A { float4 p : POSITION; float3 n : NORMAL; float4 c : COLOR; };
   struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 c : COLOR; };
   V vert(A a) { V o; o.pos = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); o.c = a.c; return o; }
   half4 frag(V i) : SV_Target
   {
    Light l = GetMainLight();
    float nd = dot(normalize(i.n), l.direction) * 0.5 + 0.5;
    float b = floor(nd * 3.0) / 2.0;
    half3 lit = lerp(0.45, 1.0, min(b, 1.0)) * l.color + 0.12;
    return half4(i.c.rgb * lit, 1);
   }
   ENDHLSL
  }
 }
}
