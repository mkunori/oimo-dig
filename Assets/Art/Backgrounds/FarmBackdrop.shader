Shader "Oimo Dig/Farm Backdrop"
{
    Properties
    {
        [MainTexture] _BaseMap("Farm Image", 2D) = "white" {}
        [Toggle] _Fullscreen("Draw Fullscreen", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Background" }

        Pass
        {
            Name "FarmBackdrop"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                float _Fullscreen;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = _Fullscreen > 0.5
                    ? float4(input.positionOS.xy * 2.0, UNITY_RAW_FAR_CLIP_VALUE, 1.0)
                    : TransformWorldToHClip(output.positionWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 screenSize = GetScaledScreenParams().xy;
                float viewportAspect = screenSize.x / screenSize.y;
                float imageAspect = _BaseMap_TexelSize.z / _BaseMap_TexelSize.w;
                float2 crop = min(1.0, float2(viewportAspect / imageAspect, imageAspect / viewportAspect));
                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                float2 uv = (screenUV - 0.5) * crop + 0.5;
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                // The 3D soil retains the potato's depth and shadow, while using the same picture.
                if (_Fullscreen < 0.5)
                {
                    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                        float4 shadowCoord = input.screenPosition;
                    #else
                        float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    #endif
                    color.rgb *= lerp(0.68h, 1.0h, MainLightRealtimeShadow(shadowCoord));
                }

                return color;
            }
            ENDHLSL
        }
    }
}
