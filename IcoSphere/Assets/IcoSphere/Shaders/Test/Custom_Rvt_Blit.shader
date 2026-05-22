Shader "Custom/Rvt/Blit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // -------------------------------------------------------------
            // 纹理数组声明（URP 标准方式）
            // -------------------------------------------------------------
            TEXTURE2D_ARRAY(albedoAtlas);
            SAMPLER(sampler_albedoAtlas);
            TEXTURE2D_ARRAY(normalAtlas);
            SAMPLER(sampler_normalAtlas);

            // 地形控制贴图（最多4张）
            TEXTURE2D(_Control0);
            TEXTURE2D(_Control1);
            TEXTURE2D(_Control2);
            TEXTURE2D(_Control3);
            SAMPLER(sampler_Control0);
            SAMPLER(sampler_Control1);
            SAMPLER(sampler_Control2);
            SAMPLER(sampler_Control3);

            // 参数
            float4 blitOffsetScale;            // x,y = offset, z,w = scale
            float4 tileData[16];               // tileData[passIndex*4 + layer] = (tilingX, tilingY, 0, 0)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            // 顶点着色器
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                // 根据地块偏移和缩放重新计算 UV
                output.uv = input.uv * blitOffsetScale.zw + blitOffsetScale.xy;
                return output;
            }

            // 混合辅助函数（单张 control 贴图 + 4层材质）
            void SplatmapMix(int passIndex, half4 control, float2 uv, inout half4 mixedDiffuse, inout half3 mixedNormal)
            {
                // 获取当前层组的4个材质的平铺系数
                float2 tiling0 = tileData[passIndex * 4 + 0].xy;
                float2 tiling1 = tileData[passIndex * 4 + 1].xy;
                float2 tiling2 = tileData[passIndex * 4 + 2].xy;
                float2 tiling3 = tileData[passIndex * 4 + 3].xy;

                // 采样 Albedo（使用 Sample 方法，自动使用 mip 0）
                mixedDiffuse += control.r * albedoAtlas.Sample(sampler_albedoAtlas, float3(uv * tiling0, passIndex * 4 + 0));
                mixedDiffuse += control.g * albedoAtlas.Sample(sampler_albedoAtlas, float3(uv * tiling1, passIndex * 4 + 1));
                mixedDiffuse += control.b * albedoAtlas.Sample(sampler_albedoAtlas, float3(uv * tiling2, passIndex * 4 + 2));
                mixedDiffuse += control.a * albedoAtlas.Sample(sampler_albedoAtlas, float3(uv * tiling3, passIndex * 4 + 3));

                // 采样 Normal
                half4 nrm0 = normalAtlas.Sample(sampler_normalAtlas, float3(uv * tiling0, passIndex * 4 + 0));
                half4 nrm1 = normalAtlas.Sample(sampler_normalAtlas, float3(uv * tiling1, passIndex * 4 + 1));
                half4 nrm2 = normalAtlas.Sample(sampler_normalAtlas, float3(uv * tiling2, passIndex * 4 + 2));
                half4 nrm3 = normalAtlas.Sample(sampler_normalAtlas, float3(uv * tiling3, passIndex * 4 + 3));

                half3 nrm = control.r * nrm0.xyz + control.g * nrm1.xyz + control.b * nrm2.xyz + control.a * nrm3.xyz;
                mixedNormal += nrm;
            }

            // MRT 输出结构
            struct FragOutput
            {
                half4 color0 : SV_Target0;
                half4 color1 : SV_Target1;
            };

            FragOutput Frag(Varyings input)
            {
                float2 uv = input.uv;

                // 采样4张控制贴图（根据地形实际层数，最多16层，每4层一张控制图）
                half4 control0 = SAMPLE_TEXTURE2D(_Control0, sampler_Control0, uv);
                half4 control1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control1, uv);
                half4 control2 = SAMPLE_TEXTURE2D(_Control2, sampler_Control2, uv);
                half4 control3 = SAMPLE_TEXTURE2D(_Control3, sampler_Control3, uv);

                half4 mixedDiffuse = 0;
                half3 mixedNormal   = 0;
                half  totalWeight = 0;

                // 处理第一组（层 0-3）
                half4 c0 = control0;
                totalWeight += dot(c0, 1);
                SplatmapMix(0, c0, uv, mixedDiffuse, mixedNormal);

                // 第二组（层 4-7）
                half4 c1 = control1;
                totalWeight += dot(c1, 1);
                SplatmapMix(1, c1, uv, mixedDiffuse, mixedNormal);

                // 第三组（层 8-11）
                half4 c2 = control2;
                totalWeight += dot(c2, 1);
                SplatmapMix(2, c2, uv, mixedDiffuse, mixedNormal);

                // 第四组（层 12-15）
                half4 c3 = control3;
                totalWeight += dot(c3, 1);
                SplatmapMix(3, c3, uv, mixedDiffuse, mixedNormal);

                // 归一化（避免权重为0）
                totalWeight = max(totalWeight, 0.001);
                mixedDiffuse /= totalWeight;
                mixedNormal   /= totalWeight;

                // 将法线从 [0,1] 范围转换回 [-1,1]（因为存储时做了 *0.5+0.5）
                mixedNormal = mixedNormal * 2.0 - 1.0;

                FragOutput output;
                output.color0 = half4(mixedDiffuse.rgb, 1.0);
                output.color1 = half4(mixedNormal * 0.5 + 0.5, 1.0);
                return output;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
