Shader "Custom/ComputeShader/Tri" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _TerrainTest ("Terrain Test", 2D) = "white" {}
        _Radius ("Radius", Float) = 1.0
        _LineWidth ("Line Width", Float) = 0.0003
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

            // xyz对应具体坐标, w对应序号
            struct InstanceData {
                float4 v0;
                float4 v1;
                float4 v2;
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
                uint3 vid : TEXCOORD3;
                nointerpolation float3 ctr : TEXCOORD4; // 当前三角形中心
                nointerpolation float4 c01 : TEXCOORD5;
                nointerpolation float4 c12 : TEXCOORD6;
                nointerpolation float4 c20 : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_TerrainTest);
            SAMPLER(sampler_TerrainTest);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Radius;
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
                float3 v0 = data.v0.xyz;
                float3 v1 = data.v1.xyz;
                float3 v2 = data.v2.xyz;

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
                o.vid = uint3(data.v0.w, data.v1.w, data.v2.w);
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

            // 判断q是否在180度夹角范围内, 返回0或者1, 这个夹角起点是p, 两条边的方向向量是v0, v1
            float In180Angle(float3 p, float3 v0, float3 v1, float3 q) {
                float3 u = q - p;
                float a = dot(v0, v0);
                float b = dot(v0, v1);
                float c = dot(v1, v1);
                float u0 = dot(u, v0);
                float u1 = dot(u, v1);
                float delta = 1.0 / (a * c - b * b);
                float alpha = (u0 * c - b * u1) * delta;
                float beta = (a * u1 - b * u0) * delta;
                return step(0.0, alpha) * step(0.0, beta);
            }

            uint Random255(uint x, uint seed) {
                uint hash = x * 0x9e3779b9u + seed;
                hash = (hash ^ (hash >> 15)) * 0x85ebca6bu;
                hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
                hash ^= (hash >> 16);
                return hash & 0xFF;
            }

            float3 RandomRgb(uint i) {
                return float3(
                    Random255(i, 11),
                    Random255(i, 45),
                    Random255(i, 14)
                ) / 255.0;
            }

            // 三角波: (0, 0) ~ (1, 1) ~ (2, 0) ~ (3, 1)
            float TriWave(float x) {
                return abs(2.0 * (x * 0.5 - floor(0.5 + x * 0.5)));
            }

            // 世界坐标转经纬度
            // 经度 (Longitude): (-1.0, 1.0] * pi
            // 纬度  (Latitude): (-0.5, 0.5] * pi
            float2 ToLonLat(float3 p) {
                p = normalize(p);
                return float2(atan2(p.y, p.x), asin(p.z));
            }

            // 外接立方体
            float3 UnitCubeToSpherePos(float3 p) {
                return normalize(p);
            }

            // 球体坐标转外接立方体坐标
            float3 UnitSphereToCubePos(float3 p) {
                p = normalize(p);
                return p / max(max(abs(p.x), abs(p.y)), abs(p.z));
            }

            float Equal(float a, float b) {
                return 1.0 - step(0.00001, abs(a - b));
            }

            // 获取此点所在的立方体面
            // x方向面: -1.0 or 1.0
            // y方向面: -2.0 or 2.0
            // z方向面: -3.0 or 3.0
            float GetCubeFace(float3 p) {
                p = normalize(p);
                float3 ap = abs(p);
                float3 sp = sign(p);
                float m = max(max(ap.x, ap.y), ap.z);
                float3 e = float3(Equal(ap.x, m), Equal(ap.y, m), Equal(ap.z, m));
                if (e.x > 0.0) {
                    return sp.x * 1.0;
                } else if (e.y > 0.0) {
                    return sp.y * 2.0;
                } else if (e.z > 0.0) {
                    return sp.z * 3.0;
                }
                return 0.0;
            }

            float4 FracCubeGrid(float3 p) {
                p = UnitSphereToCubePos(p) * _Radius;
                return float4(frac(p.x), frac(p.y), frac(p.z), 1.0);
            }

            float2 UvCubeGridFace(float3 p) {
                float2 uv = 0.0;
                float face = GetCubeFace(p);
                float absFace = abs(face);
                p = UnitSphereToCubePos(p);
                if (Equal(absFace, 1.0)) { // x face
                    uv = p.yz;
                } else if (Equal(absFace, 2.0)) { // y face
                    uv = p.xz;
                } else if (Equal(absFace, 3.0)) { // z face
                    uv = p.xy;
                }
                return (uv + 1.0) * 0.5;
            }

            float4 TriWaveCubeGrid(float3 p) {
                p = UnitSphereToCubePos(p) * _Radius;
                return float4(TriWave(p.x), TriWave(p.y), TriWave(p.z), 1.0);
            }

            float4 TerrainTest(float3 p) {
                float4 col = 0.0;
                p = UnitSphereToCubePos(p) * _Radius;
                return col;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.posWS;
                float3 o = i.ctr;
                float w = _LineWidth;
                float l0 = SmoothLine3d(p, o, i.c01.xyz, w);
                float l1 = SmoothLine3d(p, o, i.c12.xyz, w);
                float l2 = SmoothLine3d(p, o, i.c20.xyz, w);
                float l = saturate(l0 + l1 + l2);
                float4 colLine = 0.0;

                float4 col = 1.0;
                uint vid = i.vid.x;
                if (In180Angle(o, i.c01.xyz - o, i.c12.xyz - o, p) > 0.0) {
                    vid = i.vid.y;
                } else if (In180Angle(o, i.c12.xyz - o, i.c20.xyz - o, p) > 0.0) {
                    vid = i.vid.z;
                }
                col.rgb = RandomRgb(vid);
                col = lerp(col * 0.5, colLine, l);

                float4 colCubeGrid = TriWaveCubeGrid(p);
                float4 colTerrainTest = TerrainTest(p);
                float cubeFace = (GetCubeFace(p) + 3.0) / 6.0;
                float2 uvCubeGridFace = UvCubeGridFace(p);

                float t = 1.0; // (sin(_Time.y) + 1.0) * 0.5;
                col = lerp(col, cubeFace, t);

                return col;
            }
            ENDHLSL
        }
    }
}
