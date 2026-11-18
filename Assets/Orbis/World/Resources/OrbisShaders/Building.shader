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

                    // intonaco: macchie larghe, colature verticali e sporco verso terra
                    float plaster = OrbisNoise(float2(u * 0.35 + seed * 57.0, v * 0.35)) * 0.6 + OrbisNoise(float2(u * 1.7, v * 1.7 + seed * 13.0)) * 0.4;
                    float streaks = OrbisNoise(float2(u * 2.2 + seed * 7.0, v * 0.12));
                    albedo *= 0.9 + 0.14 * plaster - 0.06 * smoothstep(0.55, 0.9, streaks);
                    albedo *= lerp(0.82, 1.0, saturate(v / 2.2));
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
                    // cornici chiare attorno a finestre e porte, davanzale sotto le finestre ai piani
                    float frameMask = 0;
                    if (!blind && !aboveTop && v > 0.0 && category < 4.5 && fi > 0.5 && category < 1.5)
                    {
                        float w0f = category < 0.5 ? 0.34 : 0.22;
                        float outer = Box(bu, w0f - 0.04, 1.0 - w0f + 0.04) * Box(fv, 0.22, 0.84);
                        frameMask = saturate(outer - window);
                        float sill = Box(bu, w0f - 0.07, 1.0 - w0f + 0.07) * Box(fv, 0.19, 0.24);
                        frameMask = saturate(frameMask + sill);
                    }
                    albedo = lerp(albedo, baseColor * 1.15 + 0.05, frameMask * 0.75);
                    // persiane: alcune chiuse (seme della finestra)
                    half3 shutterColor = frac(seed * 13.7) < 0.55 ? _ShutterGreen.rgb : _ShutterBrown.rgb;
                    float closed = step(0.82, winId) * (category < 0.5 ? 1 : 0);
                    albedo = lerp(albedo, shutterColor, saturate(shutter + window * closed));
                    window *= 1 - closed;
                    half3 glass = _Glass.rgb * (0.75 + 0.45 * winId) + half3(0.05, 0.06, 0.08) * step(0.6, fv);
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
                    float patches = OrbisNoise(float2(x, y) * 0.18 + seed * 11.0);
                    albedo = baseColor * shade * (0.86 + 0.26 * tile) * (0.88 + 0.2 * patches);
                    albedo = lerp(albedo, albedo * half3(0.82, 0.8, 0.72), smoothstep(0.6, 0.85, patches) * 0.5); // coppi vecchi
                    // fuori fuoco da lontano: niente scintillio
                    float far = saturate(fwidth(x) * 4.0);
                    albedo = lerp(albedo, baseColor * 0.92, far);
                }
                else if (kind > 8.5 && kind < 9.5)
                {
                    // ---------------- parcheggio: asfalto con stalli
                    float x = i.uv0.x, y = i.uv0.y;
                    albedo = baseColor * (0.9 + 0.15 * OrbisNoise(i.positionWS.xz * 2.0));
                    float stall = Box(frac(x / 2.6), 0.0, 0.05) * Box(frac(y / 12.0), 0.08, 0.42);
                    albedo = lerp(albedo, half3(0.9, 0.9, 0.88), stall);
                }
                else if (kind > 9.5 && kind < 10.5)
                {
                    // ---------------- pavimentazione in pietra
                    float2 p = i.uv0.xy / 0.4;
                    p.x += step(0.5, frac(p.y * 0.5)) * 0.5;
                    float2 f2 = abs(frac(p) - 0.5);
                    albedo = baseColor * (0.88 + 0.16 * OrbisHash21(floor(p))) * (1.0 - 0.16 * smoothstep(0.4, 0.5, max(f2.x, f2.y)));
                }
                else if (kind > 10.5 && kind < 11.5)
                {
                    // ---------------- acqua (piscine, laghetti)
                    float ripple = OrbisNoise(i.positionWS.xz * 0.8 + _Time.y * 0.4);
                    albedo = baseColor * (0.85 + 0.25 * ripple);
                    spec = 1.0;
                }
                else if (kind > 12.5)
                {
                    // ---------------- addobbi (BuildingDecorator): parametro in uv1.z
                    float param = i.uv1.z;
                    if (kind < 13.5)
                    {
                        // oggetto verniciato (arredo, ringhiere, tavolini, pilastri): tinta piena con leggera grana
                        albedo = baseColor * (0.94 + 0.08 * OrbisNoise(i.positionWS.xz * 3.0 + i.positionWS.y));
                    }
                    else if (kind < 14.5)
                    {
                        // telo (tende, ombrelloni, bandiere): righe da 0,5 m se il parametro è 1
                        float stripe = step(0.5, frac(i.uv0.x / 0.5));
                        half3 cream = half3(0.96, 0.93, 0.86);
                        albedo = param > 0.5 ? lerp(baseColor, cream, stripe) : baseColor;
                        albedo *= 0.92 + 0.08 * OrbisNoise(i.uv0.xy * 4.0);
                    }
                    else if (kind < 15.5)
                    {
                        // insegna: fondo colorato, bordo chiaro, "scritta" a blocchi che la sera si accende
                        float w = max(i.uv0.z, 0.01), h = max(i.uv0.w, 0.01);
                        float u = i.uv0.x, v = i.uv0.y;
                        float border = 1 - Box(u, 0.05, w - 0.05) * Box(v, 0.05, h - 0.05);
                        float margin = 0.16 * w;
                        float inRow = Box(v, h * 0.26, h * 0.74) * Box(u, margin, w - margin);
                        float cell = floor(u / 0.24);
                        float letter = step(0.22, OrbisHash21(float2(cell, seed * 31.0))) * Box(frac(u / 0.24), 0.14, 0.86);
                        float text = inRow * letter * step(0.5, param);
                        half3 ink = half3(0.98, 0.95, 0.86);
                        albedo = lerp(baseColor, ink, saturate(text + border * 0.8));
                        emission = (ink * text * 1.3 + baseColor * 0.3) * _OrbisNight;
                    }
                    else if (kind < 16.5)
                    {
                        // luce (lampadine, croci luminose): pastello di giorno, accesa di notte
                        albedo = baseColor;
                        emission = baseColor * (0.12 + 1.8 * _OrbisNight) * max(param, 0.3);
                    }
                    else if (kind < 17.5)
                    {
                        // piante e fiori: fogliame a macchie con fiori colorati (densità = parametro)
                        float3 pw = i.positionWS * 6.0;
                        float leaf = OrbisNoise(pw.xz + pw.y);
                        albedo = baseColor * (0.65 + 0.5 * leaf);
                        float hsh = OrbisHash21(floor(i.positionWS.xz * 7.0 + i.positionWS.y * 5.0));
                        if (hsh < param * 0.75)
                        {
                            float pick = frac(hsh * 37.17);
                            half3 flower = pick < 0.25 ? half3(0.86, 0.12, 0.18) : (pick < 0.45 ? half3(0.95, 0.45, 0.65) :
                                (pick < 0.65 ? half3(0.97, 0.95, 0.9) : (pick < 0.82 ? half3(0.98, 0.8, 0.15) : half3(0.62, 0.35, 0.85))));
                            albedo = flower;
                        }
                        ao = 0.85;
                    }
                    else if (kind < 18.5)
                    {
                        // gradinata dello stadio: una fila di sedili (colore del vertice) ogni "param" metri di quota,
                        // con l'alzata in cemento e l'ombra sotto il gradino; da lontano un colore medio
                        float rowH = max(param, 0.3);
                        float y = i.positionWS.y / rowH;
                        float r = frac(y);
                        float w = max(fwidth(y), 1e-4);
                        float seat = saturate((r - 0.24) / w + 0.5) * saturate((0.96 - r) / w + 0.5);
                        float lip = saturate((0.07 - r) / w + 0.5);
                        half3 concrete = half3(0.74, 0.73, 0.7);
                        float speck = OrbisHash21(floor(i.positionWS.xz / 0.55) + floor(y) * 3.1);
                        half3 rows = lerp(concrete, baseColor * (0.84 + 0.28 * speck), seat) * (1.0 - 0.28 * lip);
                        float far = saturate(w * 1.5);
                        albedo = lerp(rows, lerp(concrete, baseColor, 0.8), far);
                    }
                    else
                    {
                        // pista d'atletica: tartan con le corsie da 1,22 m (uv0.y = distanza dal bordo interno, uv0.z = larghezza)
                        float y = i.uv0.y, wTrack = max(i.uv0.z, 1.0);
                        float laneY = y / 1.22;
                        float lane = frac(laneY);
                        float wl = max(fwidth(laneY), 1e-4);
                        float laneMark = saturate((0.05 - lane) / wl + 0.5) + saturate((lane - 0.95) / wl + 0.5);
                        laneMark *= step(0.05, y) * step(y, wTrack - 0.05);
                        albedo = baseColor * (0.9 + 0.12 * OrbisNoise(i.positionWS.xz * 3.0));
                        albedo = lerp(albedo, half3(0.95, 0.95, 0.93), saturate(laneMark) * (1.0 - saturate(wl * 3.0)));
                    }
                }
                else if (kind > 11.5)
                {
                    // ---------------- pannello solare: celle scure con telaio chiaro
                    float2 cell = frac(i.uv0.xy / float2(1.0, 0.55));
                    float frame = 1 - Box(cell.x, 0.04, 0.96) * Box(cell.y, 0.06, 0.94);
                    albedo = lerp(baseColor, half3(0.75, 0.77, 0.8), frame * 0.8);
                    spec = 0.9;
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
                    // ---------------- terrazzo: piastrelle (cotto o pietra) da 45 cm con le fughe, macchie larghe
                    float g = OrbisNoise(i.positionWS.xz * 1.7);
                    float2 tp = i.uv0.xy / 0.45;
                    float2 tf = abs(frac(tp) - 0.5);
                    float grout = smoothstep(0.43, 0.5, max(tf.x, tf.y));
                    float tileVar = OrbisHash21(floor(tp) + seed * 17.0);
                    float far = saturate(fwidth(tp.x) * 1.5);
                    half3 tiles = baseColor * (0.88 + 0.16 * tileVar) * (1.0 - 0.22 * grout);
                    albedo = lerp(tiles, baseColor, far) * (0.92 + 0.12 * g);
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
