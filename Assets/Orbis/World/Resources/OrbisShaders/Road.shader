// Strade, marciapiedi, piazze, sterrati, ferrovie e piste (dati: Orbis.Core.World.Meshing.RoadMesher).
Shader "Orbis/Road"
{
    Properties
    {
        _Marking ("Segnaletica", Color) = (0.93, 0.93, 0.90, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "OrbisCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Marking;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            Offset -1, -2

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
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD1;
                float4 uv1 : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Line(float x, float center, float halfWidth)
            {
                float w = max(fwidth(x), 1e-4);
                return saturate((halfWidth - abs(x - center)) / w + 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 baseColor = OrbisSRGB(i.color.rgb);
                float across = i.uv0.x;           // 0..1
                float along = i.uv0.y;            // metri
                float width = max(i.uv0.z, 0.5);
                float kind = i.uv1.x;
                float oneway = i.uv1.y;
                // segnaletica: bit 0 righe, bit 1-2 strisce pedonali a inizio/fine, sopra la lunghezza in quarti di metro
                float sig = i.uv1.w;
                float flags = fmod(sig, 8.0);
                float roadLen = floor(sig / 8.0) * 0.25;
                float markings = fmod(flags, 2.0);
                float zebraStart = fmod(floor(flags / 2.0), 2.0);
                float zebraEnd = floor(flags / 4.0);
                float xm = (across - 0.5) * width; // metri dal centro
                half3 albedo = baseColor;
                half spec = 0.05;
                float grain = OrbisNoise(i.positionWS.xz * 3.1) * 0.5 + OrbisNoise(i.positionWS.xz * 0.35) * 0.5;

                if (kind < 0.5)
                {
                    // asfalto
                    albedo *= 0.92 + 0.16 * grain;
                    if (markings > 0.5 && width > 4.5)
                    {
                        float edge = Line(abs(xm), width * 0.5 - 0.35, 0.07);
                        float center = oneway > 0.5 || oneway < -0.5 ? 0 : Line(xm, 0, 0.07) * step(frac(along / 6.0), 0.5);
                        albedo = lerp(albedo, OrbisSRGB(_Marking.rgb), saturate(edge + center) * 0.9);
                    }
                    // strisce pedonali vicino agli incroci: fasce bianche parallele alla strada, larghe 50 cm
                    float zs = zebraStart * step(5.0, along) * step(along, 8.0);
                    float ze = zebraEnd * step(roadLen - 8.0, along) * step(along, roadLen - 5.0);
                    float bars = step(0.5, frac(xm / 1.0 + 0.25)) * step(abs(xm), width * 0.5 - 0.3);
                    float wear = 0.75 + 0.25 * OrbisNoise(i.positionWS.xz * 2.3);
                    albedo = lerp(albedo, OrbisSRGB(_Marking.rgb), saturate(zs + ze) * bars * 0.92 * wear);
                    spec = 0.08 + _OrbisWet * 0.5;
                }
                else if (kind < 1.5)
                {
                    // marciapiede: lastre e cordolo verso la strada
                    float2 p = float2(xm, along) / 0.6;
                    float joints = max(Line(frac(p.x), 0.0, 0.03) + Line(frac(p.x), 1.0, 0.03), Line(frac(p.y), 0.0, 0.03) + Line(frac(p.y), 1.0, 0.03));
                    albedo *= 1.0 - 0.15 * saturate(joints);
                    albedo *= 0.94 + 0.1 * grain;
                }
                else if (kind < 2.5)
                {
                    // piazza o via pedonale: sampietrini
                    float2 p = float2(xm, along) / 0.22;
                    p.x += step(0.5, frac(p.y * 0.5)) * 0.5;
                    float2 f = abs(frac(p) - 0.5);
                    float stone = OrbisHash21(floor(p));
                    albedo *= (0.88 + 0.16 * stone) * (1.0 - 0.18 * smoothstep(0.38, 0.5, max(f.x, f.y)));
                }
                else if (kind < 3.5)
                {
                    albedo *= 0.85 + 0.3 * grain; // sterrato
                    albedo *= 1.0 - 0.12 * Line(abs(xm), width * 0.22, 0.25); // solchi delle ruote
                }
                else if (kind < 4.5)
                {
                    albedo *= 0.9 + 0.2 * grain; // sentiero / pista ciclabile
                }
                else if (kind < 5.5)
                {
                    // ferrovia: massicciata, traversine e due binari
                    albedo *= 0.85 + 0.3 * OrbisNoise(i.positionWS.xz * 9.0);
                    float sleeper = Line(frac(along / 0.65), 0.5, 0.17) * step(abs(xm), 1.25);
                    albedo = lerp(albedo, OrbisSRGB(half3(0.42, 0.33, 0.26)), sleeper);
                    float rails = saturate(Line(xm, -0.72, 0.045) + Line(xm, 0.72, 0.045));
                    albedo = lerp(albedo, OrbisSRGB(half3(0.55, 0.55, 0.57)), rails);
                    spec = rails * 0.6;
                }
                else if (kind < 6.5)
                {
                    albedo *= 1.0 - 0.25 * step(frac(along / 0.32), 0.15); // gradini
                }
                else
                {
                    albedo *= 0.92 + 0.12 * grain; // pista dell'aeroporto
                    float c = Line(xm, 0, 0.45) * step(frac(along / 50.0), 0.6);
                    albedo = lerp(albedo, OrbisSRGB(_Marking.rgb), c);
                }

                half3 color = OrbisLighting(i.positionWS, half3(0, 1, 0), albedo, 1, spec);
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            Offset -1, -2
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
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex OrbisDepthVertex
            #pragma fragment OrbisDepthNormalsFragment
            #include "OrbisPasses.hlsl"
            ENDHLSL
        }
    }
}
