Shader "Custom/Rvt/Terrain" {
    Properties {
        [HideInInspector] _Control("Control", 2D) = "red" {}
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-99"
            "TerrainCompatible" = "True"
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

            // 纹理数组
            TEXTURE2D(_VT_IdxTex);
            SAMPLER(sampler_VT_IdxTex);
            TEXTURE2D_ARRAY(_VT_AlbedoTex);
            SAMPLER(sampler_VT_AlbedoTex);
            TEXTURE2D_ARRAY(_VT_NormalTex);
            SAMPLER(sampler_VT_NormalTex);

            // 全局属性
            int _VT_RootSize;
            int _VT_ArrSize;

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
                // 采样索引贴图
                float4 indexData = SAMPLE_TEXTURE2D(_VT_IdxTex, sampler_VT_IdxTex, input.uv);
                int arrayIdx = (int)indexData.r;
                float2 offset = indexData.yz;
                float blockSize = indexData.w;

                // 计算地块内局部UV
                float2 worldPos = input.uv * _VT_RootSize;
                float2 localUV = (worldPos - offset) / blockSize;
                localUV = saturate(localUV);

                // 手动计算mipmap等级
                float lodBias = -0.65;
                float2 dx = ddx(worldPos * _VT_ArrSize);
                float2 dy = ddy(worldPos * _VT_ArrSize);
                float md = max(dot(dx, dx), dot(dy, dy));
                float mip = clamp(0.5 * log2(md) - log2(blockSize) + lodBias, 0, 3);

                // 采样Albedo和Normal
                float3 albedo = _VT_AlbedoTex.SampleLevel(sampler_VT_AlbedoTex, float3(localUV, arrayIdx), mip).rgb;
                float3 normalTS = _VT_NormalTex.SampleLevel(sampler_VT_NormalTex, float3(localUV, arrayIdx), mip).rgb;
                normalTS = normalTS * 2.0 - 1.0;

                // 切线空间法线 -> 世界空间
                float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, GetObjectToWorldMatrix()._m01_m11_m21, input.normalWS));
                normalWS = normalize(normalWS);

                // 输入数据
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS)
                    inputData.shadowCoord = input.shadowCoord;
                #endif
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0.0, 0.0, 0.0);
                inputData.bakedGI = 0.0;
                inputData.shadowMask = 1.0;
                inputData.normalizedScreenSpaceUV = 0.0;

                // 表面数据
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = 0.0;
                surfaceData.specular = 0.0;
                surfaceData.smoothness = 0.0;
                // surfaceData.occlusion = 1.0;
                surfaceData.emission = 0.0;
                surfaceData.normalTS = normalTS;

                // 最终PBR颜色
                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // 雾效
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
