// Edifici procedurali (docs/02 §4): facciate con piani e campate disegnate dallo shader
// (finestre, persiane, portoni, vetrine, cornicioni), tetti in coppi o terrazzi, luci di notte.
// Dati dai vertici: vedi Orbis.Core.World.Meshing.BuildingMesher.
Shader "Orbis/Building"
{
    Properties
    {
        _ShutterGreen ("Persiane verdi", Color) = (0.30, 0.40, 0.28, 1)
        _ShutterBrown ("Persiane marroni", Color) = (0.43, 0.31, 0.22, 1)
        _Glass ("Vetro", Color) = (0.17, 0.22, 0.27, 1)
        _NightLight ("Luce delle finestre", Color) = (1.0, 0.78, 0.45, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "OrbisCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShutterGreen;
            half4 _ShutterBrown;
            half4 _Glass;
            half4 _NightLight;
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
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD2;
                float4 uv1 : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // rettangolo morbido [a, b] su x (antialiasing con le derivate)
            float Box(float x, float a, float b)
            {
                float w = max(fwidth(x), 1e-4);
                return saturate((x - a) / w + 0.5) * saturate((b - x) / w + 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 baseColor = OrbisSRGB(i.color.rgb);
                half3 n = normalize(i.normalWS);
                float kind = i.uv1.x;
                float seed = i.uv1.y;
                half3 albedo = baseColor;
                half3 emission = 0;
                half ao = 1;
                half spec = 0;

                if (kind < 0.5)
                {
                    // ---------------- muro
                    float u = i.uv0.x, v = i.uv0.y, len = max(i.uv0.z, 0.01), wallH = max(i.uv0.w, 2.0);
                    float levels = max(i.uv1.z, 1);
                    float catShop = i.uv1.w;
                    float category = floor(catShop * 0.5 + 0.01);
                    float shop = fmod(catShop, 2.0);
                    float floorH = clamp(wallH / levels, 2.4, 6.0);
                    float bayW = category < 0.5 ? 3.3 : (category < 1.5 ? 3.8 : (category < 2.5 ? 6.5 : (category < 3.5 ? 5.0 : 3.6)));
                    float bays = max(1.0, round(len / bayW));
                    float bay = len / bays;
                    float bu = frac(u / bay);
                    float bayIndex = floor(u / bay);
                    float fl = v / floorH;
                    float fi = floor(fl);
                    float fv = frac(fl);
                    float winId = OrbisHash21(float2(bayIndex + seed * 97.0, fi + floor(len * 7.0)));

                    // zoccolo e fondamenta
                    if (v < 0.0) albedo *= 0.62;
                    else if (v < 0.6) albedo *= 0.86;
                    // marcapiano e cornicione
                    albedo *= 1.0 - 0.10 * Box(fv, 0.0, 0.035) * step(1.0, fi) * step(v, wallH - 0.5) * (category > 2.5 && category < 3.5 ? 0 : 1);
                    float cornice = Box(v, wallH - 0.45, wallH + 0.01);
                    albedo = lerp(albedo, baseColor * 1.12 + 0.03, cornice * 0.8);

                    float window = 0;
                    float shutter = 0;
                    float door = 0;
                    bool blind = category > 5.5 || len < 2.0;
                    bool aboveTop = v > wallH - 0.5 || fi >= levels;
                    if (!blind && !aboveTop && v > 0.0)
                    {
                        if (category > 4.5)
                        {
                            // serra / vetro: tutta la parete trasparente a riquadri
                            window = Box(bu, 0.04, 0.96) * Box(fv, 0.05, 0.95);
                        }
                        else if (fi < 0.5 && !(category > 2.5 && category < 3.5))
                        {
                            // piano terra
                            float mid = floor(bays * 0.5);
                            if (shop > 0.5 || category > 0.5 && category < 1.5)
                                window = Box(bu, 0.10, 0.90) * Box(fv, 0.06, 0.78);          // vetrina
                            else if (abs(bayIndex - mid) < 0.5)
                                door = Box(bu, 0.30, 0.70) * Box(fv, 0.0, 0.80);              // portone
                            else
                                window = Box(bu, 0.36, 0.64) * Box(fv, 0.34, 0.74);          // finestrella con grata
                        }
                        else if (category > 1.5 && category < 2.5)
                        {
                            window = Box(bu, 0.08, 0.92) * Box(fv, 0.55, 0.80);              // nastro industriale
                        }
                        else if (category > 2.5 && category < 3.5)
                        {
                            // chiese e monumenti: monofore alte a campate alterne, niente piani
                            float tall = Box(v / wallH, 0.42, 0.80);
                            window = Box(bu, 0.43, 0.57) * tall * step(fmod(bayIndex, 2.0), 0.5);
                        }
                        else
                        {
                            float w0 = category < 0.5 ? 0.34 : 0.22;
                            window = Box(bu, w0, 1.0 - w0) * Box(fv, 0.26, 0.80);
                            if (category < 0.5 || category > 3.5)
                                shutter = (Box(bu, w0 - 0.13, w0) + Box(bu, 1.0 - w0, 1.13 - w0)) * Box(fv, 0.26, 0.80);
                        }
                    }
                    // persiane: alcune chiuse (seme della finestra)
                    half3 shutterColor = frac(seed * 13.7) < 0.55 ? _ShutterGreen.rgb : _ShutterBrown.rgb;
                    float closed = step(0.82, winId) * (category < 0.5 ? 1 : 0);
                    albedo = lerp(albedo, shutterColor, saturate(shutter + window * closed));
                    window *= 1 - closed;
                    half3 glass = _Glass.rgb * (0.85 + 0.3 * winId);
                    albedo = lerp(albedo, glass, window);
                    albedo = lerp(albedo, half3(0.24, 0.17, 0.12), door);
                    spec = window * 0.6;
                    // di notte alcune finestre si accendono
                    float lit = step(0.35 + 0.4 * (1 - _OrbisNight), winId) * window;
                    emission = _NightLight.rgb * lit * _OrbisNight * (0.7 + 0.6 * frac(winId * 7.3));
                    ao = lerp(0.75, 1.0, saturate(v / 2.5));
                }
                else if (kind < 1.5)
                {
                    // ---------------- tetto in coppi: canali lungo la pendenza, file orizzontali
                    float x = i.uv0.x, y = i.uv0.y;
                    float channel = abs(frac(x / 0.24) - 0.5) * 2.0;
                    float course = frac(y / 0.36);
                    float tile = OrbisHash21(floor(float2(x / 0.24, y / 0.36)));
                    half shade = 0.78 + 0.22 * (1.0 - channel * channel) - 0.12 * smoothstep(0.85, 1.0, course);
                    albedo = baseColor * shade * (0.9 + 0.2 * tile);
                    // fuori fuoco da lontano: niente scintillio
                    float far = saturate(fwidth(x) * 4.0);
                    albedo = lerp(albedo, baseColor * 0.92, far);
                }
                else if (kind > 7.5)
                {
                    // ---------------- campo da calcio: strisce del prato e linee (uv0 = m nel rettangolo, lati)
                    float x = i.uv0.x, y = i.uv0.y, L = i.uv0.z, W = i.uv0.w;
                    albedo = baseColor * (0.9 + 0.12 * step(0.5, frac(x / 11.0)));
                    float m = 3.0, lw = 0.07;
                    float fx = x - m, fy = y - m, fl = L - 2 * m, fw = W - 2 * m;
                    float inside = step(-lw, fx) * step(fx, fl + lw) * step(-lw, fy) * step(fy, fw + lw);
                    float lines = (1 - Box(fx, lw, fl - lw) * Box(fy, lw, fw - lw)) * inside;
                    lines += Box(fx, fl * 0.5 - lw, fl * 0.5 + lw) * inside;
                    float r = distance(float2(fx, fy), float2(fl * 0.5, fw * 0.5));
                    lines += Box(r, 9.15 - lw, 9.15 + lw);
                    float boxW = min(40.3, fw * 0.6) * 0.5;
                    float ends = Box(fx, 16.5 - lw, 16.5 + lw) + Box(fx, fl - 16.5 - lw, fl - 16.5 + lw);
                    lines += ends * Box(fy, fw * 0.5 - boxW, fw * 0.5 + boxW);
                    lines += (Box(fx, 0, 16.5) + Box(fx, fl - 16.5, fl)) * (Box(fy, fw * 0.5 - boxW - lw, fw * 0.5 - boxW + lw) + Box(fy, fw * 0.5 + boxW - lw, fw * 0.5 + boxW + lw));
                    albedo = lerp(albedo, half3(0.92, 0.93, 0.9), saturate(lines));
                }
                else
                {
                    // ---------------- terrazzo
                    float g = OrbisNoise(i.positionWS.xz * 1.7);
                    albedo = baseColor * (0.9 + 0.15 * g);
                }

                half3 color = OrbisLighting(i.positionWS, n, albedo, ao, spec + _OrbisWet * 0.3);
                color += emission;
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
