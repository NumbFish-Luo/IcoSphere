Shader "Custom/CubemapRvt/Blit" {
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

            // 全局参数
            TEXTURE2D_ARRAY(_VT_AtlasDiffuse);
            SAMPLER(sampler_VT_AtlasDiffuse);
            TEXTURE2D_ARRAY(_VT_AtlasHeight);
            SAMPLER(sampler_VT_AtlasHeight);
            TEXTURE2D_ARRAY(_VT_AtlasMix);
            SAMPLER(sampler_VT_AtlasMix);
            int _VT_RootTexSize;
            int _VT_AtlasTexSize;

            // 参数
            float4 _NodeData; // u, v, size, face

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
                float2 tilling = _NodeData.zz / _VT_RootTexSize;
                float2 offset = _NodeData.xy / _VT_RootTexSize;
                output.uv = input.uv * tilling + offset;
                return output;
            }

            // mrt输出结构
            struct FragOutput {
                half4 diffuse : SV_Target0;
                half4 height : SV_Target1;
                half4 mix : SV_Target2;
            };

            FragOutput Frag(Varyings input) {
                float2 uv = input.uv;

                FragOutput output;
                output.diffuse = float4(uv.x, uv.y, 0.0, 1.0);
                output.height = 0.0;
                output.mix = 0.0;
                return output;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
