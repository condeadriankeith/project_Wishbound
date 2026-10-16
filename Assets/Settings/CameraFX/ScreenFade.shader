Shader "Hidden/Island/ScreenFade"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            Name "ScreenFade"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _IslandFade;   // global, 1 = black, 0 = scene (set by IslandCameraFX)

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0);
                c.rgb *= 1.0 - saturate(_IslandFade);
                return half4(c);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
