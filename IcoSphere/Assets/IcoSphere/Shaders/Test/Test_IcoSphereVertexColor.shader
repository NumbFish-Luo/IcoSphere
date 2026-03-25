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
                float4 color : COLOR;
            };

            struct Varyings {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normal : TEXCOORD1;
                float3 posWS : TEXCOORD2;
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
                o.color = i.color;
                return o;
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
                col = lerp(col, i.color, _LerpRandomColor);

                return col;
            }
            ENDHLSL
        }
    }
}
