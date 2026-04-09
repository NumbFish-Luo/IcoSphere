Shader "Custom/ComputeShader/Tri" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _LineWidth ("Line Width", Float) = 0.00005
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
                float4 c01;
                float4 c12;
                float4 c20;
                float4 col;
            };

            StructuredBuffer<InstanceData> _VisibleInstancesData;

            struct Attributes {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint id : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                float3 normal : TEXCOORD2;
                nointerpolation float3 ctr : TEXCOORD3; // 当前三角形中心
                nointerpolation float4 c01 : TEXCOORD4;
                nointerpolation float4 c12 : TEXCOORD5;
                nointerpolation float4 c20 : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _LineWidth;
            CBUFFER_END

            // 将p投影到平面上, 已知这个平面的法线n, 以及这个平面上的一个点q
            float3 ProjToNormalPlane(float3 n, float3 p, float3 q) {
                return p - n * dot(n, p - q) / dot(n, n);
            }

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

                o.posWS = p;
                o.ctr = (v0 + v1 + v2) / 3.0;
                o.normal = normalize(cross(v1 - v0, v2 - v0)); // 3点确定法线

                // 需要将毗邻三角形中心坐标投影到当前法线平面上, frag中才能正确渲染3d线条, 否则可能会嵌入球体里面
                o.c01 = float4(ProjToNormalPlane(o.normal, data.c01.xyz, o.ctr), data.c01.w);
                o.c12 = float4(ProjToNormalPlane(o.normal, data.c12.xyz, o.ctr), data.c12.w);
                o.c20 = float4(ProjToNormalPlane(o.normal, data.c20.xyz, o.ctr), data.c20.w);

                // 转换到裁剪空间
                i.vertex.xyz = p;
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

            float SdSegment3d(float3 p, float3 a, float3 b) {
                float3 pa = p - a;
                float3 ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float SmoothLine(float2 p, float2 a, float2 b, float width) {
                float d = SdSegment(p, a, b);
                return 1.0 - smoothstep(0.0, width, d);
            }

            float SmoothLine3d(float3 p, float3 a, float3 b, float width) {
                float d = SdSegment3d(p, a, b);
                return 1.0 - smoothstep(0.0, width, d);
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = i.posWS;
                float3 ctr = i.ctr;
                float w = _LineWidth;
                float l0 = SmoothLine3d(wp, ctr, i.c01.xyz, w);
                float l1 = SmoothLine3d(wp, ctr, i.c12.xyz, w);
                float l2 = SmoothLine3d(wp, ctr, i.c20.xyz, w);
                float l = saturate(l0 + l1 + l2);
                return lerp(i.color * 0.5, 0.0, l);
            }
            ENDHLSL
        }
    }
}
