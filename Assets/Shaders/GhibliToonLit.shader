Shader "Ghibli/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Painted albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _InstColor ("Instance Color", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use Vertex Color", Float) = 0
        [HideInInspector] _ProceduralGround ("Procedural Ground", Float) = 0
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
        _LocalAddLights ("Receive Point Lights", Range(0,1)) = 1
        _SpecStrength ("Toon Specular", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.45
        _ReflectStrength ("Environment Reflection", Range(0,1)) = 0
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
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 _Color, _RimColor, _EmissionColor;
            float _UseVertexColor, _ProceduralGround, _BrushScale, _BrushStrength, _RimStrength, _RimPower, _WindStrength, _Fade, _CliffSmoothness, _ShoreAA, _HeightSmooth, _LocalAddLights, _SpecStrength, _Smoothness, _ReflectStrength;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            float4 _HemiSky, _HemiGround;
            float  _HemiIntensity;
            float  _ToonAddLights;   // global: 1 inside houses (point lights light the toon surfaces), 0 outside
            struct Attributes
            {
                float4 posOS:POSITION; float3 nOS:NORMAL; half4 col:COLOR;
                float2 uv:TEXCOORD0; float4 ground:TEXCOORD1; float4 field:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 posCS:SV_POSITION; float3 posWS:TEXCOORD0; float3 nWS:TEXCOORD1;
                half4 col:COLOR; half fog:TEXCOORD2;
                float2 uv:TEXCOORD5; float4 ground:TEXCOORD3; float4 field:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            float hz(float2 p){ return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float bn(float2 p){
                float2 i=floor(p), f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(hz(i),hz(i+float2(1,0)),f.x), lerp(hz(i+float2(0,1)),hz(i+float2(1,1)),f.x), f.y);
            }
            float3 GroundColor(float2 p, float4 ground, float4 field)
            {
                // Same palette and blend order as IslandBuilder.PaintVertices, evaluated on the surface.
                float h = ground.x;
                float wet = saturate(ground.w);
                float grass = .65 * bn(p / 24.0) + .35 * bn(p * .15);
                float3 color = lerp(float3(72,150,38), float3(45,104,22), grass) / 255.0;
                color = color * (1.0 - field.a) + field.rgb;

                float cliff = 1.0 - smoothstep(.58,.82,ground.y);
                // Evaluate derivatives uniformly across neighboring fragments at the cliff edge.
                float phase = h * 1.5 + bn(p * .05) * 6.0;
                float detail = 1.0 - smoothstep(.5,3.14,fwidth(phase));
                if (cliff > 0.0)
                {
                    float strata = smoothstep(.35,.65,.5 + .5 * sin(phase) * detail);
                    float3 chalk = lerp(float3(251,251,246),float3(217,211,197),strata * .75) / 255.0;
                    float moss = smoothstep(.55,.85,bn(p * .1 + h * float2(.03,.04))) * .45 * lerp(.3,1.0,smoothstep(14.5,15.5,h));
                    chalk = lerp(chalk,float3(113,156,64) / 255.0,moss);
                    color = lerp(color,chalk,smoothstep(0.0,1.0,cliff));
                }

                float sand = (1.0 - smoothstep(1.1,3.2,h)) * (1.0 - cliff);
                float shore = smoothstep(1.0,3.0,h);
                color = lerp(color,float3(240,230,203) / 255.0,lerp(sand,sand * shore,shore));
                color = lerp(color,float3(39,141,157) / 255.0,1.0 - smoothstep(-8.0,.8,h));
                if (ground.z > 0.0)
                {
                    float3 dirt = lerp(float3(186,155,109),float3(138,114,77),bn(p * .4)) / 255.0;
                    color = lerp(color,dirt,saturate(ground.z) * smoothstep(1.2,1.4,h) * lerp(1.0,.25,cliff));
                }
                return lerp(color,float3(.40,.36,.29),wet * .55) * (1.0 - wet * .18);
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
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.col = v.col;
                o.ground = v.ground;
                o.field = v.field;
                o.fog = ComputeFogFactor(o.posCS.z);
                return o;
            }
            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(_Fade - 0.001);
                float3 N = normalize(i.nWS);
                N = IS_FRONT_VFACE(face, N, -N);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb * _Color.rgb * UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).rgb;
                bool paintGround = abs(_ProceduralGround) > .5;
                float3 groundColor = i.col.rgb;
                if (paintGround)
                    groundColor = GroundColor(float2(i.posWS.x,i.posWS.z * _ProceduralGround), i.ground, i.field);
                albedo *= paintGround ? groundColor : lerp(float3(1,1,1), groundColor, _UseVertexColor);
                float br = bn(i.posWS.xz * _BrushScale + i.posWS.y * 0.2) * 0.6 + bn(i.posWS.xz * 1.8) * 0.4;
                albedo *= (1.0 - 0.5 * _BrushStrength) + _BrushStrength * br;
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float v = saturate(dot(N, L.direction) * 0.5 + 0.5);
                
                // Smooth toon ramp with configurable bands - reduces banding on cliffs
                float smoothV = v;
                if (_ShoreAA > 0.0)
                {
                    // Detect near-shoreline by vertex color (blue channel used for water proximity)
                    float shoreProximity = saturate(1.0 - groundColor.b * 2.0);
                    smoothV = lerp(v, smoothstep(0.0, 1.0, v), shoreProximity * _ShoreAA);
                }
                
                float ramp = smoothV < 0.25 ? 0.3137 : smoothV < 0.5 ? 0.5490 : smoothV < 0.75 ? 0.8039 : 1.0;
                if (paintGround)
                {
                    float aa = max(fwidth(smoothV), .005);
                    ramp = .3137 + .2353 * smoothstep(.25-aa,.25+aa,smoothV)
                                 + .2549 * smoothstep(.50-aa,.50+aa,smoothV)
                                 + .1961 * smoothstep(.75-aa,.75+aa,smoothV);
                }
                half3 direct = L.color * ramp * L.shadowAttenuation;
                half3 hemi = lerp(_HemiGround.rgb, _HemiSky.rgb, N.y * 0.5 + 0.5) * _HemiIntensity;
                float3 Vv0 = normalize(GetWorldSpaceViewDir(i.posWS)); float specPow = exp2(1.0 + 9.0*_Smoothness); half3 spec = 0;
                if (_SpecStrength > 0.001 && !paintGround) { float3 Hm = normalize(L.direction + Vv0); spec += L.color * smoothstep(.35,.5,pow(saturate(dot(N,Hm)), specPow)) * L.shadowAttenuation; }
                half3 extra = 0;
                float addW = max(max(_ToonAddLights, _LocalAddLights), paintGround ? .55 : 0);
                if (addW > 0.001)
                {
#if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.posWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.posCS);
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light al = GetAdditionalLight(lightIndex, i.posWS, half4(1,1,1,1));
                        float av = saturate(dot(N, al.direction) * 0.5 + 0.5);
                        float ar = av < 0.25 ? 0.3137 : av < 0.5 ? 0.5490 : av < 0.75 ? 0.8039 : 1.0;
                        extra += al.color * (ar * al.distanceAttenuation * al.shadowAttenuation);
                        if (_SpecStrength > 0.001 && !paintGround) { float3 Ha = normalize(al.direction + Vv0); spec += al.color * smoothstep(.35,.5,pow(saturate(dot(N,Ha)), specPow)) * al.distanceAttenuation * al.shadowAttenuation; }
                    LIGHT_LOOP_END
#endif
                    extra *= addW;
                }
                half3 col = albedo * (direct + hemi + extra) + _EmissionColor.rgb;
                float3 Vv = Vv0;
                col += spec * _SpecStrength;
                if (_ReflectStrength > 0.001 && !paintGround) { float3 Rv = reflect(-Vv, N); half3 env = GlossyEnvironmentReflection(Rv, 1.0 - _Smoothness, 1.0); col += env * _ReflectStrength * (0.25 + 0.75 * pow(1.0 - saturate(dot(N,Vv)), 3.0)); }
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
