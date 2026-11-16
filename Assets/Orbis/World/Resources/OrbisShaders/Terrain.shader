// Terreno dei tile: colori delle aree (campi, vigneti, uliveti, boschi, città, acqua) dalla texture
// generata da LanduseRaster, con grana, pendii rocciosi e specchi d'acqua (alfa 0 nella texture).
Shader "Orbis/Terrain"
{
    Properties
    {
        _MainTex ("Aree", 2D) = "white" {}
        _Rock ("Roccia", Color) = (0.55, 0.50, 0.44, 1)
        _Water ("Acqua", Color) = (0.25, 0.45, 0.62, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "OrbisCommon.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Rock;
            half4 _Water;
        CBUFFER_END
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 uv0 : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 uv0 : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv0 = v.uv0;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 area = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv0.xy);
                half3 n = normalize(i.normalWS);
                float2 w = i.positionWS.xz;
                float grain = OrbisNoise(w * 0.9) * 0.6 + OrbisNoise(w * 0.12) * 0.4;
                half3 albedo = area.rgb * (0.9 + 0.2 * grain);
                // pendii ripidi: terra e roccia
                half steep = smoothstep(0.80, 0.62, n.y);
                albedo = lerp(albedo, _Rock.rgb * (0.85 + 0.3 * grain), steep * 0.8);
                half spec = 0;
                if (area.a < 0.5)
                {
                    // acqua di laghi e fiumi
                    float ripple = OrbisNoise(w * 0.6 + _Time.y * 0.3);
                    albedo = _Water.rgb * (0.9 + 0.2 * ripple);
                    spec = 0.8;
                }
                half3 color = OrbisLighting(i.positionWS, n, albedo, 1, spec);
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
            #include "OrbisPasses.hlsl"
            ENDHLSL
        }
    }
}
