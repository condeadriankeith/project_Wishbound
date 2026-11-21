Shader "Wishbound/CelestialOpening"
{
 Properties { _Tint("Tint",Color)=(.4,.7,1,1) _Mode("Mode",Float)=0 _Visibility("Visibility",Float)=1 _DstBlend("Destination blend",Float)=1 _Param("Param",Float)=0 }
 SubShader
 {
  Tags { "Queue"="Transparent+30" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha [_DstBlend]
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
   CBUFFER_START(UnityPerMaterial)
    float4 _Tint; float _Mode; float _Visibility; float _Param;
   CBUFFER_END
   Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;return o; }
   float hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p) {float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 frag(Varyings i):SV_Target
   {
    float2 p=(i.uv-.5)*2; float r=length(p);
    if(_Mode>7.5)
    {
     float ax=abs(p.x), ay=abs(p.y);
     if(_Mode<8.5)
     {
      // anamorphic star flare: long horizontal streak + short vertical cross + faint diagonals, chromatic fringe (cyan left -> magenta right)
      float hs=exp(-ay*70)*exp(-ax*1.9); float vs=exp(-ax*85)*exp(-ay*4.2)*.55;
      float ds=(exp(-abs(p.x-p.y)*55)+exp(-abs(p.x+p.y)*55))*exp(-r*5.5)*.2;
      float core=exp(-r*r*60); float shimmer=.85+.15*noise(float2(p.x*9-_Time.y*3,p.y*4));
      float3 fringe=lerp(float3(.25,.85,1),float3(1,.45,.9),saturate(p.x*.5+.5));
      float3 fcol=lerp(lerp(_Tint.rgb,fringe,.55),float3(1,.98,.95),saturate(core*1.4));
      float fe=smoothstep(1,.55,ax)*smoothstep(1,.55,ay);
      return half4(fcol*2.2,saturate(hs*shimmer+vs+ds+core*.6)*fe*_Tint.a*_Visibility);
     }
     if(_Mode<9.5)
     {
      // diamond twinkle sparkle (per-particle colour from vertex colour)
      float dia=saturate(1-(ax+ay)); float crs=exp(-ax*38)*exp(-ay*2.6)+exp(-ay*38)*exp(-ax*2.6); float sc=exp(-r*r*40);
      float sa=saturate(pow(dia,2.2)*1.3+crs*.8+sc);
      float3 scol=lerp(_Tint.rgb*i.color.rgb,float3(1,1,1),saturate(sc+dia*.5));
      return half4(scol*2.2,sa*smoothstep(1,.75,r)*_Tint.a*i.color.a*_Visibility);
     }
     // prismatic shockwave ring + starburst rays + soft flash. _Param = burst progress 0..1
     float bt=saturate(_Param); float ang=atan2(p.y,p.x);
     float rr=bt*.92; float rw=lerp(.09,.04,bt);
     float ring=exp(-pow((r-rr)/rw,2))*(1-bt);
     float rays=pow(saturate(abs(cos(ang*4+.35))),40)*exp(-r*3.2)*pow(1-bt,1.5);
     float h=frac(ang*2/6.2832+r*.9+_Time.y*.25);
     float3 rb=saturate(abs(frac(h+float3(0,2./3,1./3))*6-3)-1); rb=lerp(rb,float3(1,1,1),.35);
     float fl=exp(-r*r*9)*pow(1-bt,6)*.8;
     float3 bcol=lerp(rb,float3(1,.95,.9),saturate(fl));
     return half4(bcol*2.4,saturate(ring*1.5+rays*.9+fl)*_Tint.a*_Visibility*smoothstep(1,.8,r));
    }
    if(_Mode>4.5)
    {
     float along=i.uv.x; float across=abs(i.uv.y-.5)*2;
     if(_Mode<5.5)
     {
      // comet nucleus: white-hot pinpoint, tinted halo, faint anamorphic bar (no cross-shaped lens-flare spikes)
      float hot=exp(-r*r*420); float core=exp(-r*r*55); float halo=exp(-r*5.2)*.5;
      float bar=exp(-abs(p.y)*34)*exp(-abs(p.x)*2.4)*.3;
      float3 col=lerp(_Tint.rgb,float3(1,.97,.9),saturate(core*1.3+hot));
      return half4(col*(2.2+hot*4),saturate(core+halo+bar)*_Tint.a*_Visibility);
     }
     if(_Mode<6.5)
     {
      // ion tail: thin, straight, electric; u runs 0 (tail end) -> 1 (head)
      float aAlong=pow(saturate(along),1.6); float prof=exp(-across*across*3.4);
      float n=noise(float2(along*46+_Time.y*5.5,i.uv.y*7))*.6+noise(float2(along*110+_Time.y*9,i.uv.y*13))*.4;
      float hotHead=pow(saturate(along),7);
      float3 col=lerp(_Tint.rgb,float3(.9,.97,1),hotHead*.8);
      return half4(col*(1.6+hotHead*3),saturate(prof*aAlong*lerp(.55,1.25,n))*_Tint.a*_Visibility);
     }
     // dust tail: broad, smoky, warm; grain drifts slowly toward the tail end
     float dprof=smoothstep(1,0,across); dprof*=dprof;
     float dn=noise(float2(along*18+_Time.y*1.4,i.uv.y*5))*.55+noise(float2(along*40+_Time.y*2.2,i.uv.y*11))*.3+noise(float2(along*90,i.uv.y*23))*.15;
     float grain=smoothstep(.2,.85,dn);
     float dAlong=smoothstep(0,.35,along)*(.35+.65*pow(saturate(along),.8));
     float3 dcol=lerp(_Tint.rgb,float3(1,.9,.65),pow(saturate(along),3)*.6);
     return half4(dcol*1.4,saturate(dprof*grain*dAlong*1.4)*_Tint.a*_Visibility);
    }
    if(_Mode>3.5)
    {
     float2 uv=i.uv;
     float3 sky=lerp(float3(.12,.32,.62),float3(.012,.055,.19),smoothstep(.25,.85,uv.y));
     float2 q=float2(uv.x*8-_Time.y*.018,uv.y*13);
     float n=noise(q)*.55+noise(q*2.03)*.28+noise(q*4.01)*.13;
     float bank=smoothstep(.47,.68,n+(1-uv.y)*.34)*smoothstep(.6,.26,uv.y);
     float3 clouds=lerp(float3(.11,.23,.43),float3(.55,.72,.89),saturate(n*1.4));
     return half4(lerp(sky,clouds,bank),_Visibility);
    }
    if(_Mode<.5)
    {
     float core=exp(-r*r*230);
     float rays=pow(saturate(1-abs(p.x)),2)*exp(-abs(p.y)*90)+pow(saturate(1-abs(p.y)),3)*exp(-abs(p.x)*90);
     float glow=exp(-r*9)*.45;
     return half4(lerp(_Tint.rgb,float3(1,1,1),saturate(core+rays))*3, saturate(core+rays+glow)*_Visibility);
    }
    if(_Mode<1.5)
    {
     float angle=atan2(p.y,p.x)+r*3-_Time.y*.12;
     float n=noise(float2(angle*3,r*12))+noise(p*12+_Time.y*.03)*.4;
     float ring=exp(-pow((r-.58-n*.055)*6,2));
     float edge=smoothstep(1,.75,r)*smoothstep(.15,.42,r);
     return half4(lerp(_Tint.rgb,float3(.8,.9,1),saturate(n)),ring*edge*.32*_Visibility);
    }
    if(_Mode>2.5) return half4(_Tint.rgb*1.5,exp(-r*r*7)*smoothstep(1,.25,r)*_Tint.a*_Visibility);
    float wisps=noise(float2(i.uv.x*22-_Time.y*4,i.uv.y*8));
    float across=pow(saturate(1-abs(p.y)),2);
    float along=smoothstep(0,.15,i.uv.x);
    return half4(_Tint.rgb*2,across*lerp(.5,1,wisps)*_Tint.a*_Visibility);
   }
   ENDHLSL
  }
 }
}
