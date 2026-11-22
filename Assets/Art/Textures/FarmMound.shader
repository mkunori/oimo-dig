Shader "Oimo Dig/Farm Mound"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BackdropMap("Farm Backdrop", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BackdropMap);
            SAMPLER(sampler_BackdropMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BackdropMap_TexelSize;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half2 blend : TEXCOORD4;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.screenPosition = position.positionNDC;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.blend = input.color.ra;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 screen = GetScaledScreenParams().xy;
                float viewportAspect = screen.x / screen.y;
                float imageAspect = _BackdropMap_TexelSize.z / _BackdropMap_TexelSize.w;
                float2 crop = min(1.0, float2(viewportAspect / imageAspect, imageAspect / viewportAspect));
                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                float2 artUV = (screenUV - 0.5) * crop + 0.5;

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = input.screenPosition;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                half shadow = lerp(0.68h, 1.0h, sun.shadowAttenuation);
                half lambert = saturate(dot(normalize(input.normalWS), sun.direction));
                half3 paintedSoil = SAMPLE_TEXTURE2D(_BackdropMap, sampler_BackdropMap, artUV).rgb;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 mound = albedo * (0.46h + 0.54h * lambert * sun.color) * input.blend.x;
                // Both sides of the transition use the same screen projection and shadow.
                return half4(lerp(paintedSoil, mound, input.blend.y) * shadow, 1.0h);
            }
            ENDHLSL
        }
    }
}
