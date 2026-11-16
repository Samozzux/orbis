// Alberi e piante low-poly disegnati con l'instancing: colore dai vertici, vento sulla chioma
// (uv0.y = quanto il vertice si muove), variazione di tono per istanza.
Shader "Orbis/Plant"
{
    Properties
    {
        _Wind ("Vento", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #define ORBIS_VERTEX_OFFSET 1
        #include "OrbisCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Wind;
        CBUFFER_END

        float3 OrbisDisplace(float3 positionWS, float4 uv0)
        {
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float phase = dot(origin.xz, float2(0.13, 0.17));
            float sway = sin(_Time.y * 1.3 + phase) * 0.6 + sin(_Time.y * 2.1 + phase * 1.7) * 0.4;
            positionWS.xz += uv0.y * _Wind * sway * float2(1.0, 0.6);
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.positionWS = OrbisDisplace(TransformObjectToWorld(v.positionOS.xyz), v.uv0);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                half tint = 0.85 + 0.3 * OrbisHash21(origin.xz * 0.37);
                // chioma più scura verso il basso e all'interno (occlusione finta), tronco invariato
                half ao = lerp(1.0, lerp(0.62, 1.05, saturate(v.uv0.y * 1.2)), v.uv0.x);
                o.color = half4(OrbisSRGB(v.color.rgb) * lerp(1.0, tint, v.uv0.x) * ao, 1);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 color = OrbisLighting(i.positionWS, normalize(i.normalWS), i.color.rgb, 1, 0);
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex OrbisShadowVertex
            #pragma fragment OrbisDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "OrbisPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex OrbisDepthVertex
            #pragma fragment OrbisDepthFragment
            #pragma multi_compile_instancing
            #include "OrbisPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex OrbisDepthVertex
            #pragma fragment OrbisDepthNormalsFragment
            #pragma multi_compile_instancing
            #include "OrbisPasses.hlsl"
            ENDHLSL
        }
    }
}
