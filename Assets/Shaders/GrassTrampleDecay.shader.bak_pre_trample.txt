Shader "Hidden/GrassTrampleDecay"
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
            float _DecayFactor;

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
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // B = press amount; decay toward 0. R/G = push direction, leave unchanged.
                c.b = max(0.0, c.b - _DecayFactor * c.b);
                return c;
            }
            ENDHLSL
        }
    }
}
