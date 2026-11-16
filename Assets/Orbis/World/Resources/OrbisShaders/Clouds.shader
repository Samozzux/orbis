// Strato di nuvole: piano alto che segue la telecamera, rumore frattale che scorre col vento.
Shader "Orbis/Clouds"
{
    Properties
    {
        _Coverage ("Copertura", Range(0, 1)) = 0.3
        _Color ("Colore", Color) = (1, 1, 1, 1)
        _Shade ("Ombra", Color) = (0.7, 0.74, 0.8, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "OrbisCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Coverage;
                half4 _Color;
                half4 _Shade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int i = 0; i < 5; i++) { s += a * OrbisNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz / 2600.0 + _Time.y * float2(0.004, 0.0015);
                float n = Fbm(p);
                float c = smoothstep(1.0 - _Coverage, 1.0 - _Coverage + 0.25, n);
                // sfuma verso l'orizzonte (lontano dalla telecamera)
                float d = distance(i.positionWS.xz, _WorldSpaceCameraPos.xz);
                c *= saturate(1.0 - d / 45000.0);
                Light light = GetMainLight();
                half lit = saturate(light.direction.y * 2.0 + 0.3);
                half3 col = lerp(_Shade.rgb, _Color.rgb, saturate(n * 1.3 - 0.2)) * (light.color * 0.6 + 0.4 * lit) ;
                col = lerp(col, col * 0.15 + half3(0.02, 0.03, 0.05), _OrbisNight * 0.9);
                return half4(col, c * 0.92);
            }
            ENDHLSL
        }
    }
}
