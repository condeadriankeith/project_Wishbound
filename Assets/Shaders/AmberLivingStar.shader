Shader "Ghibli/AmberLivingStar" {
Properties { _Color("Amber",Color)=(1,.34,.025,1) _Emission("Glow",Float)=1.15 }
SubShader { Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Pass {
Tags {"LightMode"="UniversalForward"}
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _Color; float _Emission;
CBUFFER_END
struct A{float4 p:POSITION;float3 n:NORMAL;};
struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 world:TEXCOORD1;float3 local:TEXCOORD2;};
V vert(A i){V o;float3 p=i.p.xyz;float t=_Time.y;float f=sin(p.x*9+t)*sin(p.y*8-t*.7)*sin(p.z*9+t*.8);p+=i.n*f*.012;o.world=TransformObjectToWorld(p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.local=p;return o;}
float4 frag(V i):SV_Target {
 float3 n=normalize(i.n),v=normalize(GetWorldSpaceViewDir(i.world));
 float facing=saturate(dot(n,v));
 float light=dot(n,normalize(float3(-.4,.8,-.3)))*.5+.5;
 float bands=smoothstep(.30,.34,light)*.40+smoothstep(.68,.72,light)*.60;
 float3 gold=lerp(_Color.rgb*float3(.72,.43,.25),float3(1,.66,.12),bands);
 float flow=sin(i.local.y*17+sin(i.local.x*11+_Time.y*.8)*1.4+sin(i.local.z*13-_Time.y*.6));
 float veins=smoothstep(.86,.98,flow)*.16;
 float core=smoothstep(.70,.96,facing)*.38;
 gold=lerp(gold,float3(1,.88,.45),core+veins);
 float rim=pow(1-facing,3);
 return float4(gold*(.8+_Emission*.48)+float3(1,.46,.04)*rim*_Emission*.85,1);
}
ENDHLSL
} } }
