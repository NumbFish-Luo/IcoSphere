Shader "Custom/ComputeShader/Tri" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        // Cull Off

        Pass {
            Name "ForwardLit"
            Tags {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct InstanceData {
                float3 v1;
                float3 v2;
                float3 v3;
                float4 col;
            };

            StructuredBuffer<InstanceData> _VisibleInstancesData;

            struct Attributes {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                uint id : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes i, uint id : SV_InstanceID) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);

                // 使用实例ID作为索引，从Buffer中读取当前实例的数据
                InstanceData data = _VisibleInstancesData[id];

                // 将实例的位置应用到模型的顶点上
                float3 p = 0.0;
                switch(i.id) {
                case 0: p = data.v1; break;
                case 1: p = data.v2; break;
                case 2: p = data.v3; break;
                }

            #define TEST_PLAY_ANIM
            #ifdef TEST_PLAY_ANIM
                float3 po = (data.v1 + data.v2 + data.v3) / 3.0;
                float t = _Time.y;
                float c = cos(t);
                float s = sin(t);
                float3x3 r = float3x3(c, -s, 0,
                                      s,  c, 0,
                                      0,  0, 1);
                p = po + mul(r, p - po);
            #endif

                i.vertex.xyz = p;

                // 转换到裁剪空间
                o.vertex = TransformWorldToHClip(i.vertex.xyz);

                // 传递其他数据
                o.color = data.col;
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                return i.color * _BaseColor;
            }
            ENDHLSL
        }
    }
}
