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
                float4 posOs : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tanOs : TANGENT;
            };

            struct Varyings {
                float4 posCs : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posWs : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tanWs : TEXCOORD3; // xyz = tangent, w = sign
                #if defined(_MAIN_LIGHT_SHADOWS)
                    float4 shadowCoord : TEXCOORD4;
                #endif
                float fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes i) {
                Varyings o;
                o.posCs = TransformObjectToHClip(i.posOs.xyz);
                o.uv = i.uv;

                o.posWs = TransformObjectToWorld(i.posOs.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.tanWs = float4(TransformObjectToWorldDir(i.tanOs.xyz), i.tanOs.w);

                #if defined(_MAIN_LIGHT_SHADOWS)
                    o.shadowCoord = TransformWorldToShadowCoord(o.posWs);
                #endif

                o.fogFactor = ComputeFogFactor(o.posCs.z);
                return o;
            }

            #define FACE_R 0
            #define FACE_L 1
            #define FACE_U 2
            #define FACE_D 3
            #define FACE_F 4
            #define FACE_B 5

            // 球体坐标转立方体坐标
            void SphereToCube(float3 ps, out float3 pc, out int face, out float2 uv) {
                float absX = abs(ps.x);
                float absY = abs(ps.y);
                float absZ = abs(ps.z);
                float m = max(max(absX, absY), absZ);
                pc = ps / m;
                if (absX >= absY && absX >= absZ) {
                    if (ps.x > 0.0) {
                        face = FACE_R;
                        uv = float2(-ps.z, -ps.y);
                    } else {
                        face = FACE_L;
                        uv = float2(+ps.z, -ps.y);
                    }
                } else if (absY >= absX && absY >= absZ) {
                    if (ps.y > 0.0) {
                        face = FACE_U;
                        uv = float2(+ps.x, +ps.z);
                    } else {
                        face = FACE_D;
                        uv = float2(+ps.x, -ps.z);
                    }
                } else {
                    if (ps.z > 0.0) {
                        face = FACE_F;
                        uv = float2(+ps.x, -ps.y);
                    } else {
                        face = FACE_B;
                        uv = float2(-ps.x, -ps.y);
                    }
                }
                uv = uv / m * 0.5 + 0.5;
            }

            half4 Frag(Varyings i) : SV_Target {
                float2 uv = i.uv;
                float3 ps = normalize(i.posWs);
                float3 pc = 0.0;
                int face = 0;
                float2 uvFace = 0.0;
                SphereToCube(ps, pc, face, uvFace);

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
