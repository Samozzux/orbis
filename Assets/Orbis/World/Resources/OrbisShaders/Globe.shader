// Globo base: texture equirettangolare della Terra, luce del sole, atmosfera azzurra sul bordo,
// lato notte scuro con un filo di luce.
Shader "Orbis/Globe"
{
    Properties
    {
        _MainTex ("Terra", 2D) = "white" {}
        _Atmosphere ("Atmosfera", Color) = (0.45, 0.68, 1.0, 1)
        _SunDir ("Direzione del sole", Vector) = (0.3, 0.6, -0.7, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Globe"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Atmosphere;
                float4 _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 l = normalize(_SunDir.xyz);
                half ndl = saturate(dot(n, l) * 1.1 + 0.05);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                half rim = pow(1 - saturate(dot(n, viewDir)), 3);
                half3 col = tex * (0.08 + 1.05 * ndl);
                col += _Atmosphere.rgb * rim * (0.25 + 0.9 * ndl);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
