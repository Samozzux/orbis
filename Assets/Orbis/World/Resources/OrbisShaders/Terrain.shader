// Terreno dei tile: colori delle aree (campi, vigneti, uliveti, boschi, città, acqua) dalla texture
// generata da LanduseRaster; la mappa dei materiali (_Detail) dice il tipo di suolo, la direzione e la fase
// delle file: solchi e filari disegnati in metri, allineati ai campi veri; bordi naturali senza gradini.
Shader "Orbis/Terrain"
{
    Properties
    {
        _MainTex ("Aree", 2D) = "white" {}
        _Detail ("Suolo", 2D) = "black" {}
        _DetailSize ("Lato della mappa del suolo", Float) = 512
        _Rock ("Roccia", Color) = (0.55, 0.50, 0.44, 1)
        _Water ("Acqua", Color) = (0.12, 0.32, 0.50, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "OrbisCommon.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_Detail);
        SAMPLER(sampler_point_clamp);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _DetailSize;
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
                float2 lp = i.uv0.zw;           // metri nel tile
                float2 w = i.positionWS.xz;
                half3 n = normalize(i.normalWS);
                // bordi naturali: vicino ai confini la lettura ondeggia di un paio di metri (niente gradini dei pixel)
                float texel = 1.0 / max(_DetailSize, 1.0);
                float2 wob = float2(OrbisNoise(lp * 0.21), OrbisNoise(lp * 0.21 + 31.7)) - 0.5;
                float2 uv = i.uv0.xy + wob * texel * 1.8;
                half4 area = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half4 det = SAMPLE_TEXTURE2D(_Detail, sampler_point_clamp, uv);
                float code = floor(det.r * 255.0 + 0.5);
                float ground = floor(code / 16.0);
                float crop = code - ground * 16.0;
                float theta = (floor(det.g * 255.0 + 0.5) * 256.0 + floor(det.b * 255.0 + 0.5)) / 65535.0 * PI;
                float phase = floor(det.a * 255.0 + 0.5) / 256.0;
                float2 axis = float2(cos(theta), sin(theta));
                float across = dot(lp, axis);
                float mpp = max(fwidth(across), 1e-4);             // metri per pixel lungo le file
                // macchie larghe (umidità, esposizione) e grana fine in metri, che sparisce da lontano
                float macro = OrbisNoise(w * 0.011) * 0.6 + OrbisNoise(w * 0.037) * 0.4;
                float fineFade = saturate(1.0 - fwidth(w.x) * 1.2);
                float fine = OrbisNoise(w * 1.3);
                half3 albedo = area.rgb * (0.9 + 0.16 * macro);
                albedo *= 1.0 + (fine - 0.5) * 0.14 * fineFade;

                if (ground < 1.5)
                {
                    // prato: ciuffi, chiazze più secche e più verdi
                    albedo *= 0.88 + 0.22 * OrbisNoise(w * 0.35);
                    float dry = smoothstep(0.55, 0.8, OrbisNoise(w * 0.06 + 7.0));
                    albedo = lerp(albedo, albedo * half3(1.12, 1.04, 0.78), dry * 0.5);
                }
                else if (ground < 2.5)
                {
                    // campo coltivato a file (direzione e fase del campo vero)
                    float gap = crop < 0.5 ? 0.3 : (crop < 1.5 ? 0.45 : (crop < 2.5 ? 0.9 : 0.75));
                    float t = frac(across / gap - phase);
                    float rows = 1 - abs(t - 0.5) * 2;
                    float rowFade = saturate(1.6 - mpp / gap * 4.0);
                    if (crop > 1.5 && crop < 2.5)
                    {
                        // arato: solchi scuri e zolle
                        albedo *= lerp(1.0, 0.72 + 0.4 * smoothstep(0.35, 0.9, rows), rowFade);
                        albedo *= 0.9 + 0.2 * OrbisNoise(w * 2.1) * fineFade;
                    }
                    else if (crop > 2.5)
                    {
                        // girasole, mais: file di piante sulla terra
                        half3 soil = half3(0.55, 0.43, 0.30);
                        albedo = lerp(albedo, lerp(soil, albedo * 1.08, smoothstep(0.45, 0.75, rows)), rowFade * 0.85);
                    }
                    else
                    {
                        // grano e colture fitte: file leggere, passaggi del trattore ogni 18 m, bande mosse dal vento
                        albedo *= lerp(1.0, 0.92 + 0.12 * rows, rowFade);
                        float tram = frac(across / 18.0 - phase * 0.17);
                        float tramLine = saturate((1 - smoothstep(0.0, 0.012, abs(tram - 0.48))) + (1 - smoothstep(0.0, 0.012, abs(tram - 0.52))));
                        albedo *= 1 - 0.18 * tramLine * saturate(1.6 - mpp * 8.0);
                        albedo *= 0.94 + 0.1 * OrbisNoise(float2(across * 0.08, dot(lp, float2(-axis.y, axis.x)) * 0.02));
                    }
                }
                else if (ground < 3.5)
                {
                    // vigneto: terra lavorata sotto i filari, erba tra un filare e l'altro
                    float t = frac(across / 2.6 - phase);
                    float under = 1 - smoothstep(0.12, 0.22, abs(t - 0.5));
                    half3 soil = half3(0.58, 0.46, 0.33);
                    half3 grass = albedo * half3(0.85, 1.05, 0.8);
                    albedo = lerp(albedo, lerp(grass, soil, under), saturate(1.6 - mpp / 2.6 * 4.0) * 0.9);
                }
                else if (ground < 4.5)
                {
                    // uliveti e frutteti: erba rada a chiazze di terra
                    float bare = smoothstep(0.45, 0.7, OrbisNoise(w * 0.18));
                    albedo = lerp(albedo, albedo * half3(1.05, 0.92, 0.75), bare * 0.55);
                }
                else if (ground < 5.5)
                {
                    // bosco: sottobosco scuro a chiazze
                    albedo *= 0.78 + 0.3 * (OrbisNoise(w * 0.5) * 0.6 + OrbisNoise(w * 2.0) * 0.4 * fineFade);
                }
                else if (ground < 6.5)
                {
                    // città: cortili lastricati e giardini a chiazze
                    float garden = smoothstep(0.52, 0.66, OrbisNoise(w * 0.07 + 11.0));
                    float2 tp = lp / 0.5;
                    float2 tf = abs(frac(tp) - 0.5);
                    float joints = smoothstep(0.42, 0.5, max(tf.x, tf.y)) * fineFade;
                    half3 paving = area.rgb * (0.95 + 0.1 * OrbisHash21(floor(tp))) * (1.0 - 0.12 * joints);
                    half3 lawn = half3(0.47, 0.58, 0.32) * (0.85 + 0.25 * OrbisNoise(w * 0.4));
                    albedo = lerp(paving * (0.9 + 0.14 * OrbisNoise(w * 0.25)), lawn, garden * 0.85);
                }
                else
                {
                    // sabbia, roccia
                    albedo *= 0.9 + 0.18 * OrbisNoise(w * 0.8);
                }

                // pendii ripidi: terra e roccia
                half steep = smoothstep(0.80, 0.62, n.y);
                albedo = lerp(albedo, _Rock.rgb * (0.85 + 0.3 * fine), steep * 0.8);
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
