Shader "Test/IcoSphereVertexColor" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _LerpRandomColor ("Lerp Random Color", Range(0.0, 1.0)) = 0.5
    }

    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass {
            Name "ForwardLit"
            Tags {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                uint id : SV_VertexID;
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 posWS : TEXCOORD2;
                uint id : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _LerpRandomColor;
            CBUFFER_END

            Varyings vert(Attributes i) {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(i.vertex.xyz);
                o.vertex = v.positionCS;
                o.posWS = v.positionWS;
                o.normal = TransformObjectToWorldNormal(i.normal);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.id = i.id;
                return o;
            }

            uint IntToRandom(uint x, uint seed) {
                uint hash = x * 0x9e3779b9u + seed;
                hash = (hash ^ (hash >> 15)) * 0x85ebca6bu;
                hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
                hash = hash ^ (hash >> 16);
                return hash & 0xFF;
            }

            half4 frag(Varyings i) : SV_Target {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                InputData d = (InputData)0;
                d.positionWS = i.posWS;
                d.normalWS = normalize(i.normal);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.posWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.posWS);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo.rgb;
                s.alpha = albedo.a;

                half4 col = UniversalFragmentPBR(d, s);
                col.r = lerp(col.r, IntToRandom(i.id, 11) / 255.0, _LerpRandomColor);
                col.g = lerp(col.g, IntToRandom(i.id, 45) / 255.0, _LerpRandomColor);
                col.b = lerp(col.b, IntToRandom(i.id, 14) / 255.0, _LerpRandomColor);

                return col;
            }
            ENDHLSL
        }

        Pass {
            Name "ShadowCaster"
            Tags {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes i) {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(i.vertex.xyz);
                o.vertex = v.positionCS;
                float3 n = TransformObjectToWorldNormal(i.normal);
                Light mainLight = GetMainLight();
                float3 l = mainLight.direction;
                float3 p = v.positionWS;
                ApplyShadowBias(p, n, l);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_TARGET {
                return 0;
            }
            ENDHLSL
        }

        Pass {
            Name "DepthOnly"
            Tags {
                "LightMode" = "DepthOnly"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 vertex : POSITION;
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes i) {
                Varyings o;
                o.vertex = TransformObjectToHClip(i.vertex.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                return 0;
            }
            ENDHLSL
        }

        Pass {
            Name "DepthNormals"
            Tags {
                "LightMode" = "DepthNormals"
            }

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
