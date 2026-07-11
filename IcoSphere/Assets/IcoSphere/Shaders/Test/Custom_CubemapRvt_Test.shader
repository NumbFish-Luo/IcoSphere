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
            #pragma require 2darray

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 全局参数
            TEXTURE2D_ARRAY(_VT_ArrIdx);
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

            #define FACE_R 0
            #define FACE_L 1
            #define FACE_U 2
            #define FACE_D 3
            #define FACE_F 4
            #define FACE_B 5
            int UvToFace(float2 uv) {
                // 先确定是底面、侧面还是顶面
                float v = uv.y;
                if (v < 0.25) {
                    return FACE_D;
                }
                if (v > 0.75) {
                    return FACE_U;
                }

                // 然后确定是侧面的哪一面
                float u = uv.x;
                if (u < 0.125) {
                    return FACE_L;
                }
                if (u < 0.375) {
                    return FACE_B;
                }
                if (u < 0.625) {
                    return FACE_R;
                }
                if (u < 0.875) {
                    return FACE_F;
                }
                return FACE_L;
            }

            half4 Frag(Varyings input) : SV_Target {
                float2 uv = input.uv;
                int face = UvToFace(uv);

                float2 uvFace = uv; // TEST
                float4 idxData = SAMPLE_TEXTURE2D_ARRAY(_VT_ArrIdx, sampler_VT_ArrIdx, uvFace, face);
                int idx = (int)idxData.x;
                float2 offset = idxData.yz;
                float size = idxData.w;

                float2 uvNode = offset; // TEST
                float4 diffuse = SAMPLE_TEXTURE2D_ARRAY(_VT_ArrDiffuse, sampler_VT_ArrDiffuse, uvNode, idx);

                return diffuse;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
