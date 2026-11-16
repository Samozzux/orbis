// Mare: piano alla quota 0 che segue la telecamera, onde procedurali, riflesso del cielo e del sole.
Shader "Orbis/Water"
{
    Properties
    {
        _Deep ("Profondo", Color) = (0.10, 0.30, 0.48, 1)
        _Shallow ("Riflesso", Color) = (0.45, 0.66, 0.82, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" }

        HLSLINCLUDE
        #include "OrbisCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Deep;
            half4 _Shallow;
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
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float t = _Time.y;
                // normale da due strati di onde
                float2 g = float2(
                    OrbisNoise(p * 0.07 + float2(t * 0.05, 0)) - OrbisNoise(p * 0.07 + float2(t * 0.05 + 0.5, 0)),
                    OrbisNoise(p * 0.11 + float2(0, t * 0.04)) - OrbisNoise(p * 0.11 + float2(0.5, t * 0.04)));
                half3 n = normalize(half3(g.x * 0.35, 1, g.y * 0.35));
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fresnel = pow(1 - saturate(dot(n, viewDir)), 4);
                half3 albedo = lerp(_Deep.rgb, _Shallow.rgb, fresnel * 0.8);
                half3 color = OrbisLighting(i.positionWS, n, albedo, 1, 1.2);
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
