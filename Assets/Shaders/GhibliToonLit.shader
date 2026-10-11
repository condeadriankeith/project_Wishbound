Shader "Ghibli/ToonLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _InstColor ("Instance Color", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 0
        _BrushScale ("Brush Scale", Float) = 0.45
        _BrushStrength ("Brush Strength", Range(0,0.5)) = 0.24
        _RimColor ("Rim Color", Color) = (0.35,0.52,0.75,1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.14
        _RimPower ("Rim Power", Float) = 3.2
        _WindStrength ("Wind Strength", Float) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Fade ("Fade", Float) = 1.0
        _CliffSmoothness ("Cliff Normal Smoothness", Range(0,1)) = 0.5
        _ShoreAA ("Shoreline Anti-Aliasing", Range(0,1)) = 0.5
        _HeightSmooth ("Height-based Normal Smoothing", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 _Color, _RimColor, _EmissionColor;
            float _UseVertexColor, _BrushScale, _BrushStrength, _RimStrength, _RimPower, _WindStrength, _Fade, _CliffSmoothness, _ShoreAA, _HeightSmooth;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            float4 _HemiSky, _HemiGround;
            float  _HemiIntensity;
            struct Attributes { float4 posOS:POSITION; float3 nOS:NORMAL; half4 col:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 posCS:SV_POSITION; float3 posWS:TEXCOORD0; float3 nWS:TEXCOORD1;
                half4 col:COLOR; half fog:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            float hz(float2 p){ return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float bn(float2 p){
                float2 i=floor(p), f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(hz(i),hz(i+float2(1,0)),f.x), lerp(hz(i+float2(0,1)),hz(i+float2(1,1)),f.x), f.y);
            }
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = v.posOS.xyz;
                if (_WindStrength > 0)
                {
                    float ph = unity_ObjectToWorld._m03 * 0.08 + unity_ObjectToWorld._m23 * 0.05;
                    float t = _Time.y;
                    p.x += (sin(t*1.9+ph) + 0.5*sin(t*3.3+ph*2.0)) * p.y * 0.18 * _WindStrength;
                    p.z += cos(t*1.6+ph) * p.y * 0.12 * _WindStrength;
                }
                o.posWS = TransformObjectToWorld(p);
                o.nWS = TransformObjectToWorldNormal(v.nOS);
                
                // Smooth cliff normals: blend with upward normal based on cliff steepness
                float cliffSteep = 1.0 - saturate(o.nWS.y);
                float3 upNormal = float3(0, 1, 0);
                o.nWS = lerp(o.nWS, upNormal, cliffSteep * _CliffSmoothness * 0.3);
                
                // Height-based normal smoothing: use vertex color (height encoded in R/G/B) to detect elevation changes
                if (_HeightSmooth > 0.0)
                {
                    // Vertex color R channel typically encodes height/biome
                    float heightFactor = saturate(v.col.r * 2.0);
                    float3 smoothedNormal = lerp(o.nWS, upNormal, heightFactor * _HeightSmooth * 0.2);
                    o.nWS = lerp(o.nWS, smoothedNormal, _HeightSmooth);
                }
                
                o.posCS = TransformWorldToHClip(o.posWS);
                o.col = v.col;
                o.fog = ComputeFogFactor(o.posCS.z);
                return o;
            }
            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(_Fade - 0.001);
                float3 N = normalize(i.nWS);
                N = IS_FRONT_VFACE(face, N, -N);
                half3 albedo = _Color.rgb * UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).rgb;
                albedo = lerp(albedo, albedo * i.col.rgb, _UseVertexColor);
                float br = bn(i.posWS.xz * _BrushScale + i.posWS.y * 0.2) * 0.6 + bn(i.posWS.xz * 1.8) * 0.4;
                albedo *= (1.0 - 0.5 * _BrushStrength) + _BrushStrength * br;
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float v = saturate(dot(N, L.direction) * 0.5 + 0.5);
                
                // Smooth toon ramp with configurable bands - reduces banding on cliffs
                float smoothV = v;
                if (_ShoreAA > 0.0)
                {
                    // Detect near-shoreline by vertex color (blue channel used for water proximity)
                    float shoreProximity = saturate(1.0 - i.col.b * 2.0);
                    smoothV = lerp(v, smoothstep(0.0, 1.0, v), shoreProximity * _ShoreAA);
                }
                
                float ramp = smoothV < 0.25 ? 0.3137 : smoothV < 0.5 ? 0.5490 : smoothV < 0.75 ? 0.8039 : 1.0;
                half3 direct = L.color * ramp * L.shadowAttenuation;
                half3 hemi = lerp(_HemiGround.rgb, _HemiSky.rgb, N.y * 0.5 + 0.5) * _HemiIntensity;
                half3 col = albedo * (direct + hemi) + _EmissionColor.rgb;
                float3 Vv = normalize(GetWorldSpaceViewDir(i.posWS));
                col += pow(1.0 - saturate(dot(N, Vv)), _RimPower) * _RimColor.rgb * _RimStrength;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
