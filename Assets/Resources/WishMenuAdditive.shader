Shader "Wishbound/Menu Additive" {
Properties { _MainTex ("Texture", 2D) = "black" {} }
SubShader {
Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
Pass {
Blend One One
Cull Off ZWrite Off ZTest Always
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
sampler2D _MainTex;
v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
fixed4 frag(v2f i):SV_Target { return fixed4(tex2D(_MainTex,i.uv).rgb*i.color.rgb*i.color.a,0); }
ENDCG
}
}
}