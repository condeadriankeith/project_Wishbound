Shader "Shader Graphs/StylizedGrass"
{
    Properties
    {
        _MainTex("Main Tex", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0,0,0,1)
        _Smoothness("Smoothness", Float) = 0
        _AlphaClip("Alpha Clip Threshold", Float) = 0.5
        _NearFarRange("Near Far Range", Vector) = (10, 25, 0, 0)
        _NearColor("Near Color", Color) = (0.788, 0.831, 0.29, 1)
        _FarColor("Far Color", Color) = (0.249, 0.561, 0.227, 1)
        _HeightBlend("Height Blend", Float) = 1
        _BottomColor("Bottom Color", Color) = (0.118, 0.29, 0.133, 1)
        _TerrainSize("Terrain Size", Float) = 50
        _TerrainOffset("Terrain Offset", Float) = 0
        _TerrainColorTex("Terrain Color Tex", 2D) = "white" {}
        _UseTerrainColor("Use Terrain Color", Float) = 1
        _ShadowColor("Shadow Color", Color) = (0.29, 0.29, 0.55, 1)
        _WindSpeed("Wind Speed", Float) = 0.1
        _WindIntensity("Wind Intensity", Float) = 0.2
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindNoiseScale("Wind Noise Scale", Vector) = (40, 100, 0, 0)
        _WindNoiseSpeed("Wind Noise Speed", Float) = 1.0
        _WindNoiseContrast("Wind Noise Contrast", Vector) = (0.3, 0.7, 0, 0)
        _WindHeight("Wind Height", Float) = 0.8
        _Fade("Fade", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:GrassSetup
            #pragma instancing_options renderinglayer
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_TerrainColorTex); SAMPLER(sampler_TerrainColorTex);
            TEXTURE2D(_WindTexture); SAMPLER(sampler_WindTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Smoothness;
                float _AlphaClip;
                float4 _NearFarRange;
                float4 _NearColor;
                float4 _FarColor;
                float _HeightBlend;
                float4 _BottomColor;
                float _TerrainSize;
                float _TerrainOffset;
                float _UseTerrainColor;
                float4 _ShadowColor;
                float _WindSpeed;
                float _WindIntensity;
                float4 _WindNoiseScale;
                float _WindNoiseSpeed;
                float4 _WindNoiseContrast;
                float _WindHeight;
            float _Fade;
            CBUFFER_END

            // Trample globals (set by GrassTrampleSystem each frame)
            TEXTURE2D(_GrassTrampleTex); SAMPLER(sampler_GrassTrampleTex);
            float4 _GrassTrampleOrigin; // xz world origin of the 128m window
            float  _GrassTrampleSize;   // window size in metres

            float SG_GradientNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float2 blur = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i + float2(0.0,0.0), float2(12.9898,78.233))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1.0,0.0), float2(12.9898,78.233))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0.0,1.0), float2(12.9898,78.233))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1.0,1.0), float2(12.9898,78.233))) * 43758.5453);
                return lerp(lerp(a, b, blur.x), lerp(c, d, blur.x), blur.y);
            }

            float2 GetWindUV(float3 worldPos)
            {
                float2 wndScale = float2(max(_WindNoiseScale.x, 0.001), max(_WindNoiseScale.y, 0.001));
                float2 wxz = worldPos.xz / wndScale;
                float t = _Time.y / 20.0 * _WindNoiseSpeed;
                wxz += float2(t, t * 0.5);
                return wxz;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
 StructuredBuffer<float4x4> _GrassMatrices;
#endif
 float _GrassStart;
 void GrassSetup()
 {
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
  unity_ObjectToWorld = _GrassMatrices[(uint)_GrassStart + unity_InstanceID];
  unity_WorldToObject = unity_ObjectToWorld;
#endif
 }
Varyings LitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = input.positionOS.xyz;
                float4 wpos4 = mul(UNITY_MATRIX_M, float4(posOS, 1.0));
                // For GPU-instanced detail meshes UNITY_MATRIX_M already includes instance transform in URP? Use GetObjectToWorldMatrix for safety
                #ifdef UNITY_INSTANCING_ENABLED
                    float4x4 objToWorld = GetObjectToWorldMatrix();
                    wpos4 = mul(objToWorld, float4(posOS, 1.0));
                #endif
                float3 worldPos = wpos4.xyz;

                // Idle sway: Time * WindSpeed -> gradient noise scale 5
                float2 swayUV = float2(_Time.y * _WindSpeed, 0.0);
                float gnoise = SG_GradientNoise(swayUV * 5.0 + worldPos.xz * 0.1);
                gnoise = (gnoise - 0.5) * 2.0;
                float3 sway = gnoise * _WindIntensity;

                // Wind waves (vertex stage must use LOD sample)
                float2 windUV = GetWindUV(worldPos);
                float windSample = SAMPLE_TEXTURE2D_LOD(_WindTexture, sampler_WindTexture, windUV, 0).r;
                float windMask = smoothstep(_WindNoiseContrast.x, _WindNoiseContrast.y, windSample);
                float windOffset = windMask * -_WindHeight;

                float3 combinedXZ = (sway + float3(windOffset * 0.5, 0.0, windOffset * 0.5));
                combinedXZ.y = 0.0;

                // Pin roots: fade to zero at base using uv.y (0 root, 1 tip)
                float heightMask = saturate(input.uv.y / max(_HeightBlend, 0.001));
                // If HeightBlend ~1, uv.y 0..1 gives 0..1. Clamp.
                heightMask = saturate((input.uv.y) / max(_HeightBlend, 0.0001));
                // Reuse smooth height gradient feel
                heightMask = smoothstep(0.0, 1.0, saturate(input.uv.y / max(_HeightBlend, 0.0001) + 0.0001) );
                // Simple: use uv.y directly when HeightBlend==1
                // Keep at least small root lock:
                float rootPin = saturate(input.uv.y);

                float3 offsetOS = combinedXZ * rootPin;

                // Transform offset (world XZ) back to object: approximate by adding in world then back
                // Since grass is small and mostly unrotated, adding world offset to world pos is fine.
                worldPos += combinedXZ * rootPin;

                // Trample displacement: bend blades in the stamped push direction.
                // Only runs for blades inside the current 128m camera window.
                if (_GrassTrampleSize > 0.0)
                {
                    float2 trampleUV = float2(
                        (worldPos.x - _GrassTrampleOrigin.x) / _GrassTrampleSize,
                        (worldPos.z - _GrassTrampleOrigin.y) / _GrassTrampleSize
                    );
                    if (all(trampleUV >= 0.0) && all(trampleUV <= 1.0))
                    {
                        half4 trample   = SAMPLE_TEXTURE2D_LOD(_GrassTrampleTex, sampler_GrassTrampleTex, trampleUV, 0);
                        float press     = trample.b;
                        float2 pushDir  = trample.rg * 2.0 - 1.0; // unpack 0..1 -> -1..1
                        // Bend tip in push direction, scaled by press and root-pin mask
                        worldPos.xz += pushDir * press * rootPin * 0.6;
                        worldPos.y  -= press * rootPin * 0.3; // slight lean-down
                    }
                }

                // Recompute clip pos from modified world pos
                float4 clipPos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                #ifdef UNITY_INSTANCING_ENABLED
                    // UNITY_MATRIX_VP already handles instancing via world pos above
                #endif
                output.positionCS = clipPos;
                output.uv = input.uv;
                output.positionWS = worldPos;
                output.fogFactor = ComputeFogFactor(clipPos.z);
                return output;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                float3 worldPos = input.positionWS;

                float4 mainSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(mainSample.a - _AlphaClip);
                
                clip(_Fade - 0.001);

                // Near/far depth gradient
                float viewDist = distance(_WorldSpaceCameraPos, worldPos);
                float d = smoothstep(_NearFarRange.x, _NearFarRange.y, viewDist);
                d = 1.0 - d;
                float3 depthColor = lerp(_FarColor.rgb, _NearColor.rgb, d);

                // Terrain color (world XZ / size + offset)
                float2 tuv = worldPos.xz / max(_TerrainSize, 0.001) + _TerrainOffset;
                float3 terrainColor = SAMPLE_TEXTURE2D(_TerrainColorTex, sampler_TerrainColorTex, tuv).rgb;

                // Shadow attenuation via main light
                float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                float shadowAtten = mainLight.shadowAttenuation;

                float3 shadowedTerrain = lerp(terrainColor * _ShadowColor.rgb * 2.0, terrainColor, shadowAtten);
                // *2 to keep vivid (guide boosts shadow color); clamp later by lighting

                // Height gradient (uv.y based, matches vertex root pin)
                float hb = max(_HeightBlend, 0.0001);
                float heightGrad = smoothstep(0.0, hb, uv.y + 0.0001);
                // When HeightBlend==1, smoothstep(0,1,uv.y)

                // Wind mask again in fragment for color
                float2 windUV = GetWindUV(worldPos);
                float windSample = SAMPLE_TEXTURE2D(_WindTexture, sampler_WindTexture, windUV).r;
                float windMask = smoothstep(_WindNoiseContrast.x, _WindNoiseContrast.y, windSample);

                float3 depthWind = saturate(depthColor + windMask * 0.6);
                depthWind *= heightGrad;

                float useT = step(0.5, _UseTerrainColor);
                float3 bottomSel = lerp(_BottomColor.rgb, shadowedTerrain, useT);

                float3 topColor;
                if (_UseTerrainColor > 0.5)
                    topColor = depthWind * 0.25 + shadowedTerrain;
                else
                    topColor = saturate(depthColor + windMask * 0.6);

                // If using terrain color, wind already in depthWind; ensure top includes wind highlight on tips
                float3 finalColor = lerp(bottomSel, topColor, heightGrad);
                finalColor += _BaseColor.rgb; // Base is black normally

                // Clamp luminance so wind highlight + chalk terrain tint cannot saturate to white.
                // Rec.709 luma weights.
                float luma = dot(finalColor, float3(0.2126, 0.7152, 0.0722));
                if (luma > 0.88)
                    finalColor *= 0.88 / max(luma, 0.001);

                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:GrassSetup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _AlphaClip;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float3 _LightDirection;
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
 StructuredBuffer<float4x4> _GrassMatrices;
#endif
 float _GrassStart;
 void GrassSetup()
 {
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
  unity_ObjectToWorld = _GrassMatrices[(uint)_GrassStart + unity_InstanceID];
  unity_WorldToObject = unity_ObjectToWorld;
#endif
 }
Varyings ShadowPassVertex(Attributes input)
            {
                Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,o);
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = posCS;
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return o;
            }
            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(a - _AlphaClip);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:GrassSetup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float _AlphaClip;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
 StructuredBuffer<float4x4> _GrassMatrices;
#endif
 float _GrassStart;
 void GrassSetup()
 {
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
  unity_ObjectToWorld = _GrassMatrices[(uint)_GrassStart + unity_InstanceID];
  unity_WorldToObject = unity_ObjectToWorld;
#endif
 }
Varyings DepthOnlyVertex(Attributes input){ Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,o); o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=TRANSFORM_TEX(input.uv,_MainTex); return o; }
            half4 DepthOnlyFragment(Varyings input):SV_Target{ UNITY_SETUP_INSTANCE_ID(input); float a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).a; clip(a-_AlphaClip); return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
