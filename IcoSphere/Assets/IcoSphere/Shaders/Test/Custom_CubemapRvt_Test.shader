Shader "Custom/CubemapRvt/Test" {
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-99"
        }
        LOD 200

        Pass {
            Name "ForwardLit"
            Tags {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 全局参数
            TEXTURE2D(_VT_ArrIdx);
            SAMPLER(sampler_VT_ArrIdx);
            TEXTURE2D_ARRAY(_VT_ArrDiffuse);
            SAMPLER(sampler_VT_ArrDiffuse);
            TEXTURE2D_ARRAY(_VT_ArrHeight);
            SAMPLER(sampler_VT_ArrHeight);
            TEXTURE2D_ARRAY(_VT_ArrMix);
            SAMPLER(sampler_VT_ArrMix);
            int _VT_RootTexSize;
            int _VT_AtlasTexSize;

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3; // xyz = tangent, w = sign
                #if defined(_MAIN_LIGHT_SHADOWS)
                    float4 shadowCoord : TEXCOORD4;
                #endif
                float fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);

                #if defined(_MAIN_LIGHT_SHADOWS)
                    output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
                #endif

                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                return float4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
