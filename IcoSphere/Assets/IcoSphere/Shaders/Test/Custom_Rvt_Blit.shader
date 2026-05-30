Shader "Custom/Rvt/Blit" {
    SubShader {
        Tags {
            "RenderType" = "Opaque"
        }
        LOD 100

        Pass {
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 纹理数组
            TEXTURE2D_ARRAY(_VT_AlbedoAtlas);
            SAMPLER(sampler__VT_AlbedoAtlas);
            TEXTURE2D_ARRAY(_VT_NormalAtlas);
            SAMPLER(sampler__VT_NormalAtlas);

            // 地形控制贴图 (最多4张)
            TEXTURE2D(_Ctrl0);
            TEXTURE2D(_Ctrl1);
            TEXTURE2D(_Ctrl2);
            TEXTURE2D(_Ctrl3);
            SAMPLER(sampler__Ctrl0);
            SAMPLER(sampler__Ctrl1);
            SAMPLER(sampler__Ctrl2);
            SAMPLER(sampler__Ctrl3);

            // 参数
            float4 _VT_BlitOffsetScale; // x, y = offset, z, w = scale
            float4 _VT_TileData[16]; // _VT_TileData[passIdx * 4 + layer] = (tilingX, tilingY, 0, 0)

            struct Attributes {
                float4 posOs : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 posCs : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                output.posCs = TransformObjectToHClip(input.posOs.xyz);
                // 根据地块偏移和缩放重新计算UV
                output.uv = input.uv * _VT_BlitOffsetScale.zw + _VT_BlitOffsetScale.xy;
                return output;
            }

            // 混合辅助函数 (单张ctrl贴图 + 4层材质)
            void SplatmapMix(int passIdx, half4 ctrl, float2 uv, inout half4 mixedDiffuse, inout half3 mixedNormal) {
                // 获取当前层组的4个材质的平铺系数
                float2 tiling0 = _VT_TileData[passIdx * 4 + 0].xy;
                float2 tiling1 = _VT_TileData[passIdx * 4 + 1].xy;
                float2 tiling2 = _VT_TileData[passIdx * 4 + 2].xy;
                float2 tiling3 = _VT_TileData[passIdx * 4 + 3].xy;

                // 采样Albedo (使用Sample方法, 自动使用mip0)
                mixedDiffuse += ctrl.r * _VT_AlbedoAtlas.Sample(sampler__VT_AlbedoAtlas, float3(uv * tiling0, passIdx * 4 + 0));
                mixedDiffuse += ctrl.g * _VT_AlbedoAtlas.Sample(sampler__VT_AlbedoAtlas, float3(uv * tiling1, passIdx * 4 + 1));
                mixedDiffuse += ctrl.b * _VT_AlbedoAtlas.Sample(sampler__VT_AlbedoAtlas, float3(uv * tiling2, passIdx * 4 + 2));
                mixedDiffuse += ctrl.a * _VT_AlbedoAtlas.Sample(sampler__VT_AlbedoAtlas, float3(uv * tiling3, passIdx * 4 + 3));

                // 采样Normal
                const half scale = 1.5;
                half3 nrm0 = UnpackNormalScale(_VT_NormalAtlas.Sample(sampler__VT_NormalAtlas, float3(uv * tiling0, passIdx * 4 + 0)), scale);
                half3 nrm1 = UnpackNormalScale(_VT_NormalAtlas.Sample(sampler__VT_NormalAtlas, float3(uv * tiling1, passIdx * 4 + 1)), scale);
                half3 nrm2 = UnpackNormalScale(_VT_NormalAtlas.Sample(sampler__VT_NormalAtlas, float3(uv * tiling2, passIdx * 4 + 2)), scale);
                half3 nrm3 = UnpackNormalScale(_VT_NormalAtlas.Sample(sampler__VT_NormalAtlas, float3(uv * tiling3, passIdx * 4 + 3)), scale);

                half3 nrm = ctrl.r * nrm0 + ctrl.g * nrm1 + ctrl.b * nrm2 + ctrl.a * nrm3;
                mixedNormal += nrm;
            }

            // mrt输出结构
            struct FragOutput {
                half4 col0 : SV_Target0;
                half4 col1 : SV_Target1;
            };

            FragOutput Frag(Varyings input) {
                float2 uv = input.uv;

                // 采样4张控制贴图, 根据地形实际层数, 最多16层, 每4层一张控制图
                half4 ctrl0 = SAMPLE_TEXTURE2D(_Ctrl0, sampler__Ctrl0, uv);
                half4 ctrl1 = SAMPLE_TEXTURE2D(_Ctrl1, sampler__Ctrl1, uv);
                half4 ctrl2 = SAMPLE_TEXTURE2D(_Ctrl2, sampler__Ctrl2, uv);
                half4 ctrl3 = SAMPLE_TEXTURE2D(_Ctrl3, sampler__Ctrl3, uv);

                half4 mixedDiffuse = 0;
                half3 mixedNormal = 0;
                half totalWeight = 0;

                // 处理第一组: 层0-3
                half4 c0 = ctrl0;
                totalWeight += dot(c0, 1);
                SplatmapMix(0, c0, uv, mixedDiffuse, mixedNormal);

                // 第二组: 层4-7
                half4 c1 = ctrl1;
                totalWeight += dot(c1, 1);
                SplatmapMix(1, c1, uv, mixedDiffuse, mixedNormal);

                // 第三组: 层8-11
                half4 c2 = ctrl2;
                totalWeight += dot(c2, 1);
                SplatmapMix(2, c2, uv, mixedDiffuse, mixedNormal);

                // 第四组: 层12-15
                // half4 c3 = ctrl3;
                // totalWeight += dot(c3, 1);
                // SplatmapMix(3, c3, uv, mixedDiffuse, mixedNormal);

                // 归一化, 避免权重为0
                totalWeight = max(totalWeight, 0.001);
                mixedDiffuse /= totalWeight;
                mixedNormal /= totalWeight;

                FragOutput output;
                output.col0 = half4(mixedDiffuse.rgb, 1.0);
                output.col1 = half4(mixedNormal * 0.5 + 0.5, 1.0);
                return output;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
