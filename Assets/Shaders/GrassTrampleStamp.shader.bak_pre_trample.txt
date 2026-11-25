Shader "Hidden/GrassTrampleStamp"
{
    Properties { _MainTex ("Src", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _StampCenter;
            float  _StampRadius;
            float  _StampStrength;
            float4 _StampPushDir;

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.posCS = TransformObjectToHClip(i.posOS.xyz);
                o.uv    = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 existing = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float dist   = length(i.uv - _StampCenter.xy);
                float circle = 1.0 - smoothstep(_StampRadius * 0.6, _StampRadius, dist);
                float press  = circle * _StampStrength;
                half2 newPush  = lerp(existing.rg, _StampPushDir.xy, circle * press);
                half  newPress = saturate(existing.b + press * (1.0 - existing.b));
                return half4(newPush.x, newPush.y, newPress, 1.0);
            }
            ENDHLSL
        }
    }
}
