// Ghibli grass clump shader: re-creates the look of the package 'Shader Graphs/Grass' (bottom->top gradient, colour
// variation texture, scrolling wind lines, wind sway) and adds what the package graph lacks:
//   * trample: blades bend away from / flatten under whatever GrassTrampleSystem stamped (global _GrassTrampleTex)
//   * smooth LOD: the near and thinned 'far' clump layers cross-fade with a screen-space dither (complementary
//     thresholds, so there is never a hole or a pop), and the far edge dithers out while the clumps shrink
//   * a darker root (cheap AO) so gaps between clumps read as shaded grass, not bare terrain
// _LayerMode: 0 = near layer, 1 = far (thinned) layer, 2 = single layer (no LOD cross-fade, only the far-edge fade).
// Globals set by IslandStylizedGrass each frame: _GrassLod = (lodStart, lodEnd, fadeStart, fadeEnd) in metres.
Shader "Island/GhibliGrass"
{
    Properties
    {
        _Top_Color ("Top Color", Color) = (0.145, 0.455, 0.129, 1)
        _Bottom_Color ("Bottom Color", Color) = (0.137, 0.498, 0.314, 1)
        _Gradient_Strenght ("Gradient Strength", Range(0, 3)) = 1.17
        _Variation_Texture ("Variation Texture", 2D) = "white" {}
        _Variation_Offset ("Variation Offset", Vector) = (1, 1, 0, 0)
        _Color_Variation_Scale ("Color Variation Scale", Float) = 0.01
        _Color_Variation ("Color Variation", Color) = (0.293, 0.604, 0.346, 1)
        _Wind_Line ("Wind Line", 2D) = "white" {}
        _Wind_Color ("Wind Color", Color) = (0.341, 0.631, 0.380, 0.83)
        _Offset ("Offset", Range(0, 1)) = 0.212
        _Wind_Direction ("Wind Direction", Vector) = (20, 16.74, 0, 0)
        _Wind_Speed ("Wind Speed", Float) = 3
        _Wind_Strength ("Wind Strength", Float) = 1
        _Wind_Line_Scale ("Wind Line Scale", Float) = 0.03
        _Wind_Gradient ("Wind Gradient", Range(0, 1)) = 0.328
        _MeshHeight ("Mesh Height (m)", Float) = 0.3
        _LayerMode ("Layer (0 near, 1 far, 2 single)", Float) = 2
        _BendAmount ("Trample Bend (m)", Float) = 0.55
        _FlattenAmount ("Trample Flatten", Range(0, 1)) = 0.7
        _RootDarken ("Root Darken", Range(0, 1)) = 0.38
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Variation_Texture); SAMPLER(sampler_Variation_Texture);
        TEXTURE2D(_Wind_Line); SAMPLER(sampler_Wind_Line);
        TEXTURE2D(_GrassTrampleTex); SAMPLER(sampler_GrassTrampleTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _Top_Color, _Bottom_Color, _Color_Variation, _Wind_Color;
            float4 _Variation_Offset, _Wind_Direction;
            float _Gradient_Strenght, _Color_Variation_Scale, _Offset, _Wind_Speed, _Wind_Strength;
            float _Wind_Line_Scale, _Wind_Gradient, _MeshHeight, _LayerMode, _BendAmount, _FlattenAmount, _RootDarken;
        CBUFFER_END

        float4 _GrassTrampleOrigin;   // xy = world XZ of the window's min corner (set by GrassTrampleSystem)
        float  _GrassTrampleSize;     // window size in metres (0 = trample system absent)
        float4 _GrassLod;             // x lodStart, y lodEnd, z fadeStart, w fadeEnd

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float  hFrac      : TEXCOORD2;
            float  fogFactor  : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings GrassVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);

            float hFrac = saturate(input.positionOS.y / max(_MeshHeight, 0.0001));
            float h2 = hFrac * hFrac;                               // stiff root, floppy tip
            float3 posWS  = TransformObjectToWorld(input.positionOS.xyz);
            float3 rootWS = UNITY_MATRIX_M._m03_m13_m23;            // clump pivot

            // far edge: clumps lower toward the ground as they dither out (reads as grass thinning, not noise)
            float camDist = distance(_WorldSpaceCameraPos, rootWS);
            float edge = smoothstep(_GrassLod.z, _GrassLod.w, camDist);
            posWS.y = rootWS.y + (posWS.y - rootWS.y) * lerp(1.0, 0.3, edge);

            // wind: broad gusts from the variation noise + a small per-clump flutter
            float2 wdir = normalize(_Wind_Direction.xy + 0.0001);
            float gust = SAMPLE_TEXTURE2D_LOD(_Variation_Texture, sampler_Variation_Texture,
                              posWS.xz * 0.035 + wdir * _Time.y * _Wind_Speed * 0.03, 0).r;
            float flutter = sin(_Time.y * 2.3 + rootWS.x * 1.7 + rootWS.z * 1.3) * 0.012;
            posWS.xz += wdir * ((gust - 0.45) * 2.0 * _Wind_Strength * _Offset * 0.55 + flutter) * h2;

            // trample: bend away from the stamp and press down; fades to nothing at the window border
            if (_GrassTrampleSize > 0.0)
            {
                float2 tuv = (posWS.xz - _GrassTrampleOrigin.xy) / _GrassTrampleSize;
                float2 em = smoothstep(0.0, 0.05, tuv) * smoothstep(1.0, 0.95, tuv);
                float4 tr = SAMPLE_TEXTURE2D_LOD(_GrassTrampleTex, sampler_GrassTrampleTex, tuv, 0);
                float press = tr.b * em.x * em.y;
                float2 dir = tr.rg * 2.0 - 1.0;
                posWS.xz += dir * (press * _BendAmount * pow(hFrac, 1.3));
                posWS.y = rootWS.y + (posWS.y - rootWS.y) * (1.0 - _FlattenAmount * press);
            }

            o.positionWS = posWS;
            o.positionCS = TransformWorldToHClip(posWS);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.hFrac = hFrac;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        // Interleaved gradient noise: stable per-pixel threshold, smooth when the camera or grass moves
        float GrassDither(float4 positionCS)
        {
            return frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
        }

        void GrassLodClip(float3 positionWS, float4 positionCS)
        {
            float dist = distance(_WorldSpaceCameraPos, positionWS);
            float n = GrassDither(positionCS);
            float keepNear = 1.0 - smoothstep(_GrassLod.x, _GrassLod.y, dist);
            float keepEnd  = 1.0 - smoothstep(_GrassLod.z, _GrassLod.w, dist);
            if (_LayerMode < 0.5)       clip(keepNear - n - 0.001);                              // near layer: dithers OUT
            else if (_LayerMode < 1.5)  clip(min(n - keepNear + 0.001, keepEnd - n - 0.001));    // far layer: dithers IN (complement), then out at the far edge
            else                        clip(keepEnd - n - 0.001);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 GrassFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                GrassLodClip(i.positionWS, i.positionCS);

                float hF = saturate(i.hFrac);
                float3 col = lerp(_Bottom_Color.rgb, _Top_Color.rgb, saturate(hF * _Gradient_Strenght));

                float var = SAMPLE_TEXTURE2D(_Variation_Texture, sampler_Variation_Texture,
                                i.positionWS.xz * _Color_Variation_Scale + _Variation_Offset.xy).r;
                col = lerp(col, _Color_Variation.rgb, saturate(var * 0.55 * (0.4 + 0.6 * hF)));

                float2 wdir = normalize(_Wind_Direction.xy + 0.0001);
                float wl = SAMPLE_TEXTURE2D(_Wind_Line, sampler_Wind_Line,
                                i.positionWS.xz * _Wind_Line_Scale + wdir * _Time.y * _Wind_Speed * 0.02).r;
                col = lerp(col, _Wind_Color.rgb, smoothstep(_Wind_Gradient, 1.0, wl) * hF * _Wind_Color.a * 0.6);

                // toon-ish lighting: grass normal leans toward world-up so clumps shade like one soft meadow
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                #if defined(_LIGHT_COOKIES)
                l.color *= SampleMainLightCookie(i.positionWS);
                #endif
                float3 n = normalize(lerp(normalize(i.normalWS), float3(0, 1, 0), 0.75));
                float ndl = saturate(dot(n, l.direction) * 0.5 + 0.5);
                float lit = lerp(0.58, 1.0, ndl * l.shadowAttenuation);
                col *= lit * lerp(float3(1, 1, 1), l.color, 0.5);

                // dark roots: cheap contact shading that also hides any terrain showing between clumps
                col *= lerp(1.0 - _RootDarken, 1.0, smoothstep(0.0, 0.55, hF));

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                GrassLodClip(i.positionWS, i.positionCS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 DepthNormalsFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                GrassLodClip(i.positionWS, i.positionCS);
                float3 nWS = normalize(lerp(normalize(i.normalWS), float3(0, 1, 0), 0.75));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(nWS) * 0.5 + 0.5;
                    return half4(PackFloat2To888(oct), 0.0);
                #else
                    return half4(nWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}

