Shader "Test/Instanced" {
    Properties {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200

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
                float4 position;
                float4 color;
            };

            StructuredBuffer<InstanceData> _VisibleInstancesData;

            struct Attributes {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes i, uint id : SV_InstanceID) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);

                // 使用实例ID作为索引，从Buffer中读取当前实例的数据
                InstanceData data = _VisibleInstancesData[id];

                // 将实例的位置应用到模型的顶点上
                float3 worldPos = i.vertex.xyz + data.position.xyz;

                // 转换到裁剪空间
                o.vertex = TransformWorldToHClip(worldPos);

                // 传递实例颜色
                o.color = data.color * _BaseColor;
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                return i.color;
            }
            ENDHLSL
        }
    }
}
