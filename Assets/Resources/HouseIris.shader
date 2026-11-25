Shader "Wishbound/HouseIris"
{
 Properties { _Closure("Closure",Float)=0 _Aspect("Aspect",Float)=1 }
 SubShader { Tags { "Queue"="Overlay" "RenderType"="Transparent" } Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float _Closure,_Aspect;
 struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
 V vert(appdata_base v){V o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord.xy;return o;}
 fixed4 frag(V i):SV_Target {float2 scale=float2(_Aspect,1);float r=length((i.uv-.5)*scale);float edge=lerp(length(scale)*.5+.06,-.06,_Closure);return fixed4(.075,.095,.085,smoothstep(edge-.035,edge+.035,r));}
 ENDCG }
 } }
