Shader "Ghibli/LivingSky" {
Properties {
 _Top("Zenith",Color)=(.1,.3,.7,1)
 _Hor("Horizon",Color)=(.6,.8,1,1)
 _Cloud("Cloud",Color)=(1,1,1,1)
 _CloudShade("Cloud shade tint",Color)=(.58,.64,.8,1)
 _CloudCover("Cloud cover",Range(0,1))=.55
 _Night("Night",Range(0,1))=0
 _SunDir("Sun direction",Vector)=(0,1,0,0)
}
SubShader {
 Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
 Cull Off ZWrite Off
 Pass {
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float4 _Top,_Hor,_Cloud,_CloudShade,_SunDir; float _Night,_CloudCover;
float4 _GhibliFogColor;
struct A{float4 p:POSITION;};
struct V{float4 p:SV_POSITION;float3 d:TEXCOORD0;};
V vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.d=v.p.xyz;return o;}

float hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
float noise(float3 p){
 float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
 return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
             lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+float3(1,1,1)),f.x),f.y),f.z);
}
float fbm(float3 p){
 float a=.5,s=0;
 [unroll] for(int k=0;k<4;k++){s+=noise(p)*a;p=p*2.03+float3(11.7,3.1,7.3);a*=.5;}
 return s;
}
// Surface-distributed stars, with derivative antialiasing.
float3 starLayer(float3 d,float scale,float thr,float size,float bright){
 float2 uv=d.xz/(abs(d.x)+abs(d.y)+abs(d.z));
 float2 g=uv*scale, cell=floor(g), f=frac(g);
 float3 result=0;
 float aa=max(length(fwidth(g))*.45,.012);
 [unroll] for(int x=-1;x<=1;x++) [unroll] for(int y=-1;y<=1;y++){
  float2 id=cell+float2(x,y);float3 key=float3(id,scale);
  float r=hash(key);
  float2 center=float2(hash(key+3.1),hash(key+7.7));
  float dist=length(f-float2(x,y)-center);
  float m=1-smoothstep(size,size+aa,dist);
  float tw=.82+.18*sin(_Time.y*(.7+r*1.8)+r*100);
  result+=lerp(float3(.65,.8,1),float3(1,.85,.62),hash(key+17.9))*m*step(thr,r)*bright*tw;
 }
 return result;
}

float4 frag(V i):SV_Target{
 float3 d=normalize(i.d);
 float h=d.y;float hp=saturate(h);
 float3 sd=normalize(_SunDir.xyz);
 float day=1-_Night;
 float sunDot=dot(d,sd);
 float golden=saturate(1-abs(sd.y)*3.2)*day;

 // ---- gradient
 float3 col=lerp(_Hor.rgb,_Top.rgb,pow(hp,.5));
 col=lerp(col,_Hor.rgb*.55,1-smoothstep(-.25,0,h));

 // ---- sunrise / sunset glow on the sun side of the horizon
 float hz=exp(-abs(h)*6);
 float sunSide=saturate(sunDot*.5+.5);
 col+=float3(1,.45,.18)*golden*hz*pow(sunSide,3)*.55;
 col+=float3(1,.7,.35)*day*(pow(saturate(sunDot),12)*.18+pow(saturate(sunDot),180)*.6);
 col+=float3(1,.92,.62)*day*smoothstep(.99935,.9997,sunDot)*3;

 // ---- moon
 float3 md=-sd;float moonDot=dot(d,md);
 float mdisc=smoothstep(.9990,.9994,moonDot)*smoothstep(-.02,.02,h);
 float3 mp=d*45;float crater=noise(mp)*.5+noise(mp*2.7)*.3;
 col+=_Night*(float3(.78,.86,1)*mdisc*(.8+crater*.5)*1.8+float3(.35,.5,.9)*pow(saturate(moonDot),90)*.35);

 // ---- clouds : warped fbm, toon-lit toward the sun, silver lining
 float hc=max(h,0)+.18;
 float2 pp=d.xz/hc*.85;
 float t=_Time.y;
 float3 q=float3(pp*.9+float2(t*.012,t*.005),t*.004);
 float warp=fbm(q*.8+7.3);
 float3 qw=q+float3(warp,warp*.6,0)*.55;
 float n=fbm(qw*1.25);
 float thr=lerp(.64,.40,_CloudCover);
 float cloud=smoothstep(thr,thr+.09,n)*smoothstep(0,.1,h);
 float2 sp=normalize(sd.xz+float2(1e-4,0));
 float ns=fbm((qw+float3(sp*.18,0))*1.25);
 float lighting=saturate(.5+(n-ns)*7);
 float lit=.20+.45*smoothstep(.36,.42,lighting)+.35*smoothstep(.62,.68,lighting);
 float dens=saturate((n-thr)/.25);
 float3 lightCol=_Cloud.rgb;
 float3 shadeCol=_Cloud.rgb*_CloudShade.rgb;
 shadeCol=lerp(shadeCol,shadeCol*float3(1.15,.8,.75),golden);
 float3 cloudCol=lerp(shadeCol,lightCol,lit);
 cloudCol*=lerp(1,.82,dens*(1-lit));
 float edge=(1-smoothstep(thr+.02,thr+.2,n))*cloud;
 cloudCol+=float3(1,.82,.55)*edge*pow(saturate(sunDot),4)*day*1.3;
 cloudCol+=float3(.5,.65,1)*edge*pow(saturate(moonDot),6)*_Night*.6;
 cloudCol=lerp(_Hor.rgb,cloudCol,smoothstep(0,.4,h)*.8+.2);
 col=lerp(col,cloudCol,cloud*.96);

 // ---- high cirrus streaks
 float3 cq=float3(d.x*.6,d.z*2.4,t*.003)/(hc+.2)*1.5;
 float cir=smoothstep(.6,.85,fbm(cq))*.18*smoothstep(0,.3,h);
 col=lerp(col,_Cloud.rgb*1.05,cir*(1-cloud));

 // ---- stars + milky way (hidden by clouds)
 float vis=_Night*smoothstep(-.02,.2,h)*(1-cloud)*(1-cir*.6);
 float3 gal=normalize(float3(.35,.78,.52));
 float gb=dot(d,gal);
 float band=exp(-gb*gb*14);
 float dust=fbm(d*5+3)*fbm(d*11);
 float3 mwCol=lerp(float3(.25,.3,.6),float3(.7,.5,.55),fbm(d*3+9));
 col+=mwCol*band*(.08+dust*1.6)*.16*vis;
 float3 st=starLayer(d,65,.965,.075,2.2)+starLayer(d,145,.975,.045,1.25)+starLayer(d,260,.984-band*.025,.025,.65);
 col+=st*vis;

 // ---- horizon haze: the sky melts into the SAME fog colour the ocean fades to, so there is no visible water/sky seam
 float haze=1-smoothstep(.01,.17,h);
 col=lerp(col,_GhibliFogColor.rgb,haze);
 col+=(hash(float3(i.p.xy,1))-.5)/200;
 return float4(max(col,0),1);
}
ENDHLSL
 }
}
}
