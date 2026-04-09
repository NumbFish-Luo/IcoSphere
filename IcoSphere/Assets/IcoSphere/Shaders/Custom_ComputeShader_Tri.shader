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
                float3 v0;
                float3 v1;
                float3 v2;
                float3 c01;
                float3 c12;
                float3 c20;
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
                float3 v0 = data.v0;
                float3 v1 = data.v1;
                float3 v2 = data.v2;
                switch(i.id) {
                case 0: p = v0; break;
                case 1: p = v1; break;
                case 2: p = v2; break;
                }

            // #define TEST_PLAY_ANIM
            #ifdef TEST_PLAY_ANIM
                float t = _Time.y;
                float c = cos(t);
                float s = sin(t);
                float3x3 r = float3x3(c, -s, 0,
                                      s,  c, 0,
                                      0,  0, 1);
                float3 p0 = (v0 + v1 + v2) / 3.0;
                p = p0 + mul(r, p - p0);
            #endif

                i.vertex.xyz = p;

                // 转换到裁剪空间
                o.vertex = TransformWorldToHClip(i.vertex.xyz);

                // 传递其他数据
                o.color = data.col;
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            float SdSegment(float2 p, float2 a, float2 b) {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float SmoothLine(float2 p, float2 a, float2 b, float width) {
                float d = SdSegment(p, a, b);
                return 1.0 - smoothstep(0.0, width, d);
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                const float w = 0.05;
                const float a0 = PI * 0.5;
                const float a1 = 11.0 * PI / 6.0;
                const float a2 = 7.0 * PI / 6.0;
                const float2 p0 = 0.0;
                float2 uv = i.uv.xy;
                float l0 = SmoothLine(uv, p0, float2(cos(a0), -sin(a0)), w);
                float l1 = SmoothLine(uv, p0, float2(cos(a1), -sin(a1)), w);
                float l2 = SmoothLine(uv, p0, float2(cos(a2), -sin(a2)), w);
                return saturate(i.color * 0.2 + l0 + l1 + l2);
            }
            ENDHLSL
        }
    }
}
