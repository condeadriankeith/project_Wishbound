Shader "Custom/PlasmaOrbOuter"
{
    Properties
    {
        _ElectricTex ("Electric Texture", 2D) = "white" {}
        _ColorTint ("Color Tint (HDR)", Color) = (0.3, 0.6, 1.0, 1.0)
        _ScrollSpeed ("Scrolling Speed", Float) = 0.1
        _SurfaceMoveSpeed ("Surface Movement Speed", Float) = 0.01
        _NoiseScale ("Noise Scale", Float) = 0.1
        _DistortionSpeed ("Distortion Speed", Float) = 0.3
        _DistortionStrength ("Distortion Strength", Float) = 0.05
        _BorderSize ("Border Size (Fresnel Power)", Float) = 2.0
        _BorderPower ("Border Power", Float) = 2.0
        _BorderColor ("Border Color (HDR)", Color) = (0.6, 0.2, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_ElectricTex); SAMPLER(sampler_ElectricTex);
            float4 _ElectricTex_ST;
            float4 _ColorTint;
            float _ScrollSpeed;
            float _SurfaceMoveSpeed;
            float _NoiseScale;
            float _DistortionSpeed;
            float _DistortionStrength;
            float _BorderSize;
            float _BorderPower;
            float4 _BorderColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 screenPos    : TEXCOORD2;
                float3 viewDirWS    : TEXCOORD3;
            };

            // Simple hash-based 3D noise (stand-in for Shader Graph's Simple Noise node)
            float simpleNoise(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // --- Surface movement (vertex displacement) ---
                float3 objPos = IN.positionOS.xyz;
                float t = _Time.y * _SurfaceMoveSpeed;
                float n = simpleNoise(objPos * 4.0 + t) * _NoiseScale;
                float3 displaced = objPos + IN.normalOS * n;

                VertexPositionInputs vpi = GetVertexPositionInputs(displaced);
                OUT.positionHCS = vpi.positionCS;
                OUT.screenPos = ComputeScreenPos(vpi.positionCS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(vpi.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _ScrollSpeed;

                // --- Dual scrolling electric textures (opposite directions) ---
                float2 uv1 = IN.uv + float2(t, t);
                float2 uv2 = IN.uv + float2(-t, -t);
                half4 tex1 = SAMPLE_TEXTURE2D(_ElectricTex, sampler_ElectricTex, uv1);
                half4 tex2 = SAMPLE_TEXTURE2D(_ElectricTex, sampler_ElectricTex, uv2);
                half4 combinedTex = tex1 + tex2;
                half3 electricColor = combinedTex.rgb * _ColorTint.rgb;

                // --- Fresnel rim ---
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _BorderSize);
                float fresnelPowered = pow(fresnel, _BorderPower);
                half3 borderColor = _BorderColor.rgb * fresnelPowered;

                // --- Screen distortion (sample what's behind, offset by scrolling noise) ---
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float distortT = _Time.y * _DistortionSpeed;
                float distortNoise = simpleNoise(float3(IN.uv * 3.0, distortT)) * 2.0 - 1.0;
                float2 distortOffset = distortNoise * _DistortionStrength;
                half3 behindColor = SampleSceneColor(screenUV + distortOffset).rgb;

                half3 finalColor = behindColor + electricColor + borderColor;
                half alpha = saturate(fresnelPowered + (combinedTex.r + combinedTex.g + combinedTex.b) * 0.33);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
