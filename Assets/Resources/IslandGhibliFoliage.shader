// Ghibli-style toon foliage. Unity port of the Blender node setup:
//   Diffuse BSDF -> Shader To RGB -> Color Ramp (CONSTANT, 4 stops) -> Surface
// Normals come from the mesh (baked "custom normals": each blob's normals point away from the canopy centre,
// so the chunky low-poly blobs light like one big soft sphere).
Shader "Island/GhibliFoliage"
{
    Properties
    {
        _Col0 ("Ramp 0 (dark)", Color) = (0.25, 0.38, 0.22, 1)
        _Col1 ("Ramp 1 (mid-dark)", Color) = (0.38, 0.57, 0.31, 1)
        _Col2 ("Ramp 2 (mid)", Color) = (0.54, 0.73, 0.43, 1)
        _Col3 ("Ramp 3 (highlight)", Color) = (0.77, 0.93, 0.64, 1)
        _Step1 ("Stop 1 position", Range(0,1)) = 0.25
        _Step2 ("Stop 2 position", Range(0,1)) = 0.45
        _Step3 ("Stop 3 position", Range(0,1)) = 0.85
        _LightGain ("Light gain", Float) = 1.6
        _RimStrength ("Rim strength", Range(0,1)) = 0.45
        _Sway ("Sway", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Col0, _Col1, _Col2, _Col3;
                float _Step1, _Step2, _Step3;
                float _LightGain, _RimStrength, _Sway;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; float4 col : COLOR; float fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                ws.xz += sin(_Time.y * 1.4 + ws.x * 0.04 + ws.z * 0.04) * _Sway * i.col.a;
                o.ws = ws;
                o.cs = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.n);
                o.col = i.col;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float3 n = normalize(i.n);
                float3 v = normalize(GetCameraPositionWS() - i.ws);

                // "Shader to RGB" equivalent: plain lambert * shadow, scaled by light gain
                float lit = saturate(dot(n, l.direction) * 0.8 + 0.2) * lerp(0.55, 1.0, l.shadowAttenuation) * _LightGain;
                // rim light (Blender rim sun from behind): grazing angles on the sun-facing side
                float rim = pow(1.0 - saturate(dot(n, v)), 2.5) * saturate(dot(n, l.direction) + 0.6) * _RimStrength;
                // vertex colour .r = baked occlusion (darker deep inside the canopy)
                float t = saturate(lit + rim) * lerp(0.78, 1.0, i.col.r);

                // constant colour ramp
                float3 c = _Col0.rgb;
                c = t >= _Step1 ? _Col1.rgb : c;
                c = t >= _Step2 ? _Col2.rgb : c;
                c = t >= _Step3 ? _Col3.rgb : c;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}
